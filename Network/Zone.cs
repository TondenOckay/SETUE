using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SETUE.Network
{
    public class ZoneDefinition
    {
        public string ZoneId                 { get; set; } = "";
        public string ZoneName               { get; set; } = "";
        public float  RadiusNear             { get; set; } = 50f;
        public float  RadiusFar              { get; set; } = 200f;
        public float  UpdateFrequencyNearSec { get; set; } = 0.1f;
        public float  UpdateFrequencyFarSec  { get; set; } = 0.5f;
        public int    MaxEntitiesTracked     { get; set; } = 100;
        public float  SyncFrequencySec       { get; set; } = 0.05f;
        public bool   DeltaOnly              { get; set; } = true;
        public int    Priority               { get; set; } = 1;
        public bool   Enabled                { get; set; }
        public bool   Log                    { get; set; }
    }

    public class ZoneSession
    {
        public string       SessionId       { get; set; } = "";
        public string       ZoneId         { get; set; } = "";
        public Vector3      Position       { get; set; }
        public double       LastNearUpdate { get; set; }
        public double       LastFarUpdate  { get; set; }
        public List<string> TrackedEntities{ get; set; } = new();
    }

    public static class Zones
    {
        private static List<ZoneDefinition>             _zones        = new();
        private static Dictionary<string, ZoneDefinition> _zoneDict  = new();
        private static Dictionary<string, ZoneSession>  _zoneSessions = new();

        public static IReadOnlyDictionary<string, ZoneDefinition> All => _zoneDict;

        public static void Load()
        {
            _zones.Clear();
            _zoneDict.Clear();

            string path = "Network/Zone.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Zones] File not found: {path}"); return; }

            var lines   = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers     = lines[0].Split(',');
            int iId         = Array.IndexOf(headers, "zone_id");
            int iName       = Array.IndexOf(headers, "zone_name");
            int iNear       = Array.IndexOf(headers, "radius_near");
            int iFar        = Array.IndexOf(headers, "radius_far");
            int iFreqNear   = Array.IndexOf(headers, "update_frequency_near_sec");
            int iFreqFar    = Array.IndexOf(headers, "update_frequency_far_sec");
            int iMaxEnt     = Array.IndexOf(headers, "max_entities_tracked");
            int iSyncFreq   = Array.IndexOf(headers, "sync_frequency_sec");
            int iDelta      = Array.IndexOf(headers, "delta_only");
            int iPriority   = Array.IndexOf(headers, "priority");
            int iEnabled    = Array.IndexOf(headers, "enabled");
            int iLog        = Array.IndexOf(headers, "log");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                var zone = new ZoneDefinition
                {
                    ZoneId                 = Get(iId),
                    ZoneName               = Get(iName),
                    RadiusNear             = float.TryParse(Get(iNear),      out float near)  ? near  : 50f,
                    RadiusFar              = float.TryParse(Get(iFar),       out float far)   ? far   : 200f,
                    UpdateFrequencyNearSec = float.TryParse(Get(iFreqNear),  out float fn)    ? fn    : 0.1f,
                    UpdateFrequencyFarSec  = float.TryParse(Get(iFreqFar),   out float ff)    ? ff    : 0.5f,
                    MaxEntitiesTracked     = int.TryParse(Get(iMaxEnt),      out int   me)    ? me    : 100,
                    SyncFrequencySec       = float.TryParse(Get(iSyncFreq),  out float sf)    ? sf    : 0.05f,
                    DeltaOnly              = Get(iDelta)    == "1",
                    Priority               = int.TryParse(Get(iPriority),    out int   pri)   ? pri   : 1,
                    Enabled                = Get(iEnabled)  == "1",
                    Log                    = Get(iLog)       == "1"
                };

                if (!zone.Enabled) continue;
                _zones.Add(zone);
                _zoneDict[zone.ZoneId] = zone;
            }

            Console.WriteLine($"[Zones] Loaded {_zones.Count} zone definition(s).");
        }

        public static void PlayerEnterZone(string sessionId, string zoneId, Vector3 position)
        {
            _zoneSessions[sessionId] = new ZoneSession
            {
                SessionId = sessionId,
                ZoneId    = zoneId,
                Position  = position
            };

            if (_zoneDict.TryGetValue(zoneId, out var zone) && zone.Log)
                Console.WriteLine($"[Zones] {sessionId} entered {zoneId}");
        }

        public static void PlayerLeaveZone(string sessionId)
        {
            _zoneSessions.Remove(sessionId);
        }

        public static void UpdatePlayerPosition(string sessionId, Vector3 position)
        {
            if (_zoneSessions.TryGetValue(sessionId, out var zs))
                zs.Position = position;
        }

        public static void Update()
        {
            double now = Now();

            foreach (var kvp in _zoneSessions)
            {
                var zoneSession = kvp.Value;
                if (!_zoneDict.TryGetValue(zoneSession.ZoneId, out var zone)) continue;

                var session = Sessions.Get(zoneSession.SessionId);
                if (session == null || !session.Connected) continue;

                // Near update — high frequency, full precision
                if (now - zoneSession.LastNearUpdate >= zone.UpdateFrequencyNearSec)
                {
                    SendNearUpdates(session, zoneSession, zone);
                    zoneSession.LastNearUpdate = now;
                }

                // Far update — low frequency, reduced precision
                if (now - zoneSession.LastFarUpdate >= zone.UpdateFrequencyFarSec)
                {
                    SendFarUpdates(session, zoneSession, zone);
                    zoneSession.LastFarUpdate = now;
                }
            }
        }

        private static void SendNearUpdates(NetworkSession session, ZoneSession zoneSession, ZoneDefinition zone)
        {
            // Query ECS for all entities within RadiusNear of zoneSession.Position
            // Serialize their full state and send to session
            // DeltaOnly flag controls whether full state or only changes are sent
            // This connects to your ECS system — placeholder for that integration
            if (zone.Log)
                Console.WriteLine($"[Zones] Near update for {zoneSession.SessionId} in {zone.ZoneId}");
        }

        private static void SendFarUpdates(NetworkSession session, ZoneSession zoneSession, ZoneDefinition zone)
        {
            // Query ECS for entities between RadiusNear and RadiusFar
            // Serialize reduced state (position only, no animation, no detail)
            // Fewer updates, less bandwidth, player won't notice at that distance
            if (zone.Log)
                Console.WriteLine($"[Zones] Far update for {zoneSession.SessionId} in {zone.ZoneId}");
        }

        public static ZoneDefinition? GetZone(string zoneId) =>
            _zoneDict.TryGetValue(zoneId, out var z) ? z : null;

        private static double Now() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}
