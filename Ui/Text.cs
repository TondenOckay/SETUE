using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.Systems
{
    public static class Texts
    {
        private static Dictionary<int, float> _panelNextYOffset = new();

        // Pre-registered common string IDs
        private static int _alignLeftId;
        private static int _alignCenterId;
        private static int _alignRightId;
        private static int _valignTopId;
        private static int _valignMiddleId;
        private static int _valignBottomId;
        private static int _sourcePositionId;
        private static int _sourceRotationId;
        private static int _sourceScaleId;
        private static int _defaultFontId;

        public static void Load()
        {
            // Pre-register common strings
            _alignLeftId = StringRegistry.GetOrAdd("left");
            _alignCenterId = StringRegistry.GetOrAdd("center");
            _alignRightId = StringRegistry.GetOrAdd("right");
            _valignTopId = StringRegistry.GetOrAdd("top");
            _valignMiddleId = StringRegistry.GetOrAdd("middle");
            _valignBottomId = StringRegistry.GetOrAdd("bottom");
            _sourcePositionId = StringRegistry.GetOrAdd("position");
            _sourceRotationId = StringRegistry.GetOrAdd("rotation");
            _sourceScaleId = StringRegistry.GetOrAdd("scale");
            _defaultFontId = StringRegistry.GetOrAdd("default");

            string path = "Ui/Text.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Texts] File not found: {path}");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            Console.WriteLine($"[Texts] Headers: {string.Join(" | ", headers)}");

            int iId        = Array.IndexOf(headers, "id");
            int iPanelId   = Array.IndexOf(headers, "panel_id");
            int iText      = Array.IndexOf(headers, "text");
            int iFontId    = Array.IndexOf(headers, "font_id");
            int iColorId   = Array.IndexOf(headers, "color_id");
            int iAlign     = Array.IndexOf(headers, "align");
            int iLayer     = Array.IndexOf(headers, "layer");
            int iRotation  = Array.IndexOf(headers, "rotation");
            int iSource    = Array.IndexOf(headers, "source");
            int iPrefix    = Array.IndexOf(headers, "prefix");
            int iPadLeft   = Array.IndexOf(headers, "pad_left");
            int iPadTop    = Array.IndexOf(headers, "pad_top");
            int iLineHeight= Array.IndexOf(headers, "line_height");
            int iVAlign    = Array.IndexOf(headers, "valign");

            var world = Object.ECSWorld;
            _panelNextYOffset.Clear();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');

                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                string idStr = Get(iId);
                string panelIdStr = Get(iPanelId);
                string contentStr = Get(iText);
                string fontIdStr = string.IsNullOrEmpty(Get(iFontId)) ? "default" : Get(iFontId);
                string alignStr = string.IsNullOrEmpty(Get(iAlign)) ? "center" : Get(iAlign);
                int layer = int.TryParse(Get(iLayer), out var l) ? l : 10;
                float rotation = float.TryParse(Get(iRotation), out var rot) ? rot : 0f;
                string sourceStr = Get(iSource);
                string prefixStr = Get(iPrefix);

                float padLeft = float.TryParse(Get(iPadLeft), out var pl) ? pl : 10f;
                float padTop = float.TryParse(Get(iPadTop), out var pt) ? pt : 10f;
                float lineHeight = float.TryParse(Get(iLineHeight), out var lh) ? lh : 20f;
                string valignStr = Get(iVAlign);
                if (string.IsNullOrEmpty(valignStr)) valignStr = "top";

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
                    Id = StringRegistry.GetOrAdd(idStr),
                    ContentId = StringRegistry.GetOrAdd(contentStr),
                    FontId = StringRegistry.GetOrAdd(fontIdStr),
                    FontSize = 16f,
                    Color = color,
                    Align = StringRegistry.GetOrAdd(alignStr),
                    Rotation = rotation,
                    Layer = layer,
                    Source = StringRegistry.GetOrAdd(sourceStr),
                    Prefix = StringRegistry.GetOrAdd(prefixStr),
                    PanelId = StringRegistry.GetOrAdd(panelIdStr),
                    PadLeft = padLeft,
                    PadTop = padTop,
                    LineHeight = lineHeight,
                    VAlign = StringRegistry.GetOrAdd(valignStr)
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
            var world = Object.ECSWorld;
            Entity? selectedEntity = null;
            foreach (var e in world.Query<SelectedComponent>())
            {
                selectedEntity = e;
                break;
            }

            var panelTexts = new Dictionary<int, List<Entity>>();
            foreach (var e in world.Query<TextComponent>())
            {
                var text = world.GetComponent<TextComponent>(e);
                if (text.PanelId != 0)
                {
                    if (!panelTexts.ContainsKey(text.PanelId))
                        panelTexts[text.PanelId] = new List<Entity>();
                    panelTexts[text.PanelId].Add(e);
                }
            }

            _panelNextYOffset.Clear();

            foreach (var kvp in panelTexts)
            {
                int panelId = kvp.Key;
                var entities = kvp.Value;

                Entity? panelEntity = null;
                PanelComponent panelComp = default;
                TransformComponent panelTrans = default;
                foreach (var (pe, pc, pt) in world.Query<PanelComponent, TransformComponent>())
                {
                    if (pc.Id == panelId)
                    {
                        panelEntity = pe;
                        panelComp = pc;
                        panelTrans = pt;
                        break;
                    }
                }
                if (!panelEntity.HasValue || !panelComp.Visible) continue;

                Vector3 panelPos = panelTrans.Position;
                Vector3 panelScale = panelTrans.Scale;
                float panelLeft = panelPos.X - panelScale.X * 0.5f;
                float panelTop = panelPos.Y - panelScale.Y * 0.5f;
                float panelWidth = panelScale.X;
                float panelHeight = panelScale.Y;

                entities.Sort((a, b) =>
                {
                    var ta = world.GetComponent<TextComponent>(a);
                    var tb = world.GetComponent<TextComponent>(b);
                    return ta.Layer.CompareTo(tb.Layer);
                });

                var firstText = world.GetComponent<TextComponent>(entities[0]);
                int vAlignId = firstText.VAlign;

                bool isVertical = Math.Abs(firstText.Rotation) == 90 || Math.Abs(firstText.Rotation) == 270;

                float visualTop = 0f, visualBottom = 0f;
                float currentBaselineOffset = 0f;
                bool firstLine = true;

                foreach (var e in entities)
                {
                    var t = world.GetComponent<TextComponent>(e);
                    string fontIdStr = StringRegistry.GetString(t.FontId);
                    var font = SETUE.UI.Fonts.Get(fontIdStr);
                    if (font == null) continue;

                    float ascent = font.Ascent;
                    float descent = 0f;
                    var descentProp = font.GetType().GetProperty("Descent");
                    if (descentProp != null)
                        descent = (float)descentProp.GetValue(font);
                    else
                        descent = ascent * 0.25f;

                    string content = StringRegistry.GetString(t.ContentId);
                    if (isVertical)
                    {
                        float stringWidth = 0f;
                        foreach (char c in content)
                            if (font.Glyphs.TryGetValue(c, out var g))
                                stringWidth += g.AdvanceX;

                        if (firstLine)
                        {
                            visualTop = -stringWidth * 0.5f;
                            visualBottom = stringWidth * 0.5f;
                            firstLine = false;
                        }
                        else
                        {
                            currentBaselineOffset += t.LineHeight;
                            float lineTop = currentBaselineOffset - stringWidth * 0.5f;
                            float lineBottom = currentBaselineOffset + stringWidth * 0.5f;
                            if (lineTop < visualTop) visualTop = lineTop;
                            if (lineBottom > visualBottom) visualBottom = lineBottom;
                        }
                    }
                    else
                    {
                        if (firstLine)
                        {
                            visualTop = -ascent;
                            visualBottom = descent;
                            firstLine = false;
                        }
                        else
                        {
                            currentBaselineOffset += t.LineHeight;
                            float lineTop = currentBaselineOffset - ascent;
                            float lineBottom = currentBaselineOffset + descent;
                            if (lineTop < visualTop) visualTop = lineTop;
                            if (lineBottom > visualBottom) visualBottom = lineBottom;
                        }
                    }
                }

                float visualHeight = visualBottom - visualTop;

                float startY;
                if (vAlignId == _valignMiddleId)
                {
                    float blockTop = panelTop + (panelHeight - visualHeight) * 0.5f;
                    startY = blockTop - visualTop;
                    Console.WriteLine($"[Texts] Panel '{StringRegistry.GetString(panelId)}' MIDDLE: visualHeight={visualHeight:F1}, blockTop={blockTop:F1}, startY={startY:F1}");
                }
                else if (vAlignId == _valignBottomId)
                {
                    float blockTop = panelTop + panelHeight - visualHeight - firstText.PadTop;
                    startY = blockTop - visualTop;
                }
                else // top
                {
                    startY = panelTop + firstText.PadTop - visualTop;
                }

                currentBaselineOffset = 0f;
                foreach (var e in entities)
                {
                    var text = world.GetComponent<TextComponent>(e);
                    var transform = world.GetComponent<TransformComponent>(e);

                    if (text.Source != 0)
                    {
                        string newContent = StringRegistry.GetString(text.ContentId);
                        if (selectedEntity != null && world.HasComponent<TransformComponent>(selectedEntity.Value))
                        {
                            var selTrans = world.GetComponent<TransformComponent>(selectedEntity.Value);
                            string prefix = StringRegistry.GetString(text.Prefix);
                            if (text.Source == _sourcePositionId)
                                newContent = $"{prefix} {selTrans.Position.X:F3}, {selTrans.Position.Y:F3}, {selTrans.Position.Z:F3}";
                            else if (text.Source == _sourceRotationId)
                                newContent = $"{prefix} {selTrans.Rotation.X:F2}, {selTrans.Rotation.Y:F2}, {selTrans.Rotation.Z:F2}, {selTrans.Rotation.W:F2}";
                            else if (text.Source == _sourceScaleId)
                                newContent = $"{prefix} {selTrans.Scale.X:F3}, {selTrans.Scale.Y:F3}, {selTrans.Scale.Z:F3}";
                            else
                                newContent = $"{prefix} ---";
                        }
                        else
                        {
                            newContent = $"{StringRegistry.GetString(text.Prefix)} ---";
                        }

                        int newContentId = StringRegistry.GetOrAdd(newContent);
                        if (newContentId != text.ContentId)
                        {
                            text.ContentId = newContentId;
                            world.SetComponent(e, text);
                        }
                    }

                    float y = startY + currentBaselineOffset;
                    float x = panelLeft + text.PadLeft;
                    if (text.Align == _alignCenterId)
                        x = panelLeft + panelWidth * 0.5f;
                    else if (text.Align == _alignRightId)
                        x = panelLeft + panelWidth - text.PadLeft;

                    transform.Position = new Vector3(x, y, 0);
                    world.SetComponent(e, transform);

                    currentBaselineOffset += text.LineHeight;
                }
            }
        }
    }
}
