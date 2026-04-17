using System;
using System.Collections.Generic;
using System.IO;
using SETUE.RenderEngine;
using SETUE.Scene;

namespace SETUE
{
    public static class RenderDispatcher
    {
        private static Dictionary<string, Action> _commands = new();

        public static void Load()
        {
            string path = "Render Engine/RenderDispatcher.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[RenderDispatcher] Missing {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            _commands.Clear();
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                string name = parts[0].Trim();
                bool enabled = parts.Length > 1 && parts[1].Trim().ToLower() == "true";
                if (!enabled) continue;

                _commands[name] = name switch
                {
                    "Begin_Frame" => () => { /* handled in Vulkan */ },
                    "Draw_Meshes" => () => Draw.Execute(),
                    "Draw_UI"     => () => { SETUE.Scene.Scene2D.Update(); Draw.Execute2D(); },
                    "Draw_Text"   => () => { /* Draw.Execute handles both */ },
                    "End_Frame"   => () => { /* nothing */ },
                    _ => () => { }
                };
            }
            Console.WriteLine($"[RenderDispatcher] Loaded {_commands.Count} commands");
        }

        public static void Execute(string commandName, string? pipelineId)
        {
            if (_commands.TryGetValue(commandName, out var action))
            {
                action();
            }
        }

        public static void Execute(string commandName)
        {
            if (_commands.TryGetValue(commandName, out var action))
            {
                action();
            }
        }
    }
}
