using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Systems
{
    public static class Texts
    {
        private static Dictionary<string, float> _panelNextYOffset = new();

        public static void Load()
        {
            string path = "Ui/Text.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Texts] File not found: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

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

            var world = Object.ECSWorld;
            _panelNextYOffset.Clear();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');

                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                string id = Get(iId);
                string panelId = Get(iPanelId);
                string content = Get(iText);
                string fontId = string.IsNullOrEmpty(Get(iFontId)) ? "default" : Get(iFontId);
                string align = string.IsNullOrEmpty(Get(iAlign)) ? "center" : Get(iAlign);
                int layer = int.TryParse(Get(iLayer), out var l) ? l : 10;
                float rotation = float.TryParse(Get(iRotation), out var rot) ? rot : 0f;
                string source = Get(iSource);
                string prefix = Get(iPrefix);

                Vector4 color = new Vector4(1, 1, 1, 1);
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

                if (!string.IsNullOrEmpty(panelId) && !_panelNextYOffset.ContainsKey(panelId))
                    _panelNextYOffset[panelId] = 0f;
            }

            Console.WriteLine($"[Texts] Loaded {world.Query<TextComponent>().Count()} text entities");
        }

        public static void Update()
        {
            var world = Object.ECSWorld;
            Entity? selectedEntity = null;
            foreach (var e in world.Query<SelectedComponent>())
            {
                selectedEntity = e;
                break;
            }

            _panelNextYOffset.Clear();

            foreach (var e in world.Query<TextComponent>())
            {
                var text = world.GetComponent<TextComponent>(e);
                var transform = world.GetComponent<TransformComponent>(e);

                if (!string.IsNullOrEmpty(text.Source))
                {
                    string newContent = text.Content;
                    if (selectedEntity != null && world.HasComponent<TransformComponent>(selectedEntity.Value))
                    {
                        var selTrans = world.GetComponent<TransformComponent>(selectedEntity.Value);
                        switch (text.Source)
                        {
                            case "position":
                                newContent = $"{text.Prefix} {selTrans.Position.X:F3}, {selTrans.Position.Y:F3}, {selTrans.Position.Z:F3}";
                                break;
                            case "rotation":
                                newContent = $"{text.Prefix} {selTrans.Rotation.X:F2}, {selTrans.Rotation.Y:F2}, {selTrans.Rotation.Z:F2}, {selTrans.Rotation.W:F2}";
                                break;
                            case "scale":
                                newContent = $"{text.Prefix} {selTrans.Scale.X:F3}, {selTrans.Scale.Y:F3}, {selTrans.Scale.Z:F3}";
                                break;
                            default:
                                newContent = $"{text.Prefix} ---";
                                break;
                        }
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

                if (!string.IsNullOrEmpty(text.PanelId))
                {
                    Entity? panelEntity = null;
                    PanelComponent panelComp = default;
                    TransformComponent panelTrans = default;
                    foreach (var (pe, pc, pt) in world.Query<PanelComponent, TransformComponent>())
                    {
                        if (pc.Id == text.PanelId)
                        {
                            panelEntity = pe;
                            panelComp = pc;
                            panelTrans = pt;
                            break;
                        }
                    }

                    if (panelEntity.HasValue && panelComp.Visible)
                    {
                        Vector3 panelPos = panelTrans.Position;
                        Vector3 panelScale = panelTrans.Scale;

                        float panelLeft   = panelPos.X - panelScale.X * 0.5f;
                        float panelTop    = panelPos.Y - panelScale.Y * 0.5f;
                        float panelWidth  = panelScale.X;
                        // float panelHeight = panelScale.Y; // not directly used here

                        if (!_panelNextYOffset.ContainsKey(text.PanelId))
                            _panelNextYOffset[text.PanelId] = 0f;
                        float yOffset = _panelNextYOffset[text.PanelId];
                        float lineHeight = 20f;
                        _panelNextYOffset[text.PanelId] += lineHeight;

                        float x = panelLeft + 10f;
                        if (text.Align == "center")
                            x = panelLeft + panelWidth * 0.5f;
                        else if (text.Align == "right")
                            x = panelLeft + panelWidth - 10f;

                        float y = panelTop + 10f + yOffset;

                        transform.Position = new Vector3(x, y, 0);
                        world.SetComponent(e, transform);
                    }
                }
            }
        }
    }
}
