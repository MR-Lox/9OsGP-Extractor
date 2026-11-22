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

        static string FilePath => Path.Combine(Application.dataPath, "..", "9OsGP_settings.ini");

        public static void Load()
        {
            Width = Screen.currentResolution.width;
            Height = Screen.currentResolution.height;
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
                }
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "DisplayMode=" + DisplayMode, "Width=" + Width, "Height=" + Height,
                    "VSync=" + (VSync ? 1 : 0), "Bezel=" + (Bezel ? 1 : 0), "Outline=" + (Outline ? 1 : 0),
                    "Beautify=" + (Beautify ? 1 : 0), "Crt=" + (Crt ? 1 : 0), "LightHalftone=" + (LightHalftone ? 1 : 0),
                    "CameraView=" + CameraView, "CamHeight=" + F(CamHeight), "CamForward=" + F(CamForward),
                    "CamPitch=" + F(CamPitch), "CamFov=" + F(CamFov),
                });
            }
            catch (Exception e) { Debug.LogWarning("[RaceBoot] settings save failed: " + e.Message); }
        }

        static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        public static FullScreenMode Mode =>
            DisplayMode == 0 ? FullScreenMode.ExclusiveFullScreen :
            DisplayMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

        public static void ApplyDisplay()
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
            Screen.SetResolution(w, h, Mode);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }
    }

    // In-game options overlay (F1). Drawn with IMGUI so it does not depend on the game's UI.
    public class OptionsMenu : MonoBehaviour
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static OptionsMenu? Instance;
        bool open;
        int tab;
        float savedTimeScale = 1f;
        bool savedCursorVisible;
        CursorLockMode savedLock;
        Vector2 scroll;
        Resolution[] resolutions = new Resolution[0];
        int resIndex;
        float nextEnforce;
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
            SceneManager.sceneLoaded += (s, m) => { nextEnforce = 0f; Halftone.Apply(Settings.LightHalftone, true); };
            CameraViews.Install();
        }

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
            // Re-apply the graphics toggles regularly: the game's own FSMs may reset them.
            if (Time.unscaledTime >= nextEnforce)
            {
                nextEnforce = Time.unscaledTime + 0.5f;
                ApplyGraphics();
            }
            controls.Update();
            if (!open && Controls.GamepadBackPressed()) CameraViews.Cycle();
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
            if (!open && e.type == EventType.KeyDown && e.keyCode == KeyCode.C) { CameraViews.Cycle(); e.Use(); }
            if (!open)
            {
                if (Time.unscaledTime < 12f)
                {
                    BuildStyles(Mathf.Max(0.75f, Screen.height / 1080f));
                    GUI.Label(new Rect(20, Screen.height - 50, 900, 40), "F1 : options   —   C / bouton Y : changer de vue", small);
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
            string[] tabs = { "Affichage", "Graphismes", "Caméra", "Contrôles" };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], i == tab ? toggleOn : toggleOff, GUILayout.Height(44 * s))) tab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(16 * s);

            scroll = GUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawDisplay(s);
            else if (tab == 1) DrawGraphics(s);
            else if (tab == 2) DrawCamera(s);
            else controls.Draw(s, header!, label!, small!, button!, toggleOn!);
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fermer (F1 / Échap)", button, GUILayout.Height(44 * s))) SetOpen(false);
            if (GUILayout.Button("Quitter le jeu", button, GUILayout.Height(44 * s), GUILayout.Width(220 * s))) { Settings.Save(); Application.Quit(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        bool Row(string name, bool value, float s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(40 * s));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Oui", value ? toggleOn : toggleOff, GUILayout.Width(110 * s), GUILayout.Height(40 * s))) value = true;
            if (GUILayout.Button("Non", !value ? toggleOn : toggleOff, GUILayout.Width(110 * s), GUILayout.Height(40 * s))) value = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(6 * s);
            return value;
        }

        void DrawDisplay(float s)
        {
            GUILayout.Label("Mode d'affichage", header);
            GUILayout.BeginHorizontal();
            string[] modes = { "Plein écran", "Plein écran fenêtré", "Fenêtré" };
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

            GUILayout.Label("Résolution", header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◄", button, GUILayout.Width(60 * s), GUILayout.Height(42 * s)) && resolutions.Length > 0)
                resIndex = (resIndex - 1 + resolutions.Length) % resolutions.Length;
            string cur = resolutions.Length > 0 && resIndex >= 0 ? resolutions[resIndex].width + " × " + resolutions[resIndex].height : "?";
            GUILayout.Label(cur, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter }, GUILayout.Height(42 * s));
            if (GUILayout.Button("►", button, GUILayout.Width(60 * s), GUILayout.Height(42 * s)) && resolutions.Length > 0)
                resIndex = (resIndex + 1) % resolutions.Length;
            if (GUILayout.Button("Appliquer", button, GUILayout.Width(160 * s), GUILayout.Height(42 * s)) && resolutions.Length > 0)
            {
                Settings.Width = resolutions[resIndex].width;
                Settings.Height = resolutions[resIndex].height;
                Settings.ApplyDisplay();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Actuelle : " + Screen.width + " × " + Screen.height, small);
            GUILayout.Space(16 * s);

            bool v = Row("Synchronisation verticale (V-Sync)", Settings.VSync, s);
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

        void DrawCamera(float s)
        {
            GUILayout.Label("Vue", header);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CameraViews.Names.Length; i++)
                if (GUILayout.Button(CameraViews.Names[i], Settings.CameraView == i ? toggleOn : toggleOff, GUILayout.Height(42 * s))) Settings.CameraView = i;
            GUILayout.EndHorizontal();
            GUILayout.Label("En course : touche C (clavier) ou bouton Y / Select (manette) pour changer de vue.", small);
            GUILayout.Space(14 * s);
            GUILayout.Label("Réglages des vues intérieures", header);
            Settings.CamFov = Slider("Champ de vision", Settings.CamFov, 40f, 110f, "°", s);
            Settings.CamHeight = Slider("Hauteur", Settings.CamHeight, -0.6f, 0.6f, " m", s);
            Settings.CamForward = Slider("Avant / arrière", Settings.CamForward, -1.5f, 1.5f, " m", s);
            Settings.CamPitch = Slider("Inclinaison", Settings.CamPitch, -15f, 20f, "°", s);
            if (GUILayout.Button("Valeurs par défaut", button, GUILayout.Height(40 * s)))
            { Settings.CamFov = 70f; Settings.CamHeight = 0f; Settings.CamForward = 0f; Settings.CamPitch = 5f; }
        }

        void DrawGraphics(float s)
        {
            Settings.Bezel = Row("Bezel (cadre de la borne d'arcade)", Settings.Bezel, s);
            Settings.Crt = Row("Effet écran CRT (courbure, lignes)", Settings.Crt, s);
            Settings.Outline = Row("Contours noirs (cel shading)", Settings.Outline, s);
            Settings.Beautify = Row("Post-traitement BD (trame, couleurs)", Settings.Beautify, s);
            bool lh = Row("Trame BD sur les éclairages (halftone)", Settings.LightHalftone, s);
            if (lh != Settings.LightHalftone) { Settings.LightHalftone = lh; Halftone.Apply(lh, true); }
            GUILayout.Space(10 * s);
            GUILayout.Label("Les changements sont appliqués immédiatement et enregistrés à la fermeture du menu.", small);
            ApplyGraphics();
        }
    }
}
