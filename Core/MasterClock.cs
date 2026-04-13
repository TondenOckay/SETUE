using System;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace SETUE.Core
{
    public static class MasterClock
    {
        private static float _tickInterval = 0.012f;
        private static bool _running = false;
        private static Thread? _thread;
        public static readonly object LockObject = new object();
        private static Stopwatch _stopwatch = new Stopwatch();

        public static double CurrentTime => _stopwatch.Elapsed.TotalSeconds;

        public static void Load()
        {
            string path = "Core/MasterClock.csv";
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length > 1)
                {
                    var parts = lines[1].Split(',');
                    if (parts.Length >= 2)
                    {
                        _tickInterval = float.Parse(parts[1]);
                    }
                }
            }
            Console.WriteLine($"[MasterClock] Tick interval = {_tickInterval} seconds");
        }

        public static void Start()
        {
            if (_running) return;
            _running = true;
            _stopwatch.Start();
            Console.WriteLine("[MasterClock] Started (Single-Threaded)");
            Run();
        }

        public static void Stop()
        {
            _running = false;
            _stopwatch.Stop();
            Console.WriteLine("[MasterClock] Stopped");
        }

        public static void WaitForExit()
        {
            // No longer needed for threading, but kept for compatibility
        }

        private static void Run()
        {
            // === BOOT PHASE ===
            Schedulers.RunBoot();

            // === MAIN LOOP ===
            while (_running)
            {
                double tickStart = CurrentTime;

                // Everything happens in sequence on this one thread
                Schedulers.Update(CurrentTime);

                double elapsed = CurrentTime - tickStart;
                int sleepMs = (int)((_tickInterval - elapsed) * 1000);
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
        }
    }
}
