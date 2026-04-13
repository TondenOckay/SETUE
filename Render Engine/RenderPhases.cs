using System.Collections.Generic;
using System.IO;
using System;

namespace SETUE
{
    public class RenderPhase
    {
        public int PhaseId;
        public string CommandName = "";
        public string? PipelineId;
        public bool Enabled;
        public string DependencyId = "None";
        public string Description = "";
    }

    public static class RenderPhases
    {
        public static readonly List<RenderPhase> Phases = new();

        // Only public method – parameterless for scheduler
        public static void Load()
        {
            LoadInternal("Shaders/RenderPhases.csv");
        }

        private static void LoadInternal(string path)
        {
            Console.WriteLine($"[RenderPhases] Loading from '{path}' (full path: {Path.GetFullPath(path)})");
            Phases.Clear();
            
            if (!File.Exists(path))
            {
                Console.WriteLine($"[RenderPhases] ERROR: File not found: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            Console.WriteLine($"[RenderPhases] Read {lines.Length} lines");
            
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var row = lines[i].Split(',');
                if (row.Length < 6)
                {
                    Console.WriteLine($"[RenderPhases] Skipping row {i}: only {row.Length} columns");
                    continue;
                }

                var phase = new RenderPhase
                {
                    PhaseId = int.Parse(row[0]),
                    CommandName = row[1],
                    PipelineId = string.IsNullOrWhiteSpace(row[2]) ? null : row[2],
                    Enabled = bool.Parse(row[3]),
                    DependencyId = row[4],
                    Description = row[5]
                };
                Phases.Add(phase);
                Console.WriteLine($"[RenderPhases] Loaded phase: {phase.CommandName} Enabled={phase.Enabled}");
            }
            Console.WriteLine($"[RenderPhases] Total phases loaded: {Phases.Count}");
        }
    }
}
