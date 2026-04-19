using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;
using SETUE.Controls;
using SETUE.Systems;

namespace SETUE.UI
{
    public static class SceneTree
    {
        public class SceneTreeRow
        {
            public string Target { get; set; } = "";
            public string RowPanelId { get; set; } = "";
            public string RowTextId { get; set; } = "";
            public string RowColorId { get; set; } = "";
            public string RowSelectedColorId { get; set; } = "";
            public float IndentPerLevel { get; set; } = 20f;
            public int RowPanelIdResolved { get; set; }
            public int RowTextIdResolved { get; set; }
            public int RowColorIdResolved { get; set; }
            public int RowSelectedColorIdResolved { get; set; }
        }

        private static Dictionary<string, SceneTreeRow> _styleLookup = new();
        private static Entity? _containerEntity;
        private static TransformComponent _containerTransform;
        private static PanelComponent _containerPanel;
        private static Entity? _sceneRoot;

        private class RowData
        {
            public Entity PanelEntity;
            public Entity TextEntity;
            public Entity TargetEntity;
            public SceneTreeRow Style = null!;
            public bool LastSelectedState;
            public int Depth;
        }

        private static readonly Dictionary<Entity, RowData> _rows = new();
        private static int _containerPanelId;
        private static float _rowHeight = 24f;
        private static float _paddingX = 8f;
        private static float _paddingY = 4f;

        private class TextStyle
        {
            public int FontId;
            public float FontSize;
            public Vector4 Color;
            public int Align;
            public int VAlign;
        }
        private static Dictionary<int, TextStyle> _textStyles = new();

        // ---------------------------------------------------------------------
        // Initialization
        // ---------------------------------------------------------------------
        public static void Load()
        {
            Console.WriteLine("[SceneTree] ========== Load() ==========");
            _containerPanelId = StringRegistry.GetOrAdd("left_panel");
            Console.WriteLine($"[SceneTree] Container panel ID: {_containerPanelId} ('left_panel')");

            LoadTextStyles();
            LoadStyles("Ui/SceneTree.csv");
        }

        private static void LoadTextStyles()
        {
            int textStyleId = StringRegistry.GetOrAdd("scene_tree_text");
            _textStyles[textStyleId] = new TextStyle
            {
                FontId = StringRegistry.GetOrAdd("default"),
                FontSize = 14f,
                Color = new Vector4(1, 1, 1, 1),
                Align = StringRegistry.GetOrAdd("left"),
                VAlign = StringRegistry.GetOrAdd("middle")
            };
            Console.WriteLine("[SceneTree] Loaded text style 'scene_tree_text'");
        }

        private static void LoadStyles(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                Console.WriteLine($"[SceneTree] ERROR: Missing {csvPath}");
                return;
            }

            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                colIndex[headers[i].Trim()] = i;

            string Get(string colName, string[] parts) =>
                colIndex.TryGetValue(colName, out int idx) && idx < parts.Length ? parts[idx].Trim() : "";

            _styleLookup.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');

                string target = Get("target", parts);
                if (string.IsNullOrEmpty(target)) continue;

                var row = new SceneTreeRow
                {
                    Target = target,
                    RowPanelId = Get("row_panel_id", parts),
                    RowTextId = Get("row_text_id", parts),
                    RowColorId = Get("row_color_id", parts),
                    RowSelectedColorId = Get("row_selected_color_id", parts),
                    IndentPerLevel = float.TryParse(Get("indent", parts), out var indent) ? indent : 20f
                };

                row.RowPanelIdResolved = string.IsNullOrEmpty(row.RowPanelId) ? 0 : StringRegistry.GetOrAdd(row.RowPanelId);
                row.RowTextIdResolved = string.IsNullOrEmpty(row.RowTextId) ? 0 : StringRegistry.GetOrAdd(row.RowTextId);
                row.RowColorIdResolved = string.IsNullOrEmpty(row.RowColorId) ? 0 : StringRegistry.GetOrAdd(row.RowColorId);
                row.RowSelectedColorIdResolved = string.IsNullOrEmpty(row.RowSelectedColorId) ? 0 : StringRegistry.GetOrAdd(row.RowSelectedColorId);

                _styleLookup[target] = row;
                Console.WriteLine($"[SceneTree] Loaded style for '{target}': panel={row.RowPanelId}");
            }

            Console.WriteLine($"[SceneTree] Loaded {_styleLookup.Count} row styles.");
        }

        public static SceneTreeRow? GetStyleForType(string entityType)
        {
            _styleLookup.TryGetValue(entityType, out var style);
            if (style == null) _styleLookup.TryGetValue("Object", out style);
            return style;
        }

        // ---------------------------------------------------------------------
        // Main Update
        // ---------------------------------------------------------------------
        public static void Update()
        {
            var world = Object.ECSWorld;

            if (!TryGetContainer(world))
            {
                Console.WriteLine("[SceneTree] Container not found, skipping update.");
                return;
            }

            EnsureSceneRoot(world);
            world.ExecuteCommands();

            var hierarchy = BuildHierarchy(world);
            Console.WriteLine($"[SceneTree] Hierarchy contains {hierarchy.Count} entities.");

            SyncRows(world, hierarchy);
            UpdateRows(world, hierarchy);
        }

        // ---------------------------------------------------------------------
        // Container & Scene Root
        // ---------------------------------------------------------------------
        private static bool TryGetContainer(World world)
        {
            if (_containerEntity.HasValue &&
                world.HasComponent<PanelComponent>(_containerEntity.Value) &&
                world.HasComponent<TransformComponent>(_containerEntity.Value))
            {
                var panel = world.GetComponent<PanelComponent>(_containerEntity.Value);
                if (panel.Id == _containerPanelId)
                {
                    _containerTransform = world.GetComponent<TransformComponent>(_containerEntity.Value);
                    _containerPanel = panel;
                    return true;
                }
            }

            _containerEntity = null;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var panel = world.GetComponent<PanelComponent>(e);
                if (panel.Id == _containerPanelId)
                {
                    _containerEntity = e;
                    _containerTransform = world.GetComponent<TransformComponent>(e);
                    _containerPanel = panel;
                    Console.WriteLine($"[SceneTree] Found container entity {e.Index} for 'left_panel'");
                }
            });

            if (_containerEntity == null)
                Console.WriteLine("[SceneTree] ERROR: No entity with PanelComponent.Id == 'left_panel' found in ECS.");

            return _containerEntity.HasValue;
        }

        private static void EnsureSceneRoot(World world)
        {
            if (_sceneRoot.HasValue && world.HasComponent<SceneRootComponent>(_sceneRoot.Value))
                return;

            _sceneRoot = null;
            world.ForEach<SceneRootComponent>((Entity e) => _sceneRoot = e);

            if (!_sceneRoot.HasValue)
            {
                _sceneRoot = world.CreateEntity();
                world.AddComponent(_sceneRoot.Value, new SceneRootComponent());
                world.AddComponent(_sceneRoot.Value, new TransformComponent
                {
                    Position = Vector3.Zero,
                    Scale = Vector3.One,
                    Rotation = Quaternion.Identity
                });
                world.AddComponent(_sceneRoot.Value, new NameComponent { NameId = StringRegistry.GetOrAdd("Scene") });
                Console.WriteLine($"[SceneTree] Created Scene root entity {_sceneRoot.Value.Index}.");
            }
        }

        private static Dictionary<Entity, int> BuildHierarchy(World world)
        {
            var result = new Dictionary<Entity, int>();
            if (!_sceneRoot.HasValue) return result;

            var queue = new Queue<(Entity entity, int depth)>();
            queue.Enqueue((_sceneRoot.Value, 0));

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                result[current] = depth;

                world.ForEach<ParentComponent>((Entity child) =>
                {
                    var p = world.GetComponent<ParentComponent>(child);
                    if (p.Parent == current)
                        queue.Enqueue((child, depth + 1));
                });
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // Row Management
        // ---------------------------------------------------------------------
        private static void SyncRows(World world, Dictionary<Entity, int> hierarchy)
        {
            var toRemove = new List<Entity>();
            foreach (var kv in _rows)
                if (!hierarchy.ContainsKey(kv.Key))
                    toRemove.Add(kv.Key);
            foreach (var e in toRemove)
            {
                DestroyRow(world, _rows[e]);
                _rows.Remove(e);
            }

            foreach (var (entity, depth) in hierarchy)
            {
                if (!_rows.ContainsKey(entity))
                {
                    Console.WriteLine($"[SceneTree] Creating row for entity {entity.Index} at depth {depth}");
                    CreateRow(world, entity, depth);
                }
            }
        }

        private static void CreateRow(World world, Entity targetEntity, int depth)
        {
            string typeName = GetEntityTypeName(world, targetEntity);
            var style = GetStyleForType(typeName) ?? GetStyleForType("Object");
            if (style == null)
            {
                Console.WriteLine("[SceneTree] ERROR: No fallback style, cannot create row.");
                return;
            }

            float rowHeight = _rowHeight;
            Vector4 rowColor = new Vector4(0.18f, 0.18f, 0.24f, 1f);

            if (style.RowPanelIdResolved != 0)
            {
                var template = Panels.GetPanel(style.RowPanelIdResolved);
                if (template != null)
                {
                    rowHeight = template.Height;
                    rowColor = template.Color;
                }
            }

            if (style.RowColorIdResolved != 0)
                rowColor = GetColorFromId(style.RowColorIdResolved);

            float containerWidth = _containerTransform.Scale.X;
            float rowWidth = containerWidth - _paddingX * 2f;

            // --- Create row panel ---
            Entity panelEntity = world.CreateEntity();
            world.AddComponent(panelEntity, new TransformComponent
            {
                Position = Vector3.Zero,
                Scale = new Vector3(rowWidth, rowHeight, 1),
                Rotation = Quaternion.Identity
            });
            world.AddComponent(panelEntity, new PanelComponent
            {
                Id = StringRegistry.GetOrAdd($"_st_row_{targetEntity.Index}"),
                Visible = _containerPanel.Visible,
                Layer = _containerPanel.Layer + 1,
                Alpha = 1f,
                Clickable = true,
                TextId = 0
            });
            world.AddComponent(panelEntity, new MaterialComponent
            {
                PipelineId = StringRegistry.GetOrAdd("rect_pipeline"),
                Color = rowColor
            });

            // --- Create text entity with truncated content ---
            Entity textEntity = world.CreateEntity();
            world.AddComponent(textEntity, new TransformComponent
            {
                Position = Vector3.Zero,
                Scale = Vector3.One,
                Rotation = Quaternion.Identity
            });

            int textStyleId = style.RowTextIdResolved != 0 ? style.RowTextIdResolved : StringRegistry.GetOrAdd("scene_tree_text");
            if (!_textStyles.TryGetValue(textStyleId, out var textStyle))
                textStyle = _textStyles.Values.FirstOrDefault();

            // Get the full display name and truncate to fit available width
            string fullName = GetDisplayName(targetEntity);
            float indent = _paddingX + depth * style.IndentPerLevel;
            float availableTextWidth = rowWidth - indent - _paddingX; // Reserve right padding

            string displayText = TruncateTextToWidth(fullName, availableTextWidth, textStyle);
            int contentId = StringRegistry.GetOrAdd(displayText);

            Console.WriteLine($"[SceneTree]   Text content: '{displayText}' (original: '{fullName}')");

            world.AddComponent(textEntity, new TextComponent
            {
                Id = StringRegistry.GetOrAdd($"_st_txt_{targetEntity.Index}"),
                ContentId = contentId,
                FontId = textStyle?.FontId ?? StringRegistry.GetOrAdd("default"),
                FontSize = textStyle?.FontSize ?? 14f,
                Color = textStyle?.Color ?? new Vector4(1, 1, 1, 1),
                Align = textStyle?.Align ?? StringRegistry.GetOrAdd("left"),
                VAlign = textStyle?.VAlign ?? StringRegistry.GetOrAdd("middle"),
                Layer = _containerPanel.Layer + 2,
                PanelId = 0
            });

            world.ExecuteCommands();

            _rows[targetEntity] = new RowData
            {
                PanelEntity = panelEntity,
                TextEntity = textEntity,
                TargetEntity = targetEntity,
                Style = style,
                LastSelectedState = world.HasComponent<SelectedComponent>(targetEntity),
                Depth = depth
            };

            Console.WriteLine($"[SceneTree]   CREATED ROW: {typeName} width={rowWidth} height={rowHeight}");
        }

        // Helper to get the raw display name string
        private static string GetDisplayName(Entity entity)
        {
            var world = Object.ECSWorld;
            if (world.HasComponent<NameComponent>(entity))
            {
                int id = world.GetComponent<NameComponent>(entity).NameId;
                string name = StringRegistry.GetString(id);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            return $"Entity_{entity.Index}";
        }

        // Truncate string to fit within maxWidth pixels using font metrics
        private static string TruncateTextToWidth(string text, float maxWidth, TextStyle style)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0)
                return text;

            string fontIdStr = StringRegistry.GetString(style.FontId);
            var font = SETUE.UI.Fonts.Get(fontIdStr);
            if (font == null)
                return text;

            float currentWidth = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                float advance = font.Glyphs.TryGetValue(c, out var g) ? g.AdvanceX : 8f;
                if (currentWidth + advance > maxWidth)
                {
                    // Add ellipsis
                    string ellipsis = "...";
                    float ellipsisWidth = 0f;
                    foreach (char ec in ellipsis)
                        ellipsisWidth += font.Glyphs.TryGetValue(ec, out var eg) ? eg.AdvanceX : 8f;

                    // Ensure we have room for ellipsis
                    if (maxWidth < ellipsisWidth)
                        return ""; // Not enough space even for ellipsis

                    // Build truncated string with ellipsis
                    return text.Substring(0, i) + ellipsis;
                }
                currentWidth += advance;
            }
            return text;
        }

        private static void DestroyRow(World world, RowData row)
        {
            world.DestroyEntity(row.PanelEntity);
            world.DestroyEntity(row.TextEntity);
        }

        private static void UpdateRows(World world, Dictionary<Entity, int> hierarchy)
        {
            float containerLeft   = _containerTransform.Position.X - _containerTransform.Scale.X * 0.5f;
            float containerTop    = _containerTransform.Position.Y - _containerTransform.Scale.Y * 0.5f;
            float containerWidth  = _containerTransform.Scale.X;
            float containerHeight = _containerTransform.Scale.Y;

            float y = containerTop + _paddingY;

            var orderedEntities = hierarchy.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key.Index).Select(kv => kv.Key).ToList();

            foreach (var entity in orderedEntities)
            {
                if (!_rows.TryGetValue(entity, out var row))
                    continue;

                bool isSelected = world.HasComponent<SelectedComponent>(entity);
                var panelTrans = world.GetComponent<TransformComponent>(row.PanelEntity);
                var material = world.GetComponent<MaterialComponent>(row.PanelEntity);
                var textComp = world.GetComponent<TextComponent>(row.TextEntity);
                var textTrans = world.GetComponent<TransformComponent>(row.TextEntity);

                float rowHeight = panelTrans.Scale.Y;
                float rowWidth = containerWidth - _paddingX * 2f;

                // Position the row panel
                panelTrans.Position = new Vector3(
                    containerLeft + _paddingX + rowWidth * 0.5f,
                    y + rowHeight * 0.5f,
                    0f
                );
                panelTrans.Scale = new Vector3(rowWidth, rowHeight, 1f);
                world.SetComponent(row.PanelEntity, panelTrans);

                // Update selection color
                if (isSelected != row.LastSelectedState)
                {
                    Vector4 normalColor = row.Style.RowColorIdResolved != 0
                        ? GetColorFromId(row.Style.RowColorIdResolved)
                        : material.Color;
                    Vector4 selectedColor = row.Style.RowSelectedColorIdResolved != 0
                        ? GetColorFromId(row.Style.RowSelectedColorIdResolved)
                        : new Vector4(0.3f, 0.5f, 0.8f, 1f);
                    material.Color = isSelected ? selectedColor : normalColor;
                    world.SetComponent(row.PanelEntity, material);
                    row.LastSelectedState = isSelected;
                }

                // --- Proper vertical centering using font metrics ---
                float textY = y + rowHeight * 0.5f; // fallback
                string fontIdStr = StringRegistry.GetString(textComp.FontId);
                var font = SETUE.UI.Fonts.Get(fontIdStr);
                if (font != null)
                {
                    float ascent = font.Ascent;
                    float descent = 0f;
                    var descentProp = font.GetType().GetProperty("Descent");
                    if (descentProp != null)
                        descent = (float)descentProp.GetValue(font);
                    else
                        descent = ascent * 0.25f;

                    // The text's baseline is at y=0 in local coordinates.
                    // Visual top = -ascent, visual bottom = descent.
                    // To center the text vertically within the row, we shift the baseline so that
                    // the midpoint of the visual extent aligns with the row's center.
                    float visualMid = (-ascent + descent) * 0.5f;
                    textY = y + rowHeight * 0.5f - visualMid;
                }

                float indent = _paddingX + row.Depth * row.Style.IndentPerLevel;
                textTrans.Position = new Vector3(
                    containerLeft + indent,
                    textY,
                    0f
                );
                world.SetComponent(row.TextEntity, textTrans);

                y += rowHeight;

                if (y + rowHeight > containerTop + containerHeight - _paddingY)
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------
        private static string GetEntityTypeName(World world, Entity e)
        {
            if (world.HasComponent<SceneRootComponent>(e)) return "Scene";
            if (world.HasComponent<CameraComponent>(e)) return "Camera";
            if (world.HasComponent<LightComponent>(e)) return "Light";
            if (world.HasComponent<TerrainComponent>(e)) return "Terrain";
            if (world.HasComponent<MeshComponent>(e)) return "Object";
            if (world.HasComponent<ParentComponent>(e)) return "Parent";
            return "Object";
        }

        private static Vector4 GetColorFromId(int colorId)
        {
            string colorName = StringRegistry.GetString(colorId);
            var c = Colors.Get(colorName);
            return new Vector4(c.R, c.G, c.B, c.Alpha);
        }

        private static bool HitTestRow(Entity panelEntity, Vector2 mouse)
        {
            var world = Object.ECSWorld;
            if (!world.HasComponent<TransformComponent>(panelEntity)) return false;
            var trans = world.GetComponent<TransformComponent>(panelEntity);
            float left   = trans.Position.X - trans.Scale.X * 0.5f;
            float right  = trans.Position.X + trans.Scale.X * 0.5f;
            float top    = trans.Position.Y - trans.Scale.Y * 0.5f;
            float bottom = trans.Position.Y + trans.Scale.Y * 0.5f;
            return mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom;
        }
    }
}
