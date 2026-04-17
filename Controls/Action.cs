using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SETUE.Core;
using SETUE.ECS;

namespace SETUE.Controls
{
    public class ActionRule
    {
        public int ParentNameId;
        public int ObjectNameId;
        public string MoveEdge = "";
        public float MinX = float.NaN;
        public float MaxX = float.NaN;
    }

    public class ActiveDrag
    {
        public string Script = "";
        public Vector2 LastMousePos;
        public Dictionary<Entity, string> MoveEdges = new();
        public Dictionary<Entity, (Vector3 pos, Vector3 scale)> Originals = new();
        public Dictionary<Entity, (float minX, float maxX)> Limits = new();
        public Dictionary<Entity, float> FixedEdgePositions = new();
        public Entity ParentEntity;
        public int ParentNameId;
    }

    public class ActionRequest
    {
        public int ParentNameId;
        public Vector2 MouseStartPos;
    }

    public static class Action
    {
        private static List<ActionRule> _rules = new();
        private static readonly Dictionary<Entity, ActiveDrag> _activeDrags = new();
        private static ActionRequest? _pendingRequest;

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
                    ParentNameId = StringRegistry.GetOrAdd(Get(p)),
                    ObjectNameId = StringRegistry.GetOrAdd(Get(o)),
                    MoveEdge = Get(e),
                    MinX = float.TryParse(Get(min), out var mn) ? mn : float.NaN,
                    MaxX = float.TryParse(Get(max), out var mx) ? mx : float.NaN
                });
            }
            Console.WriteLine($"[Action] Loaded {_rules.Count} rules");
        }

        public static void CreateRequest(int parentNameId, Vector2 mousePos)
        {
            _pendingRequest = new ActionRequest
            {
                ParentNameId = parentNameId,
                MouseStartPos = mousePos
            };
        }

        public static void Update()
        {
            var world = Object.ECSWorld;

            // Process pending request
            if (_pendingRequest != null)
            {
                StartDrag(world, _pendingRequest.ParentNameId, _pendingRequest.MouseStartPos);
                _pendingRequest = null;
            }

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

                // Update last mouse position EVERY frame, even if no movement
                drag.LastMousePos = currentMouse;

                if (Math.Abs(rawDeltaX) > 0.001f || Math.Abs(rawDeltaY) > 0.001f)
                {
                    // Calculate intended delta using Movement (applies sensitivity, snapping, axis)
                    Vector3 intendedDelta = Movement.CalculateDelta(drag.Script, rawDeltaX, rawDeltaY);

                    var parentTrans = world.GetComponent<TransformComponent>(drag.ParentEntity);
                    var parentOrig = drag.Originals[drag.ParentEntity];

                    // Apply delta, then clamp final position
                    float newParentX = parentTrans.Position.X + intendedDelta.X;
                    float newParentY = parentTrans.Position.Y + intendedDelta.Y;

                    var limits = drag.Limits.GetValueOrDefault(drag.ParentEntity);
                    if (!float.IsNaN(limits.minX)) newParentX = Math.Max(newParentX, limits.minX);
                    if (!float.IsNaN(limits.maxX)) newParentX = Math.Min(newParentX, limits.maxX);

                    parentTrans.Position = new Vector3(newParentX, newParentY, parentTrans.Position.Z);
                    world.SetComponent(drag.ParentEntity, parentTrans);

                    // Calculate actual movement that occurred (after clamping)
                    float actualDeltaX = parentTrans.Position.X - parentOrig.pos.X;
                    float actualDeltaY = parentTrans.Position.Y - parentOrig.pos.Y;

                    // Move followers
                    foreach (var moveKv in drag.MoveEdges)
                    {
                        var entity = moveKv.Key;
                        if (entity.Equals(drag.ParentEntity)) continue;

                        var edge = moveKv.Value;
                        var trans = world.GetComponent<TransformComponent>(entity);
                        var orig = drag.Originals[entity];
                        var entLimits = drag.Limits.GetValueOrDefault(entity);

                        if (edge == "all")
                        {
                            float newX = orig.pos.X + actualDeltaX;
                            float newY = orig.pos.Y + actualDeltaY;
                            if (!float.IsNaN(entLimits.minX)) newX = Math.Max(newX, entLimits.minX);
                            if (!float.IsNaN(entLimits.maxX)) newX = Math.Min(newX, entLimits.maxX);
                            trans.Position = new Vector3(newX, newY, trans.Position.Z);
                        }
                        else
                        {
                            float fixedEdgePos = drag.FixedEdgePositions[entity];
                            AdjustEdgeFromParent(ref trans, orig, parentTrans, parentOrig, edge, entLimits, fixedEdgePos);
                        }

                        world.SetComponent(entity, trans);
                    }
                }
            }

            foreach (var e in toRemove)
            {
                _activeDrags.Remove(e);
                Console.WriteLine("[Action] Ended drag.");
            }
        }

        private static void StartDrag(World world, int parentNameId, Vector2 mousePos)
        {
            Entity? parentEntity = null;
            foreach (var e in world.Query<PanelComponent>())
            {
                var panel = world.GetComponent<PanelComponent>(e);
                if (panel.Id == parentNameId)
                {
                    parentEntity = e;
                    break;
                }
            }
            if (parentEntity == null) return;

            var drag = new ActiveDrag
            {
                ParentNameId = parentNameId,
                ParentEntity = parentEntity.Value,
                Script = "slide_x",
                LastMousePos = mousePos
            };

            var parentTrans = world.GetComponent<TransformComponent>(parentEntity.Value);
            drag.MoveEdges[parentEntity.Value] = "parent";
            drag.Originals[parentEntity.Value] = (parentTrans.Position, parentTrans.Scale);

            foreach (var rule in _rules)
            {
                if (rule.ParentNameId != parentNameId) continue;

                if (rule.ObjectNameId == parentNameId)
                    drag.Script = "slide_x";

                foreach (var e in world.Query<PanelComponent>())
                {
                    var panel = world.GetComponent<PanelComponent>(e);
                    if (panel.Id != rule.ObjectNameId) continue;
                    if (e.Equals(parentEntity.Value)) continue;

                    var trans = world.GetComponent<TransformComponent>(e);
                    drag.MoveEdges[e] = rule.MoveEdge;
                    drag.Originals[e] = (trans.Position, trans.Scale);
                    drag.Limits[e] = (rule.MinX, rule.MaxX);

                    if (rule.MoveEdge != "all")
                    {
                        float fixedEdge = rule.MoveEdge switch
                        {
                            "left"   => trans.Position.X + trans.Scale.X / 2f,
                            "right"  => trans.Position.X - trans.Scale.X / 2f,
                            "top"    => trans.Position.Y + trans.Scale.Y / 2f,
                            "bottom" => trans.Position.Y - trans.Scale.Y / 2f,
                            _ => 0f
                        };
                        drag.FixedEdgePositions[e] = fixedEdge;
                    }
                    break;
                }
            }

            foreach (var rule in _rules)
            {
                if (rule.ParentNameId == parentNameId && rule.ObjectNameId == parentNameId)
                {
                    drag.Limits[parentEntity.Value] = (rule.MinX, rule.MaxX);
                    break;
                }
            }

            _activeDrags[parentEntity.Value] = drag;
            Console.WriteLine($"[Action] Started drag on '{StringRegistry.GetString(parentNameId)}' with {drag.MoveEdges.Count - 1} followers.");
        }

        public static void EndDrag(int parentNameId)
        {
            Entity? toRemove = null;
            foreach (var kv in _activeDrags)
            {
                if (kv.Value.ParentNameId == parentNameId)
                {
                    toRemove = kv.Key;
                    break;
                }
            }
            if (toRemove.HasValue)
            {
                _activeDrags.Remove(toRemove.Value);
                Console.WriteLine("[Action] Ended drag.");
            }
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

                if (!float.IsNaN(limits.min)) newCenter = Math.Max(newCenter, limits.min);
                if (!float.IsNaN(limits.max)) newCenter = Math.Min(newCenter, limits.max);

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

                if (!float.IsNaN(limits.min)) newCenter = Math.Max(newCenter, limits.min);
                if (!float.IsNaN(limits.max)) newCenter = Math.Min(newCenter, limits.max);

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
