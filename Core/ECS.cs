using System;
using System.Collections.Generic;
using System.Numerics;

namespace SETUE.ECS
{
    // -------------------------------------------------------------------------
    // Entity
    // -------------------------------------------------------------------------
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Id;
        public readonly int Version;
        public Entity(int id, int version) { Id = id; Version = version; }
        public bool Equals(Entity other) => Id == other.Id && Version == other.Version;
        public override int GetHashCode() => HashCode.Combine(Id, Version);
        public override string ToString() => $"Entity({Id}:{Version})";
    }

    // -------------------------------------------------------------------------
    // Component marker
    // -------------------------------------------------------------------------
    public interface IComponent { }

    // -------------------------------------------------------------------------
    // Built‑in components
    // -------------------------------------------------------------------------
    public struct TransformComponent : IComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Matrix4x4 LocalToWorld =>
            Matrix4x4.CreateScale(Scale) *
            Matrix4x4.CreateFromQuaternion(Rotation) *
            Matrix4x4.CreateTranslation(Position);
    }

    public struct MeshComponent : IComponent
    {
        public string MeshId;
        public IntPtr VertexBuffer;
        public IntPtr IndexBuffer;
        public uint IndexCount;
        public uint VertexCount;
        public uint VertexStride;
    }

    public struct MaterialComponent : IComponent
    {
        public string PipelineId;
        public Vector4 Color;
    }

    public struct LayerComponent : IComponent
    {
        public int Layer;
    }

    public struct MVPComponent : IComponent
    {
        public Matrix4x4 MVP;
    }

    public struct SelectedComponent : IComponent { }

    public struct CameraComponent : IComponent
    {
        public Vector3 Position;
        public Vector3 Pivot;
        public float Fov;
        public float Near;
        public float Far;
        public bool InvertX;
        public bool InvertY;
    }

    public struct PanelComponent : IComponent
    {
        public string Id;
        public bool Visible;
        public int Layer;
        public float Alpha;
        public string TextId;
        public bool Clickable;
    }

    public struct TextComponent : IComponent
    {
        public string Id;
        public string Content;
        public string FontId;
        public float FontSize;
        public Vector4 Color;
        public string Align;
        public float Rotation;
        public int Layer;
        public string Source;
        public string Prefix;
        public string PanelId;
    }

    // -------------------------------------------------------------------------
    // World – simple sparse‑set storage (no archetype)
    // -------------------------------------------------------------------------
    public class World
    {
        private int _nextId = 1;
        private Dictionary<int, int> _versions = new();
        private Dictionary<Type, Dictionary<int, IComponent>> _components = new();

        public Entity CreateEntity()
        {
            int id = _nextId++;
            _versions[id] = 1;
            return new Entity(id, 1);
        }

        public void DestroyEntity(Entity e)
        {
            foreach (var dict in _components.Values)
                dict.Remove(e.Id);
            _versions[e.Id] = e.Version + 1;
        }

        public void AddComponent<T>(Entity e, T comp) where T : IComponent
        {
            var type = typeof(T);
            if (!_components.ContainsKey(type))
                _components[type] = new Dictionary<int, IComponent>();
            _components[type][e.Id] = comp;
        }

        public T GetComponent<T>(Entity e) where T : IComponent
        {
            var type = typeof(T);
            if (_components.TryGetValue(type, out var dict) && dict.TryGetValue(e.Id, out var comp))
                return (T)comp;
            throw new Exception($"Component {type} not found on entity {e}");
        }

        public void SetComponent<T>(Entity e, T comp) where T : IComponent
        {
            var type = typeof(T);
            if (_components.TryGetValue(type, out var dict) && dict.ContainsKey(e.Id))
                dict[e.Id] = comp;
            else
                AddComponent(e, comp);
        }

        public bool HasComponent<T>(Entity e) where T : IComponent
        {
            var type = typeof(T);
            return _components.TryGetValue(type, out var dict) && dict.ContainsKey(e.Id);
        }

        public void RemoveComponent<T>(Entity e) where T : IComponent
        {
            var type = typeof(T);
            if (_components.TryGetValue(type, out var dict))
                dict.Remove(e.Id);
        }

        // Single‑component query (returns entities)
        public IEnumerable<Entity> Query<T>() where T : IComponent
        {
            var type = typeof(T);
            if (!_components.TryGetValue(type, out var dict))
                yield break;
            foreach (var id in dict.Keys)
                yield return new Entity(id, _versions[id]);
        }

        // Two‑component query (returns entities with both components)
        public IEnumerable<(Entity e, T1 c1, T2 c2)> Query<T1, T2>()
            where T1 : IComponent
            where T2 : IComponent
        {
            var type1 = typeof(T1);
            var type2 = typeof(T2);
            if (!_components.TryGetValue(type1, out var dict1)) yield break;
            if (!_components.TryGetValue(type2, out var dict2)) yield break;

            // iterate over the smaller dictionary
            var small = dict1.Count < dict2.Count ? dict1 : dict2;
            var other = dict1 == small ? dict2 : dict1;
            var typeSmall = dict1 == small ? type1 : type2;
            var typeOther = dict1 == small ? type2 : type1;

            foreach (var kv in small)
            {
                int id = kv.Key;
                if (other.ContainsKey(id))
                {
                    var c1 = typeSmall == type1 ? (T1)kv.Value : (T1)other[id];
                    var c2 = typeOther == type2 ? (T2)other[id] : (T2)kv.Value;
                    yield return (new Entity(id, _versions[id]), c1, c2);
                }
            }
        }
    }
}

