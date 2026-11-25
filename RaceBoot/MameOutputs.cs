using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace RaceBoot
{
    // Cabinet lamps, published like MAME does (src/osd/modules/output), so MameHooker, LEDBlinky, DOFLinx…
    // drive the buttons of a cabinet:
    //  - Windows messages ("win32" output, what those tools use by default): a hidden top-level window of
    //    class / title "MAMEOutput", the registered messages MAMEOutputStart / Stop / Register / Unregister /
    //    UpdateState / GetIDString, names sent back with WM_COPYDATA.
    //  - Network ("network" output): TCP port 8000 on every interface, IPv6 and IPv4, "name = value\r" lines,
    //    "mame_start = <game>" on connection, "mame_stop = 1" at exit.
    // Outputs:
    //   start_lamp : blinks while the game waits for START (attract: replay, GAME OVER, "PRESS START"), off in race;
    //   view_lamp  : lit during the race (the view button can be pressed).
    public class MameOutputs : MonoBehaviour
    {
        const int Port = 8000;
        const string GameName = "9osgp";
        static MameOutputs? inst;

        readonly Dictionary<string, int> values = new Dictionary<string, int>();
        readonly Dictionary<string, int> ids = new Dictionary<string, int>();   // output name -> win32 id
        int nextId = 12345;                                                      // like MAME (0 = game, 1 = pause)
        float nextState;
        string raceState = "";
        bool active;

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootMameOutputs");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<MameOutputs>();
        }

        void Start() { if (Settings.MameOutputs) Open(); }

        void Open()
        {
            if (active) return;
            active = true;
            OpenNetwork();
            try { OpenWin32(); } catch (Exception e) { Debug.Log("[RaceBoot] MAME win32 outputs unavailable: " + e.Message); }
        }

        void Close()
        {
            if (!active) return;
            active = false;
            Broadcast("mame_stop = 1");
            foreach (var c in clients) try { c.Close(); } catch { }
            clients.Clear();
            try { listener?.Stop(); } catch { }
            listener = null;
            try { CloseWin32(); } catch { }
            values.Clear();
        }

        void OnApplicationQuit() => Close();

        void Update()
        {
            if (!Settings.MameOutputs) { Close(); return; }
            if (!active) Open();
            AcceptClients();

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
            Broadcast(name + " = " + v);
            foreach (var c in winClients) PostMessage(c.Hwnd, omUpdate, (IntPtr)Id(name), (IntPtr)v);
        }

        int Id(string name)
        {
            if (!ids.TryGetValue(name, out int id)) { id = nextId++; ids[name] = id; }
            return id;
        }

        // ---------------- network ----------------

        TcpListener? listener;
        readonly List<TcpClient> clients = new List<TcpClient>();

        void OpenNetwork()
        {
            try
            {
                // IPv6 any with dual mode: "localhost" (often ::1 on Windows), 127.0.0.1 and the network.
                listener = new TcpListener(IPAddress.IPv6Any, Port);
                listener.Server.DualMode = true;
                listener.Start();
                Debug.Log("[RaceBoot] MAME network outputs on port " + Port);
            }
            catch (Exception e)
            {
                try { listener = new TcpListener(IPAddress.Any, Port); listener.Start(); }
                catch { listener = null; Debug.Log("[RaceBoot] MAME network outputs unavailable: " + e.Message); }
            }
        }

        void AcceptClients()
        {
            if (listener == null) return;
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
                // Commands from clients (mame_message = id,value) are read and ignored.
                foreach (var c in clients) { try { if (c.Available > 0) { var b = new byte[c.Available]; c.GetStream().Read(b, 0, b.Length); } } catch { } }
            }
            catch { }
        }

        void Broadcast(string msg)
        {
            msg += "\r";
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

        // ---------------- Windows messages ----------------

        delegate IntPtr WndProcFn(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize, style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClassW(string cls, IntPtr hInst);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

        static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;
        const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WM_COPYDATA = 0x004A;
        const string ClassName = "MAMEOutput";

        static WndProcFn? wndProc;   // kept alive: the window procedure is called from native code
        IntPtr hwnd;
        uint omStart, omStop, omUpdate, omRegister, omUnregister, omGetId, imMessage;

        class WinClient { public IntPtr Hwnd; public IntPtr Id; }
        readonly List<WinClient> winClients = new List<WinClient>();

        void OpenWin32()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || hwnd != IntPtr.Zero) return;
            wndProc ??= WndProc;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = ClassName,
            };
            RegisterClassExW(ref wc);
            omStart = RegisterWindowMessageW("MAMEOutputStart");
            omStop = RegisterWindowMessageW("MAMEOutputStop");
            omUpdate = RegisterWindowMessageW("MAMEOutputUpdateState");
            omRegister = RegisterWindowMessageW("MAMEOutputRegister");
            omUnregister = RegisterWindowMessageW("MAMEOutputUnregister");
            omGetId = RegisterWindowMessageW("MAMEOutputGetIDString");
            imMessage = RegisterWindowMessageW("MAMEInputMessage");
            // Top-level (not message-only) and never shown, like MAME's: it receives the broadcasts.
            hwnd = CreateWindowExW(0, ClassName, ClassName, WS_OVERLAPPEDWINDOW, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) { Debug.Log("[RaceBoot] MAME win32 outputs: window not created"); return; }
            PostMessage(HWND_BROADCAST, omStart, hwnd, IntPtr.Zero);
            Debug.Log("[RaceBoot] MAME win32 outputs ready");
        }

        void CloseWin32()
        {
            if (hwnd == IntPtr.Zero) return;
            winClients.Clear();
            PostMessage(HWND_BROADCAST, omStop, hwnd, IntPtr.Zero);
            DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
            UnregisterClassW(ClassName, GetModuleHandle(null));
        }

        // Runs on the main thread (Unity's message loop dispatches the messages of our window).
        static IntPtr WndProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var self = inst;
            if (self != null && h == self.hwnd)
            {
                try
                {
                    if (msg == self.omRegister) return (IntPtr)self.Register(wParam, lParam);
                    if (msg == self.omUnregister) return (IntPtr)self.Unregister(wParam, lParam);
                    if (msg == self.omGetId) { self.SendIdString(wParam, (int)lParam); return IntPtr.Zero; }
                    if (msg == self.imMessage) return IntPtr.Zero;   // pause / save state requests: ignored
                }
                catch (Exception e) { Debug.Log("[RaceBoot] MAME win32 outputs: " + e.Message); }
            }
            return DefWindowProcW(h, msg, wParam, lParam);
        }

        int Register(IntPtr clientHwnd, IntPtr id)
        {
            int result = 0;
            var c = winClients.Find(x => x.Id == id);
            if (c != null) { c.Hwnd = clientHwnd; result = 1; }
            else winClients.Add(c = new WinClient { Hwnd = clientHwnd, Id = id });
            // Current state of every output, like MAME's notify_all.
            foreach (var kv in values) PostMessage(c.Hwnd, omUpdate, (IntPtr)Id(kv.Key), (IntPtr)kv.Value);
            Debug.Log("[RaceBoot] MAME win32 outputs: client registered");
            return result;
        }

        int Unregister(IntPtr clientHwnd, IntPtr id) => winClients.RemoveAll(x => x.Id == id) > 0 ? 0 : 1;

        // Name of an output id, sent back with WM_COPYDATA: { uint32 id; char string[] (NUL-terminated) }.
        void SendIdString(IntPtr clientHwnd, int id)
        {
            string name = id == 0 ? GameName : id == 1 ? "pause" : "";
            foreach (var kv in ids) if (kv.Value == id) name = kv.Key;
            var text = Encoding.ASCII.GetBytes(name);
            int size = 4 + text.Length + 1;
            var buf = Marshal.AllocHGlobal(size);
            var cds = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(COPYDATASTRUCT)));
            try
            {
                Marshal.WriteInt32(buf, id);
                Marshal.Copy(text, 0, buf + 4, text.Length);
                Marshal.WriteByte(buf + 4 + text.Length, 0);
                Marshal.StructureToPtr(new COPYDATASTRUCT { dwData = (IntPtr)1, cbData = size, lpData = buf }, cds, false);
                SendMessage(clientHwnd, WM_COPYDATA, hwnd, cds);
            }
            finally { Marshal.FreeHGlobal(cds); Marshal.FreeHGlobal(buf); }
        }
    }
}
