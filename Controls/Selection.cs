using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.ECS;

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
        public string ActionId = "";
    }

    public struct DragState : IComponent
    {
        public string ActionId;
        public Vector2 StartMousePos;    // world position
    }

    public static class Selection
    {
        private static List<SelectionRule> _rules = new();
        private static Entity? _dragStateEntity;

        public static void Load()
        {
            string path = "Controls/Selection.csv";
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            int G(string n) => Array.IndexOf(headers, n);

            _rules.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Split(',');
                string Get(int idx) => idx >= 0 && idx < p.Length ? p[idx].Trim() : "";

                _rules.Add(new SelectionRule
                {
                    Id = Get(G("id")),
                    InputAction = Get(G("input_action")),
                    HitTestType = Get(G("hit_test_type")),
                    HitTestValue = Get(G("hit_test_value")),
                    OnClickOperation = Get(G("on_click_operation")),
                    ConsumeInput = Get(G("consume_input")).ToLower() == "true",
                    ActionId = Get(G("action_id"))
                });
            }
            Console.WriteLine($"[Selection] Loaded {_rules.Count} rules");
        }

        public static void Update()
        {
            var world = Object.ECSWorld;

            // Handle active drag
            if (_dragStateEntity.HasValue && world.HasComponent<DragState>(_dragStateEntity.Value))
            {
                var state = world.GetComponent<DragState>(_dragStateEntity.Value);
                if (Input.IsActionHeld("select_object"))
                {
                    Vector2 current = Input.MousePos;
                    Vector2 delta = current - state.StartMousePos;
                    Action.ProcessDrag(state.ActionId, delta);
                }
                else
                {
                    Action.EndDrag(state.ActionId);
                    world.DestroyEntity(_dragStateEntity.Value);
                    _dragStateEntity = null;
                }
                return;
            }

            // Detect new clicks
            foreach (var rule in _rules)
            {
                if (!Input.IsActionPressed(rule.InputAction)) continue;
                var mouse = Input.MousePos;

                if (HitTest(world, rule, mouse) && !string.IsNullOrEmpty(rule.ActionId))
                {
                    _dragStateEntity = world.CreateEntity();
                    world.AddComponent(_dragStateEntity.Value, new DragState
                    {
                        ActionId = rule.ActionId,
                        StartMousePos = mouse
                    });

                    if (rule.ConsumeInput)
                        Input.Consume(rule.InputAction);
                    break;
                }
            }
        }

        private static bool HitTest(World world, SelectionRule rule, Vector2 mouse)
        {
            if (rule.HitTestType != "panel_prefix") return false;

            foreach (var e in world.Query<TransformComponent>())
            {
                if (!world.HasComponent<PanelComponent>(e)) continue;
                var trans = world.GetComponent<TransformComponent>(e);
                var panel = world.GetComponent<PanelComponent>(e);

                if (panel.Visible && panel.Id.StartsWith(rule.HitTestValue) &&
                    mouse.X >= trans.Position.X - trans.Scale.X / 2 &&
                    mouse.X <= trans.Position.X + trans.Scale.X / 2 &&
                    mouse.Y >= trans.Position.Y - trans.Scale.Y / 2 &&
                    mouse.Y <= trans.Position.Y + trans.Scale.Y / 2)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
