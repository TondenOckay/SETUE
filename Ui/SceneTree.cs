using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.UI
{
    public static class SceneTree
    {
        private static int _panelId;
        private static float _rowHeight = 20f;
        private static float _paddingX = 10f;
        private static float _paddingY = 10f;
        private static float _rowR = 0.2f, _rowG = 0.2f, _rowB = 0.2f;
        private static float _selR = 0.3f, _selG = 0.6f, _selB = 0.9f;
        private static int _fontId;
        private static float _textR = 1f, _textG = 1f, _textB = 1f;
        private static float _indentWidth = 20f;
        private static int _rectPipelineId;
        private static int _stPrefixId;

        public static void Load()
        {
            string csvPath = "Ui/SceneTree.csv";
            if (!File.Exists(csvPath)) { Console.WriteLine($"[SceneTree] Missing {csvPath}"); return; }
            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) return;
            var headers = lines[0].Split(',');
            var vals = lines[1].Split(',');
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length && i < vals.Length; i++)
                settings[headers[i].Trim()] = vals[i].Trim();

            if (settings.TryGetValue("panel_id", out var pid)) _panelId = StringRegistry.GetOrAdd(pid);
            if (settings.TryGetValue("row_height", out var rh)) _rowHeight = float.Parse(rh);
            if (settings.TryGetValue("padding_x", out var px)) _paddingX = float.Parse(px);
            if (settings.TryGetValue("padding_y", out var py)) _paddingY = float.Parse(py);
            if (settings.TryGetValue("row_r", out var rr)) _rowR = float.Parse(rr);
            if (settings.TryGetValue("row_g", out var rg)) _rowG = float.Parse(rg);
            if (settings.TryGetValue("row_b", out var rb)) _rowB = float.Parse(rb);
            if (settings.TryGetValue("selected_r", out var sr)) _selR = float.Parse(sr);
            if (settings.TryGetValue("selected_g", out var sg)) _selG = float.Parse(sg);
            if (settings.TryGetValue("selected_b", out var sb)) _selB = float.Parse(sb);
            if (settings.TryGetValue("font_id", out var fi)) _fontId = StringRegistry.GetOrAdd(fi);
            if (settings.TryGetValue("text_r", out var txr)) _textR = float.Parse(txr);
            if (settings.TryGetValue("text_g", out var txg)) _textG = float.Parse(txg);
            if (settings.TryGetValue("text_b", out var txb)) _textB = float.Parse(txb);
            if (settings.TryGetValue("indent_width", out var iw)) _indentWidth = float.Parse(iw);

            _rectPipelineId = StringRegistry.GetOrAdd("rect_pipeline");
            _stPrefixId = StringRegistry.GetOrAdd("_st_");

            Console.WriteLine($"[SceneTree] Loaded settings panel={StringRegistry.GetString(_panelId)} rowH={_rowHeight}");
        }

        public static void Update()
        {
            if (_panelId == 0) return;

            var world = Object.ECSWorld;

            Entity? containerEntity = null;
            TransformComponent containerTransform = default;
            foreach (var (e, panel, transform) in world.Query<PanelComponent, TransformComponent>())
            {
                if (panel.Id == _panelId)
                {
                    containerEntity = e;
                    containerTransform = transform;
                    break;
                }
            }
            if (containerEntity == null)
            {
                Console.WriteLine($"[SceneTree] Container panel '{StringRegistry.GetString(_panelId)}' not found in ECS");
                return;
            }

            float containerX = containerTransform.Position.X - containerTransform.Scale.X * 0.5f;
            float containerY = containerTransform.Position.Y - containerTransform.Scale.Y * 0.5f;
            float containerWidth = containerTransform.Scale.X;
            float containerHeight = containerTransform.Scale.Y;

            var entities = new List<Entity>();
            foreach (var (e, _, _) in world.Query<TransformComponent, MeshComponent>())
                entities.Add(e);

            var toRemovePanels = new List<Entity>();
            var toRemoveTexts = new List<Entity>();
            foreach (var e in world.Query<PanelComponent>())
            {
                var p = world.GetComponent<PanelComponent>(e);
                string idStr = StringRegistry.GetString(p.Id);
                if (idStr.StartsWith("_st_"))
                    toRemovePanels.Add(e);
            }
            foreach (var e in world.Query<TextComponent>())
            {
                var t = world.GetComponent<TextComponent>(e);
                string idStr = StringRegistry.GetString(t.Id);
                if (idStr.StartsWith("_st_txt_"))
                    toRemoveTexts.Add(e);
            }
            foreach (var e in toRemovePanels) world.DestroyEntity(e);
            foreach (var e in toRemoveTexts) world.DestroyEntity(e);

            float y = containerY + _paddingY;
            foreach (var entity in entities)
            {
                var transform = world.GetComponent<TransformComponent>(entity);
                string entityName = entity.ToString();

                int depth = 0;
                float indent = _paddingX + depth * _indentWidth;
                bool isSelected = world.HasComponent<SelectedComponent>(entity);

                string rowIdStr = $"_st_{entity.Index}";
                string txtIdStr = $"_st_txt_{entity.Index}";
                int rowId = StringRegistry.GetOrAdd(rowIdStr);
                int txtId = StringRegistry.GetOrAdd(txtIdStr);

                Entity rowEntity = world.CreateEntity();
                world.AddComponent(rowEntity, new TransformComponent
                {
                    Position = new Vector3(containerX + indent + (containerWidth - indent - _paddingX) * 0.5f, y + _rowHeight * 0.5f, 0),
                    Scale = new Vector3(containerWidth - indent - _paddingX, _rowHeight - 2f, 1),
                    Rotation = Quaternion.Identity
                });
                world.AddComponent(rowEntity, new PanelComponent
                {
                    Id = rowId,
                    Visible = true,
                    Layer = 1,
                    Alpha = 1f,
                    Clickable = true,
                    TextId = txtId
                });
                world.AddComponent(rowEntity, new MaterialComponent
                {
                    PipelineId = _rectPipelineId,
                    Color = isSelected ? new Vector4(_selR, _selG, _selB, 1f) : new Vector4(_rowR, _rowG, _rowB, 1f)
                });

                Entity textEntity = world.CreateEntity();
                world.AddComponent(textEntity, new TransformComponent
                {
                    Position = new Vector3(containerX + indent + 4f, y + _rowHeight * 0.5f, 0),
                    Scale = new Vector3(1, 1, 1),
                    Rotation = Quaternion.Identity
                });
                world.AddComponent(textEntity, new TextComponent
                {
                    Id = txtId,
                    ContentId = StringRegistry.GetOrAdd(entityName),
                    FontId = _fontId,
                    FontSize = _rowHeight * 0.6f,
                    Color = new Vector4(_textR, _textG, _textB, 1f),
                    Align = StringRegistry.GetOrAdd("left"),
                    Rotation = 0,
                    PanelId = rowId
                });

                y += _rowHeight;
                if (y + _rowHeight > containerY + containerHeight) break;
            }

            Console.WriteLine($"[SceneTree] Updated with {entities.Count} entities");
        }
    }
}
