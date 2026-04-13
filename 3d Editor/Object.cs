using System;
using System.IO;
using System.Linq;
using System.Numerics;
using SETUE.ECS;
using SETUE.RenderEngine;

namespace SETUE
{
    public static class Object  // Renamed from ObjectLoader
    {
        public static World ECSWorld = new World();

        public static void Load()
        {
            string path = "3d Editor/Object.csv";
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path);
            bool first = true;
            foreach (var line in lines)
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 17) continue;

                string id = parts[0].Trim();
                string meshId = parts[1].Trim();
                Vector3 pos = new Vector3(
                    float.Parse(parts[2]), float.Parse(parts[3]), float.Parse(parts[4]));
                float size = float.Parse(parts[5]);
                Vector3 pivot = new Vector3(
                    float.Parse(parts[6]), float.Parse(parts[7]), float.Parse(parts[8]));
                Vector3 rot = new Vector3(
                    float.Parse(parts[9]), float.Parse(parts[10]), float.Parse(parts[11]));
                Vector3 color = new Vector3(
                    float.Parse(parts[12]), float.Parse(parts[13]), float.Parse(parts[14]));
                bool visible = bool.Parse(parts[15]);
                int layer = int.Parse(parts[16]);
                string pipelineId = parts.Length > 17 ? parts[17].Trim() : "mesh_pipeline_back";
                string parent = parts.Length > 18 ? parts[18].Trim() : "";

                if (!MeshBuffer.GetMeshData(meshId, out var meshData))
                {
                    Console.WriteLine($"[Object] Mesh '{meshId}' not found for entity {id}");
                    continue;
                }

                Entity e = ECSWorld.CreateEntity();

                Vector3 worldPos = pos + pivot;
                Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
                    rot.Y * MathF.PI / 180f,
                    rot.X * MathF.PI / 180f,
                    rot.Z * MathF.PI / 180f);
                Vector3 scale = new Vector3(size, size, size);

                ECSWorld.AddComponent(e, new TransformComponent
                {
                    Position = worldPos,
                    Rotation = rotation,
                    Scale = scale
                });

                ECSWorld.AddComponent(e, new MeshComponent
                {
                    MeshId = meshId,
                    VertexBuffer = (IntPtr)meshData.VertexBuffer.Handle,
                    IndexBuffer = (IntPtr)meshData.IndexBuffer.Handle,
                    IndexCount = meshData.IndexCount,
                    VertexCount = meshData.VertexCount,
                    VertexStride = meshData.VertexStride
                });

                ECSWorld.AddComponent(e, new MaterialComponent
                {
                    PipelineId = pipelineId,
                    Color = new Vector4(color.X, color.Y, color.Z, 1f)
                });

                ECSWorld.AddComponent(e, new LayerComponent { Layer = layer });

                Console.WriteLine($"[Object] Created entity {e} ({id}) with mesh {meshId}");
            }

            int count = ECSWorld.Query<TransformComponent, MeshComponent>().Count();
            Console.WriteLine($"[Object] Loaded {count} objects");
        }
    }
}
