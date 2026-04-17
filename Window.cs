using System;
using SDL3;
using SETUE.Controls;
using SETUE.Core;

namespace SETUE
{
    public static class Window
    {
        private static nint _window;
        private static bool _shouldQuit;
        private static bool _relativeMouseActive = false;

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
                Input.ProcessEvent(ev);

                if (ev.Type == (uint)SDL.EventType.MouseButtonDown)
                {
                    if ((ev.Button.Button == 2 || ev.Button.Button == 3) && !_relativeMouseActive)
                    {
                        SDL.SetWindowRelativeMouseMode(_window, true);
                        _relativeMouseActive = true;
                    }
                }
                if (ev.Type == (uint)SDL.EventType.MouseButtonUp)
                {
                    if ((ev.Button.Button == 2 || ev.Button.Button == 3) && _relativeMouseActive)
                    {
                        SDL.SetWindowRelativeMouseMode(_window, false);
                        _relativeMouseActive = false;
                    }
                }

                // Allow OS close button to work
                if ((SDL.EventType)ev.Type == SDL.EventType.Quit)
                {
                    Console.WriteLine("[Window] SDL_QUIT received. Exiting...");
                    _shouldQuit = true;
                }

                // Also allow Escape key to quit
                if (ev.Type == (uint)SDL.EventType.KeyDown && ev.Key.Scancode == SDL.Scancode.Escape)
                {
                    Console.WriteLine("[Window] Escape pressed. Exiting...");
                    _shouldQuit = true;
                }
            }

            if (_shouldQuit)
            {
                MasterClock.Stop();
            }
        }
    }
}
