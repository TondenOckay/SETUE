using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Silk.NET.Vulkan;
using SETUE.ECS;
using SETUE.RenderEngine;
using SETUE.Systems;
using static SETUE.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace SETUE.Scene
{
    public class DrawCommand2D
    {
        public Matrix4x4 Transform;
        public Vector4 Color;
        public string PipelineId = "";
        public VkBuffer VertexBuffer;
        public VkBuffer IndexBuffer;
        public uint IndexCount;
        public int Order;
        public bool IsText;
        public string FontId = "";
    }

    public class Scene2DRule
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

    public static class Scene2D
    {
        private static List<Scene2DRule> _rules = new();
        public static List<DrawCommand2D> Commands { get; private set; } = new();

        public static void Load()
        {
            string path = "Ui/Scene2D.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Scene2D] Missing {path}");
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

                _rules.Add(new Scene2DRule
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
            Console.WriteLine($"[Scene2D] Loaded {_rules.Count} rules");
        }

        public static void Update()
        {
            float sw = SwapExtent.Width;
            float sh = SwapExtent.Height;
            Commands.Clear();

            var world = Object.ECSWorld;

            foreach (var rule in _rules)
            {
                if (!rule.Enabled) continue;

                switch (rule.DataSource)
                {
                    case "panels":
                        foreach (var (e, transform, panel) in world.Query<TransformComponent, PanelComponent>())
                        {
                            if (!panel.Visible) continue;
                            var material = world.GetComponent<MaterialComponent>(e);

                            float left   = transform.Position.X - transform.Scale.X * 0.5f;
                            float top    = transform.Position.Y - transform.Scale.Y * 0.5f;
                            float width  = transform.Scale.X;
                            float height = transform.Scale.Y;

                            // Original NDC mapping (no Y flip)
                            float x = (left / sw) * 2f - 1f;
                            float y = (top / sh) * 2f - 1f;
                            float w = (width / sw) * 2f;
                            float h = (height / sh) * 2f;
                            float cx = x + w * 0.5f;
                            float cy = y + h * 0.5f;

                            Matrix4x4 panelTransform = Matrix4x4.CreateScale(w, h, 1f) *
                                                       Matrix4x4.CreateTranslation(cx, cy, 0f);

                            string meshId = rule.MeshSource.StartsWith("fixed:") ? rule.MeshSource.Substring(6) : rule.MeshSource;
                            if (MeshBuffer.Get(meshId, out var vbuf, out var ibuf, out uint idxCount))
                            {
                                Commands.Add(new DrawCommand2D
                                {
                                    Transform = panelTransform,
                                    Color = material.Color,
                                    PipelineId = material.PipelineId,
                                    VertexBuffer = vbuf,
                                    IndexBuffer = ibuf,
                                    IndexCount = idxCount,
                                    Order = rule.Order,
                                    IsText = false
                                });
                            }
                            else
                            {
                                Console.WriteLine($"[Scene2D] Mesh '{meshId}' not found for panel '{panel.Id}'");
                            }
                        }
                        break;

                    case "texts":
                        foreach (var (e, text, transform) in world.Query<TextComponent, TransformComponent>())
                        {
                            string fontId = string.IsNullOrEmpty(text.FontId) ? "default" : text.FontId;
                            var font = SETUE.UI.Fonts.Get(fontId);
                            if (font == null) continue;

                            BuildTextBuffers(text, transform, font, sw, sh, out var vbuf, out var ibuf, out uint idxCount);
                            if (idxCount > 0)
                            {
                                Commands.Add(new DrawCommand2D
                                {
                                    Transform = Matrix4x4.Identity,
                                    Color = text.Color,
                                    PipelineId = "text_pipeline",
                                    VertexBuffer = vbuf,
                                    IndexBuffer = ibuf,
                                    IndexCount = idxCount,
                                    Order = rule.Order + text.Layer,
                                    IsText = true,
                                    FontId = fontId
                                });
                            }
                        }
                        break;
                }
            }

            Commands.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        private static void BuildTextBuffers(TextComponent text, TransformComponent transform, SETUE.UI.Font font, float sw, float sh,
            out VkBuffer vbuf, out VkBuffer ibuf, out uint indexCount)
        {
            vbuf = default; ibuf = default; indexCount = 0;
            if (string.IsNullOrEmpty(text.Content)) return;

            List<float> verts = new();
            List<uint> indices = new();
            uint idxOffset = 0;

            float startX = transform.Position.X;
            float startY = transform.Position.Y;

            float totalWidth = 0f;
            foreach (char c in text.Content)
                if (font.Glyphs.TryGetValue(c, out var g)) totalWidth += g.AdvanceX;

            float penX = startX;
            if (text.Align == "center")
                penX -= totalWidth * 0.5f;
            else if (text.Align == "right")
                penX -= totalWidth;

            float cosR = MathF.Cos(text.Rotation * MathF.PI / 180f);
            float sinR = MathF.Sin(text.Rotation * MathF.PI / 180f);

            foreach (char c in text.Content)
            {
                if (!font.Glyphs.TryGetValue(c, out var glyph))
                {
                    penX += 8f;
                    continue;
                }

                float x0 = penX;
                float y0 = startY - font.Ascent;
                float x1 = x0 + glyph.Width;
                float y1 = y0 + glyph.Height;

                float rx0, ry0, rx1, ry1, rx2, ry2, rx3, ry3;
                if (text.Rotation == 0)
                {
                    rx0 = x0; ry0 = y0;
                    rx1 = x1; ry1 = y0;
                    rx2 = x1; ry2 = y1;
                    rx3 = x0; ry3 = y1;
                }
                else
                {
                    (rx0, ry0) = RotatePoint(x0, y0, startX, startY, cosR, sinR);
                    (rx1, ry1) = RotatePoint(x1, y0, startX, startY, cosR, sinR);
                    (rx2, ry2) = RotatePoint(x1, y1, startX, startY, cosR, sinR);
                    (rx3, ry3) = RotatePoint(x0, y1, startX, startY, cosR, sinR);
                }

                // Original NDC conversion (no Y flip)
                float nx0 = (rx0 / sw) * 2f - 1f;
                float ny0 = (ry0 / sh) * 2f - 1f;
                float nx1 = (rx1 / sw) * 2f - 1f;
                float ny1 = (ry1 / sh) * 2f - 1f;
                float nx2 = (rx2 / sw) * 2f - 1f;
                float ny2 = (ry2 / sh) * 2f - 1f;
                float nx3 = (rx3 / sw) * 2f - 1f;
                float ny3 = (ry3 / sh) * 2f - 1f;

                // Vertex format: vec2 position, vec2 texCoord (4 floats per vertex)
                verts.AddRange(new[] {
                    nx0, ny0, glyph.U0, glyph.V0,
                    nx1, ny1, glyph.U1, glyph.V0,
                    nx2, ny2, glyph.U1, glyph.V1,
                    nx0, ny0, glyph.U0, glyph.V0,
                    nx2, ny2, glyph.U1, glyph.V1,
                    nx3, ny3, glyph.U0, glyph.V1
                });

                indices.AddRange(new[] {
                    idxOffset, idxOffset+1, idxOffset+2,
                    idxOffset+3, idxOffset+4, idxOffset+5
                });
                idxOffset += 6;

                penX += glyph.AdvanceX;
            }

            if (verts.Count == 0) return;

            ulong vSize = (ulong)(verts.Count * sizeof(float));
            ulong iSize = (ulong)(indices.Count * sizeof(uint));
            Vulkan.CreateBuffer(vSize, BufferUsageFlags.VertexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out vbuf, out var vmem);
            Vulkan.CreateBuffer(iSize, BufferUsageFlags.IndexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out ibuf, out var imem);

            unsafe
            {
                void* mapped;
                Vulkan.VK.MapMemory(Vulkan.Device, vmem, 0, vSize, 0, &mapped);
                fixed (float* src = verts.ToArray())
                    System.Buffer.MemoryCopy(src, mapped, (long)vSize, (long)vSize);
                Vulkan.VK.UnmapMemory(Vulkan.Device, vmem);

                Vulkan.VK.MapMemory(Vulkan.Device, imem, 0, iSize, 0, &mapped);
                fixed (uint* src = indices.ToArray())
                    System.Buffer.MemoryCopy(src, mapped, (long)iSize, (long)iSize);
                Vulkan.VK.UnmapMemory(Vulkan.Device, imem);
            }

            indexCount = (uint)indices.Count;
            Vulkan.DeferFree(vbuf, vmem);
            Vulkan.DeferFree(ibuf, imem);
        }

        private static (float, float) RotatePoint(float x, float y, float cx, float cy, float cosR, float sinR)
        {
            float dx = x - cx;
            float dy = y - cy;
            return (cx + dx * cosR - dy * sinR, cy + dx * sinR + dy * cosR);
        }
    }
}
