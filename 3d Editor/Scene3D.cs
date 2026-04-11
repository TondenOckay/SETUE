using VkBuffer = Silk.NET.Vulkan.Buffer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Silk.NET.Vulkan;
using SETUE.Core;
using SETUE.Components;
using SETUE.RenderEngine;

namespace SETUE.Scene
{
    public class DrawCommand3D
    {
        public Matrix4x4 Transform;
        public Vector4 Color;
        public string PipelineId = "";
        public VkBuffer VertexBuffer;
        public VkBuffer IndexBuffer;
        public uint IndexCount;
        public int Order;
    }

    public class Scene3DRule
    {
        public string Id = "";
        public bool Enabled;
        public int Order;
        public string DataSource = "";
        public string ItemFilter = "";
        public string MeshSource = "";
        public string TransformSource = "";
        public string ColorSource = "";
    }

    public static class Scene3D
    {
        private static List<Scene3DRule> _rules = new();
        public static List<DrawCommand3D> Commands { get; private set; } = new();

        private static float _vpX = 220f, _vpY = 75f, _vpW = 840f, _vpH = 570f;

        public static void Load()
        {
            string path = "3d Editor/Scene3D.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Scene3D] Missing {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            var headers = lines[0].Split(',');

            int idxId = Array.IndexOf(headers, "id");
            int idxEnabled = Array.IndexOf(headers, "enabled");
            int idxOrder = Array.IndexOf(headers, "order");
            int idxDataSource = Array.IndexOf(headers, "data_source");
            int idxFilter = Array.IndexOf(headers, "item_filter");
            int idxMesh = Array.IndexOf(headers, "mesh_source");
            int idxTransform = Array.IndexOf(headers, "transform_source");
            int idxColor = Array.IndexOf(headers, "color_source");

            _rules.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var p = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < p.Length ? p[idx].Trim() : "";

                _rules.Add(new Scene3DRule
                {
                    Id = Get(idxId),
                    Enabled = Get(idxEnabled).ToLower() == "true",
                    Order = int.TryParse(Get(idxOrder), out var o) ? o : 0,
                    DataSource = Get(idxDataSource),
                    ItemFilter = Get(idxFilter),
                    MeshSource = Get(idxMesh),
                    TransformSource = Get(idxTransform),
                    ColorSource = Get(idxColor)
                });
            }

            _rules.Sort((a, b) => a.Order.CompareTo(b.Order));
            Console.WriteLine($"[Scene3D] Loaded {_rules.Count} rules");
        }

        // Get optional component or default
        private static T GetComponentOrDefault<T>(ECS.Entity entity) where T : struct, IComponent
        {
            return ECS.HasComponent<T>(entity) ? ECS.GetComponent<T>(entity) : default;
        }

        // Compute model matrix from available components
        private static Matrix4x4 ComputeModelMatrix(ECS.Entity entity)
        {
            var pos = GetComponentOrDefault<Position>(entity);
            var scaleComp = GetComponentOrDefault<Scale>(entity);
            var rotComp = GetComponentOrDefault<Rotation>(entity);

            Vector3 translation = new Vector3(pos.X, pos.Y, pos.Z);
            float scale = scaleComp.Equals(default(Scale)) ? 1.0f : scaleComp.Value;
            Vector3 rotation = rotComp.Equals(default(Rotation)) ? Vector3.Zero : new Vector3(rotComp.X, rotComp.Y, rotComp.Z);

            Matrix4x4 rotMatrix = Matrix4x4.CreateFromYawPitchRoll(
                rotation.Y * MathF.PI / 180f,
                rotation.X * MathF.PI / 180f,
                rotation.Z * MathF.PI / 180f);

            return Matrix4x4.CreateScale(scale) * rotMatrix * Matrix4x4.CreateTranslation(translation);
        }

        // Get active camera (first Camera component found)
        private static Camera GetActiveCamera()
        {
            foreach (var camEntity in ECS.Query<Camera>())
                return ECS.GetComponent<Camera>(camEntity);
            return default;
        }

        // Compute view matrix from camera component
        private static Matrix4x4 ComputeViewMatrix(Camera cam)
        {
            Vector3 camPos = new Vector3(cam.PosX, cam.PosY, cam.PosZ);
            Vector3 camLook = new Vector3(cam.PivotX, cam.PivotY, cam.PivotZ);
            return Matrix4x4.CreateLookAt(camPos, camLook, Vector3.UnitY);
        }

        // Compute projection matrix from camera component
        private static Matrix4x4 ComputeProjectionMatrix(Camera cam)
        {
            float aspect = _vpW / _vpH;
            float fovRad = cam.Fov * MathF.PI / 180f;
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(fovRad, aspect, cam.Near, cam.Far);
            proj.M22 *= -1f;
            return proj;
        }

        public static void Update()
        {
            Commands.Clear();

            Camera activeCam = GetActiveCamera();
            if (activeCam.Equals(default(Camera)))
            {
                Console.WriteLine("[Scene3D] No active camera found!");
                return;
            }

            Matrix4x4 view = ComputeViewMatrix(activeCam);
            Matrix4x4 proj = ComputeProjectionMatrix(activeCam);

            foreach (var rule in _rules)
            {
                if (!rule.Enabled) continue;

                if (rule.DataSource == "ecs")
                {
                    int entityCount = 0;
                    foreach (var entity in ECS.Query<Position>())
                    {
                        if (!ECS.HasComponent<Mesh>(entity)) continue;
                        entityCount++;

                        var mesh = ECS.GetComponent<Mesh>(entity);

                        if (!string.IsNullOrEmpty(rule.ItemFilter))
                        {
                            if (rule.ItemFilter.StartsWith("Layer="))
                            {
                                if (!int.TryParse(rule.ItemFilter.Substring(6), out int layer) || mesh.Layer != layer)
                                    continue;
                            }
                        }

                        if (!MeshBuffer.Get(mesh.MeshId, out var vbuf, out var ibuf, out uint idxCount))
                        {
                            if (!MeshBuffer.Get("cube", out vbuf, out ibuf, out idxCount))
                                continue;
                        }

                        Matrix4x4 model = ComputeModelMatrix(entity);
                        Matrix4x4 mvp = model * view * proj;

                        var colorComp = GetComponentOrDefault<Color>(entity);
                        Vector4 color = colorComp.Equals(default(Color))
                            ? new Vector4(1, 1, 1, 1)
                            : new Vector4(colorComp.R, colorComp.G, colorComp.B, 1f);

                        Commands.Add(new DrawCommand3D
                        {
                            Transform = mvp,
                            Color = color,
                            PipelineId = string.IsNullOrEmpty(mesh.PipelineId) ? "mesh_pipeline" : mesh.PipelineId,
                            VertexBuffer = vbuf,
                            IndexBuffer = ibuf,
                            IndexCount = idxCount,
                            Order = rule.Order
                        });
                    }
                    if (entityCount > 0)
                        Console.WriteLine($"[Scene3D] Found {entityCount} entities with Position and Mesh");
                }
            }

            Commands.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
