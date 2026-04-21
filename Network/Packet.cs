using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SETUE.Network
{
    public class PacketDefinition
    {
        public int    PacketId             { get; set; }
        public string PacketName           { get; set; } = "";
        public string HandlerClass         { get; set; } = "";
        public string HandlerMethod        { get; set; } = "";
        public string DataFormat           { get; set; } = "binary";
        public bool   CompressionEnabled   { get; set; }
        public int    MaxSizeBytes         { get; set; } = 4096;
        public bool   RequiresAck          { get; set; }
        public int    MaxRetries           { get; set; }
        public float  RetryIntervalSec     { get; set; } = 1f;
        public float  MaxMoveSpeed         { get; set; }
        public float  MaxDamage            { get; set; }
        public float  MaxInteractionRange  { get; set; }
        public string RejectAction         { get; set; } = "ignore";
        public int    Priority             { get; set; } = 5;
        public bool   Reliable             { get; set; }
        public bool   Enabled              { get; set; }
        public bool   Log                  { get; set; }
    }

    public static class Packets
    {
        private static List<PacketDefinition>                                                      _packets     = new();
        private static Dictionary<int, PacketDefinition>                                           _packetDict  = new();
        private static Dictionary<int, Action<NetworkSession, byte[]>>                             _handlers    = new();
        private static Dictionary<int, List<(NetworkSession session, byte[] data, int retries, double nextRetry)>> _pendingAcks = new();

        public static void Load()
        {
            _packets.Clear();
            _packetDict.Clear();
            _handlers.Clear();
            _pendingAcks.Clear();

            string path = "Network/Packet.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Packets] File not found: {path}"); return; }

            var lines   = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers     = lines[0].Split(',');
            int iId         = Array.IndexOf(headers, "packet_id");
            int iName       = Array.IndexOf(headers, "packet_name");
            int iClass      = Array.IndexOf(headers, "handler_class");
            int iMethod     = Array.IndexOf(headers, "handler_method");
            int iFormat     = Array.IndexOf(headers, "data_format");
            int iCompress   = Array.IndexOf(headers, "compression_enabled");
            int iMaxSize    = Array.IndexOf(headers, "max_size_bytes");
            int iReqAck     = Array.IndexOf(headers, "requires_ack");
            int iMaxRetry   = Array.IndexOf(headers, "max_retries");
            int iRetryInt   = Array.IndexOf(headers, "retry_interval_sec");
            int iMaxMove    = Array.IndexOf(headers, "max_move_speed");
            int iMaxDmg     = Array.IndexOf(headers, "max_damage");
            int iMaxRange   = Array.IndexOf(headers, "max_interaction_range");
            int iReject     = Array.IndexOf(headers, "reject_action");
            int iPriority   = Array.IndexOf(headers, "priority");
            int iReliable   = Array.IndexOf(headers, "reliable");
            int iEnabled    = Array.IndexOf(headers, "enabled");
            int iLog        = Array.IndexOf(headers, "log");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                var packet = new PacketDefinition
                {
                    PacketId            = int.TryParse(Get(iId),        out int   pid)  ? pid  : 0,
                    PacketName          = Get(iName),
                    HandlerClass        = Get(iClass),
                    HandlerMethod       = Get(iMethod),
                    DataFormat          = Get(iFormat),
                    CompressionEnabled  = Get(iCompress)  == "1",
                    MaxSizeBytes        = int.TryParse(Get(iMaxSize),   out int   ms)   ? ms   : 4096,
                    RequiresAck         = Get(iReqAck)    == "1",
                    MaxRetries          = int.TryParse(Get(iMaxRetry),  out int   mr)   ? mr   : 0,
                    RetryIntervalSec    = float.TryParse(Get(iRetryInt),out float ri)   ? ri   : 1f,
                    MaxMoveSpeed        = float.TryParse(Get(iMaxMove), out float mms)  ? mms  : 0f,
                    MaxDamage           = float.TryParse(Get(iMaxDmg),  out float md)   ? md   : 0f,
                    MaxInteractionRange = float.TryParse(Get(iMaxRange),out float mir)  ? mir  : 0f,
                    RejectAction        = Get(iReject),
                    Priority            = int.TryParse(Get(iPriority),  out int   pri)  ? pri  : 5,
                    Reliable            = Get(iReliable)  == "1",
                    Enabled             = Get(iEnabled)   == "1",
                    Log                 = Get(iLog)        == "1"
                };

                if (!packet.Enabled) continue;

                _packets.Add(packet);
                _packetDict[packet.PacketId] = packet;

                // Bind handler via reflection — same pattern as Scheduler.cs
                if (string.IsNullOrEmpty(packet.HandlerClass) || string.IsNullOrEmpty(packet.HandlerMethod)) continue;

                Type? type = Type.GetType(packet.HandlerClass);
                if (type == null) { Console.WriteLine($"[Packets] ERROR: Handler class not found: {packet.HandlerClass}"); continue; }

                var method = type.GetMethod(packet.HandlerMethod,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (method == null) { Console.WriteLine($"[Packets] ERROR: Handler method not found: {packet.HandlerMethod}"); continue; }

                try
                {
                    var handler = (Action<NetworkSession, byte[]>)
                        Delegate.CreateDelegate(typeof(Action<NetworkSession, byte[]>), method);
                    _handlers[packet.PacketId] = handler;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Packets] ERROR binding handler for {packet.PacketName}: {ex.Message}");
                }
            }

            Console.WriteLine($"[Packets] Loaded {_packets.Count} packet definitions.");
        }

        public static void Route(NetworkSession session, byte[] data)
        {
            if (data.Length < 1) return;

            int packetId = data[0];

            if (!_packetDict.TryGetValue(packetId, out var def))
            {
                Console.WriteLine($"[Packets] Unknown packet type {packetId} from {session.SessionId}");
                return;
            }

            // Validate size
            if (data.Length > def.MaxSizeBytes)
            {
                Console.WriteLine($"[Packets] {def.PacketName} exceeds max size from {session.SessionId}");
                Reject(session, def);
                return;
            }

            // Validate move speed — populated by game layer when packet data is parsed
            // MaxMoveSpeed > 0 means this packet type has movement validation enabled
            // Actual speed extraction happens in the handler before calling this check
            // Example: if (def.MaxMoveSpeed > 0 && extractedSpeed > def.MaxMoveSpeed) Reject()

            // Validate damage — same pattern
            // Example: if (def.MaxDamage > 0 && extractedDamage > def.MaxDamage) Reject()

            // Validate interaction range — same pattern
            // Example: if (def.MaxInteractionRange > 0 && extractedRange > def.MaxInteractionRange) Reject()

            if (def.Log)
                Console.WriteLine($"[Packets] Routing {def.PacketName} from {session.SessionId}");

            if (!_handlers.TryGetValue(packetId, out var handler)) return;

            try   { handler(session, data); }
            catch (Exception ex) { Console.WriteLine($"[Packets] ERROR in handler for {def.PacketName}: {ex.Message}"); }
        }

        public static void Send(NetworkSession session, int packetId, byte[] data)
        {
            if (!_packetDict.TryGetValue(packetId, out var def)) return;

            byte[] packet = new byte[data.Length + 1];
            packet[0] = (byte)packetId;
            Array.Copy(data, 0, packet, 1, data.Length);

            session.Send(packet);

            if (!def.RequiresAck || def.MaxRetries <= 0) return;

            if (!_pendingAcks.ContainsKey(packetId))
                _pendingAcks[packetId] = new();

            _pendingAcks[packetId].Add((session, packet, 0, Now() + def.RetryIntervalSec));
        }

        public static void Update()
        {
            double now = Now();

            foreach (var kvp in _pendingAcks)
            {
                if (!_packetDict.TryGetValue(kvp.Key, out var def)) continue;
                var pending = kvp.Value;

                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var (session, data, retries, nextRetry) = pending[i];
                    if (now < nextRetry) continue;

                    if (retries >= def.MaxRetries)
                    {
                        Console.WriteLine($"[Packets] Max retries reached for {def.PacketName} to {session.SessionId}");
                        pending.RemoveAt(i);
                        continue;
                    }

                    session.Send(data);
                    pending[i] = (session, data, retries + 1, now + def.RetryIntervalSec);
                }
            }
        }

        public static void Acknowledge(int packetId, NetworkSession session)
        {
            if (_pendingAcks.TryGetValue(packetId, out var pending))
                pending.RemoveAll(p => p.session.SessionId == session.SessionId);
        }

        private static void Reject(NetworkSession session, PacketDefinition def)
        {
            switch (def.RejectAction.ToLower())
            {
                case "disconnect": session.Disconnect(); break;
                case "kick":       session.Disconnect(); break;
                case "ignore":                           break;
                default:                                 break;
            }
        }

        private static double Now() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}
