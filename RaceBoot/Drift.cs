using System;
using System.Reflection;
using UnityEngine;

namespace RaceBoot
{
    // Drift styles selectable in F1 > Gameplay, modelled on the arcade games they are named after.
    //
    // How the car of 9OsGP works: one rigidbody that only yaws (strong angular drag), driven by a forward force,
    // a yaw torque for the steering and a "grip" that cancels the sideways velocity (ApplyCustomGrip of the
    // "downforce" FSM), so the car goes where its nose points. The game's own drift only rotates the car body
    // visually ("car body drift angle") while the car turns harder.
    //
    //  0 = Origine: the game's drift, untouched (brake held + full steering lock above 150 km/h).
    //  1 = Ridge Racer: enter by lifting off and back on the throttle while turning (or a brake tap). Very
    //      stable "rail" slide that keeps its speed; the wheel sets the line (into the turn = tighter and a
    //      bigger angle, counter-steer = wider and smaller). Lift off or straighten the wheel: grip again.
    //  2 = OutRun 2: same entry. Huge, very forgiving slide that holds as long as the wheel is turned, even off
    //      the throttle; a firm counter-steer flips it into the other direction (S-bends in one slide).
    //  3 = Daytona USA / Scud Race: brake tap (or lift-off) while turning. A real slide: the tail steps out
    //      and keeps rotating under power, so you hold the angle with counter-steer, or spin. Speed is
    //      scrubbed while sideways; lifting off the throttle tucks the tail back in.
    public class Drift : MonoBehaviour
    {
        public static readonly string[] StyleNames = { "Origine", "Ridge Racer", "OutRun 2", "Daytona / Scud Race" };

        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const float DefaultGrip = 0.45f;
        static Drift? inst;

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootDrift");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Drift>();
        }

        // Test bench: inputs forced from a script, and what the drift is doing.
        public static bool ForceInput;
        public static float ForceAccel, ForceBrake, ForceSteer;
        public static bool Drifting => inst != null && inst.drifting;
        public static bool Spinning => inst != null && inst.spinTime > 0f;
        public static float SlipAngle => inst != null ? inst.slip : 0f;
        public static float Kmh => inst != null ? inst.kmh : 0f;
        public static float YawRate => inst != null && inst.rb ? inst.rb!.angularVelocity.y : 0f;
        public static int Direction => inst != null ? inst.dir : 0;

        // ---------------- game objects ----------------

        Transform? car;
        Rigidbody? rb;
        MonoBehaviour? input, steerFsm, accelFsm, angleFsm, gameDrift, gameDriftEnter, raceManager;
        MonoBehaviour? bodyTilt, steerWheels, shake;
        FsmF accelVar, brakeVar, steerVar, steerValue, clampMin, clampMax, angleVar, grip, kmhVar;
        FsmB driftingVar;
        object? rmFsm;
        PropertyInfo? rmState;

        // A PlayMaker variable whose Value is read / written through reflection.
        struct FsmF
        {
            readonly object? v; readonly PropertyInfo? p;
            public FsmF(object? o) { v = o; p = o?.GetType().GetProperty("Value"); }
            public float Get() => v != null && p != null ? (float)p.GetValue(v) : 0f;
            public void Set(float x) { if (v != null) p?.SetValue(v, x); }
        }
        struct FsmB
        {
            readonly object? v; readonly PropertyInfo? p;
            public FsmB(object? o) { v = o; p = o?.GetType().GetProperty("Value"); }
            public void Set(bool x) { if (v != null) p?.SetValue(v, x); }
        }

        static object? Var(MonoBehaviour? fsm, string kind, string name)
        {
            if (!fsm) return null;
            var vars = fsm!.GetType().GetProperty("FsmVariables")?.GetValue(fsm);
            return vars?.GetType().GetMethod(kind, new[] { typeof(string) })?.Invoke(vars, new object[] { name });
        }

        bool Resolve()
        {
            if (car && rb && input && raceManager) return true;
            var go = GameObject.Find("race_car_01");
            if (!go) return false;
            car = go.transform;
            rb = go.GetComponent<Rigidbody>();
            input = Showcase.Fsm("race_car_01", "INPUT");
            steerFsm = Showcase.Fsm("race_car_01", "steer");
            accelFsm = Showcase.Fsm("race_car_01", "accelerate brake");
            angleFsm = Showcase.Fsm("race_car_01", "car body drift angle");
            gameDrift = Showcase.Fsm("race_car_01", "drift");
            gameDriftEnter = Showcase.Fsm("race_car_01", "drift enter exit");
            bodyTilt = Showcase.Fsm("car_body", "body tilt");
            steerWheels = Showcase.Fsm("car_body", "steer wheels");
            shake = Showcase.Fsm("car_body", "shake animation");
            raceManager = Showcase.Fsm("GameManager", "race manager");
            rmFsm = raceManager ? raceManager!.GetType().GetProperty("Fsm")?.GetValue(raceManager) : null;
            rmState = rmFsm?.GetType().GetProperty("ActiveStateName", F);
            if (!rb || !input) return false;

            accelVar = new FsmF(Var(input, "GetFsmFloat", "accelerate"));
            brakeVar = new FsmF(Var(input, "GetFsmFloat", "brake"));
            steerVar = new FsmF(Var(input, "GetFsmFloat", "steer raw"));
            steerValue = new FsmF(Var(steerFsm, "GetFsmFloat", "steer_value"));
            clampMin = new FsmF(Var(steerFsm, "GetFsmFloat", "steer_clamp_min"));
            clampMax = new FsmF(Var(steerFsm, "GetFsmFloat", "steer_clamp_max"));
            angleVar = new FsmF(Var(angleFsm, "GetFsmFloat", "drift_angle"));
            driftingVar = new FsmB(Var(accelFsm, "GetFsmBool", "drifting"));
            var globals = Type.GetType("HutongGames.PlayMaker.FsmVariables, PlayMaker")?.GetProperty("GlobalVariables", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            kmhVar = new FsmF(globals?.GetType().GetMethod("GetFsmFloat", new[] { typeof(string) })?.Invoke(globals, new object[] { "90s_kmph_speed" }));
            grip = new FsmF(FindGrip());
            applied = -1;
            drifting = false;
            spinTime = 0f;
            lastBody = 0;
            return true;
        }

        // The "grip" of ApplyCustomGrip (downforce FSM): share of the sideways velocity removed every step.
        static object? FindGrip()
        {
            var down = Showcase.Fsm("race_car_01", "downforce");
            var fsm = down ? down!.GetType().GetProperty("Fsm")?.GetValue(down) : null;
            if (!(fsm?.GetType().GetProperty("States", F)?.GetValue(fsm) is Array states)) return null;
            foreach (var st in states)
                if (st.GetType().GetProperty("Actions", F)?.GetValue(st) is Array acts)
                    foreach (var a in acts)
                        if (a.GetType().Name == "ApplyCustomGrip") return a.GetType().GetField("grip", F)?.GetValue(a);
            return null;
        }

        string State() => rmState?.GetValue(rmFsm) as string ?? "";

        // ---------------- style switch ----------------

        int applied = -1;

        void Update()
        {
            if (!Resolve()) return;
            int style = Settings.DriftStyle;
            if (style == applied) return;
            if (drifting) Exit();
            applied = style;
            bool custom = style != 0;
            // The game's drift FSMs only run in the original style.
            if (gameDrift) gameDrift!.enabled = !custom;
            if (gameDriftEnter) gameDriftEnter!.enabled = !custom;
            if (steerFsm) steerFsm!.enabled = true;
            grip.Set(DefaultGrip);
            if (custom) { steerValue.Set(85f); clampMin.Set(-200f); clampMax.Set(200f); driftingVar.Set(false); angleVar.Set(0f); }
        }

        // ---------------- inputs and gestures ----------------

        float accel, brake, steer, kmh;
        float lastAccel = 1f, liftedAt = -10f, brakeOnAt = -10f, lastBrake;
        float t;   // fixed time

        void ReadInputs()
        {
            if (ForceInput) { accel = ForceAccel; brake = ForceBrake; steer = ForceSteer; }
            else { accel = accelVar.Get(); brake = brakeVar.Get(); steer = steerVar.Get(); }
            kmh = kmhVar.Get();
            if (accel < 0.25f && lastAccel >= 0.25f) liftedAt = t;
            if (brake > 0.3f && lastBrake <= 0.3f) brakeOnAt = t;
            lastAccel = accel; lastBrake = brake;
        }

        // Lift-off then back on the throttle within half a second ("gas flick").
        bool Flick() => accel > 0.7f && t - liftedAt < 0.5f && t - liftedAt > 0.02f;
        // Brake tap: the brake has just been pressed.
        bool BrakeTap() => t - brakeOnAt < 0.25f;

        // ---------------- drift ----------------

        bool drifting;
        int dir;              // +1 right, -1 left
        float driftTime, slip, yaw, lowTime, liftTime, spinTime;

        void FixedUpdate()
        {
            if (!Resolve()) return;
            t += Time.fixedDeltaTime;
            int style = Settings.DriftStyle;
            if (style == 0) return;
            ReadInputs();

            if (Time.timeScale <= 0f || State() != "racing")
            {
                if (drifting) Exit();
                return;
            }

            float dt = Time.fixedDeltaTime;
            var fwd = Vector3.ProjectOnPlane(car!.forward, Vector3.up).normalized;
            var vel = Vector3.ProjectOnPlane(rb!.linearVelocity, Vector3.up);
            // Slip: angle of the nose from the direction of travel (positive = nose to the right).
            slip = vel.magnitude > 3f ? Vector3.SignedAngle(vel, fwd, Vector3.up) : 0f;

            if (spinTime > 0f) { Spin(dt); return; }

            if (!drifting)
            {
                float minKmh = style == 1 ? 120f : style == 2 ? 110f : 130f;
                bool gesture = Flick() || BrakeTap() || (style == 3 && accel < 0.2f && Mathf.Abs(steer) > 0.85f && kmh > 250f);
                if (kmh > minKmh && Mathf.Abs(steer) > 0.45f && gesture) Enter(steer > 0f ? 1 : -1);
                return;
            }

            driftTime += dt;
            if (style == 3) Daytona(dt);
            else Rail(style, dt);
        }

        float savedDamping = -1f;

        // The car's strong angular drag would eat most of the yaw the drift sets: off while sliding.
        void FreeYaw(bool free)
        {
            if (free) { if (savedDamping < 0f) savedDamping = rb!.angularDamping; rb!.angularDamping = 0f; }
            else if (savedDamping >= 0f) { rb!.angularDamping = savedDamping; savedDamping = -1f; }
        }

        void Enter(int d)
        {
            drifting = true;
            FreeYaw(true);
            dir = d;
            driftTime = lowTime = liftTime = 0f;
            yaw = rb!.angularVelocity.y;
            if (steerFsm) steerFsm!.enabled = false;     // the drift drives the yaw
            driftingVar.Set(true);                        // smoke, skid sound, body angle FSM
            BodyEvent(dir);
        }

        void Exit()
        {
            drifting = false;
            if (spinTime <= 0f) FreeYaw(false);
            if (steerFsm) steerFsm!.enabled = true;
            grip.Set(DefaultGrip);
            driftingVar.Set(false);
            angleVar.Set(0f);
            BodyEvent(0);
        }

        // ---- Ridge Racer / OutRun 2: the car follows the line, the body shows the angle ----

        void Rail(int style, float dt)
        {
            bool outrun = style == 2;
            float into = steer * dir;   // 1 = full lock into the turn, -1 = full counter-steer

            // OutRun 2: a firm counter-steer flips the slide to the other side.
            if (outrun && into < -0.55f && driftTime > 0.3f)
            {
                dir = -dir; into = -into; driftTime = 0f;
                BodyEvent(dir);
            }

            // Exits.
            if (Mathf.Abs(steer) < 0.15f || (!outrun && into < -0.3f)) lowTime += dt; else lowTime = 0f;
            if (accel < 0.3f) liftTime += dt; else liftTime = 0f;
            bool exit = kmh < (outrun ? 60f : 70f)
                || lowTime > (outrun ? 0.4f : 0.2f)
                || (!outrun && liftTime > 0.3f);
            if (exit) { Exit(); rb!.angularVelocity = new Vector3(0f, rb.angularVelocity.y * 0.3f, 0f); return; }

            // Yaw rate set by the wheel: tighter into the turn, wider on counter-steer.
            float k = (into + 1f) * 0.5f;
            float target = dir * (outrun ? Mathf.Lerp(0.30f, 2.0f, k) : Mathf.Lerp(0.40f, 1.85f, k));
            yaw = Mathf.Lerp(yaw, target, 1f - Mathf.Exp(-(outrun ? 7f : 10f) * dt));
            rb!.angularVelocity = new Vector3(0f, yaw, 0f);
            grip.Set(DefaultGrip);

            // Speed: almost kept (more lost in OutRun 2 when off the throttle).
            float loss = outrun ? (accel > 0.3f ? 0.015f : 0.06f) : 0.03f;
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * (1f - loss * dt), v.y, v.z * (1f - loss * dt));

            // Body angle shown by the game's "car body drift angle" FSM.
            float angle = outrun ? Mathf.Lerp(30f, 55f, k) : Mathf.Lerp(22f, 48f, k);
            angleVar.Set(dir * angle);
        }

        // ---- Daytona / Scud Race: a real slide ----

        void Daytona(float dt)
        {
            float into = steer * dir;
            float slipDir = slip * dir;          // > 0 when the tail is out on the drift side

            // Tail out under power, tucked in when lifting; counter-steer is the only way to hold the angle.
            float drive = accel > 0.3f ? Mathf.Lerp(0.55f, 1.25f, accel) : -0.6f;
            float target = dir * (drive + Mathf.Max(0f, slipDir - 25f) * 0.02f) + steer * 1.6f;
            yaw = Mathf.Lerp(yaw, target, 1f - Mathf.Exp(-5f * dt));
            rb!.angularVelocity = new Vector3(0f, yaw, 0f);

            // Little sideways grip while the tail is out, more when lifting.
            grip.Set(accel > 0.3f ? 0.03f : 0.10f);

            // Scrubbing speed while sideways.
            float scrub = 0.65f * Mathf.Abs(Mathf.Sin(slip * Mathf.Deg2Rad));
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * (1f - scrub * dt), v.y, v.z * (1f - scrub * dt));
            angleVar.Set(0f);

            if (Mathf.Abs(slip) > 80f) { spinTime = 1.1f; Exit(); return; }   // too much: spin
            if (Mathf.Abs(slip) < 6f && driftTime > 0.35f && Mathf.Abs(into) < 0.5f) lowTime += dt; else lowTime = 0f;
            if (lowTime > 0.15f || kmh < 60f) Exit();
        }

        // Spin-out: the car keeps turning and loses most of its speed, then grips again.
        void Spin(float dt)
        {
            spinTime -= dt;
            if (steerFsm && steerFsm!.enabled) steerFsm.enabled = false;
            grip.Set(0.04f);
            var v = rb!.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * (1f - 1.2f * dt), v.y, v.z * (1f - 1.2f * dt));
            float w = rb.angularVelocity.y;
            rb.angularVelocity = new Vector3(0f, Mathf.Lerp(w, (w >= 0f ? 1f : -1f) * 4f, 3f * dt), 0f);
            if (spinTime <= 0f) { if (steerFsm) steerFsm!.enabled = true; grip.Set(DefaultGrip); rb.angularVelocity = Vector3.zero; FreeYaw(false); }
        }

        int lastBody;

        void BodyEvent(int d)
        {
            if (d == lastBody) return;
            lastBody = d;
            string ev = d < 0 ? "drift left" : d > 0 ? "drift right" : "idle";
            Showcase.Send(bodyTilt, ev);
            Showcase.Send(steerWheels, ev);
            Showcase.Send(shake, ev);
        }
    }
}
