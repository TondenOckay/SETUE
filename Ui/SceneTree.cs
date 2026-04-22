using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.UI
{
    public static class SceneTree
    {
        private static Dictionary<string, string> _rowTemplateMap = new();
        private static Dictionary<string, int> _menuTemplateIdMap = new();

        private static Entity? _containerEntity;
        private static TransformComponent _containerTransform;
        private static Entity? _sceneRoot;
        private static int _containerPanelId;
        private static float _paddingX;
        private static float _paddingY;

        private class RowData
        {
            public Entity PanelEntity;
            public Entity TargetEntity;
            public int Depth;
        }
        private static readonly Dictionary<Entity, RowData> _rows = new();

        // Active context menu (reuses template + children)
        private static Entity? _activeMenuContainer;
        private static List<Entity> _activeMenuItems = new();
        private static Dictionary<Entity, Vector3> _originalPositions = new();
        private static Entity? _pendingRenameEntity;

        // ---------------------------------------------------------------------
        // Initialization
        // ---------------------------------------------------------------------
        public static void Load()
        {
            Console.WriteLine("[SceneTree] ========== Load() ==========");
            _containerPanelId = StringRegistry.GetOrAdd("left_panel");
            LoadSettings("Ui/SceneTree.csv");
            LoadRowTemplates("Ui/SceneTree.csv");
            BuildMenuTemplateMap();
            HideAllTemplates();
        }

        private static void LoadSettings(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[SceneTree] ERROR: Missing {path}");
                return;
            }
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            int idxKey = Array.IndexOf(headers, "key");
            int idxValue = Array.IndexOf(headers, "value");

            if (idxKey < 0 || idxValue < 0) return;

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length <= Math.Max(idxKey, idxValue)) continue;
                string key = parts[idxKey].Trim();
                string val = parts[idxValue].Trim();

                if (key == "padding_x" && float.TryParse(val, out float px))
                    _paddingX = px;
                else if (key == "padding_y" && float.TryParse(val, out float py))
                    _paddingY = py;
            }
            Console.WriteLine($"[SceneTree] Padding: X={_paddingX}, Y={_paddingY}");
        }

        private static void LoadRowTemplates(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[SceneTree] ERROR: Missing {path}");
                return;
            }
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            int idxTarget = Array.IndexOf(headers, "target");
            int idxTemplate = Array.IndexOf(headers, "row_panel_id");

            _rowTemplateMap.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length <= Math.Max(idxTarget, idxTemplate)) continue;
                string target = parts[idxTarget].Trim();
                string template = parts[idxTemplate].Trim();
                if (!string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(template))
                {
                    _rowTemplateMap[target] = template;
                    Console.WriteLine($"[SceneTree] Row template: {target} -> {template}");
                }
            }
            Console.WriteLine($"[SceneTree] Loaded {_rowTemplateMap.Count} row templates.");
        }

        private static void BuildMenuTemplateMap()
        {
            _menuTemplateIdMap["Scene"]   = StringRegistry.GetOrAdd("scene_menu_Scene");
            _menuTemplateIdMap["Camera"]  = StringRegistry.GetOrAdd("scene_menu_Camera");
            _menuTemplateIdMap["Light"]   = StringRegistry.GetOrAdd("scene_menu_Light");
            _menuTemplateIdMap["Terrain"] = StringRegistry.GetOrAdd("scene_menu_Terrain");
            _menuTemplateIdMap["Object"]  = StringRegistry.GetOrAdd("scene_menu_Object");
            _menuTemplateIdMap["Parent"]  = StringRegistry.GetOrAdd("scene_menu_Parent");
        }

        private static void HideAllTemplates()
        {
            var world = Object.ECSWorld;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                string id = StringRegistry.GetString(p.Id);
                if (id.StartsWith("_st_template_") || id.StartsWith("scene_menu_"))
                {
                    p.Visible = false;
                    world.SetComponent(e, p);
                }
            });
            world.ExecuteCommands();
            Console.WriteLine("[SceneTree] All templates hidden.");
        }

        // ---------------------------------------------------------------------
        // Main Update
        // ---------------------------------------------------------------------
        public static void Update()
        {
            var world = Object.ECSWorld;
            if (!TryGetContainer(world)) return;
            EnsureSceneRoot(world);
            world.ExecuteCommands();
            var hierarchy = BuildHierarchy(world);
            SyncRows(world, hierarchy);
            UpdateRows(world, hierarchy);
        }

        private static bool TryGetContainer(World world)
        {
            if (_containerEntity.HasValue &&
                world.HasComponent<PanelComponent>(_containerEntity.Value) &&
                world.HasComponent<TransformComponent>(_containerEntity.Value))
            {
                var p = world.GetComponent<PanelComponent>(_containerEntity.Value);
                if (p.Id == _containerPanelId)
                {
                    _containerTransform = world.GetComponent<TransformComponent>(_containerEntity.Value);
                    return true;
                }
            }
            _containerEntity = null;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (p.Id == _containerPanelId)
                {
                    _containerEntity = e;
                    _containerTransform = world.GetComponent<TransformComponent>(e);
                }
            });
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
                world.AddComponent(_sceneRoot.Value, new TransformComponent { Position = Vector3.Zero, Scale = Vector3.One });
                world.AddComponent(_sceneRoot.Value, new NameComponent { NameId = StringRegistry.GetOrAdd("Scene") });
                Console.WriteLine($"[SceneTree] Created Scene root entity {_sceneRoot.Value.Index}.");
            }
        }

        private static Dictionary<Entity, int> BuildHierarchy(World world)
        {
            var result = new Dictionary<Entity, int>();
            if (!_sceneRoot.HasValue) return result;
            var queue = new Queue<(Entity, int)>();
            queue.Enqueue((_sceneRoot.Value, 0));
            while (queue.Count > 0)
            {
                var (cur, depth) = queue.Dequeue();
                result[cur] = depth;
                world.ForEach<ParentComponent>((Entity child) =>
                {
                    var p = world.GetComponent<ParentComponent>(child);
                    if (p.Parent == cur) queue.Enqueue((child, depth + 1));
                });
            }
            return result;
        }

        private static void SyncRows(World world, Dictionary<Entity, int> hierarchy)
        {
            var toRemove = _rows.Keys.Where(e => !hierarchy.ContainsKey(e)).ToList();
            foreach (var e in toRemove)
            {
                world.DestroyEntity(_rows[e].PanelEntity);
                _rows.Remove(e);
            }
            foreach (var (e, depth) in hierarchy)
            {
                if (e == _sceneRoot) continue;
                if (!_rows.ContainsKey(e))
                    CreateRow(world, e, depth);
            }
        }

        private static void CreateRow(World world, Entity target, int depth)
        {
            string type = GetEntityTypeName(world, target);
            if (!_rowTemplateMap.TryGetValue(type, out string? tmpl) &&
                !_rowTemplateMap.TryGetValue("Object", out tmpl))
            {
                Console.WriteLine($"[SceneTree] No row template for type '{type}' or 'Object'");
                return;
            }

            Entity? template = FindPanelById(world, StringRegistry.GetOrAdd(tmpl));
            if (!template.HasValue) return;

            Entity row = ClonePanel(world, template.Value, target);
            if (row.Index == 0) return;

            var drag = world.HasComponent<DragComponent>(row) ? world.GetComponent<DragComponent>(row) : new DragComponent();
            drag.ParentNameId = _containerPanelId;
            world.SetComponent(row, drag);

            _rows[target] = new RowData { PanelEntity = row, TargetEntity = target, Depth = depth };
        }

        private static Entity? FindPanelById(World world, int id)
        {
            Entity? found = null;
            world.ForEach<PanelComponent>((Entity e) => { if (world.GetComponent<PanelComponent>(e).Id == id) found = e; });
            return found;
        }

        private static Entity ClonePanel(World world, Entity template, Entity target)
        {
            var tTrans = world.GetComponent<TransformComponent>(template);
            var tPanel = world.GetComponent<PanelComponent>(template);
            var tMat   = world.GetComponent<MaterialComponent>(template);

            float w = _containerTransform.Scale.X - _paddingX * 2f;
            float h = tTrans.Scale.Y;

            int newId = StringRegistry.GetOrAdd($"_st_{target.Index}");
            Entity e = world.CreateEntity();
            world.AddComponent(e, new TransformComponent { Position = Vector3.Zero, Scale = new Vector3(w, h, 1) });
            world.AddComponent(e, new PanelComponent
            {
                Id = newId,
                Visible = true,
                Layer = tPanel.Layer,
                Clickable = tPanel.Clickable,
                TextId = tPanel.TextId,
                Alpha = tPanel.Alpha,
                ClipChildren = tPanel.ClipChildren
            });
            world.AddComponent(e, new MaterialComponent
            {
                PipelineId = tMat.PipelineId,
                Color = tMat.Color
            });

            if (world.HasComponent<TextComponent>(template))
            {
                var txt = world.GetComponent<TextComponent>(template);
                string name = GetDisplayName(target);
                world.AddComponent(e, new TextComponent
                {
                    Id = StringRegistry.GetOrAdd($"_st_txt_{target.Index}"),
                    ContentId = StringRegistry.GetOrAdd(name),
                    FontId = txt.FontId,
                    FontSize = txt.FontSize,
                    Color = txt.Color,
                    Align = txt.Align,
                    VAlign = txt.VAlign,
                    Layer = txt.Layer,
                    PanelId = newId,
                    PadLeft = txt.PadLeft,
                    PadTop = txt.PadTop,
                    LineHeight = txt.LineHeight,
                    Rotation = txt.Rotation,
                    Source = txt.Source,
                    Prefix = txt.Prefix,
                    StyleId = txt.StyleId
                });
            }
            return e;
        }

        private static void UpdateRows(World world, Dictionary<Entity, int> hierarchy)
        {
            float left = _containerTransform.Position.X - _containerTransform.Scale.X * 0.5f;
            float top  = _containerTransform.Position.Y - _containerTransform.Scale.Y * 0.5f;
            float y = 104 + _paddingY;

            foreach (var e in hierarchy.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key.Index).Select(kv => kv.Key))
            {
                if (e == _sceneRoot || !_rows.TryGetValue(e, out var row)) continue;
                var trans = world.GetComponent<TransformComponent>(row.PanelEntity);
                float h = trans.Scale.Y;
                trans.Position = new Vector3(left + _paddingX + (_containerTransform.Scale.X - _paddingX * 2f) * 0.5f, y + h * 0.5f, 0);
                world.SetComponent(row.PanelEntity, trans);
                y += h;
                if (y + h > top + _containerTransform.Scale.Y - _paddingY) break;
            }
        }

        private static string GetEntityTypeName(World world, Entity e)
        {
            if (world.HasComponent<SceneRootComponent>(e)) return "Scene";
            if (world.HasComponent<CameraComponent>(e))   return "Camera";
            if (world.HasComponent<LightComponent>(e))    return "Light";
            if (world.HasComponent<TerrainComponent>(e))  return "Terrain";
            if (world.HasComponent<MeshComponent>(e))     return "Object";
            return "Object";
        }

        private static string GetDisplayName(Entity e)
        {
            var world = Object.ECSWorld;
            if (world.HasComponent<NameComponent>(e))
                return StringRegistry.GetString(world.GetComponent<NameComponent>(e).NameId);
            return $"Entity_{e.Index}";
        }

        // ---------------------------------------------------------------------
        // Context Menu – Reuses template, shifts all parts together
        // ---------------------------------------------------------------------
        public static void ShowContextMenu(World world, int clickedPanelId, Vector2 mousePos)
        {
            Console.WriteLine($"[SceneTree] ShowContextMenu: clicked={StringRegistry.GetString(clickedPanelId)} mouse=({mousePos.X:F0},{mousePos.Y:F0})");

            Entity? target = null;
            if (clickedPanelId == StringRegistry.GetOrAdd("_st_Scene"))
                target = _sceneRoot;
            else
                foreach (var kv in _rows)
                    if (world.GetComponent<PanelComponent>(kv.Value.PanelEntity).Id == clickedPanelId)
                        target = kv.Key;

            if (!target.HasValue) return;

            string type = GetEntityTypeName(world, target.Value);
            if (!_menuTemplateIdMap.TryGetValue(type, out int templateId)) return;

            Entity? template = FindPanelById(world, templateId);
            if (!template.HasValue) return;

            // Hide any currently open menu (restores original positions)
            HideContextMenu(world);

            // Use the template entity directly
            _activeMenuContainer = template.Value;
            int containerPanelId = world.GetComponent<PanelComponent>(_activeMenuContainer.Value).Id;

            // Collect all menu items (children of this container)
            _activeMenuItems.Clear();
            world.ForEach<PanelComponent>((Entity e) =>
            {
                if (world.HasComponent<DragComponent>(e))
                {
                    var drag = world.GetComponent<DragComponent>(e);
                    if (drag.ParentNameId == containerPanelId)
                    {
                        _activeMenuItems.Add(e);
                    }
                }
            });

            // Store original positions of container and all children
            _originalPositions.Clear();
            var containerTrans = world.GetComponent<TransformComponent>(_activeMenuContainer.Value);
            _originalPositions[_activeMenuContainer.Value] = containerTrans.Position;
            foreach (var item in _activeMenuItems)
            {
                var itemTrans = world.GetComponent<TransformComponent>(item);
                _originalPositions[item] = itemTrans.Position;
            }

            // Calculate desired top-left position (mouse + small offset)
            float menuWidth  = containerTrans.Scale.X;
            float menuHeight = containerTrans.Scale.Y;
            float desiredX = mousePos.X + 5f;
            float desiredY = mousePos.Y + 5f;

            // Clamp to screen
            float sw = Vulkan.SwapExtent.Width;
            float sh = Vulkan.SwapExtent.Height;
            if (desiredX + menuWidth > sw) desiredX = sw - menuWidth;
            if (desiredY + menuHeight > sh) desiredY = sh - menuHeight;
            if (desiredX < 0) desiredX = 0;
            if (desiredY < 0) desiredY = 0;

            // Compute offset from current container top-left to desired top-left
            float currentLeft = containerTrans.Position.X - menuWidth * 0.5f;
            float currentTop  = containerTrans.Position.Y - menuHeight * 0.5f;
            float offsetX = desiredX - currentLeft;
            float offsetY = desiredY - currentTop;

            // Apply offset to container and all children
            containerTrans.Position += new Vector3(offsetX, offsetY, 0);
            world.SetComponent(_activeMenuContainer.Value, containerTrans);

            foreach (var item in _activeMenuItems)
            {
                var itemTrans = world.GetComponent<TransformComponent>(item);
                itemTrans.Position += new Vector3(offsetX, offsetY, 0);
                world.SetComponent(item, itemTrans);
            }

            // Make container and children visible
            var panel = world.GetComponent<PanelComponent>(_activeMenuContainer.Value);
            panel.Visible = true;
            world.SetComponent(_activeMenuContainer.Value, panel);

            foreach (var item in _activeMenuItems)
            {
                var childPanel = world.GetComponent<PanelComponent>(item);
                childPanel.Visible = true;
                world.SetComponent(item, childPanel);
            }

            world.ExecuteCommands();
            Console.WriteLine($"[SceneTree] Menu '{StringRegistry.GetString(panel.Id)}' shown at ({desiredX:F0},{desiredY:F0})");
        }

        public static void HideContextMenu(World world)
        {
            if (_activeMenuContainer.HasValue)
            {
                // Restore original positions
                foreach (var kv in _originalPositions)
                {
                    if (world.HasComponent<TransformComponent>(kv.Key))
                    {
                        var trans = world.GetComponent<TransformComponent>(kv.Key);
                        trans.Position = kv.Value;
                        world.SetComponent(kv.Key, trans);
                    }
                }

                // Hide container
                var panel = world.GetComponent<PanelComponent>(_activeMenuContainer.Value);
                panel.Visible = false;
                world.SetComponent(_activeMenuContainer.Value, panel);

                // Hide children
                foreach (var item in _activeMenuItems)
                {
                    if (world.HasComponent<PanelComponent>(item))
                    {
                        var childPanel = world.GetComponent<PanelComponent>(item);
                        childPanel.Visible = false;
                        world.SetComponent(item, childPanel);
                    }
                }

                _activeMenuContainer = null;
                _activeMenuItems.Clear();
                _originalPositions.Clear();
                world.ExecuteCommands();
                Console.WriteLine("[SceneTree] Context menu hidden and original positions restored.");
            }
        }

        // ---------------------------------------------------------------------
        // Action Methods
        // ---------------------------------------------------------------------
        public static void RenameSelected(int panelId, Vector2 mousePos)
        {
            var world = Object.ECSWorld;
            Entity? target = null;
            if (panelId == StringRegistry.GetOrAdd("_st_Scene")) target = _sceneRoot;
            else foreach (var kv in _rows) if (world.GetComponent<PanelComponent>(kv.Value.PanelEntity).Id == panelId) target = kv.Key;
            if (!target.HasValue) return;

            string curName = GetDisplayName(target.Value);
            SETUE.Controls.Input.StartEdit(curName, $"Rename {curName}");
            _pendingRenameEntity = target;
            HideContextMenu(world);
        }

        public static void DeleteSelected(int panelId, Vector2 mousePos)
        {
            var world = Object.ECSWorld;
            Entity? target = null;
            if (panelId == StringRegistry.GetOrAdd("_st_Scene")) target = _sceneRoot;
            else foreach (var kv in _rows) if (world.GetComponent<PanelComponent>(kv.Value.PanelEntity).Id == panelId) target = kv.Key;
            if (!target.HasValue || target == _sceneRoot) return;

            world.DestroyEntity(target.Value);
            world.ExecuteCommands();
            Console.WriteLine($"[SceneTree] Deleted entity {target.Value.Index}");
            HideContextMenu(world);
        }

        public static void CreateChild(int panelId, Vector2 mousePos)
        {
            Console.WriteLine($"[SceneTree] CreateChild called from panel {StringRegistry.GetString(panelId)}");
            HideContextMenu(Object.ECSWorld);
        }

        public static void OnEditConfirmed()
        {
            if (!_pendingRenameEntity.HasValue) return;
            string newName = SETUE.Controls.Input.EditBuffer;
            if (string.IsNullOrWhiteSpace(newName)) return;

            var world = Object.ECSWorld;
            if (world.HasComponent<NameComponent>(_pendingRenameEntity.Value))
            {
                var nameComp = world.GetComponent<NameComponent>(_pendingRenameEntity.Value);
                nameComp.NameId = StringRegistry.GetOrAdd(newName);
                world.SetComponent(_pendingRenameEntity.Value, nameComp);
            }
            else
            {
                world.AddComponent(_pendingRenameEntity.Value, new NameComponent { NameId = StringRegistry.GetOrAdd(newName) });
            }

            if (_rows.TryGetValue(_pendingRenameEntity.Value, out var row))
            {
                var txt = world.GetComponent<TextComponent>(row.PanelEntity);
                txt.ContentId = StringRegistry.GetOrAdd(newName);
                world.SetComponent(row.PanelEntity, txt);
            }
            _pendingRenameEntity = null;
            Console.WriteLine($"[SceneTree] Renamed to '{newName}'");
        }

        public static void OnEditCancelled()
        {
            _pendingRenameEntity = null;
            Console.WriteLine("[SceneTree] Rename cancelled.");
        }
    }
}
