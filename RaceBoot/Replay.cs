using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace RaceBoot
{
    // Race replay.
    // While racing, the pose of every car (body, body pivot, wheels) is sampled 30 times per second and a
    // visual-only copy of each car is made (meshes kept, physics / scripts / sounds stripped) in a
    // DontDestroyOnLoad stage. After the race the game reloads the scene and waits on its "PRESS START"
    // screen: the replay then plays in a loop there, filmed by an automatic director (trackside TV cameras,
    // chase, helicopter, front, side and hood shots), until a new race is started.
    public class Replay : MonoBehaviour
    {
        public static bool Playing => inst != null && inst.playing;
        public static string? DebugForceShot;   // test bench only

        const float SampleRate = 30f;
        const float MaxDuration = 20f * 60f;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        class Track
        {
            public string Path = "";
            public bool World;
            public Transform? Src;
            public readonly List<Transform> Dst = new List<Transform>();
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Quaternion> R = new List<Quaternion>();
        }

        class Actor
        {
            public string Name = "";
            public bool Player;
            public Transform? Src;
            public GameObject? Clone;
            public Bounds Bounds;
            public Vector3 Scale = Vector3.one;
            public readonly List<Track> Tracks = new List<Track>();
            public readonly List<bool> Visible = new List<bool>();
        }

        class Recording
        {
            public readonly List<float> Times = new List<float>();
            public readonly List<Actor> Actors = new List<Actor>();
            public Vector3? Hood;
            public float Start;          // loop start (just before GO)
            public float Duration => Times.Count > 0 ? Times[Times.Count - 1] : 0f;
        }

        static Replay? inst;
        Recording? rec, last;
        GameObject? stage;
        bool recording, playing;
        float recStart, nextSample, discoverUntil, nextDiscover, skipSince = -1f;
        readonly HashSet<Transform> known = new HashSet<Transform>();

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootReplay");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Replay>();
            SceneManager.sceneLoaded += (s, m) => inst!.OnSceneLoaded();
        }

        // ---------------- race state ----------------

        MonoBehaviour? raceManager;
        object? rmFsm;
        PropertyInfo? rmActive;

        string RaceState()
        {
            if (!raceManager)
            {
                raceManager = null; rmFsm = null;
                foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                    if (mb.GetType().Name == "PlayMakerFSM" && mb.gameObject.name == "GameManager"
                        && mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string == "race manager") { raceManager = mb; break; }
                if (!raceManager) return "";
                rmFsm = raceManager!.GetType().GetProperty("Fsm")?.GetValue(raceManager);
                rmActive = rmFsm?.GetType().GetProperty("ActiveStateName", F);
            }
            return rmActive?.GetValue(rmFsm) as string ?? "";
        }

        static bool Racing(string st) => st == "3" || st == "2" || st == "1" || st == "go" || st == "racing" || st == "finished!";

        void OnSceneLoaded()
        {
            raceManager = null;
            if (recording) EndRecording();
            if (playing) StopPlayback();
        }

        static Transform? PlayerBody()
        {
            var car = GameObject.Find("race_car_01");
            return car ? car.transform.Find("car_body") : null;
        }

        string lastState = "";

        void Update()
        {
            string st = RaceState();
            RaceTimer(st, lastState);
            lastState = st;
            if (playing)
            {
                if ((st != "skip?" && st != "play_intro") || !Settings.ReplayEnabled) StopPlayback();
                return;
            }

            if (!recording && Racing(st) && PlayerBody()) BeginRecording();
            if (recording)
            {
                if (st == "go" && rec != null && rec.Start <= 0f) rec.Start = Mathf.Max(0f, Time.time - recStart - 1.5f);
                if (!Racing(st) && st != "") EndRecording();
                else
                {
                    if (Time.time < discoverUntil && Time.time >= nextDiscover) { nextDiscover = Time.time + 0.5f; Discover(); }
                    if (Time.timeScale > 0f && Time.time >= nextSample) Sample();
                    if (rec != null && rec.Duration > MaxDuration) EndRecording();
                }
            }

            if (!recording && last != null && st == "skip?" && Settings.ReplayEnabled)
            {
                if (skipSince < 0f) skipSince = Time.unscaledTime;
                else if (Time.unscaledTime - skipSince > 0.4f) StartPlayback();
            }
            else skipSince = -1f;
        }

        // The race timer (FSM "timer countdown") is enabled as soon as the race scene loads, so it already
        // counts down on the "PRESS START" screen: a long wait (or a replay) ends in a game over that reloads
        // the scene, and a race started late begins with only a few seconds left. It is held on the title
        // screen and restarted from its "update" state (50 s) at GO.
        MonoBehaviour? timerFsm;

        void RaceTimer(string st, string previous)
        {
            if (!timerFsm)
            {
                timerFsm = null;
                foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                    if (mb.GetType().Name == "PlayMakerFSM" && mb.gameObject.name == "GameManager"
                        && mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string == "timer countdown") { timerFsm = mb; break; }
                if (!timerFsm) return;
            }
            if (st == "play_intro" || st == "skip?")
            {
                if (timerFsm!.enabled) timerFsm.enabled = false;
            }
            else if (st == "go" && previous != "go")
            {
                timerFsm!.enabled = true;
                var fsm = timerFsm.GetType().GetProperty("Fsm")?.GetValue(timerFsm);
                fsm?.GetType().GetMethod("SetState", new[] { typeof(string) })?.Invoke(fsm, new object[] { "update" });
                Debug.Log("[RaceBoot] race timer restarted");
            }
        }

        // ---------------- recording ----------------

        void BeginRecording()
        {
            recording = true;
            rec = new Recording();
            known.Clear();
            recStart = Time.time;
            nextSample = 0f;
            discoverUntil = Time.time + 8f;
            nextDiscover = 0f;
            if (!stage)
            {
                stage = new GameObject("RaceBootReplayStage");
                DontDestroyOnLoad(stage);
                stage.SetActive(false);
            }
            AddActor(PlayerBody()!, true);
            try { rec.Hood = CameraViews.HoodLocal(); } catch { }
            Discover();
            Debug.Log("[RaceBoot] replay: recording started, player scale " + PlayerBody()!.lossyScale.ToString("F3") + " hood " + rec.Hood);
        }

        void Discover()
        {
            foreach (var rb in FindObjectsOfType<Rigidbody>())
            {
                if (!rb.name.StartsWith("AI_car")) continue;
                var body = rb.transform.Find("car_body");
                if (body && !known.Contains(body!)) AddActor(body!, false);
            }
        }

        void AddActor(Transform body, bool player)
        {
            known.Add(body);
            var a = new Actor { Name = body.parent ? body.parent.name : body.name, Player = player, Src = body };

            // Visual-only copy, created under the inactive stage so none of its scripts wake up.
            var clone = Instantiate(body.gameObject, stage!.transform);
            clone.name = "Replay " + a.Name;
            a.Scale = body.lossyScale;
            clone.transform.localScale = a.Scale;   // the stage has no scale
            Strip(clone);
            a.Clone = clone;

            a.Tracks.Add(new Track { World = true, Src = body, Dst = { clone.transform } });
            foreach (var d in body.GetComponentsInChildren<Transform>(true))
            {
                if (d == body) continue;
                string n = d.name;
                bool wheel = n.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!(n == "body_pivot" || n == "Body" || (wheel && !n.EndsWith("LOD2") && !n.EndsWith("LOD3")))) continue;
                var t = new Track { Path = RelPath(body, d), Src = d };
                var dst = clone.transform.Find(t.Path);
                if (!dst) continue;
                t.Dst.Add(dst!);
                // LOD1 drives its LOD2 / LOD3 siblings too.
                if (n.EndsWith("LOD1") && dst!.parent)
                {
                    string prefix = n.Substring(0, n.Length - 4);
                    foreach (Transform sib in dst.parent)
                        if (sib != dst && sib.name.StartsWith(prefix)) t.Dst.Add(sib);
                }
                a.Tracks.Add(t);
            }

            // Mesh bounds in car_body space, for camera framing.
            bool first = true;
            foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == "car_collider" || r.name.StartsWith("shadow")) continue;
                var b = r.bounds;
                var p0 = body.InverseTransformPoint(b.min); var p1 = body.InverseTransformPoint(b.max);
                if (first) { a.Bounds = new Bounds(p0, Vector3.zero); first = false; } else a.Bounds.Encapsulate(p0);
                a.Bounds.Encapsulate(p1);
            }
            if (first) a.Bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2, 1.2f, 4.5f));

            // Pad the frames recorded before this car showed up.
            int n0 = rec!.Times.Count;
            foreach (var t in a.Tracks)
                for (int i = 0; i < n0; i++) { t.P.Add(Pos(t)); t.R.Add(Rot(t)); }
            for (int i = 0; i < n0; i++) a.Visible.Add(false);
            rec.Actors.Add(a);
        }

        static string RelPath(Transform root, Transform t) => t.parent == root ? t.name : RelPath(root, t.parent) + "/" + t.name;

        // Removes everything but the visuals (in dependency order).
        static void Strip(GameObject g)
        {
            void Kill<T>() where T : Component
            {
                foreach (var c in g.GetComponentsInChildren<T>(true))
                    try { if (c) DestroyImmediate(c); } catch { }
            }
            Kill<MonoBehaviour>();
            Kill<Joint>();
            Kill<Collider>();
            Kill<Rigidbody>();
            Kill<AudioSource>();
            Kill<AudioListener>();
            Kill<Animator>();
            Kill<Camera>();
            Kill<ParticleSystem>();
            Kill<ParticleSystemRenderer>();
            Kill<TrailRenderer>();
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "car_collider") { r.enabled = false; continue; }
                // The interior view hides the player's car body (shadows only): the copy must be visible.
                r.forceRenderingOff = false;
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) r.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        static Vector3 Pos(Track t) => t.Src ? (t.World ? t.Src!.position : t.Src!.localPosition) : (t.P.Count > 0 ? t.P[t.P.Count - 1] : Vector3.zero);
        static Quaternion Rot(Track t) => t.Src ? (t.World ? t.Src!.rotation : t.Src!.localRotation) : (t.R.Count > 0 ? t.R[t.R.Count - 1] : Quaternion.identity);

        void Sample()
        {
            nextSample = Time.time + 1f / SampleRate;
            rec!.Times.Add(Time.time - recStart);
            foreach (var a in rec.Actors)
            {
                a.Visible.Add(a.Src && a.Src!.gameObject.activeInHierarchy);
                foreach (var t in a.Tracks) { t.P.Add(Pos(t)); t.R.Add(Rot(t)); }
            }
        }

        void EndRecording()
        {
            recording = false;
            if (rec == null) return;
            foreach (var a in rec.Actors) { a.Src = null; foreach (var t in a.Tracks) t.Src = null; }
            float travelled = 0f;
            var pt = rec.Actors.Count > 0 ? rec.Actors[0].Tracks[0].P : null;
            if (pt != null) for (int i = 1; i < pt.Count; i++) travelled += Vector3.Distance(pt[i - 1], pt[i]);
            Debug.Log($"[RaceBoot] replay: recording ended, {rec.Duration:F1} s, {rec.Times.Count} frames, {rec.Actors.Count} cars, player travelled {travelled:F0} m");
            if (rec.Duration < 5f) { Discard(rec); rec = null; return; }
            if (last != null) Discard(last);
            last = rec;
            rec = null;
        }

        static void Discard(Recording r)
        {
            foreach (var a in r.Actors) if (a.Clone) Destroy(a.Clone);
        }

        // ---------------- playback ----------------

        Camera? cam;
        float t, startedAt;
        int cursor;
        readonly HashSet<Camera> muted = new HashSet<Camera>();
        Canvas? pressStart;

        void StartPlayback()
        {
            if (last == null) return;
            playing = true;
            t = last.Start; cursor = 0; startedAt = Time.unscaledTime;
            stage!.SetActive(true);

            // Parts of the track are hidden on the title screen.
            foreach (var tr in Resources.FindObjectsOfTypeAll<Transform>())
                if (tr.name.StartsWith("IslandTrackVisibility") && tr.gameObject.scene.isLoaded && !tr.gameObject.activeSelf)
                    tr.gameObject.SetActive(true);

            var go = new GameObject("RaceBootReplayCamera");
            go.transform.SetParent(stage.transform, false);
            cam = go.AddComponent<Camera>();
            Camera? reference = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>())
                if (c.name == "Main Camera" && c.gameObject.scene.isLoaded) { reference = c; break; }
            if (reference) { cam.CopyFrom(reference); CopyUrpData(reference!, cam); }
            cam.depth = 50;
            cam.targetTexture = null;
            cam.enabled = true;

            foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>())
                if (cv.name == "press_start_canvas" && cv.gameObject.scene.isLoaded) pressStart = cv;

            director = new Director(last, this);
            Debug.Log($"[RaceBoot] replay: playing {last.Duration:F1} s");
        }

        // URP camera settings (post-processing, anti-aliasing…) live on a separate component.
        static void CopyUrpData(Camera from, Camera to)
        {
            Component? src = null;
            foreach (var c in from.GetComponents<Component>()) if (c && c.GetType().Name == "UniversalAdditionalCameraData") src = c;
            if (!src) return;
            var type = src!.GetType();
            var dst = to.GetComponent(type) ?? to.gameObject.AddComponent(type);
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                if (!(p.PropertyType.IsValueType || p.PropertyType.IsEnum)) continue;
                try { p.SetValue(dst, p.GetValue(src)); } catch { }
            }
        }

        void StopPlayback()
        {
            playing = false;
            if (stage) stage!.SetActive(false);
            if (cam) Destroy(cam!.gameObject);
            cam = null;
            foreach (var c in muted) if (c) c.enabled = true;
            muted.Clear();
            Debug.Log("[RaceBoot] replay: stopped");
        }

        void LateUpdate()
        {
            if (!playing || last == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            t += dt;
            if (t > last.Duration) { t = last.Start; cursor = 0; director = new Director(last, this); }
            Apply(t);
            director?.Update(t, dt);

            // Only our camera renders; the title screen's "PRESS START" canvas is drawn through it.
            foreach (var c in Camera.allCameras)
                if (c != cam && c.targetTexture == null) { c.enabled = false; muted.Add(c); }
            if (pressStart && pressStart!.renderMode == RenderMode.ScreenSpaceCamera) pressStart.worldCamera = cam;
        }

        void Apply(float time)
        {
            var r = last!;
            int n = r.Times.Count;
            if (cursor >= n || r.Times[cursor] > time) cursor = 0;
            while (cursor + 1 < n && r.Times[cursor + 1] <= time) cursor++;
            int i = cursor, j = Mathf.Min(i + 1, n - 1);
            float span = r.Times[j] - r.Times[i];
            float k = span > 1e-5f ? Mathf.Clamp01((time - r.Times[i]) / span) : 0f;
            foreach (var a in r.Actors)
            {
                if (!a.Clone) continue;
                bool vis = a.Visible[i];
                if (a.Clone!.activeSelf != vis) a.Clone.SetActive(vis);
                if (!vis) continue;
                foreach (var tr in a.Tracks)
                {
                    var p = Vector3.Lerp(tr.P[i], tr.P[j], k);
                    var q = Quaternion.Slerp(tr.R[i], tr.R[j], k);
                    foreach (var d in tr.Dst)
                    {
                        if (!d) continue;
                        if (tr.World) d.SetPositionAndRotation(p, q);
                        else { d.localPosition = p; d.localRotation = q; }
                    }
                }
            }
        }

        static void PoseAt(Recording r, Actor a, float time, out Vector3 pos, out Quaternion rot)
        {
            int n = r.Times.Count;
            time = Mathf.Clamp(time, 0f, r.Duration);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (r.Times[mid] <= time) lo = mid; else hi = mid; }
            float span = r.Times[hi] - r.Times[lo];
            float k = span > 1e-5f ? Mathf.Clamp01((time - r.Times[lo]) / span) : 0f;
            var tr = a.Tracks[0];
            pos = Vector3.Lerp(tr.P[lo], tr.P[hi], k);
            rot = Quaternion.Slerp(tr.R[lo], tr.R[hi], k);
        }

        // ---------------- director ----------------

        Director? director;

        class Director
        {
            enum Shot { Tv, Chase, Heli, Front, Side, Hood }
            readonly Recording r;
            readonly Replay owner;
            readonly Actor player;
            Shot shot = Shot.Chase;
            float shotStart, shotEnd = -1f, side = 1f;
            Vector3 camPos, vel, tvPos, offset;
            bool snap = true;

            public Director(Recording rec, Replay o)
            {
                r = rec; owner = o;
                player = rec.Actors.Find(a => a.Player) ?? rec.Actors[0];
            }

            public void Update(float t, float dt)
            {
                var cam = owner.cam;
                if (!cam) return;
                PoseAt(r, player, t, out var pos, out var rot);
                var f = rot * Vector3.forward; var u = rot * Vector3.up; var right = rot * Vector3.right;
                var flatF = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
                if (flatF.sqrMagnitude < 0.01f) flatF = f;
                float h = Mathf.Max(0.5f, player.Bounds.center.y * player.Scale.y + 0.3f);
                var pivot = pos + Vector3.up * h;

                if (t >= shotEnd || (shot == Shot.Tv && Vector3.Distance(tvPos, pos) > 95f && t > shotStart + 1.5f)) NextShot(t);

                Vector3 want, look; float fov; Quaternion? fixedRot = null; float smooth = 0f; bool arm = true;
                switch (shot)
                {
                    case Shot.Tv:
                        want = tvPos; look = pivot; arm = false;
                        float dist = Vector3.Distance(tvPos, look);
                        fov = Mathf.Clamp(2f * Mathf.Atan(5.5f / Mathf.Max(dist, 0.1f)) * Mathf.Rad2Deg, 12f, 60f);
                        break;
                    case Shot.Heli:
                        want = pos + Offset(shot, rot, side); look = pos + flatF * 4f; fov = 52f; smooth = 0.5f;
                        break;
                    case Shot.Front:
                        want = pos + Offset(shot, rot, side); look = pivot; fov = 48f;
                        break;
                    case Shot.Side:
                        want = pos + Offset(shot, rot, side); look = pos + Vector3.up * h * 0.8f; fov = 55f;
                        break;
                    case Shot.Hood when r.Hood.HasValue:
                        // Default hood framing (the bonnet stays visible at the bottom of the screen).
                        want = pos + rot * Vector3.Scale(r.Hood!.Value, player.Scale); look = want + f; fov = 70f; arm = false;
                        fixedRot = Quaternion.LookRotation(f, Vector3.Lerp(Vector3.up, u, 0.5f)) * Quaternion.Euler(5f, 0f, 0f);
                        break;
                    default: // chase
                        want = pos + Offset(Shot.Chase, rot, side); look = pos + f * 4f + Vector3.up * h; fov = 62f; smooth = 0.22f;
                        break;
                }

                // Smooth the offset to the car, not the absolute position: at 300 km/h a smoothed position lags 30 m behind.
                if (snap || smooth <= 0f) { offset = want - pos; vel = Vector3.zero; snap = false; }
                else offset = Vector3.SmoothDamp(offset, want - pos, ref vel, smooth, Mathf.Infinity, dt);
                camPos = shot == Shot.Tv || shot == Shot.Hood ? want : pos + offset;

                // Spring arm: never put the camera behind a wall.
                if (arm && t > shotStart + 0.5f && Physics.Linecast(camPos, pivot, ~0, QueryTriggerInteraction.Ignore)
                    && !Physics.Linecast(pivot, camPos, ~0, QueryTriggerInteraction.Ignore))
                    shotEnd = t;   // the car is inside / behind a wall from this side: cut
                if (arm && Physics.Linecast(pivot, camPos, out var hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < 2.5f && t > shotStart + 0.5f) shotEnd = t;   // too close to a wall: cut
                    camPos = hit.point + (pivot - camPos).normalized * 0.4f;
                }


                var q = fixedRot ?? Quaternion.LookRotation((look - camPos).sqrMagnitude > 1e-4f ? look - camPos : f, Vector3.up);
                cam!.transform.SetPositionAndRotation(camPos, q);
                cam.fieldOfView = fov;
            }

            void NextShot(float t)
            {
                // Weighted pick, never twice the same shot in a row.
                var pool = new List<Shot> { Shot.Tv, Shot.Tv, Shot.Tv, Shot.Chase, Shot.Chase, Shot.Heli, Shot.Front, Shot.Side };
                if (r.Hood.HasValue) pool.Add(Shot.Hood);
                pool.RemoveAll(s => s == shot);
                side = Random.value < 0.5f ? -1f : 1f;
                Shot next = Shot.Hood;
                bool found = false;
                for (int tries = 0; tries < 8 && !found && pool.Count > 0; tries++)
                {
                    next = pool[Random.Range(0, pool.Count)];
                    found = next == Shot.Tv ? FindTvSpot(t) : Clear(next, t);
                    if (!found) pool.RemoveAll(x => x == next);
                }
                if (!found) next = r.Hood.HasValue ? Shot.Hood : Shot.Chase;
                if (DebugForceShot != null && Enum.TryParse(DebugForceShot, out Shot forced)) next = forced;
                shot = next;
                shotStart = t;
                shotEnd = t + Random.Range(4.5f, 7.5f);
                snap = true;
            }

            static Vector3 Offset(Shot s, Quaternion rot, float side)
            {
                var f = rot * Vector3.forward;
                var flatF = Vector3.ProjectOnPlane(f, Vector3.up).normalized;
                if (flatF.sqrMagnitude < 0.01f) flatF = f;
                switch (s)
                {
                    case Shot.Heli: return -flatF * 16f + Vector3.up * 11f;
                    case Shot.Front: return f * 9f + Vector3.up * 1.4f;
                    case Shot.Side: return rot * Vector3.right * side * 6f + Vector3.up * 1.3f + f * 1.5f;
                    default: return -flatF * 7.5f + Vector3.up * 2.6f;
                }
            }

            // A car-mounted shot is usable when its camera keeps a clear view of the car for the next 3 seconds.
            bool Clear(Shot s, float t)
            {
                if (s == Shot.Hood) return r.Hood.HasValue;
                float h = Mathf.Max(0.5f, player.Bounds.center.y * player.Scale.y + 0.3f);
                for (float k = 0f; k <= 3f; k += 0.5f)
                {
                    PoseAt(r, player, t + k, out var p, out var q);
                    var pivot = p + Vector3.up * h;
                    var off = Offset(s, q, side);
                    if (Physics.Linecast(pivot, p + off, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.distance < off.magnitude * 0.75f)
                        return false;
                    if (Physics.Linecast(p + off, pivot, ~0, QueryTriggerInteraction.Ignore)) return false;
                    if (Physics.CheckSphere(p + off, 0.4f, ~0, QueryTriggerInteraction.Ignore)) return false;
                }
                return true;
            }

            // Time at which the car has driven `metres` further along its recorded path (or t + 3 s when slow).
            float TimeAhead(float t, float metres)
            {
                PoseAt(r, player, t, out var prev, out _);
                float acc = 0f;
                for (float k = 0.1f; k <= 4f; k += 0.1f)
                {
                    PoseAt(r, player, t + k, out var p, out _);
                    acc += Vector3.Distance(prev, p); prev = p;
                    if (acc >= metres) return t + k;
                }
                return t + Mathf.Min(3f, r.Duration - t);
            }

            // A trackside camera 35-70 m ahead of the car, with a clear line of sight while the car is within range.
            bool FindTvSpot(float t)
            {
                var why = new System.Text.StringBuilder();
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    float tf = TimeAhead(t, Random.Range(35f, 70f));
                    PoseAt(r, player, tf, out var p, out var q);
                    float s = Random.value < 0.5f ? -1f : 1f;
                    var cand = p + q * Vector3.right * s * Random.Range(6f, 12f) + Vector3.up * Random.Range(1.2f, 4.5f) + q * Vector3.forward * Random.Range(0f, 10f);
                    var inside = Physics.OverlapSphere(cand, 0.6f, ~0, QueryTriggerInteraction.Ignore);
                    if (inside.Length > 0) { why.Append(" in:" + inside[0].name); continue; }
                    bool clear = true;
                    for (float k = 0f; k <= 8f && clear; k += 0.4f)
                    {
                        PoseAt(r, player, t + k, out var pk, out _);
                        if (k > 1f && Vector3.Distance(pk, cand) > 95f) break;
                        if (Physics.Linecast(cand, pk + Vector3.up * 1.0f, out var hit, ~0, QueryTriggerInteraction.Ignore)) { clear = false; why.Append(" los:" + hit.collider.name); }
                    }
                    if (!clear) continue;
                    tvPos = cand;
                    return true;
                }
                return false;
            }
        }

        // ---------------- overlay ----------------

        GUIStyle? style, shadow;

        void OnGUI()
        {
            if (!playing) return;
            float s = Mathf.Max(0.75f, Screen.height / 1080f);
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
                shadow = new GUIStyle(style);
            }
            style.fontSize = shadow!.fontSize = Mathf.RoundToInt(46 * s);
            bool blink = ((Time.unscaledTime - startedAt) % 1.2f) < 0.8f;
            style.normal.textColor = new Color(1f, 0.15f, 0.1f, blink ? 1f : 0.35f);
            shadow.normal.textColor = new Color(0f, 0f, 0f, blink ? 0.8f : 0.3f);
            var rect = new Rect(40 * s, 30 * s, 600 * s, 80 * s);
            GUI.Label(new Rect(rect.x + 3 * s, rect.y + 3 * s, rect.width, rect.height), "● REPLAY", shadow);
            GUI.Label(rect, "● REPLAY", style);
        }
    }
}
