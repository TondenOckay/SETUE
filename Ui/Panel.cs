using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.Systems
{
    public class Panel
    {
        public int Id { get; set; }
        public string IdString { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public Vector4 Color { get; set; } = new Vector4(1, 1, 1, 1);
        public bool Visible { get; set; }
        public int Layer { get; set; }
        public bool Clickable { get; set; }
        public float Alpha { get; set; } = 1f;
        public int TextId { get; set; }
        public string TextIdString { get; set; } = "";
        public int FontId { get; set; }
        public string FontIdString { get; set; } = "default";
        public Vector4 TextColor { get; set; } = new Vector4(1, 1, 1, 1);
        public string MoveEdge { get; set; } = "";
        public float MinX { get; set; } = float.NaN;
        public float MaxX { get; set; } = float.NaN;
        public string CallScript { get; set; } = "";
    }

    public static class Panels
    {
        private static List<Panel> _panels = new();
        private static Dictionary<int, Panel> _panelDict = new();

        public static IReadOnlyDictionary<int, Panel> All => _panelDict;

        public static void Load()
        {
            _panels.Clear();
            _panelDict.Clear();

            string path = "Ui/Panel.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Panels] File not found: {path}"); return; }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                colIndex[headers[i].Trim()] = i;

            string Get(string colName, string[] parts) =>
                colIndex.TryGetValue(colName, out int idx) && idx < parts.Length ? parts[idx].Trim() : "";

            var world = Object.ECSWorld;

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');

                string parentStr = Get("parent_name", parts);
                string objectStr = Get("object_name", parts);
                string leftStr   = Get("left", parts);

                if (string.IsNullOrEmpty(leftStr)) continue;

                string idStr = objectStr;
                if (string.IsNullOrEmpty(idStr)) continue;

                string textStr = Get("text", parts);
                string fontIdStr = Get("font_id", parts);
                if (string.IsNullOrEmpty(fontIdStr)) fontIdStr = "default";

                Vector4 color = new Vector4(0.5f, 0.5f, 0.5f, 1f);
                string cid = Get("color_id", parts);
                if (!string.IsNullOrEmpty(cid))
                {
                    var c = Colors.Get(cid);
                    color = new Vector4(c.R, c.G, c.B, c.Alpha);
                }

                Vector4 textColor = new Vector4(1, 1, 1, 1);
                string tcid = Get("text_color_id", parts);
                if (!string.IsNullOrEmpty(tcid))
                {
                    var c = Colors.Get(tcid);
                    textColor = new Vector4(c.R, c.G, c.B, 1f);
                }

                var panel = new Panel
                {
                    Id = StringRegistry.GetOrAdd(idStr),
                    IdString = idStr,
                    X = float.TryParse(leftStr, out float l) ? l : 0,
                    Width = float.TryParse(Get("right", parts), out float r) ? r - l : 0,
                    Y = float.TryParse(Get("top", parts), out float t) ? t : 0,
                    Height = float.TryParse(Get("bottom", parts), out float b) ? b - t : 0,
                    Color = color,
                    Visible = Get("visible", parts).ToLower() != "false",
                    Layer = int.TryParse(Get("layer", parts), out int layer) ? layer : 0,
                    Clickable = Get("clickable", parts).ToLower() == "true",
                    Alpha = float.TryParse(Get("alpha", parts), out float alpha) ? alpha : 1f,
                    TextId = StringRegistry.GetOrAdd(textStr),
                    TextIdString = textStr,
                    FontId = StringRegistry.GetOrAdd(fontIdStr),
                    FontIdString = fontIdStr,
                    TextColor = textColor,
                    MoveEdge = Get("move_edge", parts),
                    MinX = float.TryParse(Get("min_x", parts), out float minX) ? minX : float.NaN,
                    MaxX = float.TryParse(Get("max_x", parts), out float maxX) ? maxX : float.NaN,
                    CallScript = Get("call_script", parts)
                };

                _panels.Add(panel);
                _panelDict[panel.Id] = panel;

                Entity e = world.CreateEntity();

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
                    TextId = panel.TextId
                });

                world.AddComponent(e, new MaterialComponent
                {
                    PipelineId = StringRegistry.GetOrAdd("rect_pipeline"),
                    Color = panel.Color
                });

                if (panel.TextId != 0)
                {
                    world.AddComponent(e, new TextComponent
                    {
                        ContentId = panel.TextId,
                        FontId = panel.FontId,
                        FontSize = 16f,
                        Color = panel.TextColor,
                        Align = StringRegistry.GetOrAdd("center"),
                        Layer = panel.Layer + 1,
                        StyleId = 0,
                        PanelId = panel.Id   // FIX: link text to its parent panel for visibility control
                    });
                }

                var dragComp = new DragComponent();

                if (!string.IsNullOrEmpty(parentStr))
                    dragComp.ParentNameId = StringRegistry.GetOrAdd(parentStr);

                if (!string.IsNullOrEmpty(panel.CallScript))
                    dragComp.MovementId = StringRegistry.GetOrAdd(panel.CallScript);

                if (!string.IsNullOrEmpty(panel.MoveEdge))
                    dragComp.MoveEdge = StringRegistry.GetOrAdd(panel.MoveEdge);
                else if (!string.IsNullOrEmpty(parentStr))
                {
                    string edge = Get("move_edge", parts);
                    if (!string.IsNullOrEmpty(edge))
                        dragComp.MoveEdge = StringRegistry.GetOrAdd(edge);
                }

                dragComp.MinX = panel.MinX;
                dragComp.MaxX = panel.MaxX;

                if (dragComp.ParentNameId != 0 || dragComp.MovementId != 0 || dragComp.MoveEdge != 0)
                {
                    world.AddComponent(e, dragComp);
                    Console.WriteLine($"[Panels] DragComponent: {idStr} parentId={dragComp.ParentNameId} movementId={dragComp.MovementId} moveEdge={StringRegistry.GetString(dragComp.MoveEdge)}");
                }
            }

            world.ExecuteCommands();
            Console.WriteLine($"[Panels] Loaded {_panels.Count} visual panels.");
        }

        public static Panel? GetPanel(int id) => _panelDict.TryGetValue(id, out var p) ? p : null;

        // ---------------------------------------------------------------------
        // Action Methods (called via call_script)
        // ---------------------------------------------------------------------
        public static void ToggleVisibility(int panelId, Vector2 mousePos)
        {
            var world = Object.ECSWorld;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (p.Id == panelId)
                {
                    p.Visible = !p.Visible;
                    world.SetComponent(e, p);
                    bool newState = p.Visible;
                    Console.WriteLine($"[Panels] Toggled '{StringRegistry.GetString(panelId)}' to {newState}");

                    // Recursively set all children to the same visibility
                    SetChildrenVisibility(world, panelId, newState);
                }
            });
        }

        private static void SetChildrenVisibility(World world, int parentId, bool visible)
        {
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (world.HasComponent<DragComponent>(e))
                {
                    var d = world.GetComponent<DragComponent>(e);
                    if (d.ParentNameId == parentId)
                    {
                        p.Visible = visible;
                        world.SetComponent(e, p);
                        Console.WriteLine($"[Panels]   Set child '{StringRegistry.GetString(p.Id)}' to {visible}");
                        SetChildrenVisibility(world, p.Id, visible);
                    }
                }
            });
        }

        public static void FileNew(int panelId, Vector2 mousePos)   => Console.WriteLine("[Panels] File > New clicked");
        public static void FileOpen(int panelId, Vector2 mousePos)  => Console.WriteLine("[Panels] File > Open clicked");
        public static void FileSave(int panelId, Vector2 mousePos)  => Console.WriteLine("[Panels] File > Save clicked");
        public static void EditUndo(int panelId, Vector2 mousePos)  => Console.WriteLine("[Panels] Edit > Undo clicked");
        public static void EditRedo(int panelId, Vector2 mousePos)  => Console.WriteLine("[Panels] Edit > Redo clicked");
        public static void ToolsOptions(int panelId, Vector2 mousePos) => Console.WriteLine("[Panels] Tools > Options clicked");
    }
}
