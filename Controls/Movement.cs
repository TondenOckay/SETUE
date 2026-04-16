using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SETUE.Controls
{
    public enum AxisConstraint { None, X, Y, Z, XY, XZ, YZ }

    public class MovementRule
    {
        public string Id = "";
        public AxisConstraint AxisConstraint;
        public bool SnapEnabled;
        public float SnapValue;
        public float Sensitivity = 1.0f;
    }

    public static class Movement
    {
        private static Dictionary<string, MovementRule> _rules = new();

        public static void Load()
        {
            string path = "Controls/Movement.csv";
            _rules.Clear();
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Split(',');
                if (p.Length < 5) continue;
                _rules[p[0]] = new MovementRule
                {
                    Id = p[0],
                    AxisConstraint = Enum.TryParse<AxisConstraint>(p[1], out var ax) ? ax : AxisConstraint.X,
                    SnapEnabled = p[2].ToLower() == "true",
                    SnapValue = float.TryParse(p[3], out var sv) ? sv : 1f,
                    Sensitivity = float.TryParse(p[4], out var sens) ? sens : 1f
                };
            }
            Console.WriteLine($"[Movement] Loaded {_rules.Count} rules");
        }

        public static Vector3 CalculateDelta(string ruleId, float rawDeltaX, float rawDeltaY)
        {
            if (!_rules.TryGetValue(ruleId, out var rule))
                return new Vector3(rawDeltaX, 0, 0);   // Safe fallback: X only

            float amountX = rawDeltaX * rule.Sensitivity;
            float amountY = rawDeltaY * rule.Sensitivity;

            if (rule.SnapEnabled && rule.SnapValue > 0)
            {
                amountX = MathF.Round(amountX / rule.SnapValue) * rule.SnapValue;
                amountY = MathF.Round(amountY / rule.SnapValue) * rule.SnapValue;
            }

            return rule.AxisConstraint switch
            {
                AxisConstraint.X => new Vector3(amountX, 0, 0),
                AxisConstraint.Y => new Vector3(0, amountY, 0),
                AxisConstraint.Z => new Vector3(0, 0, amountX),
                AxisConstraint.XY => new Vector3(amountX, amountY, 0),
                AxisConstraint.XZ => new Vector3(amountX, 0, amountX),
                AxisConstraint.YZ => new Vector3(0, amountY, amountY),
                _ => new Vector3(amountX, 0, 0)   // Fallback to X only
            };
        }

        public static bool IsXAxisMovement(string ruleId)
        {
            if (_rules.TryGetValue(ruleId, out var rule))
            {
                return rule.AxisConstraint == AxisConstraint.X ||
                       rule.AxisConstraint == AxisConstraint.XY ||
                       rule.AxisConstraint == AxisConstraint.XZ;
            }
            return true; // Default
        }
    }
}
