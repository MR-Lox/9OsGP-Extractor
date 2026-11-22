using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // Repositions / rescales / hides the blocks of the race HUD (90sGP_canvas/in_game).
    // The game keeps updating the texts; we only apply an offset and a scale on top of what it sets.
    public class Hud : MonoBehaviour
    {
        public class Group
        {
            public string Key = "", Name = "";
            public string[] Parts = new string[0];
            public bool Movable = true;      // false: centred messages, only scaled
            public bool Gauge;
            public readonly List<RectTransform> Rts = new List<RectTransform>();
            public readonly List<Bounds> LocalBounds = new List<Bounds>();  // in each element's own space
        }

        public static readonly Group[] Groups =
        {
            new Group { Key = "Gauge",    Name = "Compte-tours et vitesse", Parts = new[] { "revs", "needle", "spedo" }, Gauge = true },
            new Group { Key = "Timer",    Name = "Temps restant",           Parts = new[] { "timer" } },
            new Group { Key = "Position", Name = "Position",                Parts = new[] { "position" } },
            new Group { Key = "Laps",     Name = "Tour",                    Parts = new[] { "laps" } },
            new Group { Key = "LapTimes", Name = "Temps au tour",           Parts = new[] { "lap_times" } },
            new Group { Key = "Messages", Name = "Messages (départ, checkpoint, contresens…)", Movable = false,
                        Parts = new[] { "RaceStartCountdown", "finished!", "checkpoint!", "wipeout", "wrong way" } },
        };

        class Track { public Vector2 BasePos, WrittenPos; public Vector3 BaseScale, WrittenScale; public bool Written; }

        static Hud? instance;
        RectTransform? root, canvasRt;
        readonly Dictionary<RectTransform, Track> track = new Dictionary<RectTransform, Track>();
        GameObject? minimap;
        bool minimapForced;
        public static bool MinimapAvailable { get; private set; }

        public static void Install()
        {
            if (instance) return;
            var go = new GameObject("RaceBootHud");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Hud>();
            SceneManager.sceneLoaded += (s, m) => { if (instance) instance!.root = null; };
        }

        bool Resolve()
        {
            if (root && root!.gameObject.scene.isLoaded) return true;
            root = null; track.Clear(); minimap = null; minimapForced = false; MinimapAvailable = false;
            foreach (var t in Resources.FindObjectsOfTypeAll<RectTransform>())
                if (t.name == "in_game" && t.parent && t.parent.name == "90sGP_canvas" && t.gameObject.scene.isLoaded) { root = t; break; }
            if (!root) return false;
            canvasRt = root!.parent as RectTransform;
            foreach (var g in Groups)
            {
                g.Rts.Clear(); g.LocalBounds.Clear();
                foreach (var p in g.Parts)
                {
                    var rt = root.Find(p) as RectTransform;
                    if (!rt) continue;
                    g.Rts.Add(rt!);
                    g.LocalBounds.Add(MeasureLocal(rt!));
                }
            }
            minimap = root.Find("minimap")?.gameObject;
            if (minimap)
            {
                var raw = minimap!.GetComponent("RawImage");
                MinimapAvailable = raw && raw.GetType().GetProperty("texture")?.GetValue(raw) is Texture;
            }
            return true;
        }

        // Union of the rects of the element and of its descendants that draw something, in the element's own space.
        static Bounds MeasureLocal(RectTransform rt)
        {
            var b = new Bounds();
            bool first = true;
            var corners = new Vector3[4];
            foreach (var r in rt.GetComponentsInChildren<RectTransform>(true))
            {
                if (!r.GetComponent<CanvasRenderer>()) continue;
                r.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    var p = rt.InverseTransformPoint(c);
                    p.z = 0;
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            if (first) b = new Bounds(Vector3.zero, new Vector3(rt.rect.width, rt.rect.height, 0));
            return b;
        }

        Track Get(RectTransform rt)
        {
            if (!track.TryGetValue(rt, out var t)) { t = new Track(); track[rt] = t; }
            // Anything the game wrote since our last frame becomes the new base.
            if (!t.Written || rt.anchoredPosition != t.WrittenPos) t.BasePos = rt.anchoredPosition;
            if (!t.Written || rt.localScale != t.WrittenScale) t.BaseScale = rt.localScale;
            return t;
        }

        void LateUpdate()
        {
            if (!Resolve()) return;

            // Screen area in in_game space.
            var corners = new Vector3[4];
            canvasRt!.GetWorldCorners(corners);
            var a0 = root!.InverseTransformPoint(corners[0]);
            var a2 = root.InverseTransformPoint(corners[2]);
            var area = new Rect(a0.x, a0.y, a2.x - a0.x, a2.y - a0.y);
            float margin = Settings.HudMargin * area.height;
            float scale = Settings.HudScale;
            bool interior = Settings.CameraView != CameraViews.Chase;

            // Base bounds of every group (in in_game space, without our changes).
            var gb = new Dictionary<Group, Bounds>();
            var tracks = new Dictionary<RectTransform, Track>();
            Bounds content = new Bounds();
            bool firstContent = true;
            foreach (var g in Groups)
            {
                bool first = true;
                var b = new Bounds();
                for (int i = 0; i < g.Rts.Count; i++)
                {
                    var rt = g.Rts[i];
                    if (!rt) continue;
                    var t = Get(rt);
                    tracks[rt] = t;
                    // Base pivot position: current local position minus the offset we applied.
                    var basePivot = (Vector2)rt.localPosition - (rt.anchoredPosition - t.BasePos);
                    var lb = g.LocalBounds[i];
                    var min = basePivot + Vector2.Scale(lb.min, t.BaseScale);
                    var max = basePivot + Vector2.Scale(lb.max, t.BaseScale);
                    var eb = new Bounds((min + max) / 2, max - min);
                    if (first) { b = eb; first = false; } else b.Encapsulate(eb);
                }
                if (first) continue;
                gb[g] = b;
                if (g.Movable && !g.Gauge)
                {
                    if (firstContent) { content = b; firstContent = false; } else content.Encapsulate(b);
                }
            }

            foreach (var g in Groups)
            {
                if (!gb.TryGetValue(g, out var b)) continue;
                float gs = g.Gauge ? Settings.GaugeScale : scale;
                Vector2 c = b.center, size = b.size * gs;
                Vector2 target = c;
                if (g.Gauge)
                {
                    // The speedometer is placed on its own: optional low position in the interior views, then free offsets.
                    if (interior && Settings.HudGaugeLow) target = new Vector2(area.center.x, area.yMin + margin + size.y / 2);
                    target += new Vector2(Settings.GaugeX * area.width, Settings.GaugeY * area.height);
                }
                else if (g.Movable)
                {
                    float fx = Full(b.min.x, b.size.x, content.min.x, content.size.x, area.xMin + margin, area.width - 2 * margin, size.x);
                    float fy = Full(b.min.y, b.size.y, content.min.y, content.size.y, area.yMin + margin, area.height - 2 * margin, size.y);
                    target = new Vector2(Mathf.Lerp(c.x, fx, Settings.HudSpreadX), Mathf.Lerp(c.y, fy, Settings.HudSpreadY) + Settings.HudInfoY * area.height);
                }

                // Very wide screens leave less height to the canvas: keep every block inside the screen.
                if (g.Movable)
                    target = new Vector2(
                        ClampInside(target.x, size.x, area.xMin + margin, area.xMax - margin),
                        ClampInside(target.y, size.y, area.yMin + margin, area.yMax - margin));

                bool shown = Settings.HudShown(g.Key);
                foreach (var rt in g.Rts)
                {
                    if (!rt) continue;
                    var t = tracks[rt];
                    var basePivot = (Vector2)rt.localPosition - (rt.anchoredPosition - t.BasePos);
                    var newPivot = target + (basePivot - c) * gs;
                    t.WrittenPos = t.BasePos + (newPivot - basePivot);
                    t.WrittenScale = t.BaseScale * gs;
                    t.Written = true;
                    rt.anchoredPosition = t.WrittenPos;
                    rt.localScale = t.WrittenScale;

                    var cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
                    cg.alpha = shown ? 1f : 0f;
                    cg.blocksRaycasts = false;
                }
            }

            if (minimap)
            {
                bool want = MinimapAvailable && Settings.HudShown("Minimap");
                if (want && !minimap!.activeSelf) { minimap.SetActive(true); minimapForced = true; }
                else if (!want && minimapForced) { minimap!.SetActive(false); minimapForced = false; }
            }
        }

        static float ClampInside(float centre, float size, float min, float max)
        {
            if (size >= max - min) return (min + max) / 2f;
            return Mathf.Clamp(centre, min + size / 2f, max - size / 2f);
        }

        // Centre of a group when spread to the screen edges: its relative position inside the HUD
        // (0 = leftmost group, 1 = rightmost) is mapped to the full width of the screen.
        static float Full(float gMin, float gSize, float cMin, float cSize, float aMin, float aSize, float newSize)
        {
            float room = cSize - gSize;
            float t = room > 1e-3f ? Mathf.Clamp01((gMin - cMin) / room) : 0.5f;
            return aMin + newSize / 2 + t * Mathf.Max(0f, aSize - newSize);
        }
    }
}
