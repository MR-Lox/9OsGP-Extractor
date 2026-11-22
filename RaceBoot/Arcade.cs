using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RaceBoot
{
    // Start of a race, arcade style.
    //  - Intro: when START is pressed, three camera shots close in on the car, then the race view, on which
    //    the countdown runs. The game keeps running (confetti, crowd…): only the countdown is held.
    //  - The car cannot move during the countdown (the original game lets the throttle push it forward);
    //    the engine can still be revved.
    public class Arcade : MonoBehaviour
    {
        public static bool IntroPlaying => inst != null && inst.introCam != null;
        // During the presentation shots the car must stay visible, whatever the race view.
        public static bool ShotsPlaying => IntroPlaying && !inst!.counting;

        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static Arcade? inst;

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootArcade");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Arcade>();
        }

        // ---------------- race state ----------------

        MonoBehaviour? rm;
        object? rmFsm;
        PropertyInfo? rmActive;
        string last = "";
        float racingSince = -1f;

        string State()
        {
            if (!rm)
            {
                rm = Showcase.Fsm("GameManager", "race manager");
                rmFsm = rm ? rm!.GetType().GetProperty("Fsm")?.GetValue(rm) : null;
                rmActive = rmFsm?.GetType().GetProperty("ActiveStateName", F);
                waitTime = null; heldWait = false;
                if (!rm) return "";
            }
            if (!rm!.enabled) return "";
            return rmActive?.GetValue(rmFsm) as string ?? "";
        }

        static bool Countdown(string st) => st == "3" || st == "2" || st == "1";

        void Update()
        {
            string st = State();
            if (st == "3" && last != "3" && (last == "skip?" || last == "play_intro" || last == "") && Settings.IntroEnabled && !introCam)
                BeginIntro();
            if (st != "3" && waitTime != null) RestoreWait();
            HoldCar(Countdown(st) || introCam);

            // Learn where the game's chase camera sits behind the car, for the countdown view of the next races.
            // Measured just after GO, when the game camera has settled behind the still slow car.
            if (st == "go" || st == "racing") { if (racingSince < 0f) racingSince = Time.time; }
            else racingSince = -1f;
            if (racingSince >= 0f && !introCam && Time.time - racingSince > 0.25f && Time.time - racingSince < 0.45f && Settings.CameraView == CameraViews.Chase)
                LearnChasePose();
            last = st;
        }

        // ---------------- car hold ----------------

        Rigidbody? held;
        RigidbodyConstraints savedConstraints;

        void HoldCar(bool hold)
        {
            if (hold && !held)
            {
                var car = GameObject.Find("race_car_01");
                var rb = car ? car!.GetComponent<Rigidbody>() : null;
                if (!rb) return;
                held = rb;
                savedConstraints = rb!.constraints;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            }
            else if (!hold && held)
            {
                held!.constraints = savedConstraints;
                held = null;
            }
        }

        // ---------------- countdown hold ----------------
        // The first countdown state ("3") waits 1 s before "2": that wait is stretched during the intro.

        object? waitAction, waitFloat;
        PropertyInfo? waitTime;
        float waitOriginal;
        bool heldWait;

        void HoldWait()
        {
            heldWait = false;
            waitAction = null; waitFloat = null; waitTime = null;
            var states = rmFsm?.GetType().GetProperty("States", F)?.GetValue(rmFsm) as Array;
            if (states == null) return;
            foreach (var s in states)
            {
                if (s.GetType().GetProperty("Name")?.GetValue(s) as string != "3") continue;
                if (!(s.GetType().GetProperty("Actions", F)?.GetValue(s) is Array acts)) continue;
                foreach (var a in acts)
                    if (a.GetType().Name == "Wait")
                    {
                        waitAction = a;
                        waitFloat = a.GetType().GetField("time", F)?.GetValue(a);
                        waitTime = waitFloat?.GetType().GetProperty("Value");
                        if (waitTime == null) return;
                        waitOriginal = (float)waitTime.GetValue(waitFloat);
                        waitTime.SetValue(waitFloat, 100000f);
                        heldWait = true;
                        return;
                    }
            }
        }

        // The countdown resumes: "3" stays on screen for its normal duration from now.
        void ResumeWait()
        {
            if (!heldWait || waitTime == null) return;
            heldWait = false;
            float elapsed = 0f;
            var timer = waitAction?.GetType().GetField("timer", F);
            if (timer != null && timer.GetValue(waitAction) is float tv) elapsed = tv;
            waitTime.SetValue(waitFloat, elapsed + waitOriginal);
        }

        void RestoreWait()
        {
            waitTime?.SetValue(waitFloat, waitOriginal);
            waitTime = null; heldWait = false;
        }

        // ---------------- race view pose ----------------

        // Game chase camera relative to the car (flat car frame) and its field of view, learned during a race.
        static Vector3 chaseOffset = new Vector3(0f, 1.0614f, -4.5499f);   // measured in game
        static Quaternion chaseRot = Quaternion.identity;
        static float chaseFov = 90f;
        static bool learned;

        public static string PoseString() => !learned ? "" :
            string.Join(",", new[] { chaseOffset.x, chaseOffset.y, chaseOffset.z, chaseRot.x, chaseRot.y, chaseRot.z, chaseRot.w, chaseFov }
                .Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));

        public static void LoadPose(string v)
        {
            var p = v.Split(',');
            if (p.Length != 8) return;
            var f = new float[8];
            for (int i = 0; i < 8; i++) if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return;
            chaseOffset = new Vector3(f[0], f[1], f[2]);
            chaseRot = new Quaternion(f[3], f[4], f[5], f[6]);
            chaseFov = f[7];
            learned = true;
        }

        static Camera? MainCamera()
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>())
                if (c && c.name == "Main Camera" && c.gameObject.scene.isLoaded) return c;
            return null;
        }

        void LearnChasePose()
        {
            var main = MainCamera();
            var car = GameObject.Find("race_car_01");
            var b = car ? car!.transform.Find("car_body") : null;
            if (!main || !b) return;
            var f = Vector3.ProjectOnPlane(b!.forward, Vector3.up).normalized;
            var inv = Quaternion.Inverse(Quaternion.LookRotation(f, Vector3.up));
            chaseOffset = inv * (main!.transform.position - b.position);
            chaseRot = inv * main.transform.rotation;
            chaseFov = main.fieldOfView;
            bool first = !learned;
            learned = true;
            if (first) Settings.Save();
        }

        // Where the countdown is seen from: the race view chosen by the player.
        void RaceView(out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (CameraViews.InteriorPose(out pos, out rot)) { fov = Settings.CamFov; return; }
            var frame = Quaternion.LookRotation(carF, Vector3.up);
            pos = carPos + frame * chaseOffset;
            rot = frame * chaseRot;
            fov = chaseFov;
        }

        // ---------------- intro ----------------

        Camera? introCam;
        float introStart;
        bool counting;
        readonly HashSet<Camera> muted = new HashSet<Camera>();
        readonly List<Canvas> hiddenUi = new List<Canvas>();
        Transform? body;
        Vector3 carPos, carF, carR;

        struct ShotDef { public float Len; public Vector3 From, To; public float FovFrom, FovTo; }
        ShotDef[] shots = new ShotDef[0];

        void BeginIntro()
        {
            var car = GameObject.Find("race_car_01");
            body = car ? car!.transform.Find("car_body") : null;
            var main = MainCamera();
            if (!body || !main) return;

            HoldWait();
            introStart = Time.unscaledTime;
            counting = false;

            var go = new GameObject("RaceBootIntroCamera");
            DontDestroyOnLoad(go);
            introCam = go.AddComponent<Camera>();
            introCam.CopyFrom(main);
            Replay.CopyUrpData(main!, introCam);
            introCam.depth = 45;
            introCam.targetTexture = null;

            // No countdown / HUD during the shots.
            foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>())
                if (cv.name == "90sGP_canvas" && cv.gameObject.scene.isLoaded && cv.enabled) { cv.enabled = false; hiddenUi.Add(cv); }

            carPos = body!.position;
            carF = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            carR = Vector3.Cross(Vector3.up, carF);
            // Offsets relative to the car: (right, up, forward).
            shots = new[]
            {
                new ShotDef { Len = 1.7f, From = new Vector3(-15f, 9f, -30f), To = new Vector3(-11f, 6f, -21f), FovFrom = 40f, FovTo = 36f },  // wide, behind (the player starts last)
                new ShotDef { Len = 1.5f, From = new Vector3(-8f, 1.8f, -6f), To = new Vector3(-6.5f, 1.5f, -2f), FovFrom = 50f, FovTo = 48f }, // medium, side / rear
                new ShotDef { Len = 1.5f, From = new Vector3(3.2f, 0.7f, 5.5f), To = new Vector3(2.4f, 0.6f, 4.2f), FovFrom = 55f, FovTo = 52f }, // close, nose
                new ShotDef { Len = 1.5f, From = new Vector3(0f, 6f, -18f), To = Vector3.zero, FovFrom = 58f, FovTo = 0f },                      // into the race view
            };
            Debug.Log("[RaceBoot] intro started, wait held = " + heldWait);
        }

        void EndIntro()
        {
            counting = false;
            if (introCam) Destroy(introCam!.gameObject);
            introCam = null;
            foreach (var c in muted) if (c) c.enabled = true;
            muted.Clear();
            foreach (var cv in hiddenUi) if (cv) cv.enabled = true;
            hiddenUi.Clear();
            ResumeWait();
            Debug.Log("[RaceBoot] intro done");
        }

        // The countdown runs on the race view; the game camera takes over at GO.
        void StartCountdown()
        {
            counting = true;
            foreach (var cv in hiddenUi) if (cv) cv.enabled = true;
            hiddenUi.Clear();
            ResumeWait();
        }

        Vector3 World(Vector3 o) => carPos + carR * o.x + Vector3.up * o.y + carF * o.z;

        void LateUpdate()
        {
            if (!introCam) return;
            if (!body) { EndIntro(); return; }
            foreach (var c in Camera.allCameras)
                if (c != introCam && c.targetTexture == null && c.name != FreeCam.CameraName) { c.enabled = false; muted.Add(c); }

            RaceView(out var viewPos, out var viewRot, out var viewFov);

            if (counting)
            {
                if (!Countdown(State())) { EndIntro(); return; }
                introCam!.transform.SetPositionAndRotation(viewPos, viewRot);
                introCam.fieldOfView = viewFov;
                return;
            }

            float t = Time.unscaledTime - introStart;
            int i = 0;
            while (i < shots.Length && t > shots[i].Len) { t -= shots[i].Len; i++; }
            if (i >= shots.Length)
            {
                StartCountdown();
                introCam!.transform.SetPositionAndRotation(viewPos, viewRot);
                introCam.fieldOfView = viewFov;
                return;
            }
            var s = shots[i];
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / s.Len));
            var look = carPos + Vector3.up * 0.6f;
            Vector3 pos;
            Quaternion rot;
            if (i == shots.Length - 1)
            {
                // From high behind into the race view.
                var from = World(s.From);
                pos = Vector3.Lerp(from, viewPos, k);
                rot = Quaternion.Slerp(Quaternion.LookRotation(look - from, Vector3.up), viewRot, k);
                introCam!.fieldOfView = Mathf.Lerp(s.FovFrom, viewFov, k);
            }
            else
            {
                pos = World(Vector3.Lerp(s.From, s.To, k));
                rot = Quaternion.LookRotation(look - pos, Vector3.up);
                introCam!.fieldOfView = Mathf.Lerp(s.FovFrom, s.FovTo, k);
            }
            introCam.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
