using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.Systems
{
    public class Panel
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
        public bool Visible { get; set; }
        public int Layer { get; set; }
        public string Text { get; set; } = "";
        public string FontId { get; set; } = "default";
        public bool Clickable { get; set; }
        public string TextAlign { get; set; } = "center";
        public float Alpha { get; set; } = 1f;
        public float TextR { get; set; } = 1f;
        public float TextG { get; set; } = 1f;
        public float TextB { get; set; } = 1f;
        public float OriginalWidth { get; set; }
    }

    public static class Panels
    {
        private static List<Panel> _panels = new();
        private static Dictionary<string, Panel> _panelDict = new();
        private static int _windowWidth = 1920;
        private static int _windowHeight = 1080;

        public static IReadOnlyDictionary<string, Panel> All => _panelDict;
        public static IEnumerable<Panel> Sorted => _panels.OrderBy(p => p.Layer);

        public static void Add(Panel p) { _panels.Add(p); _panelDict[p.Id] = p; }
        public static void Remove(string id) { _panels.RemoveAll(p => p.Id == id); _panelDict.Remove(id); }

        public static void Load()
        {
            _panels.Clear();
            string path = "Ui/Panel.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Panels] File not found: {path}"); return; }

            var lines = File.ReadAllLines(path);
            var headers = lines[0].Split(',');

            int iId = Array.IndexOf(headers, "id");
            int iLeft = Array.IndexOf(headers, "left");
            int iRight = Array.IndexOf(headers, "right");
            int iTop = Array.IndexOf(headers, "top");
            int iBottom = Array.IndexOf(headers, "bottom");
            int iR = Array.IndexOf(headers, "r");
            int iG = Array.IndexOf(headers, "g");
            int iB = Array.IndexOf(headers, "b");
            int iVis = Array.IndexOf(headers, "visible");
            int iLayer = Array.IndexOf(headers, "layer");
            int iText = Array.IndexOf(headers, "text");
            int iFontId = Array.IndexOf(headers, "font_id");
            int iClickable = Array.IndexOf(headers, "clickable");
            int iTextR = Array.IndexOf(headers, "text_r");
            int iTextG = Array.IndexOf(headers, "text_g");
            int iTextB = Array.IndexOf(headers, "text_b");
            int iAlpha = Array.IndexOf(headers, "alpha");
            int iColorId = Array.IndexOf(headers, "color_id");
            int iTextColorId = Array.IndexOf(headers, "text_color_id");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var p = line.Split(',');

                string Get(int idx) => idx >= 0 && idx < p.Length ? p[idx].Trim() : "";

                var panel = new Panel
                {
                    Id = Get(iId),
                    X = float.TryParse(Get(iLeft), out float l) ? l : 0,
                    Width = float.TryParse(Get(iRight), out float r) ? r - (float.TryParse(Get(iLeft), out float l2) ? l2 : 0) : 0,
                    Y = float.TryParse(Get(iTop), out float t) ? t : 0,
                    Height = float.TryParse(Get(iBottom), out float b) ? b - (float.TryParse(Get(iTop), out float t2) ? t2 : 0) : 0,
                    R = float.Parse(Get(iR)),
                    G = float.Parse(Get(iG)),
                    B = float.Parse(Get(iB)),
                    Visible = bool.Parse(Get(iVis)),
                    Layer = int.Parse(Get(iLayer)),
                    Text = Get(iText),
                    FontId = string.IsNullOrEmpty(Get(iFontId)) ? "default" : Get(iFontId),
                    Clickable = Get(iClickable) == "true",
                    TextR = float.TryParse(Get(iTextR), out var tr) ? tr : 1f,
                    TextG = float.TryParse(Get(iTextG), out var tg) ? tg : 1f,
                    TextB = float.TryParse(Get(iTextB), out var tb) ? tb : 1f,
                    Alpha = float.TryParse(Get(iAlpha), out var al) ? al : 1f,
                };

                string cid = Get(iColorId);
                string tcid = Get(iTextColorId);
                if (!string.IsNullOrEmpty(cid)) { var c = Colors.Get(cid); panel.R = c.R; panel.G = c.G; panel.B = c.B; panel.Alpha = c.Alpha; }
                if (!string.IsNullOrEmpty(tcid)) { var c = Colors.Get(tcid); panel.TextR = c.R; panel.TextG = c.G; panel.TextB = c.B; }

                _panels.Add(panel);

                var world = Object.ECSWorld; // Changed from ObjectLoader
                var e = world.CreateEntity();
                world.AddComponent(e, new TransformComponent
                {
                    Position = new Vector3(panel.X + panel.Width * 0.5f, panel.Y + panel.Height * 0.5f, 0),
                    Scale = new Vector3(panel.Width, panel.Height, 1),
                    Rotation = Quaternion.Identity
                });
                world.AddComponent(e, new PanelComponent
                {
                    Id = panel.Id,
                    Visible = panel.Visible,
                    Layer = panel.Layer,
                    Alpha = panel.Alpha,
                    Clickable = panel.Clickable,
                    TextId = panel.Text
                });
                world.AddComponent(e, new MaterialComponent
                {
                    PipelineId = "rect_pipeline",
                    Color = new Vector4(panel.R, panel.G, panel.B, panel.Alpha)
                });
            }

            _panelDict = _panels.ToDictionary(p => p.Id);
            Console.WriteLine($"[Panels] Loaded {_panels.Count} panels");
        }

        public static void UpdateLayout(int windowWidth, int windowHeight)
        {
            _windowWidth = windowWidth;
            _windowHeight = windowHeight;
        }

        public static void SetPanelProperty(string panelId, string prop, float value)
        {
            if (_panelDict.TryGetValue(panelId, out var panel))
            {
                switch (prop)
                {
                    case "x": panel.X = value; break;
                    case "y": panel.Y = value; break;
                    case "width": panel.Width = value; break;
                    case "height": panel.Height = value; break;
                }
            }
        }
    }
}
