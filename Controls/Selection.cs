using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.Core;
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
        public int ActionId;
    }

    public static class Selection
    {
        private static List<SelectionRule> _rules = new();

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
                    ActionId = StringRegistry.GetOrAdd(Get(G("action_id")))
                });
            }
            Console.WriteLine($"[Selection] Loaded {_rules.Count} rules");
        }

        public static void Update()
        {
            var world = Object.ECSWorld;

            foreach (var rule in _rules)
            {
                if (!Input.IsActionPressed(rule.InputAction)) continue;
                var mouse = Input.MousePos;

                if (HitTest(world, rule, mouse) && rule.ActionId != 0)
                {
                    Action.CreateRequest(rule.ActionId, mouse);

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

                string panelIdStr = StringRegistry.GetString(panel.Id);
                if (panel.Visible && panelIdStr.StartsWith(rule.HitTestValue) &&
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
