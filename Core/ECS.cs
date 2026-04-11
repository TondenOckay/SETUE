using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SETUE.Core
{
    public interface IComponent { }

    public static class ECS
    {
        private static Dictionary<Type, object> _componentStores = new();
        private static int _nextEntityId = 1;

        public struct Entity
        {
            public int Id;
            public Entity(int id) => Id = id;
        }

        public static Entity CreateEntity() => new Entity(_nextEntityId++);

        public static void AddComponent<T>(Entity e, T component) where T : struct, IComponent
        {
            var type = typeof(T);
            if (!_componentStores.TryGetValue(type, out var storeObj))
            {
                storeObj = new Dictionary<Entity, T>();
                _componentStores[type] = storeObj;
            }
            var store = (Dictionary<Entity, T>)storeObj;
            store[e] = component;
        }

        public static T GetComponent<T>(Entity e) where T : struct, IComponent
        {
            var store = (Dictionary<Entity, T>)_componentStores[typeof(T)];
            return store[e];
        }

        public static bool HasComponent<T>(Entity e) where T : struct, IComponent
        {
            if (!_componentStores.TryGetValue(typeof(T), out var storeObj)) return false;
            var store = (Dictionary<Entity, T>)storeObj;
            return store.ContainsKey(e);
        }

        public static IEnumerable<Entity> Query<T>() where T : struct, IComponent
        {
            if (!_componentStores.TryGetValue(typeof(T), out var storeObj)) yield break;
            var store = (Dictionary<Entity, T>)storeObj;
            foreach (var kv in store)
                yield return kv.Key;
        }

        public static void RemoveComponent<T>(Entity e) where T : struct, IComponent
        {
            if (_componentStores.TryGetValue(typeof(T), out var storeObj))
                ((Dictionary<Entity, T>)storeObj).Remove(e);
        }

        public static void DestroyEntity(Entity e)
        {
            foreach (var kv in _componentStores)
                ((dynamic)kv.Value).Remove(e);
        }

        private static object ConvertValue(string str, Type t)
        {
            if (t == typeof(string)) return str;
            if (t == typeof(bool)) return bool.TryParse(str, out var b) ? b : false;
            if (t == typeof(int)) return int.TryParse(str, out var iv) ? iv : 0;
            if (t == typeof(uint)) return uint.TryParse(str, out var ui) ? ui : 0u;
            if (t == typeof(float)) return float.TryParse(str, out var f) ? f : 0f;
            if (t.IsEnum) return Enum.TryParse(t, str, true, out var e) ? e : 0;
            return null;
        }

        // Load multiple component types from the same CSV file, one entity per row.
        private static void LoadFromCSV(string path, params Type[] componentTypes)
        {
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            var header = lines[0].Split(',');
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                var entity = CreateEntity();
                foreach (var compType in componentTypes)
                {
                    var component = Activator.CreateInstance(compType);
                    foreach (var field in compType.GetFields())
                    {
                        int idx = Array.IndexOf(header, field.Name);
                        if (idx >= 0 && idx < parts.Length)
                        {
                            var val = ConvertValue(parts[idx], field.FieldType);
                            field.SetValueDirect(__makeref(component), val);
                        }
                    }
                    // Add component using reflection
                    var addMethod = typeof(ECS).GetMethod("AddComponent").MakeGenericMethod(compType);
                    addMethod.Invoke(null, new object[] { entity, component });
                }
            }
        }

        public static void LoadAll()
        {
            Console.WriteLine("[ECS] Loading all components...");
            string ecsCsvPath = "Core/ECS.csv";
            if (!File.Exists(ecsCsvPath))
            {
                Console.WriteLine($"[ECS] Missing {ecsCsvPath}");
                return;
            }
            var lines = File.ReadAllLines(ecsCsvPath);
            if (lines.Length < 2) return;
            var header = lines[0].Split(',');
            int idxComp = Array.IndexOf(header, "ComponentName");
            int idxPath = Array.IndexOf(header, "CSVPath");
            int idxEnabled = Array.IndexOf(header, "Enabled");

            // Group by CSV path to load all components from the same file onto one entity per row
            var grouped = new Dictionary<string, List<Type>>();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length <= idxEnabled) continue;
                if (parts[idxEnabled].Trim().ToLower() != "true") continue;
                var compName = parts[idxComp].Trim();
                var path = parts[idxPath].Trim();
                var type = Type.GetType($"SETUE.Components.{compName}");
                if (type != null)
                {
                    if (!grouped.ContainsKey(path))
                        grouped[path] = new List<Type>();
                    grouped[path].Add(type);
                }
            }

            foreach (var kv in grouped)
            {
                LoadFromCSV(kv.Key, kv.Value.ToArray());
                Console.WriteLine($"[ECS] Loaded {string.Join(", ", kv.Value.Select(t => t.Name))} from {kv.Key}");
            }
        }
    }
}
