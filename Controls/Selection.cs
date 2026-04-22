using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using SETUE.Core;
using SETUE.ECS;
using SETUE.Systems;

namespace SETUE.Controls
{
    public class SelectionRule
    {
        public string Id = "";
        public string InputAction = "";
        public string HitTestType = "";
        public string HitTestValue = "";
        public string OnClickOperation = "";
        public bool ConsumeInput;
        public int ActionId;
    }

    public static class Selection
    {
        private static List<SelectionRule> _rules = new();

        private static Entity? _hoveredEntity = null;
        private static Vector4 _originalHoverColor;
        private static bool _hoverColorStored = false;

        private static Entity? _selectedEntity = null;
        private static Vector4 _originalSelectedColor;
        private static bool _selectedColorStored = false;

        private static int _openDropdownPanelId = 0;

        private const float HOVER_BRIGHTNESS = 1.3f;
        private static readonly Vector4 SELECTED_COLOR = new Vector4(0.3f, 0.5f, 0.8f, 1.0f);

        public static bool IsMouseOverUI { get; private set; }

        public static void Load()
        {
            string path = "Controls/Selection.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Selection] File not found: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            int G(string n) => Array.IndexOf(headers, n);

            _rules.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var p = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < p.Length ? p[idx].Trim() : "";

                var rule = new SelectionRule
                {
                    Id = Get(G("id")),
                    InputAction = Get(G("input_action")),
                    HitTestType = Get(G("hit_test_type")),
                    HitTestValue = Get(G("hit_test_value")),
                    OnClickOperation = Get(G("on_click_operation")),
                    ConsumeInput = Get(G("consume_input")).ToLower() == "true",
                    ActionId = StringRegistry.GetOrAdd(Get(G("action_id")))
                };

                if (!string.IsNullOrEmpty(rule.InputAction))
                {
                    _rules.Add(rule);
                    Console.WriteLine($"[Selection] Loaded rule: {rule.Id} -> {rule.InputAction} (type='{rule.HitTestType}', val='{rule.HitTestValue}', op='{rule.OnClickOperation}')");
                }
            }
            Console.WriteLine($"[Selection] Loaded {_rules.Count} rules");
        }

        public static void Update()
        {
            var world = Object.ECSWorld;
            Vector2 mouse = Input.MousePos;

            // ===== HOVER DETECTION (respect layer order) =====
            var candidates = new List<(Entity entity, TransformComponent trans, PanelComponent panel)>();
            foreach (var e in world.Query<TransformComponent>())
            {
                if (!world.HasComponent<PanelComponent>(e)) continue;
                var panel = world.GetComponent<PanelComponent>(e);
                if (!panel.Visible || !panel.Clickable) continue;
                if (panel.TextId == 0) continue;

                var trans = world.GetComponent<TransformComponent>(e);
                candidates.Add((e, trans, panel));
            }

            candidates.Sort((a, b) => b.panel.Layer.CompareTo(a.panel.Layer));

            Entity? newHovered = null;
            foreach (var (e, trans, panel) in candidates)
            {
                float left   = trans.Position.X - trans.Scale.X * 0.5f;
                float right  = trans.Position.X + trans.Scale.X * 0.5f;
                float top    = trans.Position.Y - trans.Scale.Y * 0.5f;
                float bottom = trans.Position.Y + trans.Scale.Y * 0.5f;

                if (mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom)
                {
                    newHovered = e;
                    break;
                }
            }

            IsMouseOverUI = newHovered.HasValue;

            // Update hover highlighting
            if (newHovered != _hoveredEntity)
            {
                if (_hoveredEntity.HasValue && _hoverColorStored)
                {
                    if (world.HasComponent<MaterialComponent>(_hoveredEntity.Value) && _hoveredEntity != _selectedEntity)
                    {
                        var prevMat = world.GetComponent<MaterialComponent>(_hoveredEntity.Value);
                        prevMat.Color = _originalHoverColor;
                        world.SetComponent(_hoveredEntity.Value, prevMat);
                    }
                    _hoverColorStored = false;
                }

                if (newHovered.HasValue && newHovered != _selectedEntity)
                {
                    var mat = world.GetComponent<MaterialComponent>(newHovered.Value);
                    _originalHoverColor = mat.Color;
                    _hoverColorStored = true;

                    mat.Color = new Vector4(
                        Math.Min(mat.Color.X * HOVER_BRIGHTNESS, 1.0f),
                        Math.Min(mat.Color.Y * HOVER_BRIGHTNESS, 1.0f),
                        Math.Min(mat.Color.Z * HOVER_BRIGHTNESS, 1.0f),
                        mat.Color.W
                    );
                    world.SetComponent(newHovered.Value, mat);
                }
                _hoveredEntity = newHovered;
            }

            // Handle text editing
            if (Input.IsEditing)
            {
                if (Input.EditConfirmed)
                {
                    SETUE.UI.SceneTree.OnEditConfirmed();
                    Input.EndEdit();
                }
                else if (Input.EditCancelled)
                {
                    SETUE.UI.SceneTree.OnEditCancelled();
                    Input.EndEdit();
                }
            }

            // ===== CLICK HANDLING =====
            string pressedAction = null;
            if (Input.IsActionPressed("select_object"))
                pressedAction = "select_object";
            else if (Input.IsActionPressed("context_menu"))
                pressedAction = "context_menu";

            if (pressedAction == null)
                return;

            Console.WriteLine($"[Selection] Action pressed: {pressedAction}, mouse=({mouse.X:F0},{mouse.Y:F0})");

            // Collect all rules that match the pressed action
            var candidateRules = new List<SelectionRule>();
            foreach (var rule in _rules)
            {
                if (rule.InputAction == pressedAction)
                    candidateRules.Add(rule);
            }

            if (candidateRules.Count == 0)
            {
                Console.WriteLine("[Selection] No rules for this action.");
                return;
            }

            // Find the best hit: for each rule, get the topmost panel that satisfies it.
            // Then among those panels, pick the one with the highest layer.
            int bestPanelId = 0;
            SelectionRule? bestRule = null;
            int bestLayer = int.MinValue;

            foreach (var rule in candidateRules)
            {
                int panelId = HitTestTopmost(world, rule, mouse);
                if (panelId == 0) continue;

                // Get the layer of that panel
                int layer = int.MinValue;
                world.ForEach<PanelComponent>((Entity e) =>
                {
                    var p = world.GetComponent<PanelComponent>(e);
                    if (p.Id == panelId)
                        layer = p.Layer;
                });

                if (layer > bestLayer)
                {
                    bestLayer = layer;
                    bestPanelId = panelId;
                    bestRule = rule;
                }
            }

            if (bestPanelId == 0 || bestRule == null)
            {
                Console.WriteLine("[Selection] Clicked empty space (no rule matched).");
                CloseOpenDropdown(world);
                SETUE.UI.SceneTree.HideContextMenu(world);
                ClearSelectedEntity(world);
                return;
            }

            // Find the entity for the best panel
            Entity? clickedEntity = null;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (p.Id == bestPanelId) clickedEntity = e;
            });

            if (!clickedEntity.HasValue)
                return;

            // Consume input to prevent underlying systems
            Input.Consume(pressedAction);
            Console.WriteLine($"[Selection] Consumed input: {pressedAction} (UI panel hit)");

            string panelIdStr = StringRegistry.GetString(bestPanelId);
            string actionName = bestRule.ActionId != 0 ? StringRegistry.GetString(bestRule.ActionId) : "";
            Console.WriteLine($"[Selection] Clicked panel '{panelIdStr}' (layer {bestLayer}) with rule '{bestRule.Id}' op='{bestRule.OnClickOperation}', action='{actionName}'");

            bool handled = false;

            switch (bestRule.OnClickOperation.ToLower())
            {
                case "start_drag":
                    Movement.StartDrag(bestPanelId, mouse, actionName);
                    handled = true;
                    break;

                case "select":
                    SetSelectedEntity(world, clickedEntity.Value);
                    CloseOpenDropdown(world);
                    SETUE.UI.SceneTree.HideContextMenu(world);
                    handled = true;
                    break;

                case "context_menu":
                    SETUE.UI.SceneTree.ShowContextMenu(world, bestPanelId, mouse);
                    handled = true;
                    break;

                case "rename":
                    SETUE.UI.SceneTree.RenameSelected(bestPanelId, mouse);
                    handled = true;
                    break;

                case "delete":
                    SETUE.UI.SceneTree.DeleteSelected(bestPanelId, mouse);
                    handled = true;
                    break;

                case "create":
                    SETUE.UI.SceneTree.CreateChild(bestPanelId, mouse);
                    handled = true;
                    break;

                case "toggle_visibility":
                    if (IsDropdownPanel(bestPanelId))
                    {
                        ToggleDropdown(world, bestPanelId);
                    }
                    else
                    {
                        Panels.ToggleVisibility(bestPanelId, mouse);
                    }
                    handled = true;
                    break;
            }

            if (!handled)
            {
                if (actionName.Contains('.'))
                {
                    ExecuteMethod(actionName, bestRule.ActionId, mouse);
                    CloseOpenDropdown(world);
                    SETUE.UI.SceneTree.HideContextMenu(world);
                    handled = true;
                }
                else if (Movement.RuleExists(actionName))
                {
                    Movement.StartDrag(bestPanelId, mouse, actionName);
                    handled = true;
                }
                else if (!string.IsNullOrEmpty(actionName))
                {
                    int targetPanelId = StringRegistry.GetOrAdd(actionName);
                    if (IsDropdownPanel(targetPanelId))
                    {
                        ToggleDropdown(world, targetPanelId);
                        handled = true;
                    }
                    else
                    {
                        Panels.ToggleVisibility(targetPanelId, mouse);
                        handled = true;
                    }
                }
            }

            if (!handled && IsDropdownPanel(bestPanelId))
            {
                ToggleDropdown(world, bestPanelId);
                handled = true;
            }
        }

        private static bool IsDropdownPanel(int panelId)
        {
            string name = StringRegistry.GetString(panelId);
            return name.EndsWith("_menu") || name == "header_file_menu" || name == "header_edit_menu" || name == "header_tools_menu" || name == "scene_context_menu";
        }

        private static void ToggleDropdown(World world, int dropdownPanelId)
        {
            if (_openDropdownPanelId != 0 && _openDropdownPanelId != dropdownPanelId)
            {
                CloseDropdown(world, _openDropdownPanelId);
            }

            bool isNowOpen = Panels.ToggleVisibilityReturnState(dropdownPanelId, Vector2.Zero);
            _openDropdownPanelId = isNowOpen ? dropdownPanelId : 0;
        }

        private static void CloseDropdown(World world, int dropdownPanelId)
        {
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (p.Id == dropdownPanelId && p.Visible)
                {
                    p.Visible = false;
                    world.SetComponent(e, p);
                    Panels.SetChildrenVisibility(world, dropdownPanelId, false);
                }
            });
        }

        private static void CloseOpenDropdown(World world)
        {
            if (_openDropdownPanelId != 0)
            {
                CloseDropdown(world, _openDropdownPanelId);
                _openDropdownPanelId = 0;
            }
        }

        private static void SetSelectedEntity(World world, Entity entity)
        {
            if (_selectedEntity == entity) return;

            if (_selectedEntity.HasValue && _selectedColorStored)
            {
                if (world.HasComponent<MaterialComponent>(_selectedEntity.Value))
                {
                    var prevMat = world.GetComponent<MaterialComponent>(_selectedEntity.Value);
                    prevMat.Color = _originalSelectedColor;
                    world.SetComponent(_selectedEntity.Value, prevMat);
                }
                _selectedColorStored = false;
            }

            var mat = world.GetComponent<MaterialComponent>(entity);
            _originalSelectedColor = mat.Color;
            _selectedColorStored = true;
            mat.Color = SELECTED_COLOR;
            world.SetComponent(entity, mat);

            if (_hoveredEntity == entity && _hoverColorStored)
            {
                _hoverColorStored = false;
                _hoveredEntity = null;
            }

            _selectedEntity = entity;
        }

        private static void ClearSelectedEntity(World world)
        {
            if (_selectedEntity.HasValue && _selectedColorStored)
            {
                if (world.HasComponent<MaterialComponent>(_selectedEntity.Value))
                {
                    var prevMat = world.GetComponent<MaterialComponent>(_selectedEntity.Value);
                    prevMat.Color = _originalSelectedColor;
                    world.SetComponent(_selectedEntity.Value, prevMat);
                }
                _selectedColorStored = false;
            }
            _selectedEntity = null;
        }

        private static void ExecuteMethod(string methodName, int actionId, Vector2 mousePos)
        {
            int lastDot = methodName.LastIndexOf('.');
            if (lastDot <= 0) return;
            string typeName = methodName[..lastDot];
            string shortMethodName = methodName[(lastDot + 1)..];

            Type? type = Type.GetType(typeName) ?? Type.GetType("SETUE.Systems." + typeName) ?? Type.GetType("SETUE.UI." + typeName) ?? Type.GetType("SETUE.Controls." + typeName);
            if (type == null) return;
            MethodInfo? method = type.GetMethod(shortMethodName, BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, new object[] { actionId, mousePos });
        }

        /// <summary>
        /// Returns the panel ID of the topmost (highest layer) panel that satisfies the rule and contains the mouse.
        /// </summary>
        private static int HitTestTopmost(World world, SelectionRule rule, Vector2 mouse)
        {
            string hitType = rule.HitTestType;
            string hitValue = rule.HitTestValue;

            var matches = new List<(Entity e, TransformComponent trans, PanelComponent panel)>();

            foreach (var e in world.Query<TransformComponent>())
            {
                if (!world.HasComponent<PanelComponent>(e)) continue;
                var trans = world.GetComponent<TransformComponent>(e);
                var panel = world.GetComponent<PanelComponent>(e);
                if (!panel.Visible || !panel.Clickable) continue;

                float left   = trans.Position.X - trans.Scale.X * 0.5f;
                float right  = trans.Position.X + trans.Scale.X * 0.5f;
                float top    = trans.Position.Y - trans.Scale.Y * 0.5f;
                float bottom = trans.Position.Y + trans.Scale.Y * 0.5f;

                if (!(mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom))
                    continue;

                bool matchesRule = false;

                if (hitType == "panel_prefix")
                {
                    if (StringRegistry.GetString(panel.Id).StartsWith(hitValue))
                        matchesRule = true;
                }
                else if (!string.IsNullOrEmpty(hitType))
                {
                    int targetId = StringRegistry.GetOrAdd(hitType);
                    if (panel.Id == targetId)
                        matchesRule = true;
                }

                if (matchesRule)
                    matches.Add((e, trans, panel));
            }

            if (matches.Count == 0) return 0;

            matches.Sort((a, b) => b.panel.Layer.CompareTo(a.panel.Layer));
            return matches[0].panel.Id;
        }
    }
}
