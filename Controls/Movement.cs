using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.Controls
{
    public enum AxisConstraint { None, X, Y, Z, XY, XZ, YZ }

    public class MovementRule
    {
        public string Id = "";
        public AxisConstraint AxisConstraint;
        public bool SnapEnabled;
        public float SnapValue;
        public float Sensitivity = 1.0f;
    }

    public static class Movement
    {
        private static Dictionary<string, MovementRule> _rules = new();
        private static readonly Dictionary<Entity, ActiveDrag> _activeDrags = new();

        private class ActiveDrag
        {
            public Vector2 LastMousePos;
            public Entity ParentEntity;
            public MovementRule Rule = null!;
            public float MinX, MaxX;
            public Dictionary<Entity, string> FollowerEdges = new();
            public Dictionary<Entity, Vector3> OriginalPositions = new();
            public Dictionary<Entity, Vector3> OriginalScales = new();
            public Dictionary<Entity, float> FixedEdgePositions = new();
        }

        public static void Load()
        {
            string path = "Controls/Movement.csv";
            _rules.Clear();
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Split(',');
                if (p.Length < 5) continue;
                _rules[p[0]] = new MovementRule
                {
                    Id = p[0],
                    AxisConstraint = Enum.TryParse<AxisConstraint>(p[1], out var ax) ? ax : AxisConstraint.X,
                    SnapEnabled = p[2].ToLower() == "true",
                    SnapValue = float.TryParse(p[3], out var sv) ? sv : 1f,
                    Sensitivity = float.TryParse(p[4], out var sens) ? sens : 1f
                };
            }
            Console.WriteLine($"[MOVEMENT] Loaded {_rules.Count} rules");
        }

        public static bool RuleExists(string ruleId) => _rules.ContainsKey(ruleId);

        public static Vector3 CalculateDelta(string ruleId, float rawDeltaX, float rawDeltaY)
        {
            if (!_rules.TryGetValue(ruleId, out var rule))
                return new Vector3(rawDeltaX, 0, 0);

            float amountX = rawDeltaX * rule.Sensitivity;
            float amountY = rawDeltaY * rule.Sensitivity;

            if (rule.SnapEnabled && rule.SnapValue > 0)
            {
                amountX = MathF.Round(amountX / rule.SnapValue) * rule.SnapValue;
                amountY = MathF.Round(amountY / rule.SnapValue) * rule.SnapValue;
            }

            return rule.AxisConstraint switch
            {
                AxisConstraint.X => new Vector3(amountX, 0, 0),
                AxisConstraint.Y => new Vector3(0, amountY, 0),
                AxisConstraint.Z => new Vector3(0, 0, amountX),
                AxisConstraint.XY => new Vector3(amountX, amountY, 0),
                AxisConstraint.XZ => new Vector3(amountX, 0, amountX),
                AxisConstraint.YZ => new Vector3(0, amountY, amountY),
                _ => new Vector3(amountX, 0, 0)
            };
        }

        public static void StartDrag(int panelId, Vector2 mousePos, string ruleId)
        {
            Console.WriteLine($"[MOVEMENT] ========== StartDrag ==========");
            Console.WriteLine($"[MOVEMENT] panelId={panelId} ('{StringRegistry.GetString(panelId)}') rule='{ruleId}'");

            var world = Object.ECSWorld;

            Console.WriteLine("[MOVEMENT] All DragComponents in ECS:");
            world.ForEach<DragComponent>((Entity e) =>
            {
                var d = world.GetComponent<DragComponent>(e);
                string panelName = "?";
                if (world.HasComponent<PanelComponent>(e))
                    panelName = StringRegistry.GetString(world.GetComponent<PanelComponent>(e).Id);
                Console.WriteLine($"[MOVEMENT]   Entity {e}: Panel='{panelName}' ParentNameId={d.ParentNameId} MoveEdge='{StringRegistry.GetString(d.MoveEdge)}'");
            });

            Entity? parentEntity = null;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (p.Id == panelId) parentEntity = e;
            });
            if (parentEntity == null)
            {
                Console.WriteLine($"[MOVEMENT] Panel entity not found for ID {panelId}");
                return;
            }

            if (!world.HasComponent<DragComponent>(parentEntity.Value))
            {
                Console.WriteLine("[MOVEMENT] Parent panel has no DragComponent.");
                return;
            }

            if (!_rules.TryGetValue(ruleId, out var rule))
            {
                Console.WriteLine($"[MOVEMENT] Rule '{ruleId}' not found.");
                return;
            }

            var dragComp = world.GetComponent<DragComponent>(parentEntity.Value);
            var drag = new ActiveDrag
            {
                ParentEntity = parentEntity.Value,
                LastMousePos = mousePos,
                Rule = rule,
                MinX = dragComp.MinX,
                MaxX = dragComp.MaxX
            };

            var parentTrans = world.GetComponent<TransformComponent>(parentEntity.Value);
            drag.OriginalPositions[parentEntity.Value] = parentTrans.Position;
            drag.OriginalScales[parentEntity.Value] = parentTrans.Scale;

            int followerCount = 0;
            world.ForEach<PanelComponent>((Entity e) =>
            {
                var p = world.GetComponent<PanelComponent>(e);
                if (!world.HasComponent<DragComponent>(e)) return;
                var d = world.GetComponent<DragComponent>(e);
                if (d.ParentNameId == panelId)
                {
                    var trans = world.GetComponent<TransformComponent>(e);
                    drag.OriginalPositions[e] = trans.Position;
                    drag.OriginalScales[e] = trans.Scale;
                    string edge = StringRegistry.GetString(d.MoveEdge);
                    drag.FollowerEdges[e] = edge;
                    followerCount++;

                    if (edge != "all")
                    {
                        float fixedEdge = edge switch
                        {
                            "left"   => trans.Position.X + trans.Scale.X * 0.5f,
                            "right"  => trans.Position.X - trans.Scale.X * 0.5f,
                            "top"    => trans.Position.Y + trans.Scale.Y * 0.5f,
                            "bottom" => trans.Position.Y - trans.Scale.Y * 0.5f,
                            _ => 0f
                        };
                        drag.FixedEdgePositions[e] = fixedEdge;
                        Console.WriteLine($"[MOVEMENT]   Follower '{StringRegistry.GetString(p.Id)}' edge='{edge}' fixed={fixedEdge}");
                    }
                    else
                    {
                        Console.WriteLine($"[MOVEMENT]   Follower '{StringRegistry.GetString(p.Id)}' edge='all'");
                    }
                }
            });

            Console.WriteLine($"[MOVEMENT] Found {followerCount} followers.");
            _activeDrags[parentEntity.Value] = drag;
        }

        public static void UpdateDrags()
        {
            var world = Object.ECSWorld;
            var toRemove = new List<Entity>();

            foreach (var kv in _activeDrags)
            {
                var parentEntity = kv.Key;
                var drag = kv.Value;

                if (!Input.IsActionHeld("select_object"))
                {
                    toRemove.Add(parentEntity);
                    continue;
                }

                Vector2 currentMouse = Input.MousePos;
                float rawDeltaX = currentMouse.X - drag.LastMousePos.X;
                float rawDeltaY = currentMouse.Y - drag.LastMousePos.Y;
                drag.LastMousePos = currentMouse;

                if (Math.Abs(rawDeltaX) > 0.001f || Math.Abs(rawDeltaY) > 0.001f)
                {
                    Vector3 intendedDelta = CalculateDelta(drag.Rule.Id, rawDeltaX, rawDeltaY);

                    var parentTrans = world.GetComponent<TransformComponent>(parentEntity);
                    var parentOrig = drag.OriginalPositions[parentEntity];

                    float newParentX = parentTrans.Position.X + intendedDelta.X;
                    float newParentY = parentTrans.Position.Y + intendedDelta.Y;

                    if (!float.IsNaN(drag.MinX)) newParentX = Math.Max(newParentX, drag.MinX);
                    if (!float.IsNaN(drag.MaxX)) newParentX = Math.Min(newParentX, drag.MaxX);

                    parentTrans.Position = new Vector3(newParentX, newParentY, parentTrans.Position.Z);
                    world.SetComponent(parentEntity, parentTrans);

                    float actualDeltaX = parentTrans.Position.X - parentOrig.X;
                    float actualDeltaY = parentTrans.Position.Y - parentOrig.Y;

                    foreach (var followerKv in drag.FollowerEdges)
                    {
                        var entity = followerKv.Key;
                        string edge = followerKv.Value;

                        var trans = world.GetComponent<TransformComponent>(entity);
                        var origPos = drag.OriginalPositions[entity];
                        var origScale = drag.OriginalScales[entity];

                        if (edge == "all")
                        {
                            trans.Position = new Vector3(origPos.X + actualDeltaX, origPos.Y + actualDeltaY, trans.Position.Z);
                        }
                        else
                        {
                            float fixedPos = drag.FixedEdgePositions[entity];
                            AdjustEdgeFromParent(ref trans, origPos, origScale, parentTrans.Position, drag.OriginalScales[parentEntity], edge, fixedPos);
                        }
                        world.SetComponent(entity, trans);
                    }
                }
            }

            foreach (var e in toRemove)
                _activeDrags.Remove(e);
        }

        private static void AdjustEdgeFromParent(ref TransformComponent follower,
            Vector3 origPos, Vector3 origScale,
            Vector3 parentPos, Vector3 parentScale,
            string edge, float fixedEdgePosition)
        {
            if (edge == "left" || edge == "right")
            {
                float parentAttachEdge = (edge == "left")
                    ? parentPos.X + parentScale.X * 0.5f
                    : parentPos.X - parentScale.X * 0.5f;

                float newWidth = Math.Abs(parentAttachEdge - fixedEdgePosition);
                float newCenter = (parentAttachEdge + fixedEdgePosition) * 0.5f;

                follower.Scale.X = Math.Max(1f, newWidth);
                follower.Position.X = newCenter;
            }
            else if (edge == "top" || edge == "bottom")
            {
                float parentAttachEdge = (edge == "top")
                    ? parentPos.Y + parentScale.Y * 0.5f
                    : parentPos.Y - parentScale.Y * 0.5f;

                float newHeight = Math.Abs(parentAttachEdge - fixedEdgePosition);
                float newCenter = (parentAttachEdge + fixedEdgePosition) * 0.5f;

                follower.Scale.Y = Math.Max(1f, newHeight);
                follower.Position.Y = newCenter;
            }
        }
    }
}
