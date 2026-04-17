using System;
using System.Numerics;
using SETUE.ECS;
using static SETUE.Vulkan;

namespace SETUE
{
    public static class Scene3D
    {
        public static void Load() { }

        public static void Update()
        {
            Console.WriteLine("[Scene3D] Update() started");
            var world = Object.ECSWorld;

            Entity? cameraEntity = null;
            foreach (var e in world.Query<CameraComponent>())
            {
                cameraEntity = e;
                break;
            }
            if (cameraEntity == null)
            {
                Console.WriteLine("[Scene3D] No camera entity found!");
                return;
            }

            var camera = world.GetComponent<CameraComponent>(cameraEntity.Value);
            Console.WriteLine($"[Scene3D] Camera found: pos={camera.Position}, fov={camera.Fov}");

            uint width  = SwapExtent.Width;
            uint height = SwapExtent.Height;
            float aspect = (float)width / Math.Max(1, height);

            Vector3 cameraPos = camera.Position;
            Vector3 target    = camera.Pivot;

            Vector3 forward = Vector3.Normalize(target - cameraPos);
            float   upDot   = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY));
            Vector3 up      = upDot > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

            Matrix4x4 view = Matrix4x4.CreateLookAt(cameraPos, target, up);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
                camera.Fov * MathF.PI / 180f, aspect, camera.Near, camera.Far);

            int entityCount = 0;
            foreach (var (e, t, m) in world.Query<TransformComponent, MeshComponent>())
            {
                entityCount++;
                var mvp = t.LocalToWorld * view * proj;

                if (world.HasComponent<MVPComponent>(e))
                    world.SetComponent(e, new MVPComponent { MVP = mvp });
                else
                    world.AddComponent(e, new MVPComponent { MVP = mvp });
            }
            Console.WriteLine($"[Scene3D] Processed {entityCount} entities with Transform+Mesh, added/updated MVPComponent.");
        }
    }
}
