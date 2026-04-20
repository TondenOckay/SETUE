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

        private static bool _verboseHitTest = true;   // <-- ENABLED FOR DIAGNOSTICS

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
                    Console.WriteLine($"[Selection] Loaded rule: {rule.Id} -> {rule.InputAction} (op='{rule.OnClickOperation}', action='{Get(G("action_id"))}')");
                }
            }
            Console.WriteLine($"[Selection] Loaded {_rules.Count} rules");
        }

        public static void Update()
        {
            var world = Object.ECSWorld;
            Vector2 mouse = Input.MousePos;

            // Hover detection (only for panels with text)
            Entity? newHovered = null;
            foreach (var e in world.Query<TransformComponent>())
            {
                if (!world.HasComponent<PanelComponent>(e)) continue;
                var panel = world.GetComponent<PanelComponent>(e);
                if (!panel.Visible || !panel.Clickable) continue;
                if (panel.TextId == 0) continue;

                var trans = world.GetComponent<TransformComponent>(e);
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

            // Click handling
            if (!Input.IsActionPressed("select_object"))
                return;

            Console.WriteLine($"[Selection] Left click at ({mouse.X:F0},{mouse.Y:F0})");

            Entity? clickedEntity = null;
            SelectionRule? matchedRule = null;
            int hitPanelId = 0;

            foreach (var rule in _rules)
            {
                hitPanelId = HitTest(world, rule, mouse);
                if (hitPanelId != 0)
                {
                    world.ForEach<PanelComponent>((Entity e) =>
                    {
                        var p = world.GetComponent<PanelComponent>(e);
                        if (p.Id == hitPanelId) clickedEntity = e;
                    });
                    matchedRule = rule;
                    break;
                }
            }

            if (!clickedEntity.HasValue || matchedRule == null)
            {
                Console.WriteLine("[Selection] Clicked empty space.");
                CloseOpenDropdown(world);
                ClearSelectedEntity(world);
                return;
            }

            string panelIdStr = StringRegistry.GetString(hitPanelId);
            string actionName = matchedRule.ActionId != 0 ? StringRegistry.GetString(matchedRule.ActionId) : "";
            Console.WriteLine($"[Selection] Clicked panel '{panelIdStr}' with op '{matchedRule.OnClickOperation}', action '{actionName}'");

            bool handled = false;

            switch (matchedRule.OnClickOperation.ToLower())
            {
                case "start_drag":
                    Movement.StartDrag(hitPanelId, mouse, actionName);
                    handled = true;
                    break;

                case "select":
                    SetSelectedEntity(world, clickedEntity.Value);
                    CloseOpenDropdown(world);
                    handled = true;
                    break;

                case "toggle_visibility":
                    if (IsDropdownPanel(hitPanelId))
                    {
                        ToggleDropdown(world, hitPanelId);
                    }
                    else
                    {
                        Panels.ToggleVisibility(hitPanelId, mouse);
                    }
                    handled = true;
                    break;

                case "":
                    if (!string.IsNullOrEmpty(actionName) && IsDropdownPanel(StringRegistry.GetOrAdd(actionName)))
                    {
                        ToggleDropdown(world, StringRegistry.GetOrAdd(actionName));
                        handled = true;
                    }
                    else if (IsDropdownPanel(hitPanelId))
                    {
                        ToggleDropdown(world, hitPanelId);
                        handled = true;
                    }
                    break;
            }

            if (!handled)
            {
                if (actionName.Contains('.'))
                {
                    ExecuteMethod(actionName, matchedRule.ActionId, mouse);
                    CloseOpenDropdown(world);
                }
                else if (Movement.RuleExists(actionName))
                {
                    Movement.StartDrag(hitPanelId, mouse, actionName);
                }
                else if (!string.IsNullOrEmpty(actionName))
                {
                    int targetPanelId = StringRegistry.GetOrAdd(actionName);
                    if (IsDropdownPanel(targetPanelId))
                        ToggleDropdown(world, targetPanelId);
                    else
                        Panels.ToggleVisibility(targetPanelId, mouse);
                }
            }

            if (matchedRule.ConsumeInput)
            {
                Input.Consume(matchedRule.InputAction);
                Console.WriteLine($"[Selection] Consumed input: {matchedRule.InputAction}");
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

        private static int HitTest(World world, SelectionRule rule, Vector2 mouse)
        {
            if (rule.HitTestType != "panel_prefix") return 0;

            foreach (var e in world.Query<TransformComponent>())
            {
                if (!world.HasComponent<PanelComponent>(e)) continue;
                var trans = world.GetComponent<TransformComponent>(e);
                var panel = world.GetComponent<PanelComponent>(e);
                if (!panel.Visible || !panel.Clickable) continue;
                if (!StringRegistry.GetString(panel.Id).StartsWith(rule.HitTestValue)) continue;

                float left   = trans.Position.X - trans.Scale.X * 0.5f;
                float right  = trans.Position.X + trans.Scale.X * 0.5f;
                float top    = trans.Position.Y - trans.Scale.Y * 0.5f;
                float bottom = trans.Position.Y + trans.Scale.Y * 0.5f;

                if (_verboseHitTest)
                {
                    Console.WriteLine($"[Selection] HitTest rule '{rule.Id}' vs panel '{StringRegistry.GetString(panel.Id)}': bounds=({left:F0},{top:F0})-({right:F0},{bottom:F0}) mouse=({mouse.X:F0},{mouse.Y:F0})");
                }

                if (mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom)
                {
                    if (_verboseHitTest) Console.WriteLine($"[Selection]   -> HIT");
                    return panel.Id;
                }
            }
            return 0;
        }
    }
}
