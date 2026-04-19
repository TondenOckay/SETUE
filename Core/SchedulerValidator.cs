using System;
using System.Collections.Generic;
using System.Linq;

namespace SETUE.Core
{
    public static class SchedulerValidator
    {
        public class ValidationResult
        {
            public bool IsValid { get; set; } = true;
            public List<string> Errors { get; set; } = new();
        }

        public static ValidationResult Validate(List<SchedulerEntry> entries)
        {
            var result = new ValidationResult();

            // Required classes (must have at least one enabled entry)
            var requiredClasses = new[]
            {
                "SETUE.Window",
                "SETUE.Vulkan",
                "SETUE.RenderEngine.Shaders",
                "SETUE.Systems.Panels",
                "SETUE.Controls.Input",
                "SETUE.Controls.Selection",
                "SETUE.Scene.Scene2D",
                "SETUE.UI.SceneTree",
                "SETUE.Controls.Movement",
                "SETUE.Scene3D"
            };

            foreach (var className in requiredClasses)
            {
                bool present = entries.Any(e => e.ClassName == className && e.Enabled);
                if (!present)
                {
                    result.IsValid = false;
                    result.Errors.Add($"Required class missing or disabled: {className}");
                }
            }

            // Required specific load methods
            var requiredLoads = new[]
            {
                "SETUE.Vulkan.CreateSurface"
            };

            foreach (var fullMethod in requiredLoads)
            {
                var parts = fullMethod.Split('.');
                if (parts.Length != 2) continue;
                string className = parts[0];
                string methodName = parts[1];

                bool found = entries.Any(e =>
                    e.ClassName == className &&
                    e.LoadMethod == methodName &&
                    e.Enabled);

                if (!found)
                {
                    result.IsValid = false;
                    result.Errors.Add($"Required load method missing or disabled: {fullMethod}");
                }
            }

            // Required specific update methods
            var requiredUpdates = new[]
            {
                "SETUE.Window.ProcessEvents",
                "SETUE.Vulkan.DoDrawFrame",
                "SETUE.Controls.Input.Flush",
                "SETUE.Scene.Scene2D.Update",
                "SETUE.Controls.Movement.UpdateDrags"
            };

            foreach (var fullMethod in requiredUpdates)
            {
                var parts = fullMethod.Split('.');
                if (parts.Length != 2) continue;
                string className = parts[0];
                string methodName = parts[1];

                bool found = entries.Any(e =>
                    e.ClassName == className &&
                    e.UpdateMethod == methodName &&
                    e.Enabled &&
                    e.Loop != "Boot");

                if (!found)
                {
                    result.IsValid = false;
                    result.Errors.Add($"Required update method missing or disabled: {fullMethod}");
                }
            }

            return result;
        }
    }
}
