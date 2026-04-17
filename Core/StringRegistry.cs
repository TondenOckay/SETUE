using System;
using System.Collections.Generic;

namespace SETUE.Core
{
    public static class StringRegistry
    {
        private static Dictionary<string, int> _stringToId = new();
        private static List<string> _idToString = new() { "" };

        public static int GetOrAdd(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (_stringToId.TryGetValue(s, out int id)) return id;
            id = _idToString.Count;
            _stringToId[s] = id;
            _idToString.Add(s);
            return id;
        }

        public static string GetString(int id) => id >= 0 && id < _idToString.Count ? _idToString[id] : "";

        public static void Clear()
        {
            _stringToId.Clear();
            _idToString.Clear();
            _idToString.Add("");
        }
    }
}
