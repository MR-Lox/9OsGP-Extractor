using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // Persistent settings stored next to the executable (9OsGP_settings.ini).
    public static class Settings
    {
        public static int DisplayMode = 1;   // 0 = plein écran exclusif, 1 = plein écran fenêtré, 2 = fenêtré
        public static int Width, Height;
        public static bool VSync = true;
        public static bool Bezel = true;
        public static bool Outline = true;
        public static bool Beautify = true;
        public static bool Crt = true;
        public static bool LightHalftone = true;
        public static int CameraView = 0;
        public static float CamHeight = 0f, CamForward = 0f, CamPitch = 5f, CamFov = 70f;
        public static float HudScale = 1f, HudSpreadX = 0f, HudSpreadY = 0f, HudMargin = 0.03f;
        public static bool HudGaugeLow = true;   // gauge at the bottom in the interior views
        public static float HudInfoY = 0f, GaugeX = 0f, GaugeY = 0f, GaugeScale = 1f;
        public static bool Ffb = true, FfbInvert;
        public static bool ReplayEnabled = true;
        public static bool IntroEnabled = true;
        public static bool MameOutputs = true;
        public static float VolMaster = 1f, VolMusic = 1f, VolEngine = 1f, VolFx = 1f;
        public static string Soundtrack = GameAudio.Original;
        public static float FfbStrength = 0.7f, FfbCentering = 0.6f, FfbEffects = 0.6f;
        public static int DriftStyle = 0;      // 0 = Origine, 1 = Ridge Racer, 2 = OutRun 2, 3 = Daytona 2 / Scud Race
        public static HashSet<string> HudHidden = new HashSet<string> { "Minimap" };
        public static bool HudShown(string key) => !HudHidden.Contains(key);
        public static void SetHudShown(string key, bool v) { if (v) HudHidden.Remove(key); else HudHidden.Add(key); }

        static string FilePath => Path.Combine(Application.dataPath, "..", "9OsGP_settings.ini");

        public static void Load()
        {
            Width = Screen.currentResolution.width;
            Height = Screen.currentResolution.height;
            L.Code = L.Default();
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var kv = line.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                string k = kv[0].Trim(), v = kv[1].Trim();
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float fl);
                bool b = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                switch (k)
                {
                    case "Language": if (Array.IndexOf(L.Codes, v) >= 0) L.Code = v; break;
                    case "DisplayMode": DisplayMode = Mathf.Clamp(n, 0, 2); break;
                    case "Width": if (n > 0) Width = n; break;
                    case "Height": if (n > 0) Height = n; break;
                    case "VSync": VSync = b; break;
                    case "Bezel": Bezel = b; break;
                    case "Outline": Outline = b; break;
                    case "Beautify": Beautify = b; break;
                    case "Crt": Crt = b; break;
                    case "LightHalftone": LightHalftone = b; break;
                    case "CameraView": CameraView = Mathf.Clamp(n, 0, 2); break;
                    case "CamHeight": CamHeight = fl; break;
                    case "CamForward": CamForward = fl; break;
                    case "CamPitch": CamPitch = fl; break;
                    case "CamFov": if (fl > 10) CamFov = fl; break;
                    case "HudScale": if (fl > 0.1f) HudScale = fl; break;
                    case "HudSpreadX": HudSpreadX = Mathf.Clamp01(fl); break;
                    case "HudSpreadY": HudSpreadY = Mathf.Clamp01(fl); break;
                    case "HudMargin": HudMargin = Mathf.Clamp(fl, 0f, 0.2f); break;
                    case "HudGaugeLow": HudGaugeLow = b; break;
                    case "HudInfoY": HudInfoY = Mathf.Clamp(fl, -0.5f, 0.5f); break;
                    case "GaugeX": GaugeX = Mathf.Clamp(fl, -0.5f, 0.5f); break;
                    case "GaugeY": GaugeY = Mathf.Clamp(fl, -0.5f, 0.5f); break;
                    case "GaugeScale": if (fl > 0.1f) GaugeScale = fl; break;
                    case "Ffb": Ffb = b; break;
                    case "Replay": ReplayEnabled = b; break;
                    case "Intro": IntroEnabled = b; break;
                    case "VolMaster": VolMaster = Mathf.Clamp(fl, 0f, 2f); break;
                    case "VolMusic": VolMusic = Mathf.Clamp(fl, 0f, 2f); break;
                    case "VolEngine": VolEngine = Mathf.Clamp(fl, 0f, 2f); break;
                    case "VolFx": VolFx = Mathf.Clamp(fl, 0f, 2f); break;
                    case "Soundtrack": Soundtrack = v; break;
                    case "RaceCam": Arcade.LoadPose(v); break;
                    case "FfbInvert": FfbInvert = b; break;
                    case "FfbStrength": FfbStrength = Mathf.Clamp01(fl); break;
                    case "FfbCentering": FfbCentering = Mathf.Clamp01(fl); break;
                    case "FfbEffects": FfbEffects = Mathf.Clamp01(fl); break;
                    case "WheelDevice": Wheel.Device = v; break;
                    case "WheelSteerAxis": Wheel.SteerAxis = n; break;
                    case "WheelSteerCenter": Wheel.SteerCenter = fl; break;
                    case "WheelSteerInvert": Wheel.SteerInvert = b; break;
                    case "WheelSteerSens": if (fl >= 1f) Wheel.SteerSens = fl; break;
                    case "WheelAccelAxis": Wheel.AccelAxis = n; break;
                    case "WheelAccelRest": Wheel.AccelRest = fl; break;
                    case "WheelAccelFull": Wheel.AccelFull = fl; break;
                    case "WheelBrakeAxis": Wheel.BrakeAxis = n; break;
                    case "WheelBrakeRest": Wheel.BrakeRest = fl; break;
                    case "WheelBrakeFull": Wheel.BrakeFull = fl; break;
                    case "WheelPedalDeadZone": Wheel.PedalDeadZone = Mathf.Clamp(fl, 0f, 0.3f); break;
                    case "WheelViewButton": Wheel.ViewButton = n; break;
                    case "WheelMenuButton": Wheel.MenuButton = n; break;
                    case "WheelStartButton": Wheel.StartButton = n; break;
                    case "WheelSteerHalf": if (fl > 0.01f) Wheel.SteerHalf = fl; break;
                    case "MameOutputs": MameOutputs = b; break;
                    case "DriftStyle": DriftStyle = Mathf.Clamp(n, 0, 3); break;
                    case "HudHidden": HudHidden = new HashSet<string>(v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())); break;
                }
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "Language=" + L.Code, "DisplayMode=" + DisplayMode, "Width=" + Width, "Height=" + Height,
                    "VSync=" + (VSync ? 1 : 0), "Bezel=" + (Bezel ? 1 : 0), "Outline=" + (Outline ? 1 : 0),
                    "Beautify=" + (Beautify ? 1 : 0), "Crt=" + (Crt ? 1 : 0), "LightHalftone=" + (LightHalftone ? 1 : 0),
                    "CameraView=" + CameraView, "CamHeight=" + F(CamHeight), "CamForward=" + F(CamForward),
                    "CamPitch=" + F(CamPitch), "CamFov=" + F(CamFov),
                    "HudScale=" + F(HudScale), "HudSpreadX=" + F(HudSpreadX), "HudSpreadY=" + F(HudSpreadY),
                    "HudMargin=" + F(HudMargin), "HudGaugeLow=" + (HudGaugeLow ? 1 : 0), "HudHidden=" + string.Join(",", HudHidden),
                    "HudInfoY=" + F(HudInfoY), "GaugeX=" + F(GaugeX), "GaugeY=" + F(GaugeY), "GaugeScale=" + F(GaugeScale),
                    "Ffb=" + (Ffb ? 1 : 0), "Replay=" + (ReplayEnabled ? 1 : 0), "Intro=" + (IntroEnabled ? 1 : 0),
                    "VolMaster=" + F(VolMaster), "VolMusic=" + F(VolMusic), "VolEngine=" + F(VolEngine), "VolFx=" + F(VolFx), "Soundtrack=" + Soundtrack, "RaceCam=" + Arcade.PoseString(), "FfbInvert=" + (FfbInvert ? 1 : 0), "FfbStrength=" + F(FfbStrength),
                    "FfbCentering=" + F(FfbCentering), "FfbEffects=" + F(FfbEffects),
                    "WheelDevice=" + Wheel.Device, "WheelSteerAxis=" + Wheel.SteerAxis, "WheelSteerCenter=" + F(Wheel.SteerCenter),
                    "WheelSteerInvert=" + (Wheel.SteerInvert ? 1 : 0), "WheelSteerSens=" + F(Wheel.SteerSens),
                    "WheelAccelAxis=" + Wheel.AccelAxis, "WheelAccelRest=" + F(Wheel.AccelRest), "WheelAccelFull=" + F(Wheel.AccelFull),
                    "WheelBrakeAxis=" + Wheel.BrakeAxis, "WheelBrakeRest=" + F(Wheel.BrakeRest), "WheelBrakeFull=" + F(Wheel.BrakeFull),
                    "WheelPedalDeadZone=" + F(Wheel.PedalDeadZone), "WheelViewButton=" + Wheel.ViewButton, "WheelMenuButton=" + Wheel.MenuButton,
                    "WheelStartButton=" + Wheel.StartButton, "WheelSteerHalf=" + F(Wheel.SteerHalf), "MameOutputs=" + (MameOutputs ? 1 : 0),
                    "DriftStyle=" + DriftStyle,
                });
            }
            catch (Exception e) { Debug.LogWarning("[RaceBoot] settings save failed: " + e.Message); }
        }

        static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        public static FullScreenMode Mode =>
            DisplayMode == 0 ? FullScreenMode.ExclusiveFullScreen :
            DisplayMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

        public static Vector2Int TargetSize()
        {
            int w = Width, h = Height;
            if (DisplayMode == 2)
            {
                // A window as large as the monitor would hide its bottom behind the taskbar / off-screen:
                // shrink it to fit in ~90% of the desktop while keeping the aspect ratio.
                var native = Screen.currentResolution;
                float k = Mathf.Min(1f, native.width * 0.9f / w, native.height * 0.9f / h);
                w = Mathf.RoundToInt(w * k); h = Mathf.RoundToInt(h * k);
            }
            return new Vector2Int(w, h);
        }

        public static void ApplyDisplay()
        {
            var t = TargetSize();
            Screen.SetResolution(t.x, t.y, Mode);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }

        // True when the window no longer matches the settings (the original game's own options
        // re-apply their saved fullscreen / resolution when a scene loads).
        public static bool DisplayDiffers()
        {
            var t = TargetSize();
            if (Screen.fullScreenMode != Mode) return true;
            // A window may be clamped by Windows to the work area: only check the size of the fullscreen modes.
            return DisplayMode != 2 && (Mathf.Abs(Screen.width - t.x) > 2 || Mathf.Abs(Screen.height - t.y) > 2);
        }
    }

    // In-game options overlay (F1). Drawn with IMGUI so it does not depend on the game's UI.
    public class OptionsMenu : MonoBehaviour
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static OptionsMenu? Instance;
        bool open;
        static OptionsMenu? instance;
        public static bool IsOpen => instance != null && instance.open;
        public static void SetOpenForTest(bool v) => instance?.SetOpen(v);   // test bench only
        public static void AskQuitForTest(bool v) { if (instance != null) { if (v) instance.AskQuit(); else instance.CloseQuit(); } }   // test bench only

        // ---------------- Esc: quit confirmation ----------------

        bool quitAsk;
        float quitTimeScale = 1f;
        bool quitCursorVisible;
        CursorLockMode quitLock;

        void AskQuit()
        {
            if (quitAsk) return;
            quitAsk = true;
            quitTimeScale = Time.timeScale; Time.timeScale = 0f;
            quitCursorVisible = Cursor.visible; quitLock = Cursor.lockState;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
            AudioListener.pause = true;
        }

        void CloseQuit()
        {
            if (!quitAsk) return;
            quitAsk = false;
            if (Time.timeScale == 0f) Time.timeScale = quitTimeScale;
            Cursor.visible = quitCursorVisible; Cursor.lockState = quitLock;
            AudioListener.pause = false;
        }

        void ConfirmQuit()
        {
            Settings.Save();
            Application.Quit();
        }

        // Gamepad: A / Cross (menu next) quits, B / Circle (menu back) cancels.
        void QuitPad()
        {
            if (!quitAsk || !Rewired.ReInput.isReady) return;
            var p = Rewired.ReInput.players.GetPlayer(0);
            if (p == null) return;
            if (p.GetButtonDown("menu back")) CloseQuit();
            else if (p.GetButtonDown("menu next")) ConfirmQuit();
        }

        void DrawQuit()
        {
            float s = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            BuildStyles(s);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float w = 620 * s, h = 250 * s;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(r, GUIContent.none, box);
            GUILayout.BeginArea(new Rect(r.x + 30 * s, r.y + 24 * s, r.width - 60 * s, r.height - 48 * s));
            GUILayout.Label(L.T("Quitter le jeu ?"), title);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.T("Quitter"), toggleOn, GUILayout.Height(48 * s))) ConfirmQuit();
            GUILayout.Space(16 * s);
            if (GUILayout.Button(L.T("Annuler"), button, GUILayout.Height(48 * s))) CloseQuit();
            GUILayout.EndHorizontal();
            GUILayout.Space(8 * s);
            GUILayout.Label(L.T("Entrée : quitter   —   Échap : annuler"), small);
            GUILayout.EndArea();
        }
        public static void SetTabForTest(int t) { if (instance != null) instance.tab = t; }   // test bench only
        int tab;
        float savedTimeScale = 1f;
        bool savedCursorVisible;
        CursorLockMode savedLock;
        Vector2 scroll;
        Resolution[] resolutions = new Resolution[0];
        int resIndex;
        float nextEnforce;
        int displayFixes;
        GUIStyle? title, header, label, button, toggleOn, toggleOff, box, small;
        Texture2D? bg, btnBg, btnHover, onBg;
        readonly Controls controls = new Controls();

        public static void Create()
        {
            if (Instance) return;
            var go = new GameObject("RaceBootOptions");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<OptionsMenu>();
        }

        void Start()
        {
            Settings.Load();
            Settings.ApplyDisplay();
            RefreshResolutions();
            SceneManager.sceneLoaded += (s, m) => { nextEnforce = 0f; displayFixes = 0; Halftone.Apply(Settings.LightHalftone, true); };
            instance = this;
            FreeCam.Install();
            Arcade.Install();
            GameAudio.Install();
            MameOutputs.Install();
            Ranking.Install();
            CameraViews.Install();
            Hud.Install();
            Replay.Install();
            Drift.Install();
        }

        void OnApplicationQuit() => Ffb.Shutdown(L.T("arrêté"));

        void RefreshResolutions()
        {
            resolutions = Screen.resolutions
                .GroupBy(r => (r.width, r.height)).Select(g => g.First())
                .OrderBy(r => r.width).ThenBy(r => r.height).ToArray();
            resIndex = Array.FindIndex(resolutions, r => r.width == Settings.Width && r.height == Settings.Height);
            if (resIndex < 0) resIndex = resolutions.Length - 1;
        }

        void Update()
        {
            QuitPad();
            // The pause FSM of the race switches the arcade bezel on: keep it as configured, every frame.
            var bezel = FindAnywhere("crt_frame");
            if (bezel && bezel!.activeSelf != Settings.Bezel) bezel.SetActive(Settings.Bezel);

            // Re-apply the graphics toggles regularly: the game's own FSMs may reset them.
            if (Time.unscaledTime >= nextEnforce)
            {
                nextEnforce = Time.unscaledTime + 0.5f;
                ApplyGraphics();
                // A few attempts per scene only, so a mode the system refuses cannot cause endless flicker.
                if (displayFixes < 6 && Settings.DisplayDiffers())
                {
                    displayFixes++;
                    Debug.Log($"[RaceBoot] display reset by the game ({Screen.fullScreenMode} {Screen.width}x{Screen.height}), re-applying settings");
                    Settings.ApplyDisplay();
                }
            }
            controls.Update();
            Wheel.Update();
            PauseMenu.Tick();
            Ffb.Tick(open);
            if (!open && (Controls.GamepadBackPressed() || Wheel.ViewPressed())) CameraViews.Cycle();
            if (Wheel.MenuPressed()) SetOpen(!open);
        }

        // ---------- graphics ----------

        static readonly Dictionary<string, GameObject?> cache = new Dictionary<string, GameObject?>();

        static GameObject? FindAnywhere(string name)
        {
            if (cache.TryGetValue(name, out var c) && c) return c;
            GameObject? found = null;
            for (int s = 0; s < SceneManager.sceneCount && !found; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var t = FindChild(root.transform, name);
                    if (t) { found = t!.gameObject; break; }
                }
            }
            cache[name] = found;
            return found;
        }

        static Transform? FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform ch in t)
            {
                var r = FindChild(ch, name);
                if (r) return r;
            }
            return null;
        }

        public static void ApplyGraphics()
        {
            // Every AI car carries an enabled "debug_camera" that renders the whole scene under the real one.
            foreach (var c in Camera.allCameras)
                if (c.name == "debug_camera") c.enabled = false;

            var frame = FindAnywhere("crt_frame");
            if (frame && frame!.activeSelf != Settings.Bezel) frame.SetActive(Settings.Bezel);

            var crt = FindAnywhere("post_process_arcade_crt");
            var normal = FindAnywhere("post_process_normal");
            if (crt && normal)
            {
                // Only swap when the race is showing one of the two volumes (game over etc. may change them).
                if (Settings.Crt && !crt!.activeSelf && normal!.activeSelf) { normal.SetActive(false); crt.SetActive(true); }
                if (!Settings.Crt && crt!.activeSelf) { crt.SetActive(false); normal!.SetActive(true); }
            }

            SetRendererFeature("OutlineRenderFeature", Settings.Outline);
            SetRendererFeature("BeautifyRendererFeature", Settings.Beautify);
            Halftone.Apply(Settings.LightHalftone);
        }

        static void SetRendererFeature(string typeName, bool active)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (!rp) return;
            if (!(rp.GetType().GetField("m_RendererDataList", F)?.GetValue(rp) is Array list)) return;
            foreach (var rd in list)
            {
                if (rd == null) continue;
                if (!(rd.GetType().GetProperty("rendererFeatures", F)?.GetValue(rd) is System.Collections.IList feats)) continue;
                foreach (var f in feats)
                {
                    if (f == null || f.GetType().Name != typeName) continue;
                    var cur = f.GetType().GetProperty("isActive", F)?.GetValue(f) as bool?;
                    if (cur != active) f.GetType().GetMethod("SetActive", F)?.Invoke(f, new object[] { active });
                }
            }
        }

        // ---------- IMGUI ----------

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        void BuildStyles(float s)
        {
            bg ??= Tex(new Color(0.05f, 0.05f, 0.09f, 0.94f));
            btnBg ??= Tex(new Color(0.18f, 0.18f, 0.26f, 1f));
            btnHover ??= Tex(new Color(0.30f, 0.30f, 0.45f, 1f));
            onBg ??= Tex(new Color(0.95f, 0.55f, 0.10f, 1f));
            int fs = Mathf.RoundToInt(18 * s);
            box = new GUIStyle(GUI.skin.box) { normal = { background = bg } };
            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.6f, 0.1f) } };
            header = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * s), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            label = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(14 * s), wordWrap = true, normal = { textColor = new Color(0.7f, 0.7f, 0.75f) } };
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = fs, normal = { background = btnBg, textColor = Color.white }, hover = { background = btnHover, textColor = Color.white },
                active = { background = onBg, textColor = Color.black }, padding = new RectOffset(10, 10, 6, 6),
            };
            toggleOn = new GUIStyle(button) { normal = { background = onBg, textColor = Color.black }, hover = { background = onBg, textColor = Color.black }, fontStyle = FontStyle.Bold };
            toggleOff = button;
        }

        void SetOpen(bool v)
        {
            if (open == v) return;
            open = v;
            if (open)
            {
                savedTimeScale = Time.timeScale; Time.timeScale = 0f;
                savedCursorVisible = Cursor.visible; savedLock = Cursor.lockState;
                Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
                AudioListener.pause = true;
                RefreshResolutions();
            }
            else
            {
                controls.CancelRebind();
                Time.timeScale = savedTimeScale;
                Cursor.visible = savedCursorVisible; Cursor.lockState = savedLock;
                AudioListener.pause = false;
                Settings.Save();
            }
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1 && !controls.Rebinding)
            {
                SetOpen(!open);
                e.Use();
            }
            if (quitAsk)
            {
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.N) { CloseQuit(); e.Use(); return; }
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.O || e.keyCode == KeyCode.Y) { ConfirmQuit(); e.Use(); return; }
                }
                DrawQuit();
                return;
            }
            if (!open && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { AskQuit(); e.Use(); return; }
            if (!open && e.type == EventType.KeyDown && e.keyCode == KeyCode.C) { CameraViews.Cycle(); e.Use(); }
            if (!open)
            {
                if (Time.unscaledTime < 12f)
                {
                    BuildStyles(Mathf.Max(0.75f, Screen.height / 1080f));
                    GUI.Label(new Rect(20, Screen.height - 50, 900, 40), L.T("F1 : options   —   C / bouton Y : changer de vue"), small);
                }
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && !controls.Rebinding) { SetOpen(false); e.Use(); return; }

            float s = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            BuildStyles(s);
            float w = 900 * s, h = 860 * s;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(r, GUIContent.none, box);
            GUILayout.BeginArea(new Rect(r.x + 30 * s, r.y + 20 * s, r.width - 60 * s, r.height - 40 * s));
            GUILayout.Label("9OsGP — OPTIONS", title);
            GUILayout.Space(10 * s);
            GUILayout.BeginHorizontal();
            string[] tabs = { L.T("Affichage"), L.T("Graphismes"), L.T("Caméra"), L.T("HUD"), L.T("Audio"), L.T("Gameplay"), L.T("Contrôles") };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], i == tab ? toggleOn : toggleOff, GUILayout.Height(44 * s))) tab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(16 * s);

            scroll = GUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawDisplay(s);
            else if (tab == 1) DrawGraphics(s);
            else if (tab == 2) DrawCamera(s);
            else if (tab == 3) DrawHud(s);
            else if (tab == 4) DrawAudio(s);
            else if (tab == 5) DrawGameplay(s);
            else controls.Draw(s, header!, label!, small!, button!, toggleOn!);
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.T("Fermer (F1 / Échap)"), button, GUILayout.Height(44 * s))) SetOpen(false);
            if (GUILayout.Button(L.T("Quitter le jeu"), button, GUILayout.Height(44 * s), GUILayout.Width(220 * s))) { Settings.Save(); Application.Quit(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        bool Row(string name, bool value, float s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(40 * s));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L.T("Oui"), value ? toggleOn : toggleOff, GUILayout.Width(110 * s), GUILayout.Height(40 * s))) value = true;
            if (GUILayout.Button(L.T("Non"), !value ? toggleOn : toggleOff, GUILayout.Width(110 * s), GUILayout.Height(40 * s))) value = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(6 * s);
            return value;
        }

        void DrawDisplay(float s)
        {
            GUILayout.Label(L.T("Langue"), header);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < L.Codes.Length; i++)
                if (GUILayout.Button(L.Names[i], L.Code == L.Codes[i] ? toggleOn : toggleOff, GUILayout.Height(42 * s))) L.Code = L.Codes[i];
            GUILayout.EndHorizontal();
            GUILayout.Space(14 * s);
            GUILayout.Label(L.T("Mode d'affichage"), header);
            GUILayout.BeginHorizontal();
            string[] modes = { L.T("Plein écran"), L.T("Plein écran fenêtré"), L.T("Fenêtré") };
            for (int i = 0; i < 3; i++)
                if (GUILayout.Button(modes[i], Settings.DisplayMode == i ? toggleOn : toggleOff, GUILayout.Height(42 * s)))
                {
                    Settings.DisplayMode = i;
                    if (i != 2 && resolutions.Length > 0)
                    {
                        // Fullscreen modes: default to the monitor's native resolution if the current one is smaller.
                        var native = Screen.currentResolution;
                        if (Settings.Width < 640) { Settings.Width = native.width; Settings.Height = native.height; }
                    }
                    Settings.ApplyDisplay();
                }
            GUILayout.EndHorizontal();
            GUILayout.Space(16 * s);

            GUILayout.Label(L.T("Résolution"), header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄", button, GUILayout.Width(60 * s), GUILayout.Height(42 * s)) && resolutions.Length > 0)
                resIndex = (resIndex - 1 + resolutions.Length) % resolutions.Length;
            string cur = resolutions.Length > 0 && resIndex >= 0 ? resolutions[resIndex].width + " × " + resolutions[resIndex].height : "?";
            GUILayout.Label(cur, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter }, GUILayout.Height(42 * s));
            if (GUILayout.Button("►", button, GUILayout.Width(60 * s), GUILayout.Height(42 * s)) && resolutions.Length > 0)
                resIndex = (resIndex + 1) % resolutions.Length;
            if (GUILayout.Button(L.T("Appliquer"), button, GUILayout.Width(160 * s), GUILayout.Height(42 * s)) && resolutions.Length > 0)
            {
                Settings.Width = resolutions[resIndex].width;
                Settings.Height = resolutions[resIndex].height;
                Settings.ApplyDisplay();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(L.T("Actuelle : ") + Screen.width + " × " + Screen.height, small);
            GUILayout.Space(16 * s);

            bool v = Row(L.T("Synchronisation verticale (V-Sync)"), Settings.VSync, s);
            if (v != Settings.VSync) { Settings.VSync = v; Settings.ApplyDisplay(); }
        }

        float Slider(string name, float v, float min, float max, string unit, float s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(36 * s), GUILayout.Width(300 * s));
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Height(36 * s));
            GUILayout.Label(v.ToString("0.00") + unit, label, GUILayout.Width(110 * s));
            GUILayout.EndHorizontal();
            return v;
        }

        List<string>? trackIds;
        List<string>? trackNames;
        float tracksAt = -10f;

        // 0 to 200 %, by steps of 5 %.
        float Percent(string name, float v, float s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(36 * s), GUILayout.Width(300 * s));
            v = GUILayout.HorizontalSlider(v, 0f, 2f, GUILayout.Height(36 * s));
            v = Mathf.Round(v * 20f) / 20f;
            GUILayout.Label(Mathf.RoundToInt(v * 100f) + " %", label, GUILayout.Width(110 * s));
            GUILayout.EndHorizontal();
            return v;
        }

        void DrawAudio(float s)
        {
            GUILayout.Label(L.T("Volumes"), header);
            Settings.VolMaster = Percent(L.T("Général"), Settings.VolMaster, s);
            Settings.VolMusic = Percent(L.T("Musique"), Settings.VolMusic, s);
            Settings.VolEngine = Percent(L.T("Moteurs"), Settings.VolEngine, s);
            Settings.VolFx = Percent(L.T("Effets sonores"), Settings.VolFx, s);
            GUILayout.Label(L.T("100 % = volume d'origine. Au-delà, le son est amplifié (le moteur est discret dans le jeu d'origine)."), small);

            GUILayout.Space(14 * s);
            GUILayout.Label(L.T("Bande-son"), header);
            if (trackIds == null || (Event.current.type == EventType.Layout && Time.unscaledTime - tracksAt > 2f))
            {
                tracksAt = Time.unscaledTime;
                trackIds = new List<string> { GameAudio.Original };
                trackNames = new List<string> { L.T("Originale (90's GP)") };
                var files = GameAudio.UserFiles();
                if (files.Count > 1) { trackIds.Add(GameAudio.Random); trackNames.Add(L.T("Aléatoire (vos musiques)")); }
                foreach (var f in files) { trackIds.Add("file:" + f); trackNames.Add("♪ " + f); }
            }
            int idx = Mathf.Max(0, trackIds.IndexOf(Settings.Soundtrack));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄", button, GUILayout.Width(60 * s), GUILayout.Height(42 * s))) idx = (idx + trackIds.Count - 1) % trackIds.Count;
            GUILayout.Label(trackNames![idx], header, GUILayout.Height(42 * s));
            if (GUILayout.Button("►", button, GUILayout.Width(60 * s), GUILayout.Height(42 * s))) idx = (idx + 1) % trackIds.Count;
            GUILayout.EndHorizontal();
            Settings.Soundtrack = trackIds[idx];
            GUILayout.Label(L.T("Vos propres musiques (mp3, ogg, wav) : placez-les dans le dossier « Music » à côté de 9OsGP.exe."), small);
        }

        void DrawCamera(float s)
        {
            GUILayout.Label(L.T("Vue"), header);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CameraViews.Names.Length; i++)
                if (GUILayout.Button(L.T(CameraViews.Names[i]), Settings.CameraView == i ? toggleOn : toggleOff, GUILayout.Height(42 * s))) Settings.CameraView = i;
            GUILayout.EndHorizontal();
            GUILayout.Label(L.T("En course : touche C (clavier) ou bouton Y / Select (manette) pour changer de vue."), small);
            GUILayout.Space(10 * s);
            Settings.ReplayEnabled = Row(L.T("Replay de la course sur l'écran « PRESS START »"), Settings.ReplayEnabled, s);
            GUILayout.Label(L.T("F9 sur l'écran « PRESS START » : revoir le dernier replay (gardé même après avoir quitté le jeu)."), small);
            Settings.IntroEnabled = Row(L.T("Présentation de la voiture avant le compte à rebours"), Settings.IntroEnabled, s);
            GUILayout.Space(14 * s);
            GUILayout.Label(L.T("Réglages des vues intérieures"), header);
            Settings.CamFov = Slider(L.T("Champ de vision"), Settings.CamFov, 40f, 110f, "°", s);
            Settings.CamHeight = Slider(L.T("Hauteur"), Settings.CamHeight, -0.6f, 0.6f, " m", s);
            Settings.CamForward = Slider(L.T("Avant / arrière"), Settings.CamForward, -1.5f, 1.5f, " m", s);
            Settings.CamPitch = Slider(L.T("Inclinaison"), Settings.CamPitch, -15f, 20f, "°", s);
            if (GUILayout.Button(L.T("Valeurs par défaut"), button, GUILayout.Height(40 * s)))
            { Settings.CamFov = 70f; Settings.CamHeight = 0f; Settings.CamForward = 0f; Settings.CamPitch = 5f; }
        }

        void DrawHud(float s)
        {
            GUILayout.Label(L.T("Éléments affichés"), header);
            foreach (var g in Hud.Groups)
                Settings.SetHudShown(g.Key, Row(L.T(g.Name), Settings.HudShown(g.Key), s));
            if (Hud.MinimapAvailable)
                Settings.SetHudShown("Minimap", Row(L.T("Mini-carte (cachée par les développeurs)"), Settings.HudShown("Minimap"), s));
            GUILayout.Space(10 * s);
            GUILayout.Label(L.T("Compte-tours et vitesse"), header);
            Settings.GaugeScale = Slider(L.T("Taille"), Settings.GaugeScale, 0.4f, 2f, "", s);
            Settings.GaugeX = Slider(L.T("Position horizontale"), Settings.GaugeX, -0.5f, 0.5f, "", s);
            Settings.GaugeY = Slider(L.T("Position verticale"), Settings.GaugeY, -0.5f, 0.5f, "", s);
            Settings.HudGaugeLow = Row(L.T("En bas de l'écran en vue capot / intérieure"), Settings.HudGaugeLow, s);
            GUILayout.Space(8 * s);
            GUILayout.Label(L.T("Autres informations (temps, position, tours)"), header);
            Settings.HudScale = Slider(L.T("Taille"), Settings.HudScale, 0.5f, 1.5f, "", s);
            Settings.HudInfoY = Slider(L.T("Hauteur"), Settings.HudInfoY, -0.3f, 0.3f, "", s);
            Settings.HudSpreadX = Slider(L.T("Écartement horizontal"), Settings.HudSpreadX, 0f, 1f, "", s);
            Settings.HudSpreadY = Slider(L.T("Écartement vertical"), Settings.HudSpreadY, 0f, 1f, "", s);
            Settings.HudMargin = Slider(L.T("Marge aux bords"), Settings.HudMargin, 0f, 0.15f, "", s);
            GUILayout.Label(L.T("Écartement à 1 : blocs plaqués contre les bords de l'écran (utile en 16:9 / 21:9). Hauteur positive = plus haut."), small);
            if (GUILayout.Button(L.T("Valeurs par défaut"), button, GUILayout.Height(40 * s)))
            {
                Settings.HudScale = 1f; Settings.HudSpreadX = 0f; Settings.HudSpreadY = 0f; Settings.HudMargin = 0.03f;
                Settings.HudGaugeLow = true; Settings.HudHidden = new HashSet<string> { "Minimap" };
                Settings.HudInfoY = 0f; Settings.GaugeX = 0f; Settings.GaugeY = 0f; Settings.GaugeScale = 1f;
            }
        }

        void DrawGraphics(float s)
        {
            Settings.Bezel = Row(L.T("Bezel (cadre de la borne d'arcade)"), Settings.Bezel, s);
            Settings.Crt = Row(L.T("Effet écran CRT (courbure, lignes)"), Settings.Crt, s);
            Settings.Outline = Row(L.T("Contours noirs (cel shading)"), Settings.Outline, s);
            Settings.Beautify = Row(L.T("Post-traitement BD (trame, couleurs)"), Settings.Beautify, s);
            bool lh = Row(L.T("Trame BD sur les éclairages (halftone)"), Settings.LightHalftone, s);
            if (lh != Settings.LightHalftone) { Settings.LightHalftone = lh; Halftone.Apply(lh, true); }
            GUILayout.Space(10 * s);
            GUILayout.Label(L.T("Les changements sont appliqués immédiatement et enregistrés à la fermeture du menu."), small);
            ApplyGraphics();
        }

        static readonly string[] DriftDescriptions =
        {
            "Dérapage d'origine du jeu : frein maintenu + braquage à fond au-dessus de 150 km/h.",
            "Lâchez l'accélérateur un moment puis donnez un coup de volant (ou, en braquant : lâcher, coup de frein, regaz, aussi vite que vous voulez). La glisse dure tant que la voiture n'est pas remise droite : contre-braquez pour la redresser et elle reprend son grip ; la faire passer de l'autre côté enchaîne les virages.",
            "Comme Ridge Racer, mais plus facile à lancer : un coup de volant avec un coup de frein ou un bref lâcher, et on peut garder l'accélérateur.",
            "Appel puis contre-appel du volant (braquer d'un côté puis vivement de l'autre), avec un coup de frein ou un lâcher. La voiture retrouve plus facilement son grip : l'angle se referme de lui-même dès qu'on ne braque plus dans le virage.",
        };

        void DrawGameplay(float s)
        {
            GUILayout.Label(L.T("Style de dérapage"), header);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Drift.StyleNames.Length; i++)
                if (GUILayout.Button(L.T(Drift.StyleNames[i]), Settings.DriftStyle == i ? toggleOn : toggleOff, GUILayout.Height(44 * s)))
                    Settings.DriftStyle = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(10 * s);
            GUILayout.Label(L.T(DriftDescriptions[Mathf.Clamp(Settings.DriftStyle, 0, DriftDescriptions.Length - 1)]), small);
        }
    }
}
