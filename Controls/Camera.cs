using System;
using System.IO;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Controls
{
    public static class Camera
    {
        public static void Load()
        {
            string path = "3d Editor/Camera.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Camera] Missing {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            var headers = lines[0].Split(',');
            var values = lines[1].Split(',');

            int GetIdx(string name) => Array.FindIndex(headers, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            float Get(int idx) => idx >= 0 && idx < values.Length && float.TryParse(values[idx].Trim(), out var f) ? f : 0f;
            bool GetBool(int idx) => idx >= 0 && idx < values.Length && bool.TryParse(values[idx].Trim(), out var b) ? b : false;

            var pos   = new Vector3(Get(GetIdx("PosX")), Get(GetIdx("PosY")), Get(GetIdx("PosZ")));
            var pivot = new Vector3(Get(GetIdx("PivotX")), Get(GetIdx("PivotY")), Get(GetIdx("PivotZ")));
            var fov   = Get(GetIdx("Fov"));
            var near  = Get(GetIdx("Near"));
            var far   = Get(GetIdx("Far"));
            var invX  = GetBool(GetIdx("InvertX"));
            var invY  = GetBool(GetIdx("InvertY"));

            Console.WriteLine($"[Camera] Loaded: pos=<{pos.X}, {pos.Y}, {pos.Z}> pivot=<{pivot.X}, {pivot.Y}, {pivot.Z}> dist={Vector3.Distance(pos, pivot):F1} invX={invX} invY={invY}");

            var world = Object.ECSWorld;  // Changed from ObjectLoader
            Entity? existing = null;
            foreach (var e in world.Query<CameraComponent>())
            {
                existing = e;
                break;
            }
            if (existing != null)
                world.DestroyEntity(existing.Value);

            Entity cameraEntity = world.CreateEntity();
            world.AddComponent(cameraEntity, new CameraComponent
            {
                Position = pos,
                Pivot = pivot,
                Fov = fov,
                Near = near,
                Far = far,
                InvertX = invX,
                InvertY = invY
            });
            Console.WriteLine($"[Camera] Created camera entity {cameraEntity}");
        }

        public static void Update()
        {
            // Input handling will go here later (e.g., orbit controls)
        }
    }
}
