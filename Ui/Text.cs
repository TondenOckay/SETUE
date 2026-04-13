using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Systems
{
    public static class Texts
    {
        public static void Load()
        {
            string path = "Ui/Text.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Texts] File not found: {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headers = lines[0].Split(',');
            int iId      = Array.IndexOf(headers, "id");
            int iPanelId = Array.IndexOf(headers, "panel_id");
            int iText    = Array.IndexOf(headers, "text");
            int iFontId  = Array.IndexOf(headers, "font_id");
            int iColorId = Array.IndexOf(headers, "color_id");
            int iAlign   = Array.IndexOf(headers, "align");
            int iLayer   = Array.IndexOf(headers, "layer");
            int iRotation= Array.IndexOf(headers, "rotation");
            int iSource  = Array.IndexOf(headers, "source");
            int iPrefix  = Array.IndexOf(headers, "prefix");

            var world = Object.ECSWorld; // Changed from ObjectLoader

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var p = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < p.Length ? p[idx].Trim() : "";

                string id = Get(iId);
                string panelId = Get(iPanelId);
                string content = Get(iText);
                string fontId = string.IsNullOrEmpty(Get(iFontId)) ? "default" : Get(iFontId);
                string align = string.IsNullOrEmpty(Get(iAlign)) ? "center" : Get(iAlign);
                int layer = int.TryParse(Get(iLayer), out var l) ? l : 10;
                float rotation = float.TryParse(Get(iRotation), out var rot) ? rot : 0f;
                string source = Get(iSource);
                string prefix = Get(iPrefix);

                Vector4 color = new Vector4(1,1,1,1);
                string cid = Get(iColorId);
                if (!string.IsNullOrEmpty(cid))
                {
                    var c = Colors.Get(cid);
                    color = new Vector4(c.R, c.G, c.B, c.Alpha);
                }

                Entity e = world.CreateEntity();
                world.AddComponent(e, new TextComponent
                {
                    Id = id,
                    Content = content,
                    FontId = fontId,
                    FontSize = 16f,
                    Color = color,
                    Align = align,
                    Rotation = rotation,
                    Layer = layer,
                    Source = source,
                    Prefix = prefix,
                    PanelId = panelId
                });
                world.AddComponent(e, new TransformComponent
                {
                    Position = Vector3.Zero,
                    Scale = Vector3.One,
                    Rotation = Quaternion.Identity
                });
            }
            Console.WriteLine($"[Texts] Loaded {world.Query<TextComponent>().Count()} text entities");
        }

        public static void Update()
        {
            var world = Object.ECSWorld; // Changed from ObjectLoader
            Entity? selectedEntity = null;
            foreach (var e in world.Query<SelectedComponent>())
            {
                selectedEntity = e;
                break;
            }

            foreach (var e in world.Query<TextComponent>())
            {
                var text = world.GetComponent<TextComponent>(e);
                if (string.IsNullOrEmpty(text.Source)) continue;

                string newContent = text.Content;
                if (selectedEntity != null)
                {
                    newContent = $"{text.Prefix} 0.000";
                }
                else
                {
                    newContent = $"{text.Prefix} ---";
                }

                if (newContent != text.Content)
                {
                    text.Content = newContent;
                    world.SetComponent(e, text);
                }
            }
        }
    }
}
