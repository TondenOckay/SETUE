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
        public readonly int Index;
        public readonly int Generation;

        public Entity(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool Equals(Entity other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object? obj) => obj is Entity e && Equals(e);
        public override int GetHashCode() => HashCode.Combine(Index, Generation);
        public override string ToString() => $"Entity({Index}:{Generation})";
        public static bool operator ==(Entity a, Entity b) => a.Index == b.Index && a.Generation == b.Generation;
        public static bool operator !=(Entity a, Entity b) => !(a == b);
        public static readonly Entity Null = new(0, 0);
    }

    // -------------------------------------------------------------------------
    // Component marker
    // -------------------------------------------------------------------------
    public interface IComponent { }

    // -------------------------------------------------------------------------
    // Components
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
        public int MeshId;
        public ulong VertexBuffer;
        public ulong IndexBuffer;
        public uint IndexCount;
        public uint VertexCount;
        public uint VertexStride;
    }

    public struct MaterialComponent : IComponent
    {
        public Vector4 Color;
        public int PipelineId;
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
        public int Id;
        public int TextId;
        public bool Visible;
        public int Layer;
        public float Alpha;
        public bool Clickable;
    }

    public struct TextComponent : IComponent
    {
        public int Id;
        public int ContentId;
        public int FontId;
        public float FontSize;
        public Vector4 Color;
        public int Align;
        public float Rotation;
        public int Layer;
        public int Source;
        public int Prefix;
        public int PanelId;
        public float PadLeft;
        public float PadTop;
        public float LineHeight;
        public int VAlign;
    }

    // -------------------------------------------------------------------------
    // Sparse Array Storage
    // -------------------------------------------------------------------------
    internal interface IComponentStorage
    {
        void Remove(int entityIndex);
        bool Has(int entityIndex);
    }

    internal class ComponentStorage<T> : IComponentStorage where T : struct, IComponent
    {
        private T[] _dense = new T[64];
        private int[] _sparse = new int[1024];
        private int[] _entities = new int[64];
        private int _count;

        public ref T Add(int entityIndex, T value)
        {
            if (entityIndex >= _sparse.Length)
                Array.Resize(ref _sparse, Math.Max(entityIndex + 1, _sparse.Length * 2));

            if (_count == _dense.Length)
            {
                Array.Resize(ref _dense, _dense.Length * 2);
                Array.Resize(ref _entities, _entities.Length * 2);
            }

            _sparse[entityIndex] = _count;
            _entities[_count] = entityIndex;
            _dense[_count] = value;
            return ref _dense[_count++];
        }

        public ref T Get(int entityIndex)
        {
            int denseIndex = _sparse[entityIndex];
            return ref _dense[denseIndex];
        }

        public void Remove(int entityIndex)
        {
            // ✅ SAFETY: Only remove if the entity actually has this component
            if (!Has(entityIndex))
                return;

            int denseIndex = _sparse[entityIndex];
            int lastIndex = _count - 1;
            if (denseIndex != lastIndex)
            {
                int lastEntity = _entities[lastIndex];
                _dense[denseIndex] = _dense[lastIndex];
                _entities[denseIndex] = lastEntity;
                _sparse[lastEntity] = denseIndex;
            }
            _count--;
            _sparse[entityIndex] = 0;
        }

        public bool Has(int entityIndex) =>
            entityIndex < _sparse.Length && _sparse[entityIndex] < _count && _entities[_sparse[entityIndex]] == entityIndex;

        public int Count => _count;
        public T[] GetDense() => _dense;
        public int[] GetEntities() => _entities;
    }

    // -------------------------------------------------------------------------
    // World – Sparse Set ECS with Command Buffer
    // -------------------------------------------------------------------------
    public class World
    {
        private int _nextEntityId = 1;
        private int[] _generations = new int[1024];
        private readonly Queue<int> _freeIndices = new();

        private readonly Dictionary<Type, IComponentStorage> _storages = new();
        private readonly Queue<Action> _commands = new();

        public World() { }

        private ComponentStorage<T> GetStorage<T>() where T : struct, IComponent
        {
            var type = typeof(T);
            if (!_storages.TryGetValue(type, out var storage))
            {
                storage = new ComponentStorage<T>();
                _storages[type] = storage;
            }
            return (ComponentStorage<T>)storage;
        }

        public Entity CreateEntity()
        {
            int index;
            if (_freeIndices.Count > 0)
                index = _freeIndices.Dequeue();
            else
            {
                index = _nextEntityId++;
                if (index >= _generations.Length)
                    Array.Resize(ref _generations, _generations.Length * 2);
            }

            int gen = _generations[index] + 1;
            _generations[index] = gen;
            return new Entity(index, gen);
        }

        public void DestroyEntity(Entity e)
        {
            _commands.Enqueue(() =>
            {
                // Verify entity is still valid (generation match)
                if (e.Index >= _generations.Length || _generations[e.Index] != e.Generation)
                    return;

                foreach (var storage in _storages.Values)
                    storage.Remove(e.Index);

                _generations[e.Index] = e.Generation + 1;
                _freeIndices.Enqueue(e.Index);
            });
        }

        public void AddComponent<T>(Entity e, T comp) where T : struct, IComponent
        {
            _commands.Enqueue(() =>
            {
                if (e.Index >= _generations.Length || _generations[e.Index] != e.Generation)
                    return;

                var storage = GetStorage<T>();
                if (!storage.Has(e.Index))
                    storage.Add(e.Index, comp);
                else
                    storage.Get(e.Index) = comp;
            });
        }

        public T GetComponent<T>(Entity e) where T : struct, IComponent
        {
            if (e.Index >= _generations.Length || _generations[e.Index] != e.Generation)
                return default;

            var storage = GetStorage<T>();
            return storage.Has(e.Index) ? storage.Get(e.Index) : default;
        }

        public void SetComponent<T>(Entity e, T comp) where T : struct, IComponent
        {
            _commands.Enqueue(() =>
            {
                if (e.Index >= _generations.Length || _generations[e.Index] != e.Generation)
                    return;

                var storage = GetStorage<T>();
                if (storage.Has(e.Index))
                    storage.Get(e.Index) = comp;
                else
                    storage.Add(e.Index, comp);
            });
        }

        public bool HasComponent<T>(Entity e) where T : struct, IComponent
        {
            if (e.Index >= _generations.Length || _generations[e.Index] != e.Generation)
                return false;
            return _storages.TryGetValue(typeof(T), out var s) && ((ComponentStorage<T>)s).Has(e.Index);
        }

        public void RemoveComponent<T>(Entity e) where T : struct, IComponent
        {
            _commands.Enqueue(() =>
            {
                if (e.Index >= _generations.Length || _generations[e.Index] != e.Generation)
                    return;
                if (_storages.TryGetValue(typeof(T), out var s))
                    ((ComponentStorage<T>)s).Remove(e.Index);
            });
        }

        public void ExecuteCommands()
        {
            while (_commands.Count > 0)
                _commands.Dequeue()();
        }

        // ---------------------------------------------------------------------
        // Zero‑allocation iteration
        // ---------------------------------------------------------------------
        public void ForEach<T>(Action<Entity> action) where T : struct, IComponent
        {
            var storage = GetStorage<T>();
            var entities = storage.GetEntities();
            int count = storage.Count;
            for (int i = 0; i < count; i++)
            {
                int idx = entities[i];
                action(new Entity(idx, _generations[idx]));
            }
        }

        public void ForEach<T1, T2>(Action<Entity, T1, T2> action)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            var s1 = GetStorage<T1>();
            var s2 = GetStorage<T2>();

            if (s1.Count <= s2.Count)
            {
                var entities = s1.GetEntities();
                var comps = s1.GetDense();
                int count = s1.Count;
                for (int i = 0; i < count; i++)
                {
                    int idx = entities[i];
                    if (s2.Has(idx))
                        action(new Entity(idx, _generations[idx]), comps[i], s2.Get(idx));
                }
            }
            else
            {
                var entities = s2.GetEntities();
                var comps = s2.GetDense();
                int count = s2.Count;
                for (int i = 0; i < count; i++)
                {
                    int idx = entities[i];
                    if (s1.Has(idx))
                        action(new Entity(idx, _generations[idx]), s1.Get(idx), comps[i]);
                }
            }
        }

        public void ForEach<T1, T2, T3>(Action<Entity, T1, T2, T3> action)
            where T1 : struct, IComponent
            where T2 : struct, IComponent
            where T3 : struct, IComponent
        {
            var s1 = GetStorage<T1>();
            var s2 = GetStorage<T2>();
            var s3 = GetStorage<T3>();

            int c1 = s1.Count, c2 = s2.Count, c3 = s3.Count;
            if (c1 <= c2 && c1 <= c3)
            {
                var entities = s1.GetEntities();
                var comps = s1.GetDense();
                for (int i = 0; i < c1; i++)
                {
                    int idx = entities[i];
                    if (s2.Has(idx) && s3.Has(idx))
                        action(new Entity(idx, _generations[idx]), comps[i], s2.Get(idx), s3.Get(idx));
                }
            }
            else if (c2 <= c1 && c2 <= c3)
            {
                var entities = s2.GetEntities();
                var comps = s2.GetDense();
                for (int i = 0; i < c2; i++)
                {
                    int idx = entities[i];
                    if (s1.Has(idx) && s3.Has(idx))
                        action(new Entity(idx, _generations[idx]), s1.Get(idx), comps[i], s3.Get(idx));
                }
            }
            else
            {
                var entities = s3.GetEntities();
                var comps = s3.GetDense();
                for (int i = 0; i < c3; i++)
                {
                    int idx = entities[i];
                    if (s1.Has(idx) && s2.Has(idx))
                        action(new Entity(idx, _generations[idx]), s1.Get(idx), s2.Get(idx), comps[i]);
                }
            }
        }

        // ---------------------------------------------------------------------
        // Legacy IEnumerable support
        // ---------------------------------------------------------------------
        public IEnumerable<Entity> Query<T>() where T : struct, IComponent
        {
            var list = new List<Entity>();
            ForEach<T>(e => list.Add(e));
            return list;
        }

        public IEnumerable<(Entity e, T1 c1, T2 c2)> Query<T1, T2>()
            where T1 : struct, IComponent
            where T2 : struct, IComponent
        {
            var list = new List<(Entity, T1, T2)>();
            ForEach<T1, T2>((e, c1, c2) => list.Add((e, c1, c2)));
            return list;
        }

        public IEnumerable<(Entity e, T1 c1, T2 c2, T3 c3)> Query<T1, T2, T3>()
            where T1 : struct, IComponent
            where T2 : struct, IComponent
            where T3 : struct, IComponent
        {
            var list = new List<(Entity, T1, T2, T3)>();
            ForEach<T1, T2, T3>((e, c1, c2, c3) => list.Add((e, c1, c2, c3)));
            return list;
        }
    }
}
