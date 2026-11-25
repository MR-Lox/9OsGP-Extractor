using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Rewired;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RaceBoot
{
    // Arcade ranking: the 10 best race times (whole race, from GO to the finish line), saved next to the
    // executable. Shown at the end of every finished race and in the attract mode (during the replay and on
    // the "PRESS START" screen). A new record asks for a 3-letter name on a letter wheel.
    //
    // End of a race in the game: "racing" -> "finished!" (AB_lap_count > laps) -> "save?" (waits 4 s, the
    // FINISHED! screen) -> reload of the title screen. The wait of "save?" is stretched while the name is
    // entered and the ranking shown.
    public class Ranking : MonoBehaviour
    {
        public class Entry { public string Name = "---"; public float Time, BestLap; public bool Fresh; }

        const int Size = 10;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Ranking? inst;
        static string FilePath => Path.Combine(Application.dataPath, "..", "9OsGP_ranking.txt");

        public static List<Entry> Entries = new List<Entry>();
        public static bool Busy => inst != null && inst.phase != Phase.None;

        public static void Install()
        {
            if (inst) return;
            Load();
            var go = new GameObject("RaceBootRanking");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Ranking>();
        }

        // ---------------- file ----------------

        static void Load()
        {
            Entries.Clear();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var p = line.Split(';');
                        if (p.Length < 3) continue;
                        if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float tm)) continue;
                        float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float bl);
                        Entries.Add(new Entry { Name = p[0].Length > 3 ? p[0].Substring(0, 3) : p[0], Time = tm, BestLap = bl });
                    }
            }
            catch (Exception e) { Debug.Log("[RaceBoot] ranking: load failed: " + e.Message); }
            if (Entries.Count == 0)
            {
                // Default board, as on an arcade cabinet fresh from the factory.
                string[] names = { "SEG", "NAM", "AM2", "RRR", "OR2", "DAY", "SCR", "TKO", "GP9", "CPU" };
                for (int i = 0; i < Size; i++) Entries.Add(new Entry { Name = names[i], Time = 210f + i * 6f, BestLap = 33f + i * 0.8f });
            }
            Entries = Entries.OrderBy(e => e.Time).Take(Size).ToList();
        }

        static void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, Entries.Select(e => e.Name + ";" + e.Time.ToString("0.000", CultureInfo.InvariantCulture)
                    + ";" + e.BestLap.ToString("0.000", CultureInfo.InvariantCulture)));
            }
            catch (Exception e) { Debug.Log("[RaceBoot] ranking: save failed: " + e.Message); }
        }

        // Place a time would get (0-based), -1 if it does not enter the board.
        public static int PlaceOf(float time)
        {
            for (int i = 0; i < Size; i++) if (i >= Entries.Count || time < Entries[i].Time) return i;
            return -1;
        }

        public static string FormatTime(float s)
        {
            if (s <= 0f) return "-'--\"--";
            int cs = Mathf.RoundToInt(s * 100f);
            return $"{cs / 6000}'{cs / 100 % 60:00}\"{cs % 100:00}";
        }

        // ---------------- race timing ----------------

        MonoBehaviour? rm;
        object? rmFsm;
        PropertyInfo? rmActive;
        object? lapVar;
        PropertyInfo? lapValue;
        string last = "";
        float goTime = -1f, lapStart;
        int lastLap;
        readonly List<float> laps = new List<float>();

        string State()
        {
            if (!rm)
            {
                rm = Showcase.Fsm("GameManager", "race manager");
                rmFsm = rm ? rm!.GetType().GetProperty("Fsm")?.GetValue(rm) : null;
                rmActive = rmFsm?.GetType().GetProperty("ActiveStateName", F);
                waitAction = null;
                if (!rm) return "";
            }
            if (!rm!.enabled) return "";
            return rmActive?.GetValue(rmFsm) as string ?? "";
        }

        int Laps()
        {
            if (lapVar == null)
            {
                var globals = Type.GetType("HutongGames.PlayMaker.FsmVariables, PlayMaker")?.GetProperty("GlobalVariables", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                lapVar = globals?.GetType().GetMethod("GetFsmInt", new[] { typeof(string) })?.Invoke(globals, new object[] { "AB_lap_count" });
                lapValue = lapVar?.GetType().GetProperty("Value");
            }
            return lapValue != null ? (int)lapValue.GetValue(lapVar) : 0;
        }

        // ---------------- "save?" wait ----------------

        object? waitAction, waitFloat;
        PropertyInfo? waitTime;
        float waitOriginal = 4f;
        bool holding;

        void HoldSave()
        {
            if (waitAction == null)
            {
                var states = rmFsm?.GetType().GetProperty("States", F)?.GetValue(rmFsm) as Array;
                if (states != null)
                    foreach (var s in states)
                        if (s.GetType().GetProperty("Name")?.GetValue(s) as string == "save?" && s.GetType().GetProperty("Actions", F)?.GetValue(s) is Array acts)
                            foreach (var a in acts)
                                if (a.GetType().Name == "Wait") { waitAction = a; waitFloat = a.GetType().GetField("time", F)?.GetValue(a); waitTime = waitFloat?.GetType().GetProperty("Value"); }
                if (waitTime == null) return;
                waitOriginal = (float)waitTime.GetValue(waitFloat);
            }
            waitTime?.SetValue(waitFloat, 100000f);
            holding = true;
        }

        void ReleaseSave()
        {
            if (!holding || waitTime == null) return;
            holding = false;
            float elapsed = 0f;
            if (waitAction?.GetType().GetField("timer", F)?.GetValue(waitAction) is float tv) elapsed = tv;
            waitTime.SetValue(waitFloat, elapsed + 0.4f);
        }

        // ---------------- flow ----------------

        enum Phase { None, Entry, EndBoard, Attract }
        Phase phase;
        float phaseUntil, attractNext;
        int place = -1;
        float raceTime, raceBest;

        void Update()
        {
            string st = State();

            if (st == "go" && last != "go") { goTime = Time.time; lapStart = Time.time; laps.Clear(); lastLap = Laps(); }
            if (st == "racing")
            {
                int l = Laps();
                if (l != lastLap)
                {
                    if (lastLap > 0 && l > lastLap) laps.Add(Time.time - lapStart);
                    lapStart = Time.time;
                    lastLap = l;
                }
            }
            // "finished!" lasts no frame: the game goes on to "achievment" (1st place) or "save?" at once.
            if (last == "racing" && (st == "finished!" || st == "achievment" || st == "save?") && goTime > 0f) Finished();
            // Back to normal once the end screens are done.
            if (waitTime != null && !holding && st != "save?" && st != "finished!") { waitTime.SetValue(waitFloat, waitOriginal); }

            switch (phase)
            {
                case Phase.Entry: TickEntry(); break;
                case Phase.EndBoard:
                    if (Time.unscaledTime >= phaseUntil) { Hide(); ReleaseSave(); }
                    break;
                case Phase.Attract:
                    if (Time.unscaledTime >= phaseUntil || !AttractOk(st)) { Hide(); attractNext = Time.unscaledTime + (Replay.Playing ? 24f : 10f); }
                    break;
                case Phase.None:
                    if (AttractOk(st))
                    {
                        if (attractNext <= 0f) attractNext = Time.unscaledTime + (Replay.Playing ? 20f : 10f);
                        if (Time.unscaledTime >= attractNext) { ShowBoard(-1); phase = Phase.Attract; phaseUntil = Time.unscaledTime + 7f; }
                    }
                    else attractNext = 0f;
                    break;
            }
            last = st;
        }

        // Attract mode: the replay (not its GAME OVER) or the "PRESS START" screen, with no menu open.
        static bool AttractOk(string st) => !OptionsMenu.IsOpen && !Replay.GameOverShown && (Replay.Playing || st == "skip?");

        void Finished()
        {
            raceTime = Time.time - goTime;
            raceBest = laps.Count > 0 ? laps.Min() : 0f;
            goTime = -1f;
            place = PlaceOf(raceTime);
            Debug.Log($"[RaceBoot] ranking: race {FormatTime(raceTime)}, best lap {FormatTime(raceBest)}, place {place + 1}");
            HoldSave();
            if (place >= 0) StartEntry();
            else { ShowBoard(-1); phase = Phase.EndBoard; phaseUntil = Time.unscaledTime + 5f; }
        }

        void Insert(string name)
        {
            foreach (var e in Entries) e.Fresh = false;
            var entry = new Entry { Name = name, Time = raceTime, BestLap = raceBest, Fresh = true };
            Entries.Insert(Mathf.Clamp(place, 0, Entries.Count), entry);
            Entries = Entries.Take(Size).ToList();
            Save();
            ShowBoard(Entries.IndexOf(entry));
            phase = Phase.EndBoard;
            phaseUntil = Time.unscaledTime + 6f;
        }

        // ---------------- name entry (letter wheel) ----------------

        const string Wheel = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.!?- ";
        static readonly string[] Items = Wheel.Select(c => c.ToString()).Concat(new[] { "<", "END" }).ToArray();
        const float EntryTime = 30f;
        string typed = "";
        int cursor;
        float entryUntil, nextRepeat;
        int heldDir;
        bool lastAccel = true, lastBrake = true;

        void StartEntry()
        {
            typed = ""; cursor = 0;
            entryUntil = Time.unscaledTime + EntryTime;
            lastAccel = lastBrake = true;   // the pedals must be released first
            phase = Phase.Entry;
            BuildEntry();
        }

        void TickEntry()
        {
            var p = ReInput.isReady ? ReInput.players.GetPlayer(0) : null;
            int move = 0;
            bool ok = false, back = false;
            if (p != null)
            {
                float steer = p.GetAxis("steer");
                int dir = steer > 0.5f ? 1 : steer < -0.5f ? -1 : 0;
                if (p.GetButton("right")) dir = 1;
                if (p.GetButton("left")) dir = -1;
                // One step, then auto-repeat while held.
                if (dir != 0 && (dir != heldDir || Time.unscaledTime >= nextRepeat))
                {
                    move = dir;
                    nextRepeat = Time.unscaledTime + (dir != heldDir ? 0.35f : 0.11f);
                }
                heldDir = dir;
                bool accel = p.GetAxis("handgun") > 0.6f, brake = p.GetAxis("run") > 0.6f;
                ok = p.GetButtonDown("start") || p.GetButtonDown("menu next") || (accel && !lastAccel);
                back = p.GetButtonDown("menu back") || (brake && !lastBrake);
                lastAccel = accel; lastBrake = brake;
            }
            // Keyboard: letters typed directly (when the legacy input is available).
            string typedNow = "";
            if (ReInput.isReady)
            {
                var kb = ReInput.controllers.Keyboard;
                for (var k = KeyCode.A; k <= KeyCode.Z; k++) if (kb.GetKeyDown(k)) typedNow += (char)('A' + (k - KeyCode.A));
                for (var k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) if (kb.GetKeyDown(k)) typedNow += (char)('0' + (k - KeyCode.Alpha0));
                if (kb.GetKeyDown(KeyCode.Backspace)) typedNow += '\b';
                if (kb.GetKeyDown(KeyCode.Return) || kb.GetKeyDown(KeyCode.KeypadEnter)) typedNow += '\r';
            }
            foreach (char c in typedNow)
            {
                if (c == '\b') back = true;
                else if (c == '\r' || c == '\n') ok = true;
                else if (Wheel.IndexOf(c) >= 0 && typed.Length < 3) { typed += c; cursor = Wheel.IndexOf(c); }
            }

            if (move != 0) cursor = (cursor + move + Items.Length) % Items.Length;
            if (back && typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
            if (ok)
            {
                string item = Items[cursor];
                if (item == "<") { if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1); }
                else if (item == "END") { Done(); return; }
                else if (typed.Length < 3) typed += item;
                if (typed.Length == 3) cursor = Array.IndexOf(Items, "END");
            }
            if (Time.unscaledTime >= entryUntil) { Done(); return; }
            DrawEntry();
        }

        void Done()
        {
            string name = typed.Trim().Length == 0 ? "???" : typed.PadRight(3).Substring(0, 3);
            Hide();
            Insert(name);
        }

        // ---------------- screens ----------------

        GameObject? root;
        TMP_FontAsset? font;
        readonly List<TextMeshProUGUI> wheelTexts = new List<TextMeshProUGUI>();
        TextMeshProUGUI? slots, timer, blinkTitle;
        readonly List<TextMeshProUGUI> fresh = new List<TextMeshProUGUI>();

        static readonly Color Gold = new Color(1f, 0.78f, 0.1f), Light = new Color(0.95f, 0.95f, 0.95f), Dim = new Color(0.6f, 0.6f, 0.65f);

        Transform Canvas()
        {
            DestroyRoot();
            root = new GameObject("RaceBootRankingCanvas");
            DontDestroyOnLoad(root);
            var cv = root.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 400;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var veil = new GameObject("veil", typeof(RectTransform)).AddComponent<Image>();
            veil.transform.SetParent(root.transform, false);
            var rt = veil.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            veil.color = new Color(0f, 0f, 0f, 0.92f);   // linear colour space: looks lighter than its alpha
            // The race HUD and its FINISHED! message would show through.
            foreach (var other in Resources.FindObjectsOfTypeAll<Canvas>())
                if (other && other.name == "90sGP_canvas" && other.gameObject.scene.isLoaded && other.enabled) { other.enabled = false; hiddenCanvases.Add(other); }
            veil.raycastTarget = false;
            if (!font)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) if (f && f.name == "takeover2 SDF") font = f;
            wheelTexts.Clear(); fresh.Clear(); slots = timer = blinkTitle = null;
            return root.transform;
        }

        TextMeshProUGUI Text(Transform parent, string s, float x, float y, float size, Color c, TextAlignmentOptions align = TextAlignmentOptions.Center, float width = 900f)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = s; t.fontSize = size; t.color = c; t.alignment = align;
            t.enableWordWrapping = false; t.raycastTarget = false;
            t.outlineWidth = 0.18f; t.outlineColor = Color.black;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(width, size * 1.4f);
            rt.anchoredPosition = new Vector2(x, y);
            return t;
        }

        void DestroyRoot()
        {
            if (root) Destroy(root!);
            root = null;
            foreach (var cv in hiddenCanvases) if (cv) cv.enabled = true;
            hiddenCanvases.Clear();
        }

        readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        void Hide()
        {
            DestroyRoot();
            phase = Phase.None;
        }

        // The board; `highlight` blinks (the new entry), -1 for none.
        void ShowBoard(int highlight)
        {
            var c = Canvas();
            Text(c, L.T("MEILLEURS TEMPS"), 0f, 400f, 92f, Gold);
            Text(c, L.T("NOM"), -330f, 300f, 34f, Dim, TextAlignmentOptions.Center, 240f);
            Text(c, L.T("TEMPS"), 40f, 300f, 34f, Dim, TextAlignmentOptions.Center, 400f);
            Text(c, L.T("MEILLEUR TOUR"), 440f, 300f, 34f, Dim, TextAlignmentOptions.Center, 400f);
            for (int i = 0; i < Size; i++)
            {
                var e = i < Entries.Count ? Entries[i] : new Entry();
                float y = 225f - i * 62f;
                var col = i == highlight ? Gold : i < 3 ? Light : new Color(0.82f, 0.82f, 0.86f);
                var r = Text(c, (i + 1) + ".", -560f, y, 48f, col, TextAlignmentOptions.Right, 120f);
                var n = Text(c, e.Name, -330f, y, 48f, col, TextAlignmentOptions.Center, 240f);
                var tm = Text(c, FormatTime(e.Time), 40f, y, 48f, col, TextAlignmentOptions.Center, 400f);
                var bl = Text(c, FormatTime(e.BestLap), 440f, y, 40f, col, TextAlignmentOptions.Center, 400f);
                if (i == highlight) fresh.AddRange(new[] { r, n, tm, bl });
            }
        }

        void BuildEntry()
        {
            var c = Canvas();
            blinkTitle = Text(c, L.T("NOUVEAU RECORD !"), 0f, 380f, 100f, Gold);
            Text(c, (place + 1) + ".   " + FormatTime(raceTime), 0f, 260f, 64f, Light);
            Text(c, L.T("ENTREZ VOTRE NOM"), 0f, 160f, 44f, Dim);
            slots = Text(c, "", 0f, 40f, 120f, Light);
            for (int i = -4; i <= 4; i++) wheelTexts.Add(Text(c, "", i * 150f, -170f, 70f, Light, TextAlignmentOptions.Center, 160f));
            Text(c, "^", 0f, -262f, 48f, Gold);
            timer = Text(c, "", 0f, -380f, 44f, Dim);
            DrawEntry();
        }

        void DrawEntry()
        {
            if (!root) return;
            if (slots)
            {
                var sb = new System.Text.StringBuilder();
                bool blink = (Time.unscaledTime % 0.6f) < 0.35f;
                for (int i = 0; i < 3; i++)
                {
                    if (i > 0) sb.Append("  ");
                    sb.Append(i < typed.Length ? typed[i].ToString() : i == typed.Length && blink ? (Items[cursor].Length == 1 ? Items[cursor] : "_") : "_");
                }
                slots!.text = sb.ToString();
            }
            for (int k = 0; k < wheelTexts.Count; k++)
            {
                int off = k - wheelTexts.Count / 2;
                var t = wheelTexts[k];
                string item = Items[(cursor + off + Items.Length * 4) % Items.Length];
                t.text = item == " " ? "SP" : item;
                float d = Mathf.Abs(off);
                t.fontSize = off == 0 ? 110f : Mathf.Lerp(70f, 36f, (d - 1f) / 3f);
                t.color = off == 0 ? Gold : new Color(0.9f, 0.9f, 0.95f, Mathf.Lerp(0.9f, 0.25f, (d - 1f) / 3f));
                // Letters sit on an arc, like a wheel seen from the front.
                t.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(off * 0.33f) * 620f, -170f - (1f - Mathf.Cos(off * 0.33f)) * 260f);
            }
            if (timer) timer!.text = L.T("TEMPS") + "  " + Mathf.CeilToInt(Mathf.Max(0f, entryUntil - Time.unscaledTime));
            if (blinkTitle) blinkTitle!.enabled = (Time.unscaledTime % 0.8f) < 0.55f;
        }

        void LateUpdate()
        {
            if (fresh.Count > 0)
            {
                bool on = (Time.unscaledTime % 0.5f) < 0.3f;
                foreach (var t in fresh) if (t) t.enabled = on;
            }
        }

        // ---------------- test bench ----------------

        public static void TestRace(float time, float bestLap) { if (inst != null) inst.TestFinish(time, bestLap); }
        void TestFinish(float time, float bestLap)
        {
            raceTime = time; raceBest = bestLap;
            place = PlaceOf(raceTime);
            HoldSave();
            if (place >= 0) StartEntry(); else { ShowBoard(-1); phase = Phase.EndBoard; phaseUntil = Time.unscaledTime + 5f; }
        }
        public static void TestType(string s) { if (inst != null) inst.typed = s; }
        public static void TestDone() { if (inst != null && inst.phase == Phase.Entry) inst.Done(); }
        public static string PhaseName => inst != null ? inst.phase.ToString() : "-";
    }
}
