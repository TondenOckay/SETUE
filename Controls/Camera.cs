using System;
using System.IO;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Controls
{
    public static class Camera
    {
        public static float DeltaTime = 0.016f;

        private static float OrbitSpeed = 0.15f;
        private static float PanSpeed   = 0.01f;
        private static float ZoomSpeed  = 0.5f;
        private static bool  InvertX    = true;
        private static bool  InvertY    = true;
        private static bool  Orthographic = false;

        private static Vector3 ViewFront  = new Vector3( 0,  0, -1);
        private static Vector3 ViewBack   = new Vector3( 0,  0,  1);
        private static Vector3 ViewLeft   = new Vector3(-1,  0,  0);
        private static Vector3 ViewRight  = new Vector3( 1,  0,  0);
        private static Vector3 ViewTop    = new Vector3( 0,  1,  0);
        private static Vector3 ViewBottom = new Vector3( 0, -1,  0);

        public static void Load()
        {
            string path = "3d Editor/Camera.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Camera] Missing {path}, using defaults");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            var headers = lines[0].Split(',');
            var values  = lines[1].Split(',');

            int GetIdx(string name) => Array.FindIndex(headers, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            float Get(int idx) => idx >= 0 && idx < values.Length && float.TryParse(values[idx].Trim(), out var f) ? f : 0f;
            bool GetBool(int idx) => idx >= 0 && idx < values.Length && bool.TryParse(values[idx].Trim(), out var b) && b;

            Vector3 SafeGetVec(int baseIdx, Vector3 fallback)
            {
                if (baseIdx < 0 || baseIdx + 2 >= values.Length) return fallback;
                var v = new Vector3(Get(baseIdx), Get(baseIdx + 1), Get(baseIdx + 2));
                return v.LengthSquared() > 0.0001f ? v : fallback;
            }

            var pos   = new Vector3(Get(GetIdx("PosX")),   Get(GetIdx("PosY")),   Get(GetIdx("PosZ")));
            var pivot = new Vector3(Get(GetIdx("PivotX")), Get(GetIdx("PivotY")), Get(GetIdx("PivotZ")));
            var fov   = Get(GetIdx("Fov"));
            var near  = Get(GetIdx("Near"));
            var far   = Get(GetIdx("Far"));
            var invX  = GetBool(GetIdx("InvertX"));
            var invY  = GetBool(GetIdx("InvertY"));

            float orbitVal = Get(GetIdx("orbit_speed"));
            OrbitSpeed = orbitVal > 0 ? orbitVal : 0.15f;
            PanSpeed   = Get(GetIdx("pan_speed"));
            ZoomSpeed  = Get(GetIdx("zoom_speed"));
            InvertX    = GetBool(GetIdx("invert_x"));
            InvertY    = GetBool(GetIdx("invert_y"));

            ViewFront  = SafeGetVec(GetIdx("FrontX"),  ViewFront);
            ViewBack   = SafeGetVec(GetIdx("BackX"),   ViewBack);
            ViewLeft   = SafeGetVec(GetIdx("LeftX"),   ViewLeft);
            ViewRight  = SafeGetVec(GetIdx("RightX"),  ViewRight);
            ViewTop    = SafeGetVec(GetIdx("TopX"),    ViewTop);
            ViewBottom = SafeGetVec(GetIdx("BottomX"), ViewBottom);

            Console.WriteLine($"[Camera] Loaded: pos=<{pos.X},{pos.Y},{pos.Z}> pivot=<{pivot.X},{pivot.Y},{pivot.Z}> dist={Vector3.Distance(pos, pivot):F1}");
            Console.WriteLine($"[Camera] Controls: orbit={OrbitSpeed} pan={PanSpeed} zoom={ZoomSpeed} invertX={InvertX} invertY={InvertY}");
            Console.WriteLine($"[Camera] ViewFront={ViewFront} ViewBack={ViewBack} ViewTop={ViewTop}");

            var world = Object.ECSWorld;
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
                Pivot    = pivot,
                Fov      = fov,
                Near     = near,
                Far      = far,
                InvertX  = invX,
                InvertY  = invY
            });
            Console.WriteLine($"[Camera] Created camera entity {cameraEntity}");
        }

        public static void Update()
        {
            var world = Object.ECSWorld;
            Entity? camEntity = null;
            foreach (var e in world.Query<CameraComponent>())
            {
                camEntity = e;
                break;
            }
            if (camEntity == null) return;

            var cam   = world.GetComponent<CameraComponent>(camEntity.Value);
            Vector3 pos   = cam.Position;
            Vector3 pivot = cam.Pivot;

            float dist = Vector3.Distance(pos, pivot);
            if (dist < 0.001f) dist = 0.001f;
            Vector3 dir = Vector3.Normalize(pos - pivot);

            if (Input.IsActionPressed("view_front"))
            {
                Input.Consume("view_front");
                dir = Vector3.Normalize(ViewFront);
                pos = pivot + dir * dist;
            }
            else if (Input.IsActionPressed("view_back"))
            {
                Input.Consume("view_back");
                dir = Vector3.Normalize(ViewBack);
                pos = pivot + dir * dist;
            }

            if (Input.IsActionPressed("view_right"))
            {
                Input.Consume("view_right");
                dir = Vector3.Normalize(ViewRight);
                pos = pivot + dir * dist;
            }
            else if (Input.IsActionPressed("view_left"))
            {
                Input.Consume("view_left");
                dir = Vector3.Normalize(ViewLeft);
                pos = pivot + dir * dist;
            }

            if (Input.IsActionPressed("view_top"))
            {
                Input.Consume("view_top");
                dir = Vector3.Normalize(ViewTop);
                pos = pivot + dir * dist;
            }
            else if (Input.IsActionPressed("view_bottom"))
            {
                Input.Consume("view_bottom");
                dir = Vector3.Normalize(ViewBottom);
                pos = pivot + dir * dist;
            }

            if (Input.IsActionPressed("toggle_ortho"))
            {
                Input.Consume("toggle_ortho");
                Orthographic = !Orthographic;
                Console.WriteLine($"[Camera] Orthographic: {Orthographic}");
            }

            if (Input.IsActionPressed("set_pivot"))
            {
                Input.Consume("set_pivot");
                Vector3 newPivot = Vector3.Lerp(pivot, pos, 0.5f);
                float newDist = Vector3.Distance(pos, newPivot);
                if (newDist > 0.1f)
                {
                    pivot = newPivot;
                    dist  = newDist;
                    Console.WriteLine($"[Camera] Pivot moved to {pivot}");
                }
            }

            if (Input.IsActionHeld("rotate_left"))
            {
                float angle = -OrbitSpeed * 0.1f;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                pos = pivot + dir * dist;
            }
            if (Input.IsActionHeld("rotate_right"))
            {
                float angle = OrbitSpeed * 0.1f;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                pos = pivot + dir * dist;
            }
            if (Input.IsActionHeld("rotate_up"))
            {
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
                if (float.IsNaN(right.X)) right = Vector3.UnitX;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(right, OrbitSpeed * 0.1f));
                pos = pivot + dir * dist;
            }
            if (Input.IsActionHeld("rotate_down"))
            {
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
                if (float.IsNaN(right.X)) right = Vector3.UnitX;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(right, -OrbitSpeed * 0.1f));
                pos = pivot + dir * dist;
            }

            if (Input.IsActionHeld("camera_orbit"))
            {
                Vector2 delta = Input.MouseDelta;
                delta.X = Math.Clamp(delta.X, -50f, 50f);
                delta.Y = Math.Clamp(delta.Y, -50f, 50f);

                if (MathF.Abs(delta.X) > 0.01f || MathF.Abs(delta.Y) > 0.01f)
                {
                    if (InvertX) delta.X = -delta.X;
                    if (InvertY) delta.Y = -delta.Y;

                    float angleX = delta.X * OrbitSpeed * 0.005f * DeltaTime * 60f;
                    float angleY = delta.Y * OrbitSpeed * 0.005f * DeltaTime * 60f;

                    dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angleX));

                    Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
                    if (float.IsNaN(right.X)) right = Vector3.UnitX;
                    Vector3 newDir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(right, angleY));

                    if (MathF.Abs(Vector3.Dot(newDir, Vector3.UnitY)) < 0.99f)
                        dir = newDir;

                    pos = pivot + dir * dist;
                }
            }

            if (Input.IsActionHeld("camera_pan"))
            {
                Vector2 delta = Input.MouseDelta;
                delta.X = Math.Clamp(delta.X, -50f, 50f);
                delta.Y = Math.Clamp(delta.Y, -50f, 50f);

                if (InvertX) delta.X = -delta.X;
                if (InvertY) delta.Y = -delta.Y;

                Vector3 forward = Vector3.Normalize(pivot - pos);
                Vector3 right   = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                if (float.IsNaN(right.X)) right = Vector3.UnitX;
                Vector3 up = Vector3.Cross(right, forward);

                Vector3 pan = (-right * delta.X * PanSpeed) + (-up * delta.Y * PanSpeed);
                pos   += pan;
                pivot += pan;
            }

            float scroll = Input.ScrollDelta;
            if (MathF.Abs(scroll) > 0.001f)
            {
                dist -= scroll * ZoomSpeed;
                dist  = Math.Max(0.5f, Math.Min(100f, dist));
                pos   = pivot + dir * dist;
            }

            cam.Position = pos;
            cam.Pivot    = pivot;
            world.SetComponent(camEntity.Value, cam);
        }
    }
}
