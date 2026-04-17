using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using SDL3;
using SETUE.Core;

namespace SETUE.Controls
{
    public static class Input
    {
        private static Vector2 _mouseDeltaAccum = Vector2.Zero;
        public static Vector2 MouseDelta  => _mouseDeltaAccum;
        public static float   ScrollDelta { get; private set; }
        public static Vector2 MousePos    { get; private set; }

        static readonly Dictionary<string, List<(string input, string modifier)>> _actionBindings = new();
        static readonly HashSet<string> _actionEnabled = new();

        static readonly HashSet<string> _held    = new();
        static readonly HashSet<string> _pressed = new();
        static readonly HashSet<string> _modHeld = new();
        static readonly Dictionary<string, string> _editChar = new();

        public static bool   IsEditing     { get; private set; } = false;
        public static string EditBuffer    { get; private set; } = "";
        public static string EditSource    { get; private set; } = "";
        public static bool   EditConfirmed { get; private set; } = false;
        public static bool   EditCancelled { get; private set; } = false;

        public static bool IsCtrlHeld  => _modHeld.Contains("Ctrl");
        public static bool IsShiftHeld => _modHeld.Contains("Shift");

        public static void StartEdit(string currentValue, string source)
        {
            IsEditing     = true;
            EditBuffer    = currentValue;
            EditSource    = source;
            EditConfirmed = false;
            EditCancelled = false;
        }

        public static void EndEdit()
        {
            IsEditing     = false;
            EditBuffer    = "";
            EditSource    = "";
            EditConfirmed = false;
            EditCancelled = false;
        }

        public static void Load()
        {
            string csvPath = "Controls/Input.csv";
            _actionBindings.Clear();
            _actionEnabled.Clear();
            _editChar.Clear();

            if (!File.Exists(csvPath)) { Debug.LogError("Input", $"CSV not found: {csvPath}"); return; }
            var lines   = File.ReadAllLines(csvPath);
            var headers = lines[0].Split(',');
            int idxAction   = Array.IndexOf(headers, "action");
            int idxInput    = Array.IndexOf(headers, "input");
            int idxModifier = Array.IndexOf(headers, "modifier");
            int idxEditChar = Array.IndexOf(headers, "edit_char");
            int idxEnabled  = Array.IndexOf(headers, "enabled");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var p = line.Split(',');
                if (p.Length <= idxAction) continue;

                string action   = p[idxAction].Trim();
                string input    = idxInput    >= 0 && p.Length > idxInput    ? p[idxInput].Trim()    : "";
                string modifier = idxModifier >= 0 && p.Length > idxModifier ? p[idxModifier].Trim() : "";

                if (!_actionBindings.TryGetValue(action, out var list))
                {
                    list = new List<(string, string)>();
                    _actionBindings[action] = list;
                }
                list.Add((input, modifier));

                if (idxEditChar >= 0 && p.Length > idxEditChar && !string.IsNullOrEmpty(p[idxEditChar].Trim()))
                    _editChar[action] = p[idxEditChar].Trim();

                if (idxEnabled >= 0 && p.Length > idxEnabled && p[idxEnabled].Trim().ToLower() == "true")
                    _actionEnabled.Add(action);
            }

            int totalBindings = 0;
            foreach (var kv in _actionBindings) totalBindings += kv.Value.Count;
            Debug.LogLoad("Input", $"Loaded {_actionBindings.Count} actions, {totalBindings} bindings");
        }

        public static void ProcessEvent(SDL.Event e)
        {
            switch ((SDL.EventType)e.Type)
            {
                case SDL.EventType.MouseMotion:
                    _mouseDeltaAccum += new Vector2(e.Motion.XRel, e.Motion.YRel);
                    MousePos          = new Vector2(e.Motion.X, e.Motion.Y);
                    break;

                case SDL.EventType.MouseButtonDown:
                    string downBtn = ButtonName(e.Button.Button);
                    _held.Add(downBtn);
                    _pressed.Add(downBtn);
                    break;

                case SDL.EventType.MouseButtonUp:
                    string upBtn = ButtonName(e.Button.Button);
                    _held.Remove(upBtn);
                    break;

                case SDL.EventType.MouseWheel:
                    ScrollDelta = e.Wheel.Y;
                    break;

                case SDL.EventType.KeyDown:
                    string downKey = e.Key.Scancode.ToString();
                    Debug.LogVerbose("Input", $"KeyDown scancode: '{downKey}'");
                    _held.Add(downKey);
                    _pressed.Add(downKey);
                    if (downKey == "LShift" || downKey == "RShift") _modHeld.Add("Shift");
                    if (downKey == "LCtrl"  || downKey == "RCtrl")  _modHeld.Add("Ctrl");

                    if (IsEditing)
                    {
                        if (downKey == "Return" || downKey == "KpEnter")
                            EditConfirmed = true;
                        else if (downKey == "Escape")
                            EditCancelled = true;
                        else if (downKey == "Backspace" && EditBuffer.Length > 0)
                            EditBuffer = EditBuffer[..^1];
                        else
                        {
                            foreach (var kv in _editChar)
                                if (_actionBindings.TryGetValue(kv.Key, out var bl) &&
                                    bl.Exists(b => b.input == downKey))
                                { EditBuffer += kv.Value; break; }
                        }
                    }
                    break;

                case SDL.EventType.KeyUp:
                    string upKey = e.Key.Scancode.ToString();
                    _held.Remove(upKey);
                    if (upKey == "LShift" || upKey == "RShift") _modHeld.Remove("Shift");
                    if (upKey == "LCtrl"  || upKey == "RCtrl")  _modHeld.Remove("Ctrl");
                    break;
            }
        }

        private static bool IsModifierConflicting(string input, string heldMod)
        {
            foreach (var kv in _actionBindings)
                foreach (var (inp, mod) in kv.Value)
                    if (inp == input && string.Equals(mod, heldMod, StringComparison.OrdinalIgnoreCase))
                        return true;
            return false;
        }

        private static bool CheckModOk(string modifier, string input)
        {
            return string.IsNullOrEmpty(modifier)
                ? !_modHeld.Any(m => IsModifierConflicting(input, m))
                : _modHeld.Contains(modifier);
        }

        public static bool IsActionHeld(string action)
        {
            if (!_actionEnabled.Contains(action)) return false;
            if (!_actionBindings.TryGetValue(action, out var bindings)) return false;
            foreach (var (input, modifier) in bindings)
                if (_held.Contains(input) && CheckModOk(modifier, input))
                    return true;
            return false;
        }

        public static bool IsActionPressed(string action)
        {
            if (!_actionEnabled.Contains(action)) return false;
            if (!_actionBindings.TryGetValue(action, out var bindings)) return false;
            foreach (var (input, modifier) in bindings)
                if (_pressed.Contains(input) && CheckModOk(modifier, input))
                    return true;
            return false;
        }

        public static void Consume(string action)
        {
            if (!_actionBindings.TryGetValue(action, out var bindings)) return;
            foreach (var (input, _) in bindings)
                _pressed.Remove(input);
        }

        public static void Flush()
        {
            _mouseDeltaAccum = Vector2.Zero;
            ScrollDelta = 0f;
            _pressed.Clear();
            _held.Remove("ScrollY");
        }

        static string ButtonName(byte btn) => btn switch
        {
            1 => "MouseLeft",
            2 => "MouseMiddle",
            3 => "MouseRight",
            _ => $"Mouse{btn}"
        };
    }
}
