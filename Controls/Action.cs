using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Controls
{
    public class ActionRule
    {
        public string ParentName = "";
        public string ObjectName = "";
        public string MoveEdge = "";
        public float MinX = float.NaN;
        public float MaxX = float.NaN;
    }

    public struct ActiveDrag : IComponent
    {
        public string ParentName;
        public Entity ParentEntity;
        public Dictionary<Entity, string> MoveEdges;
        public Dictionary<Entity, (Vector3 pos, Vector3 scale)> Originals;
        public Dictionary<Entity, (float minX, float maxX)> Limits;
        public Dictionary<Entity, float> FixedEdgePositions;
    }

    public static class Action
    {
        private static List<ActionRule> _rules = new();

        public static void Load()
        {
            string path = "Controls/Action.csv";
            if (!File.Exists(path)) return;

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            int p = Array.IndexOf(headers, "parent_name");
            int o = Array.IndexOf(headers, "object_name");
            int e = Array.IndexOf(headers, "move_edge");
            int min = Array.IndexOf(headers, "min_x");
            int max = Array.IndexOf(headers, "max_x");

            _rules.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                _rules.Add(new ActionRule
                {
                    ParentName = Get(p),
                    ObjectName = Get(o),
                    MoveEdge = Get(e),
                    MinX = float.TryParse(Get(min), out var mn) ? mn : float.NaN,
                    MaxX = float.TryParse(Get(max), out var mx) ? mx : float.NaN
                });
            }
            Console.WriteLine($"[Action] Loaded {_rules.Count} rules");
        }

        public static void ProcessDrag(string parentName, Vector2 delta)
        {
            var world = Object.ECSWorld;

            Entity? dragEntity = null;
            ActiveDrag drag = default;
            foreach (var e in world.Query<ActiveDrag>())
            {
                var d = world.GetComponent<ActiveDrag>(e);
                if (d.ParentName == parentName)
                {
                    dragEntity = e;
                    drag = d;
                    break;
                }
            }

            if (!dragEntity.HasValue)
            {
                StartDrag(parentName);
                return;
            }

            // 1. Move the parent FIRST (direct delta, clamped)
            var parentTrans = world.GetComponent<TransformComponent>(drag.ParentEntity);
            var parentOrig = drag.Originals[drag.ParentEntity];
            float intendedParentX = parentOrig.pos.X + delta.X;
            float newParentX = Clamp(intendedParentX, drag.Limits[drag.ParentEntity].minX, drag.Limits[drag.ParentEntity].maxX);
            parentTrans.Position = new Vector3(newParentX, parentOrig.pos.Y, parentOrig.pos.Z);
            world.SetComponent(drag.ParentEntity, parentTrans);

            // Calculate the ACTUAL movement the parent underwent (after clamping)
            float actualParentDeltaX = parentTrans.Position.X - parentOrig.pos.X;

            // 2. Move all followers based on the PARENT'S NEW POSITION and ACTUAL DELTA
            foreach (var kv in drag.MoveEdges)
            {
                var entity = kv.Key;
                if (entity.Equals(drag.ParentEntity)) continue;

                var edge = kv.Value;
                var trans = world.GetComponent<TransformComponent>(entity);
                var orig = drag.Originals[entity];
                var limits = drag.Limits.GetValueOrDefault(entity);

                if (edge == "all")
                {
                    // Use the actual delta the parent moved, not the intended one
                    float newX = orig.pos.X + actualParentDeltaX;
                    newX = Clamp(newX, limits.minX, limits.maxX);
                    trans.Position = new Vector3(newX, orig.pos.Y, orig.pos.Z);
                }
                else
                {
                    float fixedEdgePos = drag.FixedEdgePositions[entity];
                    AdjustEdgeFromParent(ref trans, orig, parentTrans, parentOrig, edge, limits, fixedEdgePos);
                }

                world.SetComponent(entity, trans);
            }
        }

        public static void StartDrag(string parentName)
        {
            var world = Object.ECSWorld;

            Entity? parentEntity = null;
            foreach (var e in world.Query<PanelComponent>())
                if (world.GetComponent<PanelComponent>(e).Id == parentName)
                { parentEntity = e; break; }
            if (parentEntity == null) return;

            var moveEdges = new Dictionary<Entity, string>();
            var originals = new Dictionary<Entity, (Vector3, Vector3)>();
            var limits = new Dictionary<Entity, (float min, float max)>();
            var fixedEdgePositions = new Dictionary<Entity, float>();

            foreach (var rule in _rules)
            {
                if (rule.ParentName != parentName) continue;

                foreach (var e in world.Query<PanelComponent>())
                {
                    if (world.GetComponent<PanelComponent>(e).Id != rule.ObjectName) continue;

                    var trans = world.GetComponent<TransformComponent>(e);
                    moveEdges[e] = rule.MoveEdge;
                    originals[e] = (trans.Position, trans.Scale);
                    limits[e] = (rule.MinX, rule.MaxX);

                    if (rule.MoveEdge != "all")
                    {
                        float fixedEdge = 0f;
                        switch (rule.MoveEdge)
                        {
                            case "left":   fixedEdge = trans.Position.X + trans.Scale.X / 2f; break;
                            case "right":  fixedEdge = trans.Position.X - trans.Scale.X / 2f; break;
                            case "top":    fixedEdge = trans.Position.Y + trans.Scale.Y / 2f; break;
                            case "bottom": fixedEdge = trans.Position.Y - trans.Scale.Y / 2f; break;
                        }
                        fixedEdgePositions[e] = fixedEdge;
                    }
                    break;
                }
            }

            world.AddComponent(parentEntity.Value, new ActiveDrag
            {
                ParentName = parentName,
                ParentEntity = parentEntity.Value,
                MoveEdges = moveEdges,
                Originals = originals,
                Limits = limits,
                FixedEdgePositions = fixedEdgePositions
            });

            Console.WriteLine($"[Action] Started drag on '{parentName}' with {moveEdges.Count - 1} followers.");
        }

        public static void EndDrag(string parentName)
        {
            var world = Object.ECSWorld;
            foreach (var e in world.Query<ActiveDrag>())
                if (world.GetComponent<ActiveDrag>(e).ParentName == parentName)
                    world.RemoveComponent<ActiveDrag>(e);
        }

        private static void AdjustEdgeFromParent(ref TransformComponent follower,
            (Vector3 pos, Vector3 scale) followerOrig,
            TransformComponent parentTrans,
            (Vector3 pos, Vector3 scale) parentOrig,
            string edge,
            (float min, float max) limits,
            float fixedEdgePosition)
        {
            bool isHorizontal = (edge == "left" || edge == "right");
            bool attachedToRightSide = (edge == "left");

            if (isHorizontal)
            {
                float parentAttachEdge = attachedToRightSide
                    ? parentTrans.Position.X + parentTrans.Scale.X / 2f
                    : parentTrans.Position.X - parentTrans.Scale.X / 2f;

                float newSize = attachedToRightSide
                    ? fixedEdgePosition - parentAttachEdge
                    : parentAttachEdge - fixedEdgePosition;

                float newCenter = (parentAttachEdge + fixedEdgePosition) / 2f;

                newCenter = Clamp(newCenter, limits.min, limits.max);
                follower.Position.X = newCenter;
                follower.Scale.X = Math.Max(newSize, 1f);
            }
            else
            {
                bool attachedToBottomSide = (edge == "top");

                float parentAttachEdge = attachedToBottomSide
                    ? parentTrans.Position.Y + parentTrans.Scale.Y / 2f
                    : parentTrans.Position.Y - parentTrans.Scale.Y / 2f;

                float newSize = attachedToBottomSide
                    ? fixedEdgePosition - parentAttachEdge
                    : parentAttachEdge - fixedEdgePosition;

                float newCenter = (parentAttachEdge + fixedEdgePosition) / 2f;

                newCenter = Clamp(newCenter, limits.min, limits.max);
                follower.Position.Y = newCenter;
                follower.Scale.Y = Math.Max(newSize, 1f);
            }
        }

        private static float Clamp(float value, float min, float max)
        {
            if (!float.IsNaN(min)) value = Math.Max(value, min);
            if (!float.IsNaN(max)) value = Math.Min(value, max);
            return value;
        }
    }
}
