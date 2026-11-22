using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaceBoot
{
    // Adds cockpit-style views on top of the game's chase camera.
    // The game's own camera logic keeps running; we only override "Main Camera" right before it renders.
    public static class CameraViews
    {
        public const int Chase = 0, Hood = 1, Bumper = 2;
        public static readonly string[] Names = { "Poursuite (origine)", "Capot", "Intérieure sans capot" };

        static Camera? cam;
        static Transform? car;      // race_car_01/car_body
        static Transform? carRoot;  // race_car_01
        static Bounds localBounds;  // car mesh bounds, in car_body local space
        static bool overriding;
        static Vector3 savedLocalPos;
        static Quaternion savedLocalRot;
        static float savedFov;
        static readonly List<Renderer> hidden = new List<Renderer>();
        static readonly List<ShadowCastingMode> hiddenModes = new List<ShadowCastingMode>();
        static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginContextRendering += (ctx, cams) => BeforeRender();
        }

        public static void Cycle()
        {
            Settings.CameraView = (Settings.CameraView + 1) % Names.Length;
            Settings.Save();
        }

        static bool Resolve()
        {
            if (cam && car && carRoot && carRoot!.gameObject.activeInHierarchy) return true;
            cam = null; car = null; carRoot = null; hoodPoint = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>())
                if (c.name == "Main Camera" && c.gameObject.scene.IsValid() && c.transform.parent && c.transform.parent.name == "camera_hinge_01") { cam = c; break; }
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == "race_car_01" && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy)
                {
                    carRoot = t;
                    car = t.Find("car_body") ?? t;
                    break;
                }
            if (!cam || !car) return false;
            // Bounds of the visible car body, used to place the views automatically.
            var body = car!.Find("body_pivot/Body") ?? car;
            bool first = true;
            foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = car.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (first) { localBounds = new Bounds(p, Vector3.zero); first = false; } else localBounds.Encapsulate(p);
                }
            }
            if (first) localBounds = new Bounds(Vector3.up, new Vector3(2, 1.2f, 4.5f));
            Debug.Log("[RaceBoot] car bounds (local) " + localBounds);
            return true;
        }

        static void BeforeRender()
        {
            int view = Settings.CameraView;
            if (view == Chase || !Resolve())
            {
                Restore();
                return;
            }
            if (!overriding)
            {
                savedLocalPos = cam!.transform.localPosition;
                savedLocalRot = cam.transform.localRotation;
                savedFov = cam.fieldOfView;
                overriding = true;
            }

            var b = localBounds;
            Vector3 local;
            if (view == Hood)
                local = HoodPoint();
            else
                local = new Vector3(b.center.x, b.min.y + b.size.y * 0.32f, b.max.z + 0.05f);
            local += new Vector3(0f, Settings.CamHeight, Settings.CamForward);

            // Follow the car's yaw/pitch but damp its body roll so the view does not wobble too much.
            var rot = Quaternion.LookRotation(car!.forward, Vector3.Lerp(Vector3.up, car.up, 0.5f));
            cam!.transform.SetPositionAndRotation(car.TransformPoint(local), rot * Quaternion.Euler(Settings.CamPitch, 0f, 0f));
            cam.fieldOfView = Settings.CamFov;

            SetCarHidden(view == Bumper);
        }

        static Vector3? hoodPoint;

        // Finds the top of the bonnet by casting a ray down onto the car's collider, ahead of the windscreen.
        static Vector3 HoodPoint()
        {
            if (hoodPoint.HasValue) return hoodPoint.Value;
            var b = localBounds;
            var p = new Vector3(b.center.x, b.min.y + b.size.y * 0.55f, b.center.z + b.extents.z * 0.3f);
            var col = car!.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => c.name == "car_collider");
            if (col)
            {
                // Walk from the nose towards the middle of the car, sampling the surface height.
                // The windscreen starts where the height suddenly rises above the bonnet.
                float? hoodY = null, wsZ = null;
                const int steps = 40;
                for (int i = 0; i <= steps; i++)
                {
                    float z = Mathf.Lerp(b.max.z - b.size.z * 0.08f, b.center.z - b.extents.z * 0.2f, i / (float)steps);
                    var top = car.TransformPoint(new Vector3(b.center.x, b.max.y + 1f, z));
                    if (!col!.Raycast(new Ray(top, -car.up), out var hit, b.size.y * 4f + 2f)) continue;
                    float y = car.InverseTransformPoint(hit.point).y;
                    if (hoodY == null) { hoodY = y; continue; }
                    if (y > hoodY.Value + b.size.y * 0.12f) { wsZ = z; break; }
                    hoodY = Mathf.Max(hoodY.Value, y);
                }
                if (hoodY != null)
                {
                    float z = (wsZ ?? b.center.z + b.extents.z * 0.2f) + b.size.z * 0.03f;
                    p = new Vector3(b.center.x, hoodY.Value + b.size.y * 0.2f, z);
                }
                Debug.Log("[RaceBoot] hood: surface y=" + hoodY + " windscreen z=" + wsZ + " -> camera " + p);
            }
            hoodPoint = p;
            return p;
        }

        static void SetCarHidden(bool hide)
        {
            if (hide && hidden.Count == 0 && car)
            {
                foreach (var r in carRoot!.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                    hidden.Add(r);
                    hiddenModes.Add(r.shadowCastingMode);
                    if (r.shadowCastingMode != ShadowCastingMode.Off) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    else r.forceRenderingOff = true;
                }
            }
            else if (!hide && hidden.Count > 0)
            {
                for (int i = 0; i < hidden.Count; i++)
                    if (hidden[i]) { hidden[i].shadowCastingMode = hiddenModes[i]; hidden[i].forceRenderingOff = false; }
                hidden.Clear(); hiddenModes.Clear();
            }
        }

        static void Restore()
        {
            SetCarHidden(false);
            if (!overriding) return;
            overriding = false;
            if (!cam) return;
            cam!.transform.localPosition = savedLocalPos;
            cam.transform.localRotation = savedLocalRot;
            cam.fieldOfView = savedFov;
        }
    }

    // The "halftone on the lights" is the Bloom lens-dirt texture (a comic dot pattern) of the post-process volumes.
    // Turning it off sets the dirt intensity to 0; the original value is restored when turned back on.
    public static class Halftone
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<object, float> original = new Dictionary<object, float>();
        static float nextScan;

        public static void Apply(bool enabled, bool force = false)
        {
            if (!force && Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            foreach (var v in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (!v || v.GetType().Name != "Volume" || !v.gameObject.scene.IsValid()) continue;
                var prof = v.GetType().GetField("sharedProfile", F)?.GetValue(v);
                if (!(prof?.GetType().GetField("components", F)?.GetValue(prof) is IList comps)) continue;
                foreach (var c in comps)
                {
                    if (c == null || c.GetType().Name != "Bloom") continue;
                    var param = c.GetType().GetField("dirtIntensity", F)?.GetValue(c);
                    if (param == null) continue;
                    var valueProp = param.GetType().GetProperty("value", F);
                    var overrideProp = param.GetType().GetProperty("overrideState", F);
                    if (valueProp == null) continue;
                    float cur = (float)valueProp.GetValue(param);
                    if (!original.ContainsKey(param)) original[param] = cur;
                    float want = enabled ? original[param] : 0f;
                    if (cur != want)
                    {
                        overrideProp?.SetValue(param, true);
                        valueProp.SetValue(param, want);
                    }
                }
            }
        }
    }
}
