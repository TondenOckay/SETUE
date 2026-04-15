using System;
using System.IO;
using System.Collections.Generic;
using SkiaSharp;
using Silk.NET.Vulkan;

namespace SETUE.UI
{
    public struct GlyphInfo
    {
        public float U0, V0, U1, V1;
        public float Width, Height;
        public float AdvanceX;
        public float BearingX, BearingY;
    }

    public class Font
    {
        public string FontId      { get; private set; } = "";
        public int    AtlasWidth  { get; private set; } = 512;
        public int    AtlasHeight { get; private set; } = 512;
        public byte[] Pixels      { get; private set; } = Array.Empty<byte>();
        public Dictionary<char, GlyphInfo> Glyphs { get; } = new();
        public float FontSize     { get; private set; } = 16f;

        public float LineHeight { get; private set; }
        public float Ascent     { get; private set; }
        public float Descent    { get; private set; }

        // Vulkan resources – will be set by Vulkan_Helper
        public ImageView ImageView { get; set; }
        public Sampler   Sampler   { get; set; }
        public DescriptorSet DescriptorSet { get; set; }

        public bool Build(string fontId, string fontPath, float fontSize, float glyphSpacing, float rowPadding)
        {
            FontId   = fontId;
            FontSize = fontSize;

            if (!Path.IsPathRooted(fontPath))
                fontPath = Path.Combine(AppContext.BaseDirectory, fontPath);

            if (!File.Exists(fontPath))
            { Console.WriteLine($"[Font] Font not found: {fontPath}"); return false; }

            using var typeface = SKTypeface.FromFile(fontPath);
            using var font     = new SKFont(typeface, fontSize);
            font.Edging   = SKFontEdging.Alias;
            font.Hinting  = SKFontHinting.Full;
            font.Subpixel = false;
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };

            font.GetFontMetrics(out var metrics);
            Ascent     = -metrics.Ascent;
            Descent    =  metrics.Bottom;
            LineHeight =  Ascent + Descent + rowPadding;

            const string chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

            using var bitmap = new SKBitmap(AtlasWidth, AtlasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Empty);

            float x = 1f, y = (float)Math.Ceiling(Ascent);

            foreach (char c in chars)
            {
                string s      = c.ToString();
                float  adv    = font.MeasureText(s, out SKRect bounds);
                float  slotW  = (float)Math.Ceiling(adv) + glyphSpacing;
                if (x + slotW > AtlasWidth) { x = 1f; y += (float)Math.Ceiling(LineHeight); }

                canvas.DrawText(s, (float)Math.Floor(x), (float)Math.Floor(y), SKTextAlign.Left, font, paint);

                float ix      = (float)Math.Floor(x);
                float iy      = (float)Math.Floor(y);
                float iAdv    = (float)Math.Ceiling(adv);
                float slotTop = iy - Ascent;

                Glyphs[c] = new GlyphInfo
                {
                    U0       = ix / AtlasWidth,
                    V0       = slotTop / AtlasHeight,
                    U1       = (ix + iAdv) / AtlasWidth,
                    V1       = (slotTop + LineHeight) / AtlasHeight,
                    Width    = iAdv,
                    Height   = LineHeight,
                    AdvanceX = iAdv + glyphSpacing,
                    BearingX = bounds.Left,
                    BearingY = -bounds.Top
                };
                x += slotW;
            }

            var rgba = bitmap.Bytes;
            Pixels = new byte[AtlasWidth * AtlasHeight];
            for (int i = 0; i < Pixels.Length; i++)
                Pixels[i] = rgba[i * 4 + 3];

            // Debug output
            using (var dbgBmp = new SKBitmap(AtlasWidth, AtlasHeight, SKColorType.Rgba8888, SKAlphaType.Opaque))
            {
                for (int i = 0; i < Pixels.Length; i++)
                    dbgBmp.SetPixel(i % AtlasWidth, i / AtlasWidth, new SKColor(Pixels[i], Pixels[i], Pixels[i], 255));
                using var dbgImg  = SKImage.FromBitmap(dbgBmp);
                using var dbgData = dbgImg.Encode(SKEncodedImageFormat.Png, 100);
                using var dbgFs   = File.OpenWrite($"/tmp/atlas_{fontId}.png");
                dbgData.SaveTo(dbgFs);
            }

            Console.WriteLine($"[Font] Built atlas for '{fontId}' size={fontSize} lineH={LineHeight:F1} chars={Glyphs.Count} => /tmp/atlas_{fontId}.png");
            return true;
        }
    }

    public static class Fonts
    {
        public static Dictionary<string, Font> Atlases { get; } = new();

        public static void Load()
        {
            string csvPath = "Ui/Font.csv";
            if (!File.Exists(csvPath)) { Console.WriteLine($"[Fonts] Missing {csvPath}"); return; }
            var lines   = File.ReadAllLines(csvPath);
            var headers = lines[0].Split(',');
            int idxId      = Array.IndexOf(headers, "font_id");
            int idxPath    = Array.IndexOf(headers, "font_path");
            int idxSize    = Array.IndexOf(headers, "size");
            int idxSpacing = Array.IndexOf(headers, "glyph_spacing");
            int idxPadding = Array.IndexOf(headers, "row_padding");

            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Split(',');
                if (p.Length < 3) continue;
                string id      = p[idxId].Trim();
                string path    = p[idxPath].Trim();
                float  size    = float.Parse(p[idxSize].Trim());
                float  spacing = idxSpacing >= 0 && p.Length > idxSpacing ? float.Parse(p[idxSpacing].Trim()) : 1f;
                float  padding = idxPadding >= 0 && p.Length > idxPadding ? float.Parse(p[idxPadding].Trim()) : 4f;

                var atlas = new Font();
                if (atlas.Build(id, path, size, spacing, padding))
                {
                    // Upload font atlas to GPU and store descriptor set
                var ds = Vulkan.GetOrUploadAtlas(atlas, "text_pipeline");
                atlas.DescriptorSet = ds;
                Atlases[id] = atlas;
                }
            }
            Console.WriteLine($"[Fonts] Loaded {Atlases.Count} font(s)");
        }

        public static Font? Get(string id) =>
            Atlases.TryGetValue(id, out var a) ? a : null;
    }
}
