using System;
using SDL3;

namespace SETUE
{
    public static class Window
    {
        private static nint _window;
        private static bool _shouldQuit;

        public static IntPtr GetHandle() => _window;
        public static bool ShouldQuit() => _shouldQuit;

        public static void Load()
        {
            if (!SDL.Init(SDL.InitFlags.Video))
            {
                Console.WriteLine($"[Window] SDL Init failed: {SDL.GetError()}");
                return;
            }

            _window = SDL.CreateWindow("SETUE Engine", 1920, 1080, SDL.WindowFlags.Vulkan | SDL.WindowFlags.Resizable);
            if (_window == 0)
            {
                Console.WriteLine($"[Window] CreateWindow failed: {SDL.GetError()}");
                return;
            }

            Console.WriteLine("[Window] Created successfully");
        }

        public static void ProcessEvents()
        {
            while (SDL.PollEvent(out var ev))
            {
                if ((SDL.EventType)ev.Type == SDL.EventType.Quit)
                    _shouldQuit = true;
            }
            if (_shouldQuit)
            {
                Console.WriteLine("[Window] Quit requested, exiting...");
                Environment.Exit(0);
            }
        }
    }
}
