using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // Development test bench, enabled by "testbench.txt" next to the executable (first line = mode).
    //  racedump : start a race, drive a little, dump the live scene + race manager transitions.
    public class TestBench : MonoBehaviour
    {
        static string Dir => Path.Combine(Application.dataPath, "..");
        string mode = "", arg = "";

        public static void Run()
        {
            var f = Path.Combine(Dir, "testbench.txt");
            if (!File.Exists(f) || FindObjectOfType<TestBench>()) return;
            var go = new GameObject("RaceBootTestBench");
            DontDestroyOnLoad(go);
            var tb = go.AddComponent<TestBench>();
            var lines = File.ReadAllLines(f);
            tb.mode = lines.Length > 0 ? lines[0].Trim() : "";
            tb.arg = lines.Length > 1 ? lines[1].Trim() : "";
        }

        IEnumerator Start()
        {
            // Keep running (and out of the pause menu) if the window loses the focus meanwhile.
            Application.runInBackground = true;
            yield return new WaitForSecondsRealtime(4f);
            var pauseFsm = Showcase.Fsm("GameManager", "pause");
            if (pauseFsm) pauseFsm!.enabled = false;
            if (mode == "f9") { yield return F9Test(); Application.Quit(); yield break; }
            if (mode == "shaders") { ShaderDump(); Application.Quit(); yield break; }
            if (mode == "driftwall") { yield return DriftWall(); Application.Quit(); yield break; }
            if (mode == "driftlap") { yield return DriftLap(); Application.Quit(); yield break; }
            if (mode == "finish") { yield return FinishTest(); Application.Quit(); yield break; }
            if (mode == "ranking") { yield return RankingTest(); Application.Quit(); yield break; }
            if (mode == "drift") { yield return DriftTest(); Application.Quit(); yield break; }
            if (mode == "cardump") { yield return CarDump(); Application.Quit(); yield break; }
            if (mode == "volume") { yield return VolumeTest(); Application.Quit(); yield break; }
            if (mode == "quit") { yield return QuitTest(); Application.Quit(); yield break; }
            if (mode == "audio") { yield return AudioTest(); Application.Quit(); yield break; }
            if (mode == "intro") { yield return IntroTest(); Application.Quit(); yield break; }
            if (mode == "countdown") { yield return CountdownTest(); Application.Quit(); yield break; }
            if (mode == "ui") { yield return UiTest(); Application.Quit(); yield break; }
            if (mode == "langdump") { LangDump(); Application.Quit(); yield break; }
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            if (mode == "replay")
            {
                // Arcade intro before the countdown.
                var introDir = Path.Combine(Dir, "replaytest");
                Directory.CreateDirectory(introDir);
                for (int i = 0; i < 19; i++)
                {
                    yield return new WaitForSecondsRealtime(0.55f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(introDir, $"i{i:00}.png"));
                }
                while (Arcade.IntroPlaying) yield return null;
            }
            yield return new WaitForSecondsRealtime(mode == "replay" ? 0.5f : 4.5f);
            var input = Showcase.Fsm("race_car_01", "INPUT");
            if (input) input!.enabled = false;
            float drive = mode == "replay" ? 45f : 4f;
            float until = Time.realtimeSinceStartup + drive;
            var car = GameObject.Find("race_car_01");
            while (Time.realtimeSinceStartup < until)
            {
                if (input && car)
                {
                    Autopilot(car!.transform, out float steer, out float throttle);
                    Showcase.SetFloat(input!, "accelerate", throttle); Showcase.SetFloat(input!, "brake", 0f); Showcase.SetFloat(input!, "steer raw", steer);
                }
                yield return null;
            }
            if (mode == "racedump") { RaceDump(); Application.Quit(); yield break; }
            if (mode == "replay") { yield return ReplayTest(); }
            Application.Quit();
        }

        // Follows the nearest AI car ahead (they drive the racing line).
        static Rigidbody[]? ai;
        static void Autopilot(Transform car, out float steer, out float throttle)
        {
            if (ai == null || Time.frameCount % 60 == 0)
                ai = Array.FindAll(FindObjectsOfType<Rigidbody>(), r => r.name.StartsWith("AI_car"));
            Vector3 p = car.position, f = car.forward;
            Transform? best = null; float bestD = float.MaxValue;
            foreach (var r in ai)
            {
                if (!r) continue;
                var d = r.position - p;
                float dist = d.magnitude;
                if (dist < 12f || dist > 120f) continue;
                if (Vector3.Dot(d / dist, f) < 0.5f) continue;
                if (dist < bestD) { bestD = dist; best = r.transform; }
            }
            if (!best) { steer = 0f; throttle = 1f; return; }
            float angle = Vector3.SignedAngle(f, best!.position - p, Vector3.up);
            steer = Mathf.Clamp(angle / 25f, -1f, 1f);
            throttle = Mathf.Abs(angle) > 35f ? 0.4f : 1f;
        }

        IEnumerator ReplayTest()
        {
            // Force the end of the race: the game goes through its normal end sequence and reloads the title screen.
            var rm = Showcase.Fsm("GameManager", "race manager");
            var fsm = rm?.GetType().GetProperty("Fsm")?.GetValue(rm);
            fsm?.GetType().GetMethod("SetState", new[] { typeof(string) })?.Invoke(fsm, new object[] { "finished!" });
            float wait = Time.realtimeSinceStartup + 60f;
            bool reloaded = false;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoad = (sc, m) => reloaded = true;
            SceneManager.sceneLoaded += onLoad;
            var endDir = Path.Combine(Dir, "replaytest");
            Directory.CreateDirectory(endDir);
            int shot = 0;
            float next = 0f, after = -1f;
            // End of race -> replay: captures every 0.2 s to catch any "PRESS START" in between.
            while (Time.realtimeSinceStartup < wait && (after < 0f || Time.realtimeSinceStartup < after))
            {
                if (Replay.Playing && after < 0f) after = Time.realtimeSinceStartup + 1.5f;
                if (reloaded && Time.realtimeSinceStartup >= next && shot < 120) { next = Time.realtimeSinceStartup + 0.1f; ScreenCapture.CaptureScreenshot(Path.Combine(endDir, $"e{shot++:000}.png")); }
                yield return null;
            }
            SceneManager.sceneLoaded -= onLoad;
            Debug.Log("[RaceBoot] testbench: replay playing = " + Replay.Playing + ", end captures = " + shot);
            var outDir = Path.Combine(Dir, "replaytest");
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < 24; i++)
            {
                if (i == 12) Replay.DebugForceShot = "Hood";
                yield return new WaitForSecondsRealtime(2.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"r{i:00}.png"));
            }
            Replay.DebugForceShot = null;

            // START during the replay: GAME OVER, then the "PRESS START" screen, then a normal race.
            Replay.PressForTest();
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log("[RaceBoot] testbench: game over shown = " + Replay.GameOverShown);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "y_gameover.png"));
            yield return new WaitForSecondsRealtime(1.3f);
            for (int k = 0; k < 30; k++)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"x{k:00}.png"));
                yield return new WaitForSecondsRealtime(0.07f);
            }
            Debug.Log("[RaceBoot] testbench: after game over, replay playing = " + Replay.Playing);
            foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>())
                if (cv.name == "press_start_canvas" && cv.gameObject.scene.isLoaded)
                    Debug.Log($"[RaceBoot] testbench: press_start active={cv.gameObject.activeInHierarchy} enabled={cv.enabled} mode={cv.renderMode} cam={(cv.worldCamera ? cv.worldCamera.name + " en=" + cv.worldCamera.enabled : "null")}");
            var rm2 = Showcase.Fsm("GameManager", "race manager");
            var f2 = rm2?.GetType().GetProperty("Fsm")?.GetValue(rm2);
            Debug.Log("[RaceBoot] testbench: race manager enabled=" + (rm2 ? rm2!.enabled : false) + " state=" + f2?.GetType().GetProperty("ActiveStateName")?.GetValue(f2));
            for (int k = 0; k < 4; k++)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"y_pressstart{k}.png"));
                yield return new WaitForSecondsRealtime(0.2f);
            }
            yield return new WaitForSecondsRealtime(1f);
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(6f);
            Debug.Log("[RaceBoot] testbench: after new race, replay playing = " + Replay.Playing);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "z_newrace.png"));
            yield return new WaitForSecondsRealtime(1f);
        }

        // Saved replay: F9 on the title screen, without racing.
        IEnumerator F9Test()
        {
            Replay.PlaySaved();
            Debug.Log("[RaceBoot] testbench: f9 replay playing = " + Replay.Playing);
            var outDir = Path.Combine(Dir, "replaytest");
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSecondsRealtime(3f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"f{i:00}.png"));
            }
            Replay.PlaySaved();   // restart
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[RaceBoot] testbench: f9 restart playing = " + Replay.Playing);
        }

        static void LangDump()
        {
            const BindingFlags A = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var sb = new StringBuilder();
            sb.AppendLine("=== TEXT ASSETS ===");
            foreach (var t in Resources.FindObjectsOfTypeAll<TextAsset>())
                try { if (t && t.text != null) sb.AppendLine($" {t.name} ({t.text.Length}) {t.text.Substring(0, Math.Min(120, t.text.Length)).Replace((char)10, (char)32)}"); } catch { sb.AppendLine(" ! text asset " + (t ? t.name : "null")); }
            sb.AppendLine("=== FONTS ===");
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) sb.AppendLine(" Font " + f.name);
            foreach (var o in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                if (o.GetType().Name.Contains("Font")) sb.AppendLine($" {o.GetType().Name} {o.name}");
            sb.AppendLine("=== TEXTS IN UI ===");
            foreach (var c in Resources.FindObjectsOfTypeAll<Component>())
            {
                try {
                if (!c || !c.gameObject.scene.isLoaded) continue;
                var tp = c.GetType();
                if (!(tp.Name.Contains("Text") && tp.GetProperty("text") != null)) continue;
                var font = tp.GetProperty("font")?.GetValue(c) as UnityEngine.Object;
                string txt = (tp.GetProperty("text")?.GetValue(c) as string ?? "").Replace((char)10, (char)32);
                sb.AppendLine($" {PathOf(c.transform)} [{tp.Name}] font={(font ? font!.name : "-")} text={txt.Substring(0, Math.Min(60, txt.Length))}");
                } catch (Exception ex) { sb.AppendLine(" ! " + ex.Message); }
            }
            sb.AppendLine("=== LOCALIZATION TYPES ===");
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Assembly-CSharp") continue;
                foreach (var tp in asm.GetTypes())
                try {
                    if (!(tp.Name.Contains("Locali") || tp.Name.Contains("Language"))) continue;
                    sb.AppendLine(" TYPE " + tp.FullName);
                    foreach (var f in tp.GetFields(A))
                    {
                        object? v = null;
                        if (f.IsStatic) try { v = f.GetValue(null); } catch { }
                        sb.AppendLine($"   field {f.FieldType.Name} {f.Name}{(f.IsStatic ? " = " + v : "")}");
                    }
                    foreach (var m in tp.GetMethods(A)) if (m.DeclaringType == tp) sb.AppendLine("   method " + m);
                    if (typeof(UnityEngine.Object).IsAssignableFrom(tp))
                        foreach (var inst in Resources.FindObjectsOfTypeAll(tp))
                        {
                            sb.AppendLine("   INSTANCE " + inst.name);
                            foreach (var f in tp.GetFields(A)) if (!f.IsStatic) try { var v = f.GetValue(inst); sb.AppendLine($"     {f.Name} = {(v is System.Collections.IEnumerable e && !(v is string) ? string.Join(",", System.Linq.Enumerable.Cast<object>(e)) : v)}"); } catch { }
                        }
                } catch (Exception ex) { sb.AppendLine(" ! " + ex.Message); }
            }
            sb.AppendLine("=== TMP SOURCE FONTS ===");
            foreach (var o in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                if (o.GetType().Name == "TMP_FontAsset")
                {
                    var src = o.GetType().GetProperty("sourceFontFile", A)?.GetValue(o) as Font ?? o.GetType().GetField("m_SourceFontFile", A)?.GetValue(o) as Font;
                    var mode = o.GetType().GetProperty("atlasPopulationMode", A)?.GetValue(o);
                    sb.AppendLine($" {o.name}: source={(src ? src!.name + " dynamic=" + src.dynamic : "none")} mode={mode}");
                }
            sb.AppendLine("=== LANGUAGE FSMS ===");
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                try {
                if (!mb || mb.GetType().Name != "PlayMakerFSM" || !mb.gameObject.scene.isLoaded) continue;
                var fsm = mb.GetType().GetProperty("Fsm")?.GetValue(mb);
                if (fsm == null) continue;
                var states = (Array)fsm.GetType().GetProperty("States", A)!.GetValue(fsm);
                string fsmName = mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string ?? "";
                string pth = PathOf(mb.transform).ToLower();
                bool hit = pth.Contains("music") || fsmName.Contains("bgm") || fsmName.Contains("volume") || pth.Contains("transi") || pth.Contains("fade") || pth.Contains("wipe") || pth.Contains("loading") || pth.Contains("dots") || fsmName.ToLower().Contains("transi") || fsmName.ToLower().Contains("fade");
                if (!hit) continue;
                sb.AppendLine(" FSM " + PathOf(mb.transform) + " : " + mb.GetType().GetProperty("FsmName")?.GetValue(mb));
                foreach (var st in states)
                {
                    sb.Append("  state " + st.GetType().GetProperty("Name")?.GetValue(st) + ":");
                    var acts = st.GetType().GetProperty("Actions", A)?.GetValue(st) as Array;
                    if (acts != null) foreach (var ac in acts)
                    {
                        sb.Append(" [" + ac.GetType().Name);
                        foreach (var f in ac.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            var v = f.GetValue(ac);
                            string? sv = v is string str ? str : v?.GetType().GetProperty("Value")?.GetValue(v)?.ToString();
                            if (!string.IsNullOrEmpty(sv) && sv!.Length < 60) sb.Append(" " + f.Name + "=" + sv);
                        }
                        sb.Append("]");
                    }
                    sb.AppendLine();
                }
                } catch (Exception ex) { sb.AppendLine(" ! fsm " + ex.Message); }
            }
            sb.AppendLine("=== OBJECTS *over* ===");
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t && t.gameObject.scene.isLoaded && (t.name.ToLower().Contains("transi") || t.name.ToLower().Contains("fade") || t.name.ToLower().Contains("wipe") || t.name.ToLower().Contains("dot") || t.name.ToLower().Contains("halftone")))
                    sb.AppendLine($" {PathOf(t)} active={t.gameObject.activeInHierarchy}");
            sb.AppendLine("=== TRANSITIONER ===");
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t && t.gameObject.scene.isLoaded && t.name == "T2_Transitioner") Walk(t, 0, sb);
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if (mb && mb.gameObject.name == "T2_Transitioner" && mb.GetType().Name != "PlayMakerFSM")
                    foreach (var f in mb.GetType().GetFields(A)) { try { var v = f.GetValue(mb); sb.AppendLine($"  {mb.GetType().Name}.{f.Name} = {(v is UnityEngine.Object uo ? (uo ? uo.name : "null") : v)}"); } catch { } }
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>())
                if (c && c.gameObject.scene.isLoaded) sb.AppendLine($" CAM {PathOf(c.transform)} en={c.enabled} depth={c.depth} rt={(c.targetTexture ? c.targetTexture!.name : "-")} mask={c.cullingMask}");
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if (mb && mb.GetType().Name == "PlayMakerFSM" && mb.gameObject.name == "T2_Transitioner")
                {
                    var fsm = mb.GetType().GetProperty("Fsm")?.GetValue(mb);
                    foreach (var st in (Array)fsm!.GetType().GetProperty("States", A)!.GetValue(fsm))
                        foreach (var ac in (Array)st.GetType().GetProperty("Actions", A)!.GetValue(st))
                        {
                            sb.Append("  action " + ac.GetType().FullName + ":");
                            foreach (var f in ac.GetType().GetFields(A)) { try { var v = f.GetValue(ac); sb.Append(" " + f.Name + "=" + (v?.GetType().GetProperty("Value")?.GetValue(v) ?? v)); } catch { } }
                            sb.AppendLine();
                        }
                    var vars = mb.GetType().GetProperty("FsmVariables")?.GetValue(mb);
                }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var tp in SafeTypes(asm))
                    if (tp.Name.Contains("Transition") && (tp.Namespace ?? "").IndexOf("UnityEngine", StringComparison.Ordinal) < 0 && tp.Name != "Transition")
                    {
                        sb.AppendLine(" TYPE " + tp.FullName + " in " + asm.GetName().Name);
                        foreach (var m in tp.GetMethods(A)) if (m.DeclaringType == tp && m.IsPublic) sb.AppendLine("   method " + m);
                    }
            sb.AppendLine("=== AUDIO SOURCES ===");
            foreach (var src in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (src && src.gameObject.scene.IsValid())
                    sb.AppendLine($" {PathOf(src.transform)} clip={(src.clip ? src.clip!.name + " " + src.clip.length.ToString("F0") + "s" : "-")} vol={src.volume:F2} loop={src.loop} playing={src.isPlaying} group={(src.outputAudioMixerGroup ? src.outputAudioMixerGroup!.audioMixer.name + "/" + src.outputAudioMixerGroup.name : "-")} active={src.gameObject.activeInHierarchy}");
            sb.AppendLine("=== MIXERS ===");
            foreach (var m in Resources.FindObjectsOfTypeAll<UnityEngine.Audio.AudioMixer>())
            {
                sb.Append(" " + m.name + ":");
                foreach (var g in m.FindMatchingGroups("")) sb.Append(" " + g.name);
                sb.AppendLine();
                foreach (var pn in new[] { "MasterVolume", "Master", "MusicVolume", "Music", "BGM", "bgm", "SFXVolume", "SFX", "sfx", "Volume", "music_volume", "sfx_volume", "bgm_volume" })
                    if (m.GetFloat(pn, out float val)) sb.AppendLine($"   param {pn} = {val}");
            }
            sb.AppendLine("=== LONG CLIPS ===");
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (c && c.length > 40f) sb.AppendLine($" {c.name} {c.length:F0}s loadType={c.loadType} state={c.loadState}");
            sb.AppendLine("=== STATIC RENDERERS ===");
            int total = 0, withCol = 0, readable = 0, big = 0, bigNoCol = 0, bigNoColReadable = 0;
            foreach (var mr in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            try {
                if (!mr || !mr.gameObject.scene.isLoaded || !mr.gameObject.activeInHierarchy || mr.gameObject.scene.name == "DontDestroyOnLoad") continue;
                total++;
                bool col = mr.GetComponent<Collider>() != null;
                if (col) withCol++;
                var mf = mr.GetComponent<MeshFilter>();
                bool rd = mf && mf.sharedMesh && mf.sharedMesh.isReadable;
                if (rd) readable++;
                var sz = mr.bounds.size;
                if (Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) > 3f) { big++; if (!col) { bigNoCol++; if (rd) bigNoColReadable++; } }
            } catch { }
            sb.AppendLine($" total={total} withCollider={withCol} readable={readable} big={big} bigNoCollider={bigNoCol} bigNoColliderReadable={bigNoColReadable}");
            sb.AppendLine("=== PLAYERPREFS lang ===");
            foreach (var k in new[] { "language", "Language", "lang", "LanguageID", "CurrentLanguage" }) sb.AppendLine($" {k} = {PlayerPrefs.GetString(k, "?")} / {PlayerPrefs.GetInt(k, -99)}");
            File.WriteAllText(Path.Combine(Dir, "langdump.txt"), sb.ToString());
        }

        // Options menu in every language, then the F8 free camera during a race.
        IEnumerator UiTest()
        {
            var outDir = Path.Combine(Dir, "uitest");
            Directory.CreateDirectory(outDir);
            string saved = L.Code;
            OptionsMenu.SetOpenForTest(true);
            int n = 0;
            foreach (var code in L.Codes)
            {
                L.Code = code;
                OptionsMenu.SetTabForTest(n % 2 == 0 ? 0 : 2);
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"menu_{n++}_{code}.png"));
                yield return null;
            }
            L.Code = saved;
            OptionsMenu.SetOpenForTest(false);

            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(6f);
            FreeCam.ToggleForTest();
            Debug.Log("[RaceBoot] testbench: freecam active = " + FreeCam.Active + ", timeScale = " + Time.timeScale);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "free_0.png"));
            FreeCam.MoveForTest(new Vector3(0f, 25f, 0f), 120f);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "free_1.png"));
            FreeCam.MoveForTest(new Vector3(60f, 40f, -80f), 90f);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "free_2.png"));
            FreeCam.ToggleForTest();
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[RaceBoot] testbench: freecam off, active = " + FreeCam.Active + ", timeScale = " + Time.timeScale);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "free_3_back.png"));
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // Race intro with a given camera view (second line of testbench.txt), the player's view is put back.
        IEnumerator IntroTest()
        {
            int saved = Settings.CameraView;
            if (int.TryParse(arg, out int v)) Settings.CameraView = v;
            var dir = Path.Combine(Dir, "introtest");
            Directory.CreateDirectory(dir);
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            for (int i = 0; i < 25; i++)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"v{Settings.CameraView}_{i:00}.png"));
            }
            yield return null;
            Settings.CameraView = saved;
            Settings.Save();
        }

        // A pooled sound started while the effects are at 30 %: its volume must be scaled in that very frame.
        // Physics of the player's car and the race manager variables, during a race.
        // Scripted driving with a drift style (second line of testbench.txt): each game's entry gesture, then
        // straightening the car (grip must come back), then a second slide swung through to the other side.
        IEnumerator DriftTest()
        {
            int saved = Settings.DriftStyle;
            bool quick = arg.EndsWith("q");
            if (int.TryParse(arg.TrimEnd('q'), out int st)) Settings.DriftStyle = st;
            bool intro = Settings.IntroEnabled; Settings.IntroEnabled = false;
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            var input = Showcase.Fsm("race_car_01", "INPUT");
            yield return new WaitForSecondsRealtime(4.6f);
            if (input) input!.enabled = false;
            float a = 1f, b = 0f, s = 0f; string label = "";
            float next = 0f;
            IEnumerator Run(float d)
            {
                float end = Time.time + d;
                while (Time.time < end)
                {
                    Showcase.SetFloat(input!, "accelerate", a); Showcase.SetFloat(input!, "brake", b); Showcase.SetFloat(input!, "steer raw", s);
                    if (Time.time >= next)
                    {
                        next = Time.time + 0.05f;
                        Debug.Log($"[RaceBoot] drift style={Settings.DriftStyle} {label,-12} a={a:F1} b={b:F1} s={s,5:F2} kmh={Drift.Kmh,4:F0} drifting={(Drift.Drifting ? 1 : 0)} dir={Drift.Direction,2} angle={Drift.SlipAngle,6:F1} yaw={Drift.YawRate,5:F2}");
                    }
                    yield return new WaitForFixedUpdate();
                }
            }
            IEnumerator Gesture()
            {
                switch (Settings.DriftStyle)
                {
                    case 1:
                        if (quick)
                        {
                            // In a bend, lift / brake / gas in a quarter of a second, the throttle ramped back up.
                            label = "turn"; s = 0.5f; yield return Run(0.3f);
                            label = "lift"; a = 0f; yield return Run(0.04f);
                            label = "brake"; b = 1f; yield return Run(0.06f); b = 0f;
                            label = "gas"; a = 0.5f; yield return Run(0.04f); a = 0.8f; yield return Run(0.04f); a = 1f; yield return Run(0.04f);
                            break;
                        }
                        label = "lift"; a = 0f; s = 0f; yield return Run(0.45f); label = "flick"; s = 0.9f; yield return Run(0.1f); a = 1f; break;
                    case 2: label = "brake"; a = 1f; b = 1f; s = 0f; yield return Run(0.1f); b = 0f; label = "flick"; s = 0.9f; yield return Run(0.05f); break;
                    default: label = "feint"; a = 0f; b = 1f; s = -0.6f; yield return Run(0.25f); b = 0f; label = "flick"; s = 0.9f; yield return Run(0.05f); a = 1f; break;
                }
            }
            label = "straight"; a = 1f; s = 0f; yield return Run(2.6f);
            yield return Gesture();
            label = "hold"; a = 1f; s = 0.8f; yield return Run(0.15f);
            label = "neutral"; s = 0f; yield return Run(0.25f);
            // Straighten: counter-steer until the car is straight, then wheel centred.
            label = "straighten";
            float until = Time.time + 2f;
            while (Time.time < until && Drift.Drifting && Mathf.Abs(Drift.SlipAngle) > 3f) { s = -Mathf.Sign(Drift.SlipAngle) * 0.6f; yield return Run(Time.fixedDeltaTime); }
            label = "centred"; s = 0f; yield return Run(0.5f);
            Debug.Log($"[RaceBoot] drift CHECK straighten -> drifting={Drift.Drifting} kmh={Drift.Kmh:F0}");
            label = "straight2"; s = 0f; yield return Run(2.2f);
            yield return Gesture();
            label = "hold2"; a = 1f; s = 0.8f; yield return Run(0.3f);
            label = "swing"; s = -0.9f; yield return Run(1.0f);
            Debug.Log($"[RaceBoot] drift CHECK swing -> drifting={Drift.Drifting} dir={Drift.Direction}");
            label = "end"; s = 0f; yield return Run(0.3f);
            Settings.DriftStyle = saved; Settings.IntroEnabled = intro;
            Settings.Save();
        }

        // A slide straight into the wall of the start straight, then the style switched to the original.
        IEnumerator DriftWall()
        {
            int saved = Settings.DriftStyle;
            Settings.DriftStyle = 1;
            bool intro = Settings.IntroEnabled; Settings.IntroEnabled = false;
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            var input = Showcase.Fsm("race_car_01", "INPUT");
            yield return new WaitForSecondsRealtime(4.6f);
            if (input) input!.enabled = false;
            float a = 1f, s = 0f; string label = ""; float next = 0f;
            IEnumerator Run(float d)
            {
                float end = Time.time + d;
                while (Time.time < end)
                {
                    Showcase.SetFloat(input!, "accelerate", a); Showcase.SetFloat(input!, "brake", 0f); Showcase.SetFloat(input!, "steer raw", s);
                    if (Time.time >= next) { next = Time.time + 0.1f; Debug.Log($"[RaceBoot] wall {label,-10} style={Settings.DriftStyle} s={s,5:F2} kmh={Drift.Kmh,4:F0} drifting={(Drift.Drifting ? 1 : 0)} angle={Drift.SlipAngle,6:F1} yaw={Drift.YawRate,5:F2} {Drift.Debug}"); }
                    yield return new WaitForFixedUpdate();
                }
            }
            label = "straight"; yield return Run(2.4f);
            label = "lift"; a = 0f; yield return Run(0.45f);
            label = "flick"; s = 0.9f; yield return Run(0.1f); a = 1f;
            label = "into wall"; s = 0.9f; yield return Run(2.0f);
            label = "centred"; s = 0f; yield return Run(1.0f);
            Settings.DriftStyle = 2;
            label = "outrun"; yield return Run(0.6f);
            Settings.DriftStyle = 0;
            label = "origine"; yield return Run(2.0f);
            label = "steer"; s = -0.8f; yield return Run(1.0f);
            Settings.DriftStyle = saved; Settings.IntroEnabled = intro;
            Settings.Save();
        }

        // A lap of the real track with a drift style (arg): an autopilot follows the AI cars and starts a slide
        // in every bend the way a player would (lift-off flick, or brake tap for Daytona), steering as usual.
        IEnumerator DriftLap()
        {
            int saved = Settings.DriftStyle;
            if (int.TryParse(arg, out int style)) Settings.DriftStyle = style;
            bool intro = Settings.IntroEnabled; Settings.IntroEnabled = false;
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            var input = Showcase.Fsm("race_car_01", "INPUT");
            yield return new WaitForSecondsRealtime(4.6f);
            if (input) input!.enabled = false;
            var car = GameObject.Find("race_car_01")!.transform;
            float end = Time.time + 60f, lastDrift = -10f, flickUntil = -1f, next = 0f, driftStart = 0f, maxDrift = 0f;
            int entries = 0, slow = 0, spins = 0; bool wasDrift = false, wasSlow = false, wasSpin = false;
            Vector3 lastPos = car.position; float dist = 0f;
            float stuckSince = -1f, reverseUntil = -1f;
            while (Time.time < end)
            {
                float steer = 0f, throttle = 1f;
                try { Autopilot(car, out steer, out throttle); } catch { ai = null; }
                float accel = 1f, brake = 0f;
                // Stuck against something: back off like a player would.
                if (Drift.Kmh < 10f) { if (stuckSince < 0f) stuckSince = Time.time; } else stuckSince = -1f;
                if (stuckSince > 0f && Time.time - stuckSince > 1.5f) { reverseUntil = Time.time + 1.5f; stuckSince = -1f; }
                if (reverseUntil > Time.time) { accel = 0f; brake = 1f; steer = -steer; }
                // Bend ahead: start a slide like a player would.
                if (!Drift.Drifting && Time.time - lastDrift > 2f && Mathf.Abs(steer) > 0.5f && Drift.Kmh > 160f && flickUntil < 0f) flickUntil = Time.time + 0.15f;
                if (flickUntil > 0f)
                {
                    if (Settings.DriftStyle == 3 || Settings.DriftStyle == 0) brake = 1f; else accel = 0f;
                    if (Time.time > flickUntil) { flickUntil = -1f; lastDrift = Time.time; }
                }
                Showcase.SetFloat(input!, "accelerate", accel * Mathf.Max(throttle, 0.4f));
                Showcase.SetFloat(input!, "brake", brake);
                Showcase.SetFloat(input!, "steer raw", steer);
                var b = Showcase.Fsm("race_car_01", "INPUT");
                if (Drift.Drifting && !wasDrift) { entries++; driftStart = Time.time; }
                if (Drift.Drifting) maxDrift = Mathf.Max(maxDrift, Time.time - driftStart);
                if (Drift.Spinning && !wasSpin) spins++;
                bool isSlow = Drift.Kmh < 70f; if (isSlow && !wasSlow) slow++;
                wasDrift = Drift.Drifting; wasSpin = Drift.Spinning; wasSlow = isSlow;
                dist += Vector3.Distance(lastPos, car.position); lastPos = car.position;
                if (Time.time >= next)
                {
                    next = Time.time + 1f;
                    Debug.Log($"[RaceBoot] driftlap style={Settings.DriftStyle} kmh={Drift.Kmh,4:F0} drift={(Drift.Drifting ? 1 : 0)} slip={Drift.SlipAngle,6:F1} yaw={Drift.YawRate,5:F2} steer={steer,5:F2} dist={dist,5:F0}");
                }
                yield return new WaitForFixedUpdate();
            }
            Debug.Log($"[RaceBoot] driftlap RESULT style={Settings.DriftStyle} distance={dist:F0} m entries={entries} longest={maxDrift:F1}s spins={spins} under70={slow}");
            Settings.DriftStyle = saved; Settings.IntroEnabled = intro;
            Settings.Save();
        }

        // A race ended by the game itself: laps counter pushed past the last lap after 20 s of racing.
        IEnumerator FinishTest()
        {
            bool intro = Settings.IntroEnabled; Settings.IntroEnabled = false;
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(25f);
            var globals = Type.GetType("HutongGames.PlayMaker.FsmVariables, PlayMaker")?.GetProperty("GlobalVariables", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var lap = globals?.GetType().GetMethod("GetFsmInt", new[] { typeof(string) })?.Invoke(globals, new object[] { "AB_lap_count" });
            lap?.GetType().GetProperty("Value")?.SetValue(lap, 7);
            var dir = Path.Combine(Dir, "uitest");
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSecondsRealtime(2f);
                var rm = Showcase.Fsm("GameManager", "race manager");
                var fsm = rm?.GetType().GetProperty("Fsm")?.GetValue(rm);
                Debug.Log($"[RaceBoot] testbench: finish +{2 * (i + 1)}s state={fsm?.GetType().GetProperty("ActiveStateName")?.GetValue(fsm)} ranking={Ranking.PhaseName}");
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"finish{i}.png"));
            }
            Ranking.TestType("ZZZ");
            Ranking.TestDone();
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSecondsRealtime(2f);
                var rm = Showcase.Fsm("GameManager", "race manager");
                var fsm = rm?.GetType().GetProperty("Fsm")?.GetValue(rm);
                Debug.Log($"[RaceBoot] testbench: after name +{2 * (i + 1)}s state={fsm?.GetType().GetProperty("ActiveStateName")?.GetValue(fsm)} ranking={Ranking.PhaseName} replay={Replay.Playing}");
            }
            Settings.IntroEnabled = intro;
        }

        IEnumerator RankingTest()
        {
            var dir = Path.Combine(Dir, "uitest");
            Directory.CreateDirectory(dir);
            Ranking.TestRace(214.37f, 32.91f);
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rank_entry1.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            Ranking.TestType("AC");
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rank_entry2.png"));
            yield return new WaitForSecondsRealtime(0.3f);
            Ranking.TestType("ACE");
            Ranking.TestDone();
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[RaceBoot] testbench: ranking phase " + Ranking.PhaseName + ", first " + Ranking.Entries[0].Name + " " + Ranking.FormatTime(Ranking.Entries[0].Time));
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rank_board.png"));
            float until = Time.realtimeSinceStartup + 25f;
            while (Ranking.PhaseName != "Attract" && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[RaceBoot] testbench: attract phase " + Ranking.PhaseName);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "rank_attract.png"));
            yield return new WaitForSecondsRealtime(0.5f);
        }

        IEnumerator CarDump()
        {
            const BindingFlags A = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(14f);
            var sb = new StringBuilder();
            var car = GameObject.Find("race_car_01");
            foreach (var rb in car!.GetComponentsInChildren<Rigidbody>(true))
                sb.AppendLine($"RB {PathOf(rb.transform)} mass={rb.mass} drag={rb.linearDamping} adrag={rb.angularDamping} kin={rb.isKinematic} cons={rb.constraints} com={rb.centerOfMass} inertia={rb.inertiaTensor} interp={rb.interpolation}");
            foreach (var j in car.GetComponentsInChildren<Joint>(true)) sb.AppendLine($"JOINT {PathOf(j.transform)} {j.GetType().Name} -> {(j.connectedBody ? j.connectedBody!.name : "-")}");
            foreach (var c in car.GetComponentsInChildren<Collider>(true)) sb.AppendLine($"COL {PathOf(c.transform)} {c.GetType().Name} mat={(c.sharedMaterial ? c.sharedMaterial!.name + " df=" + c.sharedMaterial.dynamicFriction + " sf=" + c.sharedMaterial.staticFriction : "-")}");
            var gm = GameObject.Find("GameManager");
            var all = new List<MonoBehaviour>(car.GetComponentsInChildren<MonoBehaviour>(true));
            if (gm) all.AddRange(gm!.GetComponents<MonoBehaviour>());
            foreach (var mb in all)
            {
                if (mb.GetType().Name != "PlayMakerFSM") { sb.AppendLine("MB " + PathOf(mb.transform) + " " + mb.GetType().FullName); continue; }
                string fname = mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string ?? "";
                if (!(fname == "race manager" || fname == "timer countdown" || fname == "steer" || fname == "downforce" || fname == "drift" || fname == "drift enter exit" || fname == "accelerate brake" || fname == "INPUT" || fname == "car body drift angle" || fname == "orient to track, speed kph" || fname == "crash")) continue;
                var fsm = mb.GetType().GetProperty("Fsm")?.GetValue(mb);
                sb.AppendLine($"FSM {PathOf(mb.transform)} : {fname} active={fsm?.GetType().GetProperty("ActiveStateName")?.GetValue(fsm)}");
                var vars = mb.GetType().GetProperty("FsmVariables")?.GetValue(mb);
                foreach (var listName in new[] { "FloatVariables", "BoolVariables", "IntVariables", "Vector3Variables" })
                    if (vars?.GetType().GetProperty(listName)?.GetValue(vars) is Array arr)
                        foreach (var v in arr) sb.AppendLine($"   var {v.GetType().GetProperty("Name")?.GetValue(v)} = {v.GetType().GetProperty("Value")?.GetValue(v)}");
                foreach (var st in (Array)fsm!.GetType().GetProperty("States", A)!.GetValue(fsm))
                {
                    sb.Append("   state " + st.GetType().GetProperty("Name")?.GetValue(st) + ":");
                    foreach (var tr in (Array)st.GetType().GetProperty("Transitions", A)!.GetValue(st))
                        sb.Append(" [" + tr.GetType().GetProperty("EventName", A)?.GetValue(tr) + "->" + tr.GetType().GetProperty("ToState", A)?.GetValue(tr) + "]");
                    sb.AppendLine();
                    foreach (var ac in (Array)st.GetType().GetProperty("Actions", A)!.GetValue(st))
                    {
                        sb.Append("      " + ac.GetType().Name + ":");
                        foreach (var f in ac.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            object? val = null; try { val = f.GetValue(ac); } catch { }
                            string desc = val == null ? "null" : val.GetType().GetProperty("Name")?.GetValue(val) is string nm && nm != "" && !(val is UnityEngine.Object) ? nm + "(" + val.GetType().GetProperty("Value")?.GetValue(val) + ")" : (val.GetType().GetProperty("Value")?.GetValue(val) ?? val).ToString();
                            if (desc.Length > 70) desc = desc.Substring(0, 70);
                            sb.Append(" " + f.Name + "=" + desc);
                        }
                        sb.AppendLine();
                    }
                }
            }
            var rm = Showcase.Fsm("GameManager", "race manager");
            var rv = rm!.GetType().GetProperty("FsmVariables")?.GetValue(rm);
            foreach (var listName in new[] { "FloatVariables", "BoolVariables", "IntVariables", "StringVariables" })
                if (rv?.GetType().GetProperty(listName)?.GetValue(rv) is Array arr)
                    foreach (var v in arr) sb.AppendLine($"RM var {v.GetType().GetProperty("Name")?.GetValue(v)} = {v.GetType().GetProperty("Value")?.GetValue(v)}");
            var gl = Type.GetType("HutongGames.PlayMaker.FsmVariables, PlayMaker")?.GetProperty("GlobalVariables", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            foreach (var listName in new[] { "IntVariables", "FloatVariables", "BoolVariables" })
                if (gl?.GetType().GetProperty(listName)?.GetValue(gl) is Array arr)
                    foreach (var v in arr) sb.AppendLine($"GLOBAL {listName} {v.GetType().GetProperty("Name")?.GetValue(v)} = {v.GetType().GetProperty("Value")?.GetValue(v)}");
            File.WriteAllText(Path.Combine(Dir, "cardump.txt"), sb.ToString());
        }

        IEnumerator VolumeTest()
        {
            float fx = Settings.VolFx, eng = Settings.VolEngine;
            Settings.VolFx = 0.3f; Settings.VolEngine = 2f;
            yield return new WaitForSecondsRealtime(2f);
            AudioSource? pooled = null;
            foreach (var src in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (src && src.gameObject.scene.IsValid() && !src.gameObject.activeInHierarchy && src.clip && src.transform.parent && src.transform.parent.name == "PoolBoss_T2") { pooled = src; break; }
            if (pooled != null)
            {
                float before = pooled.volume;
                pooled.volume = 0.8f;                 // as a pool would reset it
                GameAudio.Watch = pooled;
                pooled.gameObject.SetActive(true);
                pooled.Play();
                yield return new WaitForEndOfFrame();
                Debug.Log($"[RaceBoot] testbench: pooled {pooled.name} started at volume {pooled.volume:F2} (game value 0.80, effects 30 %)");
                Debug.Log("[RaceBoot] testbench: probe " + GameAudio.Probe(pooled));
                Debug.Log("[RaceBoot] testbench: volume used by the audio mix in that frame = " + GameAudio.WatchedVolume.ToString("F2"));
                pooled.Stop(); pooled.gameObject.SetActive(false); pooled.volume = before;
            }
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(12f);
            foreach (var src in FindObjectsOfType<AudioSource>())
                if (src.outputAudioMixerGroup || src.name.ToLower().Contains("engine"))
                    Debug.Log($"[RaceBoot] testbench: engine? {PathOf(src.transform)} vol={src.volume:F2} mixer={(src.outputAudioMixerGroup ? src.outputAudioMixerGroup!.audioMixer.name : "-")} playing={src.isPlaying} {GameAudio.Probe(src)}");
            foreach (var g in FindObjectsOfType<Gain>())
                Debug.Log($"[RaceBoot] testbench: gain {g.Factor:F2} on {PathOf(g.transform)} vol={g.GetComponent<AudioSource>()?.volume:F2}");
            Settings.VolFx = fx; Settings.VolEngine = eng;
            Settings.Save();
        }

        IEnumerator QuitTest()
        {
            var p = Rewired.ReInput.players.GetPlayer(0);
            foreach (var map in p.controllers.maps.GetAllMaps(Rewired.ControllerType.Keyboard))
                foreach (var aem in map.AllMaps)
                {
                    var act = Rewired.ReInput.mapping.GetAction(aem.actionId);
                    if (act != null && (act.name == "start" || act.name == "menu back" || act.name == "menu next"))
                        Debug.Log($"[RaceBoot] testbench: key {aem.elementIdentifierName} -> {act.name}");
                }
            var dir = Path.Combine(Dir, "uitest");
            Directory.CreateDirectory(dir);
            OptionsMenu.AskQuitForTest(true);
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "quit.png"));
            yield return null;
            Debug.Log("[RaceBoot] testbench: quit dialog timeScale = " + Time.timeScale);
            OptionsMenu.AskQuitForTest(false);
            yield return null;
            Debug.Log("[RaceBoot] testbench: after cancel timeScale = " + Time.timeScale);
        }

        IEnumerator AudioTest()
        {
            string savedTrack = Settings.Soundtrack; float savedMusic = Settings.VolMusic;
            AudioSource? music = null;
            foreach (var src in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (src && src.name == "music" && src.gameObject.activeInHierarchy && src.transform.parent && src.transform.parent.name == "loading object") music = src;
            void Log(string what) => Debug.Log($"[RaceBoot] testbench: audio {what}: clip={(music && music!.clip ? music.clip!.name : "-")} playing={(music ? music!.isPlaying : false)} vol={(music ? music!.volume : -1f):F2} time={(music ? music!.time : 0f):F1}");
            Log("start");
            foreach (var id in new[] { "file:test_tone.wav", "*random", "" })
            {
                Settings.Soundtrack = id;
                yield return new WaitForSecondsRealtime(2.5f);
                Log(id == "" ? "original" : id);
            }
            Settings.VolMusic = 0.5f;
            yield return new WaitForSecondsRealtime(0.5f);
            Log("music 50%");
            var dir = Path.Combine(Dir, "uitest");
            Directory.CreateDirectory(dir);
            OptionsMenu.SetOpenForTest(true);
            OptionsMenu.SetTabForTest(4);
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "audio_tab.png"));
            yield return null;
            Settings.Soundtrack = savedTrack; Settings.VolMusic = savedMusic;
            OptionsMenu.SetOpenForTest(false);
            yield return new WaitForSecondsRealtime(0.3f);
        }

        IEnumerator CountdownTest()
        {
            var rm = Showcase.Fsm("GameManager", "race manager");
            var fsm = rm?.GetType().GetProperty("Fsm")?.GetValue(rm);
            var act = fsm?.GetType().GetProperty("ActiveStateName");
            Showcase.Send(rm, "FINISHED");
            var car = GameObject.Find("race_car_01");
            var rb = car ? car!.GetComponent<Rigidbody>() : null;
            Rigidbody? ai = null;
            foreach (var r in FindObjectsOfType<Rigidbody>()) if (r.name.StartsWith("AI_car")) { ai = r; break; }
            for (int i = 0; i < 32; i++)
            {
                Debug.Log($"[RaceBoot] testbench: cd t={Time.time:F2} st={act?.GetValue(fsm)} player={(rb ? rb!.position.ToString("F2") + " v=" + rb.linearVelocity.magnitude.ToString("F2") + " kin=" + rb.isKinematic : "-")} ai={(ai ? ai!.position.ToString("F1") + " v=" + ai.linearVelocity.magnitude.ToString("F1") : "-")}");
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        static Type[] SafeTypes(System.Reflection.Assembly a) { try { return a.GetTypes(); } catch { return new Type[0]; } }

        // Shaders of the scenery and their halftone-like settings.
        static void ShaderDump()
        {
            var sb = new StringBuilder();
            var byShader = new Dictionary<string, List<Material>>();
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (!r || !r.gameObject.scene.isLoaded) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.shader) continue;
                    if (!byShader.TryGetValue(m.shader.name, out var l)) byShader[m.shader.name] = l = new List<Material>();
                    if (!l.Contains(m)) l.Add(m);
                }
            }
            foreach (var kv in byShader.OrderByDescending(k => k.Value.Count))
            {
                var sh = kv.Value[0].shader;
                sb.AppendLine($"SHADER {kv.Key}  ({kv.Value.Count} materials)");
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                    sb.AppendLine($"   prop {sh.GetPropertyName(i)} ({sh.GetPropertyType(i)}) {sh.GetPropertyDescription(i)}");
                foreach (var m in kv.Value.Take(6))
                {
                    sb.Append($"   mat {m.name}: keywords={string.Join(" ", m.shaderKeywords)}");
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        string n = sh.GetPropertyName(i), ln = n.ToLower();
                        if (!(ln.Contains("half") || ln.Contains("dot") || ln.Contains("comic") || ln.Contains("screen") || ln.Contains("pattern") || ln.Contains("shade") || ln.Contains("tone"))) continue;
                        var t = sh.GetPropertyType(i);
                        object v = t == UnityEngine.Rendering.ShaderPropertyType.Texture ? (object)(m.GetTexture(n) ? m.GetTexture(n).name : "-")
                            : t == UnityEngine.Rendering.ShaderPropertyType.Color ? m.GetColor(n) : t == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(n) : (object)m.GetFloat(n);
                        sb.Append($" {n}={v}");
                    }
                    sb.AppendLine();
                }
            }
            File.WriteAllText(Path.Combine(Dir, "shaders.txt"), sb.ToString());
        }

        static void RaceDump()
        {
            var sb = new StringBuilder();
            for (int s = 0; s < SceneManager.sceneCount; s++)
                foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects())
                    if (root.activeInHierarchy) Walk(root.transform, 0, sb);
            sb.AppendLine("=== DDOL ===");
            var probe = new GameObject("probe"); DontDestroyOnLoad(probe);
            foreach (var root in probe.scene.GetRootGameObjects()) Walk(root.transform, 0, sb);
            Destroy(probe);
            sb.AppendLine("=== ACTIVE RIGIDBODIES ===");
            Transform? firstAi = null;
            foreach (var rb in FindObjectsOfType<Rigidbody>())
            {
                sb.AppendLine($" {PathOf(rb.transform)} kin={rb.isKinematic} pos={rb.position:F1} v={rb.linearVelocity.magnitude:F1}");
                if (!firstAi && rb.name.StartsWith("AI_car")) firstAi = rb.transform;
            }
            if (firstAi)
            {
                var top = firstAi!;

                sb.AppendLine("=== FIRST POOLED CAR ===");
                Walk(top, 0, sb);
            }
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            sb.AppendLine("=== GLOBAL INT/FLOAT VARIABLES ===");
            var fsmVarsType = Type.GetType("HutongGames.PlayMaker.FsmVariables, PlayMaker");
            var globals = fsmVarsType?.GetProperty("GlobalVariables", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            foreach (var listName in new[] { "IntVariables", "FloatVariables", "BoolVariables" })
                if (globals?.GetType().GetProperty(listName)?.GetValue(globals) is Array arr)
                    foreach (var v in arr) sb.AppendLine($" {listName} {v.GetType().GetProperty("Name")?.GetValue(v)} = {v.GetType().GetProperty("Value")?.GetValue(v)}");
            foreach (var name in new[] { "race manager", "timer countdown" })
            {
            sb.AppendLine("=== TRANSITIONS " + name + " ===");
            var fsmComp = Showcase.Fsm("GameManager", name);
            var fsm = fsmComp?.GetType().GetProperty("Fsm")?.GetValue(fsmComp);
            if (fsm != null)
            {
                sb.AppendLine("active: " + fsm.GetType().GetProperty("ActiveStateName", F)?.GetValue(fsm));
                foreach (var gt in (Array)fsm.GetType().GetProperty("GlobalTransitions", F)!.GetValue(fsm))
                    sb.AppendLine("  GLOBAL " + gt.GetType().GetProperty("EventName", F)?.GetValue(gt) + " -> " + gt.GetType().GetProperty("ToState", F)?.GetValue(gt));
                foreach (var st in (Array)fsm.GetType().GetProperty("States", F)!.GetValue(fsm))
                {
                    sb.Append(" " + st.GetType().GetProperty("Name", F)?.GetValue(st) + ":");
                    foreach (var tr in (Array)st.GetType().GetProperty("Transitions", F)!.GetValue(st))
                        sb.Append("  " + tr.GetType().GetProperty("EventName", F)?.GetValue(tr) + "->" + tr.GetType().GetProperty("ToState", F)?.GetValue(tr));
                    sb.AppendLine();
                }
            }
            }
            File.WriteAllText(Path.Combine(Dir, "racedump.txt"), sb.ToString());
        }

        internal static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 7) return;
            sb.Append(' ', depth * 2).Append(t.gameObject.activeSelf ? "+ " : "- ").Append(t.name);
            sb.Append(" @").Append(t.position.ToString("F1")).Append(" [");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                var n = c.GetType().Name;
                if (n == "PlayMakerFSM") n += ":" + c.GetType().GetProperty("FsmName")?.GetValue(c);
                if (c is Rigidbody rb) n += rb.isKinematic ? "(kin)" : "";
                sb.Append(n).Append(',');
            }
            sb.AppendLine("]");
            // Skip the static track geometry.
            if (t.childCount > 60) { sb.Append(' ', depth * 2 + 2).AppendLine("… " + t.childCount + " children"); return; }
            foreach (Transform ch in t) Walk(ch, depth + 1, sb);
        }
    }
}
