using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Controls
{
    public static class Camera
    {
        public static float DeltaTime = 0.016f;

        // All values loaded from CSV.
        private static float OrbitSpeed;
        private static float PanSpeed;
        private static float ZoomSpeed;
        private static bool  InvertX;
        private static bool  InvertY;
        private static float KeyRotateSpeed;
        private static float MouseOrbitSensitivity;
        private static float MousePanSensitivity;
        private static bool  Orthographic = false;

        private static Vector3 ViewFront;
        private static Vector3 ViewBack;
        private static Vector3 ViewLeft;
        private static Vector3 ViewRight;
        private static Vector3 ViewTop;
        private static Vector3 ViewBottom;

        public static void Load()
        {
            string path = "3d Editor/Camera.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Camera] ERROR: File not found: '{path}'. Camera not created.");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2)
            {
                Console.WriteLine($"[Camera] ERROR: '{path}' is empty or missing data row. Camera not created.");
                return;
            }

            // Build column index map (case-insensitive)
            var headers = lines[0].Split(',');
            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                colIndex[headers[i].Trim()] = i;

            // Use the first data row (index 1)
            var parts = lines[1].Split(',');

            // Helper to get value by column name
            string GetStr(string col) =>
                colIndex.TryGetValue(col, out int idx) && idx < parts.Length ? parts[idx].Trim() : "";

            float GetFloat(string col, float defaultValue = 0f)
            {
                string val = GetStr(col);
                if (string.IsNullOrEmpty(val))
                {
                    Console.WriteLine($"[Camera] Warning: Missing column '{col}' or empty value. Using {defaultValue}.");
                    return defaultValue;
                }
                if (float.TryParse(val, out float result))
                    return result;
                Console.WriteLine($"[Camera] Warning: Invalid float '{val}' for '{col}'. Using {defaultValue}.");
                return defaultValue;
            }

            bool GetBool(string col, bool defaultValue = false)
            {
                string val = GetStr(col);
                if (string.IsNullOrEmpty(val))
                {
                    Console.WriteLine($"[Camera] Warning: Missing column '{col}' or empty value. Using {defaultValue}.");
                    return defaultValue;
                }
                if (bool.TryParse(val, out bool result))
                    return result;
                // Also accept "1"/"0" as bool
                if (val == "1") return true;
                if (val == "0") return false;
                Console.WriteLine($"[Camera] Warning: Invalid bool '{val}' for '{col}'. Using {defaultValue}.");
                return defaultValue;
            }

            Vector3 GetVector3(string baseName)
            {
                return new Vector3(
                    GetFloat(baseName + "X"),
                    GetFloat(baseName + "Y"),
                    GetFloat(baseName + "Z"));
            }

            // Read critical values (with warnings but continue)
            Vector3 pos   = GetVector3("Pos");
            Vector3 pivot = GetVector3("Pivot");
            float fov     = GetFloat("Fov", 60f);
            float near    = GetFloat("Near", 0.1f);
            float far     = GetFloat("Far", 1000f);
            bool invX     = GetBool("InvertX", true);
            bool invY     = GetBool("InvertY", true);

            // Control parameters
            OrbitSpeed            = GetFloat("orbit_speed", 0.15f);
            PanSpeed              = GetFloat("pan_speed", 0.01f);
            ZoomSpeed             = GetFloat("zoom_speed", 0.5f);
            InvertX               = GetBool("invert_x", true);
            InvertY               = GetBool("invert_y", true);
            KeyRotateSpeed        = GetFloat("key_rotate_speed", 0.1f);
            MouseOrbitSensitivity = GetFloat("mouse_orbit_sensitivity", 0.005f);
            MousePanSensitivity   = GetFloat("mouse_pan_sensitivity", 0.01f);

            // View directions
            ViewFront  = GetVector3("Front");
            ViewBack   = GetVector3("Back");
            ViewLeft   = GetVector3("Left");
            ViewRight  = GetVector3("Right");
            ViewTop    = GetVector3("Top");
            ViewBottom = GetVector3("Bottom");

            Console.WriteLine($"[Camera] Loaded: pos=<{pos.X:F2},{pos.Y:F2},{pos.Z:F2}> pivot=<{pivot.X:F2},{pivot.Y:F2},{pivot.Z:F2}> dist={Vector3.Distance(pos, pivot):F2}");
            Console.WriteLine($"[Camera] Controls: orbit={OrbitSpeed} pan={PanSpeed} zoom={ZoomSpeed} keyRot={KeyRotateSpeed} mouseSens=({MouseOrbitSensitivity},{MousePanSensitivity}) invert=({InvertX},{InvertY})");

            // Create or replace camera entity
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

        /// <summary>
        /// Returns true if the mouse is over ANY visible UI panel.
        /// </summary>
        private static bool IsMouseOverUI()
        {
            var world = Object.ECSWorld;
            Vector2 mouse = Input.MousePos;

            var candidates = new List<(Entity entity, TransformComponent trans, PanelComponent panel)>();
            foreach (var e in world.Query<TransformComponent>())
            {
                if (!world.HasComponent<PanelComponent>(e)) continue;
                var panel = world.GetComponent<PanelComponent>(e);
                if (!panel.Visible) continue;

                var trans = world.GetComponent<TransformComponent>(e);
                candidates.Add((e, trans, panel));
            }

            candidates.Sort((a, b) => b.panel.Layer.CompareTo(a.panel.Layer));

            foreach (var (_, trans, _) in candidates)
            {
                float left   = trans.Position.X - trans.Scale.X * 0.5f;
                float right  = trans.Position.X + trans.Scale.X * 0.5f;
                float top    = trans.Position.Y - trans.Scale.Y * 0.5f;
                float bottom = trans.Position.Y + trans.Scale.Y * 0.5f;

                if (mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom)
                    return true;
            }
            return false;
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

            // ----- Keyboard view presets -----
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

            // ----- Keyboard rotation -----
            if (Input.IsActionHeld("rotate_left"))
            {
                float angle = -KeyRotateSpeed;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                pos = pivot + dir * dist;
            }
            if (Input.IsActionHeld("rotate_right"))
            {
                float angle = KeyRotateSpeed;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                pos = pivot + dir * dist;
            }
            if (Input.IsActionHeld("rotate_up"))
            {
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
                if (float.IsNaN(right.X)) right = Vector3.UnitX;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(right, KeyRotateSpeed));
                pos = pivot + dir * dist;
            }
            if (Input.IsActionHeld("rotate_down"))
            {
                Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
                if (float.IsNaN(right.X)) right = Vector3.UnitX;
                dir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(right, -KeyRotateSpeed));
                pos = pivot + dir * dist;
            }

            // ----- Mouse-driven controls – blocked over UI -----
            if (!IsMouseOverUI())
            {
                if (Input.IsActionHeld("camera_orbit"))
                {
                    Vector2 delta = Input.MouseDelta;
                    delta.X = Math.Clamp(delta.X, -50f, 50f);
                    delta.Y = Math.Clamp(delta.Y, -50f, 50f);

                    if (MathF.Abs(delta.X) > 0.01f || MathF.Abs(delta.Y) > 0.01f)
                    {
                        if (InvertX) delta.X = -delta.X;
                        if (InvertY) delta.Y = -delta.Y;

                        float angleX = delta.X * MouseOrbitSensitivity * DeltaTime * 60f;
                        float angleY = delta.Y * MouseOrbitSensitivity * DeltaTime * 60f;

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

                    Vector3 pan = (-right * delta.X * MousePanSensitivity) + (-up * delta.Y * MousePanSensitivity);
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
            }

            cam.Position = pos;
            cam.Pivot    = pivot;
            world.SetComponent(camEntity.Value, cam);
        }
    }
}
