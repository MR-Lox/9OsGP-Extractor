using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace RaceBoot
{
    // Cabinet lamps, MAME style: MAME's "network" output provider listens on TCP port 8000 and sends
    // "name = value\r" lines to every connected client (MameHooker, LEDBlinky, DOFLinx…), starting with
    // "mame_start = <game>" and ending with "mame_stop = 1". Outputs:
    //   start_lamp : blinks while the game waits for START (attract: replay, GAME OVER, "PRESS START"), off in race;
    //   view_lamp  : lit during the race (the view button can be pressed).
    public class MameOutputs : MonoBehaviour
    {
        const int Port = 8000;
        const string GameName = "9osgp";
        static MameOutputs? inst;

        TcpListener? listener;
        readonly List<TcpClient> clients = new List<TcpClient>();
        readonly Dictionary<string, int> values = new Dictionary<string, int>();
        float nextState;
        string raceState = "";

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootMameOutputs");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<MameOutputs>();
        }

        void Start() => Open();

        void Open()
        {
            if (listener != null || !Settings.MameOutputs) return;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                Debug.Log("[RaceBoot] MAME outputs on port " + Port);
            }
            catch (Exception e)
            {
                // Port taken (MAME running?): no outputs, the game is not affected.
                Debug.Log("[RaceBoot] MAME outputs unavailable: " + e.Message);
                listener = null;
            }
        }

        void Close()
        {
            Broadcast("mame_stop = 1\r");
            foreach (var c in clients) try { c.Close(); } catch { }
            clients.Clear();
            try { listener?.Stop(); } catch { }
            listener = null;
            values.Clear();
        }

        void OnApplicationQuit() => Close();

        void Update()
        {
            if (!Settings.MameOutputs) { if (listener != null) Close(); return; }
            if (listener == null) { if (Time.frameCount % 300 == 0) Open(); if (listener == null) return; }

            // New clients get the game name and the current state of every output.
            try
            {
                while (listener.Pending())
                {
                    var c = listener.AcceptTcpClient();
                    c.NoDelay = true;
                    clients.Add(c);
                    var sb = new StringBuilder("mame_start = " + GameName + "\r");
                    foreach (var kv in values) sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\r');
                    Send(c, sb.ToString());
                }
            }
            catch { }

            if (Time.unscaledTime >= nextState)
            {
                nextState = Time.unscaledTime + 0.1f;
                var rm = Showcase.Fsm("GameManager", "race manager");
                var fsm = rm ? rm!.GetType().GetProperty("Fsm")?.GetValue(rm) : null;
                raceState = (rm && rm!.enabled ? fsm?.GetType().GetProperty("ActiveStateName")?.GetValue(fsm) as string : null) ?? "";
            }
            bool racing = raceState == "3" || raceState == "2" || raceState == "1" || raceState == "go" || raceState == "racing";
            bool attract = !racing && (Replay.Playing || raceState == "skip?" || raceState == "");
            Set("start_lamp", attract && (Time.unscaledTime % 1f) < 0.5f ? 1 : 0);
            Set("view_lamp", racing ? 1 : 0);
        }

        void Set(string name, int v)
        {
            if (values.TryGetValue(name, out int old) && old == v) return;
            values[name] = v;
            Broadcast(name + " = " + v + "\r");
        }

        void Broadcast(string msg)
        {
            for (int i = clients.Count - 1; i >= 0; i--)
                if (!Send(clients[i], msg)) { try { clients[i].Close(); } catch { } clients.RemoveAt(i); }
        }

        static bool Send(TcpClient c, string msg)
        {
            try
            {
                if (!c.Connected) return false;
                var b = Encoding.ASCII.GetBytes(msg);
                c.GetStream().Write(b, 0, b.Length);
                return true;
            }
            catch { return false; }
        }
    }
}
