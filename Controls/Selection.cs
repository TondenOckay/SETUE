using System;
using System.Collections.Generic;
using System.Numerics;
using SETUE.ECS;

namespace SETUE.Controls
{
    class SelectionRule
    {
        public string Id = "";
        public string InputAction = "";
        public string HitTestType = "";
        public string HitTestValue = "";
        public string OnClickOperation = "";
        public string DragTarget = "";
        public string DragProperty = "";
        public string DragInputSource = "";
        public float DragMultiplier = 1.0f;
        public float DragMin = float.NaN;
        public float DragMax = float.NaN;
        public string MoveWith = "";
        public string MoveEdge = "";
        public bool ConsumeInput;
        public bool RaycastEnabled;
    }

    public static class Selection
    {
        private static List<SelectionRule> _rules = new();
        private static SelectionRule? _activeDragRule;
        private static float _dragLastMouseX;
        private static Dictionary<Entity, (Vector3 pos, Vector3 scale)> _originalTransforms = new();
        private static Entity? _draggedEntity;

        public static bool LastHitWasPanel { get; private set; } = false;
        public static string LastHitPanelId { get; private set; } = "";

        public static void Load()
        {
            string path = "Controls/Selection.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Selection] Missing {path}"); return; }
            var lines = File.ReadAllLines(path);
            var headers = lines[0].Split(',');

            int idxId = Array.IndexOf(headers, "id");
            int idxAction = Array.IndexOf(headers, "input_action");
            int idxHitType = Array.IndexOf(headers, "hit_test_type");
            int idxHitValue = Array.IndexOf(headers, "hit_test_value");
            int idxOnClick = Array.IndexOf(headers, "on_click_operation");
            int idxDragTarget = Array.IndexOf(headers, "drag_target");
            int idxDragProp = Array.IndexOf(headers, "drag_property");
            int idxDragSrc = Array.IndexOf(headers, "drag_input_source");
            int idxDragMult = Array.IndexOf(headers, "drag_multiplier");
            int idxDragMin = Array.IndexOf(headers, "drag_min");
            int idxDragMax = Array.IndexOf(headers, "drag_max");
            int idxMoveWith = Array.IndexOf(headers, "move_with");
            int idxMoveEdge = Array.IndexOf(headers, "move_edge");
            int idxConsume = Array.IndexOf(headers, "consume_input");
            int idxRaycast = Array.IndexOf(headers, "raycast_enabled");

            _rules.Clear();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var p = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < p.Length ? p[idx].Trim() : "";

                _rules.Add(new SelectionRule
                {
                    Id = Get(idxId),
                    InputAction = Get(idxAction),
                    HitTestType = Get(idxHitType),
                    HitTestValue = Get(idxHitValue),
                    OnClickOperation = Get(idxOnClick),
                    DragTarget = Get(idxDragTarget),
                    DragProperty = Get(idxDragProp),
                    DragInputSource = Get(idxDragSrc),
                    DragMultiplier = float.TryParse(Get(idxDragMult), out var m) ? m : 1.0f,
                    DragMin = float.TryParse(Get(idxDragMin), out var min) ? min : float.NaN,
                    DragMax = float.TryParse(Get(idxDragMax), out var max) ? max : float.NaN,
                    MoveWith = Get(idxMoveWith),
                    MoveEdge = Get(idxMoveEdge),
                    ConsumeInput = Get(idxConsume).ToLower() == "true",
                    RaycastEnabled = Get(idxRaycast).ToLower() == "true"
                });
            }
            Console.WriteLine($"[Selection] Loaded {_rules.Count} rules");
        }

        public static void Update()
        {
            if (Input.IsEditing)
            {
                if (Input.EditConfirmed || Input.EditCancelled)
                {
                    if (Input.EditConfirmed)
                    {
                        Console.WriteLine($"[Selection] Edit confirmed: {Input.EditBuffer}");
                    }
                    Input.EndEdit();
                }
            }

            var world = Object.ECSWorld; // Changed from ObjectLoader

            if (_activeDragRule != null && _draggedEntity != null)
            {
                if (Input.IsActionHeld(_activeDragRule.InputAction))
                {
                    float rawDelta = Input.MousePos.X - _dragLastMouseX;
                    ApplyDrag(world, rawDelta);
                    _dragLastMouseX = Input.MousePos.X;
                }
                else
                {
                    _activeDragRule = null;
                    _draggedEntity = null;
                    _originalTransforms.Clear();
                    Console.WriteLine("[Selection] Drag ended");
                }
                if (_activeDragRule?.ConsumeInput == true)
                    Input.Consume(_activeDragRule.InputAction);
                return;
            }

            LastHitWasPanel = false;
            LastHitPanelId = "";

            foreach (var rule in _rules)
            {
                if (!Input.IsActionPressed(rule.InputAction)) continue;
                var mouse = Input.MousePos;

                Entity? hitPanelEntity = null;
                PanelComponent hitPanelComp = default;
                TransformComponent hitPanelTransform = default;

                if (rule.HitTestType == "panel_prefix")
                {
                    foreach (var (e, trans, panel) in world.Query<TransformComponent, PanelComponent>())
                    {
                        if (!panel.Visible) continue;
                        if (!panel.Id.StartsWith(rule.HitTestValue)) continue;

                        float left = trans.Position.X - trans.Scale.X * 0.5f;
                        float top = trans.Position.Y - trans.Scale.Y * 0.5f;
                        float right = left + trans.Scale.X;
                        float bottom = top + trans.Scale.Y;

                        if (mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom)
                        {
                            hitPanelEntity = e;
                            hitPanelComp = panel;
                            hitPanelTransform = trans;
                            break;
                        }
                    }
                }
                else if (rule.HitTestType == "viewport")
                {
                    bool overPanel = false;
                    foreach (var (e, trans, panel) in world.Query<TransformComponent, PanelComponent>())
                    {
                        if (!panel.Visible) continue;
                        float left = trans.Position.X - trans.Scale.X * 0.5f;
                        float top = trans.Position.Y - trans.Scale.Y * 0.5f;
                        float right = left + trans.Scale.X;
                        float bottom = top + trans.Scale.Y;
                        if (mouse.X >= left && mouse.X <= right && mouse.Y >= top && mouse.Y <= bottom)
                        {
                            overPanel = true;
                            break;
                        }
                    }
                    if (!overPanel) hitPanelEntity = null;
                    else continue;
                }

                if (hitPanelEntity != null || rule.HitTestType == "viewport")
                {
                    LastHitWasPanel = hitPanelEntity != null;
                    LastHitPanelId = hitPanelComp.Id ?? "";

                    switch (rule.OnClickOperation)
                    {
                        case "select_object":
                            if (rule.HitTestValue == "_st_" && hitPanelEntity != null)
                            {
                                string entityIdStr = hitPanelComp.Id.Substring(rule.HitTestValue.Length);
                                if (int.TryParse(entityIdStr, out int id))
                                {
                                    foreach (var (entity, _, _) in world.Query<TransformComponent, MeshComponent>())
                                    {
                                        if (entity.Id == id)
                                        {
                                            if (!world.HasComponent<SelectedComponent>(entity))
                                                world.AddComponent(entity, new SelectedComponent());
                                            else
                                                world.RemoveComponent<SelectedComponent>(entity);
                                            break;
                                        }
                                    }
                                }
                            }
                            break;
                        case "start_drag":
                            if (hitPanelEntity != null)
                            {
                                _activeDragRule = rule;
                                _draggedEntity = hitPanelEntity;
                                _dragLastMouseX = Input.MousePos.X;
                                var trans = world.GetComponent<TransformComponent>(_draggedEntity.Value);
                                _originalTransforms[_draggedEntity.Value] = (trans.Position, trans.Scale);

                                foreach (var (e, _, p) in world.Query<TransformComponent, PanelComponent>())
                                {
                                    if (p.Id == rule.MoveWith)
                                    {
                                        var t = world.GetComponent<TransformComponent>(e);
                                        _originalTransforms[e] = (t.Position, t.Scale);
                                    }
                                }
                                Console.WriteLine($"[Selection] Drag started on panel {hitPanelComp.Id}");
                            }
                            break;
                        case "raycast":
                            if (rule.RaycastEnabled)
                            {
                                var hitEntity = Raycast(world, mouse);
                                if (hitEntity != null)
                                {
                                    foreach (var e in world.Query<SelectedComponent>())
                                        world.RemoveComponent<SelectedComponent>(e);
                                    world.AddComponent(hitEntity.Value, new SelectedComponent());
                                }
                                else
                                {
                                    foreach (var e in world.Query<SelectedComponent>())
                                        world.RemoveComponent<SelectedComponent>(e);
                                }
                            }
                            break;
                    }

                    if (rule.ConsumeInput)
                    {
                        Input.Consume(rule.InputAction);
                        return;
                    }
                }
            }
        }

        private static void ApplyDrag(World world, float rawDelta)
        {
            if (_activeDragRule == null || _draggedEntity == null) return;
            var rule = _activeDragRule;
            var targetEntity = _draggedEntity.Value;
            if (!world.HasComponent<TransformComponent>(targetEntity)) return;

            var targetTransform = world.GetComponent<TransformComponent>(targetEntity);
            float delta = rawDelta * rule.DragMultiplier;
            if (rule.DragInputSource == "mouse_delta_x_neg") delta = -delta;

            Vector3 newPos = targetTransform.Position;
            Vector3 newScale = targetTransform.Scale;

            switch (rule.DragProperty)
            {
                case "x":
                    newPos.X += delta;
                    if (!float.IsNaN(rule.DragMin)) newPos.X = Math.Max(newPos.X, rule.DragMin);
                    if (!float.IsNaN(rule.DragMax)) newPos.X = Math.Min(newPos.X, rule.DragMax);
                    break;
                case "y":
                    newPos.Y += delta;
                    if (!float.IsNaN(rule.DragMin)) newPos.Y = Math.Max(newPos.Y, rule.DragMin);
                    if (!float.IsNaN(rule.DragMax)) newPos.Y = Math.Min(newPos.Y, rule.DragMax);
                    break;
                case "width":
                    newScale.X += delta;
                    if (!float.IsNaN(rule.DragMin)) newScale.X = Math.Max(newScale.X, rule.DragMin);
                    if (!float.IsNaN(rule.DragMax)) newScale.X = Math.Min(newScale.X, rule.DragMax);
                    break;
                case "height":
                    newScale.Y += delta;
                    if (!float.IsNaN(rule.DragMin)) newScale.Y = Math.Max(newScale.Y, rule.DragMin);
                    if (!float.IsNaN(rule.DragMax)) newScale.Y = Math.Min(newScale.Y, rule.DragMax);
                    break;
            }

            targetTransform.Position = newPos;
            targetTransform.Scale = newScale;
            world.SetComponent(targetEntity, targetTransform);

            foreach (var ruleF in _rules)
            {
                if (ruleF.DragTarget != rule.DragTarget) continue;
                if (string.IsNullOrEmpty(ruleF.MoveWith)) continue;

                Entity? followerEntity = null;
                foreach (var (e, _, p) in world.Query<TransformComponent, PanelComponent>())
                {
                    if (p.Id == ruleF.MoveWith)
                    {
                        followerEntity = e;
                        break;
                    }
                }
                if (followerEntity == null) continue;

                var followerTransform = world.GetComponent<TransformComponent>(followerEntity.Value);
                if (!_originalTransforms.TryGetValue(followerEntity.Value, out var orig))
                    orig = (followerTransform.Position, followerTransform.Scale);

                if (!_originalTransforms.TryGetValue(targetEntity, out var targetOrig))
                    targetOrig = (targetTransform.Position, targetTransform.Scale);

                switch (ruleF.MoveEdge)
                {
                    case "left":
                        float newLeft = targetTransform.Position.X + targetTransform.Scale.X * 0.5f;
                        followerTransform.Position = new Vector3(newLeft + followerTransform.Scale.X * 0.5f, followerTransform.Position.Y, 0);
                        followerTransform.Scale = new Vector3((orig.pos.X + orig.scale.X) - newLeft, followerTransform.Scale.Y, 1);
                        break;
                    case "right":
                        float newRight = targetTransform.Position.X - targetTransform.Scale.X * 0.5f;
                        followerTransform.Scale = new Vector3(newRight - (followerTransform.Position.X - followerTransform.Scale.X * 0.5f), followerTransform.Scale.Y, 1);
                        break;
                    case "top":
                        float newTop = targetTransform.Position.Y + targetTransform.Scale.Y * 0.5f;
                        followerTransform.Position = new Vector3(followerTransform.Position.X, newTop + followerTransform.Scale.Y * 0.5f, 0);
                        followerTransform.Scale = new Vector3(followerTransform.Scale.X, (orig.pos.Y + orig.scale.Y) - newTop, 1);
                        break;
                    case "bottom":
                        float newBottom = targetTransform.Position.Y - targetTransform.Scale.Y * 0.5f;
                        followerTransform.Scale = new Vector3(followerTransform.Scale.X, newBottom - (followerTransform.Position.Y - followerTransform.Scale.Y * 0.5f), 1);
                        break;
                    case "all":
                        followerTransform.Position = new Vector3(
                            orig.pos.X + (targetTransform.Position.X - targetOrig.pos.X),
                            orig.pos.Y + (targetTransform.Position.Y - targetOrig.pos.Y),
                            0);
                        break;
                }

                if (followerTransform.Scale.X < 0) followerTransform.Scale = new Vector3(0, followerTransform.Scale.Y, 1);
                if (followerTransform.Scale.Y < 0) followerTransform.Scale = new Vector3(followerTransform.Scale.X, 0, 1);
                world.SetComponent(followerEntity.Value, followerTransform);
            }
        }

        private static Entity? Raycast(World world, Vector2 mousePos)
        {
            Entity? cameraEntity = null;
            CameraComponent camera = default;
            foreach (var e in world.Query<CameraComponent>())
            {
                cameraEntity = e;
                camera = world.GetComponent<CameraComponent>(e);
                break;
            }
            if (cameraEntity == null) return null;

            float aspect = (float)Vulkan.SwapExtent.Width / Vulkan.SwapExtent.Height;
            float fovRad = camera.Fov * MathF.PI / 180f;
            float ndcX = (mousePos.X / Vulkan.SwapExtent.Width) * 2f - 1f;
            float ndcY = (mousePos.Y / Vulkan.SwapExtent.Height) * 2f - 1f;
            Vector3 rayDir = new Vector3(ndcX * MathF.Tan(fovRad / 2f) * aspect, -ndcY * MathF.Tan(fovRad / 2f), -1f);
            rayDir = Vector3.Normalize(rayDir);

            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.Position, camera.Pivot, Vector3.UnitY);
            Matrix4x4.Invert(view, out Matrix4x4 invView);
            rayDir = Vector3.TransformNormal(rayDir, invView);
            Vector3 rayOrigin = camera.Position;

            Entity? closest = null;
            float closestDist = float.MaxValue;

            foreach (var (e, trans, _) in world.Query<TransformComponent, MeshComponent>())
            {
                Vector3 center = trans.Position;
                float radius = Math.Max(trans.Scale.X, Math.Max(trans.Scale.Y, trans.Scale.Z)) * 0.5f;

                Vector3 oc = rayOrigin - center;
                float b = Vector3.Dot(oc, rayDir);
                float c = Vector3.Dot(oc, oc) - radius * radius;
                float disc = b * b - c;
                if (disc > 0)
                {
                    float t = -b - MathF.Sqrt(disc);
                    if (t > 0 && t < closestDist)
                    {
                        closestDist = t;
                        closest = e;
                    }
                }
            }
            return closest;
        }
    }
}
