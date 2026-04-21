using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SETUE.Core
{
    public class SchedulerEntry
    {
        public string ClassName    { get; set; } = "";
        public string LoadMethod   { get; set; } = "";
        public string UpdateMethod { get; set; } = "";
        public string Loop         { get; set; } = "";
        public string Hub          { get; set; } = "";
        public int    RunOrder     { get; set; }
        public float  TimeSlot     { get; set; }
        public float  FrequencySec { get; set; }
        public bool   Enabled      { get; set; }
        public bool   Log          { get; set; }
    }

    public static class Schedulers
    {
        private static List<SchedulerEntry> _entries = new();

        // FIX 1: ConcurrentDictionary makes _lastRunTimes safe when each
        // Loop runs on its own thread. Reads and writes from different
        // threads can no longer corrupt the dictionary.
        private static ConcurrentDictionary<string, double> _lastRunTimes = new();

        private static Dictionary<string, Action> _loadActions   = new();
        private static Dictionary<string, Action> _updateActions = new();

        public static void Load()
        {
            string path = "Core/Scheduler.csv";
            _entries.Clear();
            _loadActions.Clear();
            _updateActions.Clear();

            if (!File.Exists(path))
            {
                Console.WriteLine($"[Schedulers] Missing {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers   = lines[0].Split(',');
            int idxClass  = Array.IndexOf(headers, "ClassName");
            int idxLoad   = Array.IndexOf(headers, "LoadMethod");
            int idxUpdate = Array.IndexOf(headers, "UpdateMethod");
            int idxLoop   = Array.IndexOf(headers, "Loop");
            int idxHub    = Array.IndexOf(headers, "Hub");
            int idxRun    = Array.IndexOf(headers, "RunOrder");
            int idxSlot   = Array.IndexOf(headers, "TimeSlot");
            int idxFreq   = Array.IndexOf(headers, "FrequencySec");
            int idxEnabled= Array.IndexOf(headers, "Enabled");
            int idxLog    = Array.IndexOf(headers, "Log");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');

                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                var entry = new SchedulerEntry
                {
                    ClassName    = Get(idxClass),
                    LoadMethod   = Get(idxLoad),
                    UpdateMethod = Get(idxUpdate),
                    Loop         = Get(idxLoop),
                    Hub          = Get(idxHub),
                    RunOrder     = int.TryParse(Get(idxRun),  out int   ro) ? ro : 0,
                    TimeSlot     = float.TryParse(Get(idxSlot), out float ts) ? ts : 0f,
                    FrequencySec = float.TryParse(Get(idxFreq), out float fs) ? fs : 0f,
                    Enabled      = Get(idxEnabled) == "1",
                    Log          = Get(idxLog) == "1"
                };

                if (!entry.Enabled) continue;

                _entries.Add(entry);

                Type? type = Type.GetType(entry.ClassName);
                if (type == null)
                {
                    Console.WriteLine($"[Schedulers] ERROR: Type not found: {entry.ClassName}");
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.LoadMethod))
                {
                    var method = type.GetMethod(entry.LoadMethod,
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (method != null)
                    {
                        try
                        {
                            var action = (Action)Delegate.CreateDelegate(typeof(Action), method);
                            _loadActions[entry.ClassName + "." + entry.LoadMethod] = action;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Schedulers] ERROR creating delegate for " +
                                $"{entry.ClassName}.{entry.LoadMethod}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Schedulers] ERROR: Load method '{entry.LoadMethod}' " +
                            $"not found in {entry.ClassName}");
                    }
                }

                if (!string.IsNullOrEmpty(entry.UpdateMethod))
                {
                    var method = type.GetMethod(entry.UpdateMethod,
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (method != null)
                    {
                        try
                        {
                            var action = (Action)Delegate.CreateDelegate(typeof(Action), method);
                            _updateActions[entry.ClassName + "." + entry.UpdateMethod] = action;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Schedulers] ERROR creating delegate for " +
                                $"{entry.ClassName}.{entry.UpdateMethod}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Schedulers] ERROR: Update method '{entry.UpdateMethod}' " +
                            $"not found in {entry.ClassName}");
                    }
                }
            }

            var validation = SchedulerValidator.Validate(_entries);
            if (!validation.IsValid)
            {
                Console.WriteLine("[Schedulers] FATAL: CSV validation failed. Engine will not start.");
                foreach (var err in validation.Errors)
                    Console.WriteLine($"  - {err}");
                Environment.Exit(1);
            }

            Console.WriteLine($"[Schedulers] Loaded {_entries.Count} systems");
        }

        public static void RunBoot()
        {
            if (_entries.Count == 0) Load();

            var bootEntries = _entries
                .Where(e => e.Loop == "Boot" && !string.IsNullOrEmpty(e.LoadMethod))
                .OrderBy(e => e.TimeSlot)
                .ThenBy(e => e.Loop)
                .ThenBy(e => e.Hub)
                .ThenBy(e => e.RunOrder)
                .ThenBy(e => e.ClassName);

            Console.WriteLine($"[Schedulers] Running {bootEntries.Count()} boot methods...");

            foreach (var entry in bootEntries)
            {
                string key = entry.ClassName + "." + entry.LoadMethod;
                Console.WriteLine($"[Schedulers] Boot: attempting {key}");
                if (_loadActions.TryGetValue(key, out var action))
                {
                    if (entry.Log) Console.WriteLine($"[Scheduler] Boot: {key}");
                    try
                    {
                        action();
                        Console.WriteLine($"[Schedulers] Boot: {key} succeeded.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Schedulers] ERROR in {key}: " +
                            $"{ex.InnerException?.Message ?? ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[Schedulers] Boot: {key} NOT FOUND in load actions.");
                }
            }
        }

        public static void Update(double currentTime)
        {
            if (_entries.Count == 0) Load();

            var regularEntries = _entries
                .Where(e => e.Loop != "Boot" && !string.IsNullOrEmpty(e.UpdateMethod))
                .OrderBy(e => e.TimeSlot)
                .ThenBy(e => e.Loop)
                .ThenBy(e => e.Hub)
                .ThenBy(e => e.RunOrder)
                .ThenBy(e => e.ClassName);

            foreach (var entry in regularEntries)
            {
                string key = entry.ClassName + "." + entry.UpdateMethod;

                // ConcurrentDictionary.TryGetValue is thread-safe
                if (!_lastRunTimes.TryGetValue(key, out double lastRun))
                    lastRun = 0;

                if (entry.FrequencySec > 0 && (currentTime - lastRun) < entry.FrequencySec)
                    continue;

                if (_updateActions.TryGetValue(key, out var action))
                {
                    if (entry.Log) Console.WriteLine($"[Scheduler] {key}");

                    // FIX 2: try/catch added to Update so a single broken system
                    // cannot crash the entire game loop and disconnect all players.
                    // The error is logged and the loop continues to the next system.
                    try
                    {
                        action();
                        // ConcurrentDictionary indexer assignment is thread-safe
                        _lastRunTimes[key] = currentTime;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Schedulers] ERROR in {key}: " +
                            $"{ex.InnerException?.Message ?? ex.Message}");
                    }
                }
            }
        }
    }
}
