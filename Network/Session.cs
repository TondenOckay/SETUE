using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace SETUE.Network
{
    public class SessionPolicy
    {
        public string SessionId          { get; set; } = "";
        public float  MaxIdleSec         { get; set; } = 300f;
        public float  ReconnectWindowSec { get; set; } = 30f;
        public int    MaxPacketsPerSec   { get; set; } = 100;
        public int    MaxPacketSizeBytes { get; set; } = 4096;
        public bool   Enabled            { get; set; }
        public bool   Log                { get; set; }
    }

    public class NetworkSession
    {
        public string        SessionId         { get; set; } = "";
        public int           PlayerId          { get; set; }
        public string        ZoneId            { get; set; } = "";
        public TcpClient     Client            { get; set; } = null!;
        public NetworkStream Stream            { get; set; } = null!;
        public double        LastHeartbeatTime { get; set; }
        public int           MissedBeats       { get; set; }
        public int           PacketsThisSecond { get; set; }
        public double        PacketWindowStart { get; set; }
        public bool          Connected         { get; set; }
        public SessionPolicy Policy            { get; set; } = null!;

        private byte[] _readBuffer = new byte[4096];

        public void BeginRead()
        {
            try
            {
                Stream.BeginRead(_readBuffer, 0, _readBuffer.Length, OnDataReceived, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session] ERROR beginning read for {SessionId}: {ex.Message}");
                Disconnect();
            }
        }

        private void OnDataReceived(IAsyncResult ar)
        {
            try
            {
                int bytes = Stream.EndRead(ar);
                if (bytes == 0) { Disconnect(); return; }

                byte[] data = new byte[bytes];
                Array.Copy(_readBuffer, data, bytes);
                Packets.Route(this, data);
                BeginRead();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session] ERROR reading from {SessionId}: {ex.Message}");
                Disconnect();
            }
        }

        public void Send(byte[] data)
        {
            try
            {
                if (!Connected) return;
                Stream.BeginWrite(data, 0, data.Length, null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session] ERROR sending to {SessionId}: {ex.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            Connected = false;
            try { Stream?.Close(); Client?.Close(); } catch { }
            Sessions.Remove(SessionId);
            Console.WriteLine($"[Session] Disconnected: {SessionId}");
        }
    }

    public static class Sessions
    {
        private static List<SessionPolicy>                            _policies      = new();
        private static SessionPolicy                                  _defaultPolicy = new();
        private static ConcurrentDictionary<string, NetworkSession>  _sessions      = new();
        private static int                                            _nextSessionId = 1;

        public static IReadOnlyDictionary<string, NetworkSession> All => _sessions;

        public static void Load()
        {
            _policies.Clear();

            string path = "Network/Session.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Sessions] File not found: {path}"); return; }

            var lines   = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers     = lines[0].Split(',');
            int iId         = Array.IndexOf(headers, "session_id");
            int iMaxIdle    = Array.IndexOf(headers, "max_idle_sec");
            int iReconnect  = Array.IndexOf(headers, "reconnect_window_sec");
            int iMaxPPS     = Array.IndexOf(headers, "max_packets_per_sec");
            int iMaxPktSize = Array.IndexOf(headers, "max_packet_size_bytes");
            int iEnabled    = Array.IndexOf(headers, "enabled");
            int iLog        = Array.IndexOf(headers, "log");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                var policy = new SessionPolicy
                {
                    SessionId          = Get(iId),
                    MaxIdleSec         = float.TryParse(Get(iMaxIdle),    out float idle)    ? idle    : 300f,
                    ReconnectWindowSec = float.TryParse(Get(iReconnect),  out float recon)   ? recon   : 30f,
                    MaxPacketsPerSec   = int.TryParse(Get(iMaxPPS),       out int   pps)     ? pps     : 100,
                    MaxPacketSizeBytes = int.TryParse(Get(iMaxPktSize),   out int   pktSize) ? pktSize : 4096,
                    Enabled            = Get(iEnabled) == "1",
                    Log                = Get(iLog)     == "1"
                };

                if (!policy.Enabled) continue;
                _policies.Add(policy);
                if (policy.SessionId == "default") _defaultPolicy = policy;
            }

            Console.WriteLine($"[Sessions] Loaded {_policies.Count} session policy(s).");
        }

        public static void Accept(TcpClient client)
        {
            var config = Server.Active;
            if (config == null) return;

            if (_sessions.Count >= config.MaxSessions)
            {
                Console.WriteLine("[Sessions] Max sessions reached. Rejecting connection.");
                client.Close();
                return;
            }

            string id = $"session_{_nextSessionId++}";
            var session = new NetworkSession
            {
                SessionId         = id,
                Client            = client,
                Stream            = client.GetStream(),
                LastHeartbeatTime = Now(),
                Connected         = true,
                Policy            = _defaultPolicy
            };

            _sessions[id] = session;
            session.BeginRead();
            Console.WriteLine($"[Sessions] Accepted: {id} from {client.Client.RemoteEndPoint}");
        }

        public static void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

        public static void Update()
        {
            var config = Server.Active;
            if (config == null) return;

            double now       = Now();
            var    toRemove  = new List<string>();

            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;
                if (!session.Connected) { toRemove.Add(kvp.Key); continue; }

                double elapsed = now - session.LastHeartbeatTime;
                if (elapsed < config.HeartbeatFrequencySec) continue;

                session.MissedBeats++;
                if (session.Policy.Log)
                    Console.WriteLine($"[Sessions] Missed beat {session.MissedBeats}/{config.MaxMissedBeats} for {session.SessionId}");

                if (session.MissedBeats >= config.MaxMissedBeats)
                {
                    Console.WriteLine($"[Sessions] Timeout: {session.SessionId}");
                    toRemove.Add(kvp.Key);
                }
                else
                {
                    SendHeartbeat(session);
                    session.LastHeartbeatTime = now;
                }
            }

            foreach (var id in toRemove)
                if (_sessions.TryGetValue(id, out var s)) s.Disconnect();
        }

        private static void SendHeartbeat(NetworkSession session)
        {
            try   { session.Send(new byte[] { 0x00 }); }
            catch (Exception ex) { Console.WriteLine($"[Sessions] ERROR heartbeat to {session.SessionId}: {ex.Message}"); }
        }

        public static NetworkSession? Get(string sessionId) =>
            _sessions.TryGetValue(sessionId, out var s) ? s : null;

        private static double Now() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}
