using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Silk.NET.Vulkan;
using SETUE.Core;
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
        public int Layer;
        public bool IsText;
        public string FontId = "";

        // Scissor clipping
        public bool UseScissor;
        public Rect2D Scissor; // In screen coordinates (pixels)
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

        // Store panel clip rectangles keyed by panel ID
        private static Dictionary<int, Rect2D> _panelClipRects = new();

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
            _panelClipRects.Clear();

            var world = Object.ECSWorld;

            // First pass: compute clip rectangles for all panels that act as containers
            ComputePanelClipRects(world, sw, sh);

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

                            float x = (left / sw) * 2f - 1f;
                            float y = 1f - (top / sh) * 2f;
                            float w = (width / sw) * 2f;
                            float h = (height / sh) * 2f;

                            float cx = x + w * 0.5f;
                            float cy = y - h * 0.5f;

                            Matrix4x4 panelTransform =
                                Matrix4x4.CreateScale(w, -h, 1f) *
                                Matrix4x4.CreateTranslation(cx, cy, 0f);

                            string meshId = rule.MeshSource.StartsWith("fixed:") ? rule.MeshSource[6..] : rule.MeshSource;
                            if (MeshBuffer.Get(meshId, out var vbuf, out var ibuf, out uint idxCount))
                            {
                                // Get clip rect for this panel (if it has a parent container)
                                Rect2D? clipRect = GetClipRectForPanel(world, panel.Id);

                                Commands.Add(new DrawCommand2D
                                {
                                    Transform = panelTransform,
                                    Color = material.Color,
                                    PipelineId = StringRegistry.GetString(material.PipelineId),
                                    VertexBuffer = vbuf,
                                    IndexBuffer = ibuf,
                                    IndexCount = idxCount,
                                    Layer = panel.Layer,
                                    IsText = false,
                                    UseScissor = clipRect.HasValue,
                                    Scissor = clipRect ?? default
                                });
                            }
                            else
                            {
                                Console.WriteLine($"[Scene2D] Mesh '{meshId}' not found for panel '{StringRegistry.GetString(panel.Id)}'");
                            }
                        }
                        break;

                    case "texts":
                        foreach (var (e, text, transform) in world.Query<TextComponent, TransformComponent>())
                        {
                            // Skip text whose parent panel is not visible
                            if (text.PanelId != 0)
                            {
                                bool panelVisible = false;
                                world.ForEach<PanelComponent>((Entity pe) =>
                                {
                                    var p = world.GetComponent<PanelComponent>(pe);
                                    if (p.Id == text.PanelId && p.Visible)
                                        panelVisible = true;
                                });
                                if (!panelVisible) continue;
                            }

                            string fontIdStr = StringRegistry.GetString(text.FontId);
                            if (string.IsNullOrEmpty(fontIdStr)) fontIdStr = "default";
                            var font = SETUE.UI.Fonts.Get(fontIdStr);
                            if (font == null) continue;

                            BuildTextBuffers(text, transform, font, sw, sh, out var vbuf, out var ibuf, out uint idxCount);
                            if (idxCount > 0)
                            {
                                // Text inherits clip rect from its parent panel
                                Rect2D? clipRect = text.PanelId != 0 ? GetClipRectForPanel(world, text.PanelId) : null;

                                Commands.Add(new DrawCommand2D
                                {
                                    Transform = Matrix4x4.Identity,
                                    Color = text.Color,
                                    PipelineId = "text_pipeline",
                                    VertexBuffer = vbuf,
                                    IndexBuffer = ibuf,
                                    IndexCount = idxCount,
                                    Layer = text.Layer,
                                    IsText = true,
                                    FontId = fontIdStr,
                                    UseScissor = clipRect.HasValue,
                                    Scissor = clipRect ?? default
                                });
                            }
                        }
                        break;
                }
            }

            // Sort exclusively by Layer (ascending)
            Commands.Sort((a, b) => a.Layer.CompareTo(b.Layer));
        }

        // ---------------------------------------------------------------------
        // Clip Rectangle Management
        // ---------------------------------------------------------------------
        private static void ComputePanelClipRects(World world, float screenWidth, float screenHeight)
        {
            // Identify container panels that should clip their children.
            // In this UI, "left_panel", "right_panel", and popups act as containers.
            // For simplicity, we'll treat any panel with children as a clipping container.
            // Alternatively, you can hardcode IDs or add a "clip_children" flag to Panel.csv.

            HashSet<int> containerIds = new HashSet<int>();
            world.ForEach<DragComponent>((Entity e) =>
            {
                var drag = world.GetComponent<DragComponent>(e);
                if (drag.ParentNameId != 0)
                    containerIds.Add(drag.ParentNameId);
            });

            // Also include known containers from Panel.csv (left_panel, right_panel, popups)
            containerIds.Add(StringRegistry.GetOrAdd("left_panel"));
            containerIds.Add(StringRegistry.GetOrAdd("right_panel"));
            containerIds.Add(StringRegistry.GetOrAdd("header_file_menu"));
            containerIds.Add(StringRegistry.GetOrAdd("header_edit_menu"));
            containerIds.Add(StringRegistry.GetOrAdd("header_tools_menu"));

            foreach (var containerId in containerIds)
            {
                world.ForEach<PanelComponent>((Entity e) =>
                {
                    var panel = world.GetComponent<PanelComponent>(e);
                    if (panel.Id == containerId && panel.Visible)
                    {
                        var trans = world.GetComponent<TransformComponent>(e);
                        float left   = trans.Position.X - trans.Scale.X * 0.5f;
                        float top    = trans.Position.Y - trans.Scale.Y * 0.5f;
                        float width  = trans.Scale.X;
                        float height = trans.Scale.Y;

                        // Convert to Vulkan screen coordinates (Y=0 at top, Y=height at bottom)
                        int scissorX = (int)Math.Max(0, left);
                        int scissorY = (int)Math.Max(0, top);
                        int scissorW = (int)Math.Min(width, screenWidth - scissorX);
                        int scissorH = (int)Math.Min(height, screenHeight - scissorY);

                        if (scissorW > 0 && scissorH > 0)
                        {
                            _panelClipRects[containerId] = new Rect2D(
                                new Offset2D(scissorX, scissorY),
                                new Extent2D((uint)scissorW, (uint)scissorH));
                        }
                    }
                });
            }
        }

        private static Rect2D? GetClipRectForPanel(World world, int panelId)
        {
            // If the panel itself is a container, use its own clip rect
            if (_panelClipRects.TryGetValue(panelId, out var rect))
                return rect;

            // Otherwise, find the nearest ancestor that has a clip rect
            // (This requires traversing the DragComponent parent links)
            int currentId = panelId;
            HashSet<int> visited = new();
            while (currentId != 0 && !visited.Contains(currentId))
            {
                visited.Add(currentId);
                if (_panelClipRects.TryGetValue(currentId, out rect))
                    return rect;

                // Move to parent
                int parentId = 0;
                world.ForEach<DragComponent>((Entity e) =>
                {
                    var drag = world.GetComponent<DragComponent>(e);
                    var panel = world.GetComponent<PanelComponent>(e);
                    if (panel.Id == currentId && drag.ParentNameId != 0)
                        parentId = drag.ParentNameId;
                });
                currentId = parentId;
            }

            return null;
        }

        // ---------------------------------------------------------------------
        // Text Buffer Building (unchanged)
        // ---------------------------------------------------------------------
        private static void BuildTextBuffers(TextComponent text, TransformComponent transform, SETUE.UI.Font font, float sw, float sh,
            out VkBuffer vbuf, out VkBuffer ibuf, out uint indexCount)
        {
            vbuf = default; ibuf = default; indexCount = 0;
            string content = StringRegistry.GetString(text.ContentId);
            if (string.IsNullOrEmpty(content)) return;

            List<float> verts = new();
            List<uint> indices = new();
            uint idxOffset = 0;

            float startX = transform.Position.X;
            float startY = transform.Position.Y;

            float totalWidth = 0f;
            foreach (char c in content)
                if (font.Glyphs.TryGetValue(c, out var g))
                    totalWidth += g.AdvanceX;

            float ascent = font.Ascent;
            float descent = 0f;
            var descentProp = font.GetType().GetProperty("Descent");
            if (descentProp != null)
                descent = (float)descentProp.GetValue(font);
            else
                descent = ascent * 0.25f;

            float visualHeight = ascent + descent;
            float visualTop = -ascent;
            float visualBottom = descent;

            float centerOffsetX = 0f;
            float centerOffsetY = (visualTop + visualBottom) * 0.5f;

            string alignStr = StringRegistry.GetString(text.Align);
            if (alignStr == "left")
                centerOffsetX = totalWidth * 0.5f;
            else if (alignStr == "center")
                centerOffsetX = 0f;
            else if (alignStr == "right")
                centerOffsetX = -totalWidth * 0.5f;

            float pivotX = startX + centerOffsetX;
            float pivotY = startY + centerOffsetY;

            float baseX = pivotX - totalWidth * 0.5f;
            float baseY = pivotY - centerOffsetY;

            float penX = baseX;

            float cosR = MathF.Cos(text.Rotation * MathF.PI / 180f);
            float sinR = MathF.Sin(text.Rotation * MathF.PI / 180f);

            foreach (char c in content)
            {
                if (!font.Glyphs.TryGetValue(c, out var glyph))
                {
                    penX += 8f;
                    continue;
                }

                float x0 = penX;
                float y0 = baseY - ascent;
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
                    (rx0, ry0) = RotatePoint(x0, y0, pivotX, pivotY, cosR, sinR);
                    (rx1, ry1) = RotatePoint(x1, y0, pivotX, pivotY, cosR, sinR);
                    (rx2, ry2) = RotatePoint(x1, y1, pivotX, pivotY, cosR, sinR);
                    (rx3, ry3) = RotatePoint(x0, y1, pivotX, pivotY, cosR, sinR);
                }

                float nx0 = (rx0 / sw) * 2f - 1f;
                float ny0 = 1f - (ry0 / sh) * 2f;
                float nx1 = (rx1 / sw) * 2f - 1f;
                float ny1 = 1f - (ry1 / sh) * 2f;
                float nx2 = (rx2 / sw) * 2f - 1f;
                float ny2 = 1f - (ry2 / sh) * 2f;
                float nx3 = (rx3 / sw) * 2f - 1f;
                float ny3 = 1f - (ry3 / sh) * 2f;

                verts.AddRange(new[] {
                    nx0, ny0, 0f, glyph.U0, glyph.V0, 0f, 0f, 0f,
                    nx1, ny1, 0f, glyph.U1, glyph.V0, 0f, 0f, 0f,
                    nx2, ny2, 0f, glyph.U1, glyph.V1, 0f, 0f, 0f,
                    nx0, ny0, 0f, glyph.U0, glyph.V0, 0f, 0f, 0f,
                    nx2, ny2, 0f, glyph.U1, glyph.V1, 0f, 0f, 0f,
                    nx3, ny3, 0f, glyph.U0, glyph.V1, 0f, 0f, 0f
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
