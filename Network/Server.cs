using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace SETUE.Network
{
    public class ServerConfig
    {
        public string ServerId            { get; set; } = "";
        public int    Port                { get; set; } = 7777;
        public int    MaxConnections      { get; set; } = 1000;
        public string Protocol            { get; set; } = "TCP";
        public float  ConnectionTimeoutSec{ get; set; } = 30f;
        public float  SessionTimeoutSec   { get; set; } = 300f;
        public float  HeartbeatFrequencySec{ get; set; } = 10f;
        public int    MaxMissedBeats      { get; set; } = 3;
        public int    MaxSessions         { get; set; } = 1000;
        public float  CleanupFrequencySec { get; set; } = 60f;
        public bool   Enabled             { get; set; }
        public bool   Log                 { get; set; }
    }

    public static class Server
    {
        private static List<ServerConfig> _configs  = new();
        private static TcpListener?       _listener;

        public static ServerConfig? Active { get; private set; }

        public static void Load()
        {
            _configs.Clear();

            string path = "Network/Server.csv";
            if (!File.Exists(path)) { Console.WriteLine($"[Server] File not found: {path}"); return; }

            var lines   = File.ReadAllLines(path);
            if (lines.Length < 2) return;

            var headers     = lines[0].Split(',');
            int iId         = Array.IndexOf(headers, "server_id");
            int iPort       = Array.IndexOf(headers, "port");
            int iMaxConn    = Array.IndexOf(headers, "max_connections");
            int iProtocol   = Array.IndexOf(headers, "protocol");
            int iConnTo     = Array.IndexOf(headers, "connection_timeout_sec");
            int iSessTo     = Array.IndexOf(headers, "session_timeout_sec");
            int iHbFreq     = Array.IndexOf(headers, "heartbeat_frequency_sec");
            int iMaxMissed  = Array.IndexOf(headers, "max_missed_beats");
            int iMaxSess    = Array.IndexOf(headers, "max_sessions");
            int iCleanup    = Array.IndexOf(headers, "cleanup_frequency_sec");
            int iEnabled    = Array.IndexOf(headers, "enabled");
            int iLog        = Array.IndexOf(headers, "log");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var parts = line.Split(',');
                string Get(int idx) => idx >= 0 && idx < parts.Length ? parts[idx].Trim() : "";

                var config = new ServerConfig
                {
                    ServerId              = Get(iId),
                    Port                  = int.TryParse(Get(iPort),      out int   port)     ? port     : 7777,
                    MaxConnections        = int.TryParse(Get(iMaxConn),   out int   maxConn)  ? maxConn  : 1000,
                    Protocol              = Get(iProtocol),
                    ConnectionTimeoutSec  = float.TryParse(Get(iConnTo),  out float connTo)   ? connTo   : 30f,
                    SessionTimeoutSec     = float.TryParse(Get(iSessTo),  out float sessTo)   ? sessTo   : 300f,
                    HeartbeatFrequencySec = float.TryParse(Get(iHbFreq),  out float hbFreq)   ? hbFreq   : 10f,
                    MaxMissedBeats        = int.TryParse(Get(iMaxMissed), out int   maxMissed)? maxMissed : 3,
                    MaxSessions           = int.TryParse(Get(iMaxSess),   out int   maxSess)  ? maxSess  : 1000,
                    CleanupFrequencySec   = float.TryParse(Get(iCleanup), out float cleanup)  ? cleanup  : 60f,
                    Enabled               = Get(iEnabled) == "1",
                    Log                   = Get(iLog)     == "1"
                };

                if (!config.Enabled) continue;
                _configs.Add(config);
            }

            if (_configs.Count == 0) { Console.WriteLine("[Server] No enabled server config found."); return; }

            Active = _configs[0];
            StartListener();
            Console.WriteLine($"[Server] Loaded {_configs.Count} config(s). Active: {Active.ServerId} on port {Active.Port}");
        }

        private static void StartListener()
        {
            if (Active == null) return;
            try
            {
                _listener = new TcpListener(IPAddress.Any, Active.Port);
                _listener.Start(Active.MaxConnections);
                _listener.BeginAcceptTcpClient(OnClientConnected, null);
                if (Active.Log) Console.WriteLine($"[Server] Listening on port {Active.Port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] ERROR starting listener: {ex.Message}");
            }
        }

        private static void OnClientConnected(IAsyncResult ar)
        {
            try
            {
                if (_listener == null) return;
                var client = _listener.EndAcceptTcpClient(ar);
                _listener.BeginAcceptTcpClient(OnClientConnected, null);
                Sessions.Accept(client);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] ERROR accepting client: {ex.Message}");
            }
        }

        public static void Update()
        {
            // Scheduler driven — active server monitoring goes here
        }

        public static void Stop()
        {
            _listener?.Stop();
            Console.WriteLine("[Server] Listener stopped.");
        }
    }
}
