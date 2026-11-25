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
    //  1 = Ridge Racer: off the throttle long enough then a sharp turn of the wheel (or lift, brake tap, floor
    //      it again while turning). The car slides at an angle to its path that the wheel opens or closes; it
    //      only grips again once it is straight: not holding it straight keeps the slide and chains the bends.
    //  2 = OutRun 2: the same slide, easier to start: a sharp turn of the wheel with a brake tap or a short
    //      lift, the throttle can stay down.
    //  3 = Daytona / Scud Race: a feint (wheel one way, then sharply the other) with a brake tap or a lift;
    //      the car finds its grip again more easily (the angle closes by itself when not steering into it).
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
        public static bool Spinning => false;
        public static float SlipAngle => inst != null ? inst.angle : 0f;
        public static float Kmh => inst != null ? inst.kmh : 0f;
        public static float YawRate => inst != null && inst.rb ? inst.rb!.angularVelocity.y : 0f;
        public static int Direction => inst != null ? inst.dir : 0;
        public static string Debug => inst == null || !inst.rb ? "-" :
            $"damping={inst.rb!.angularDamping:F1} steerFsm={(inst.steerFsm ? inst.steerFsm!.enabled : false)} grip={inst.grip.Get():F2} bodyAngle={inst.angleVar.Get():F1} crash={inst.Crashing()} gameDrift={(inst.gameDrift ? inst.gameDrift!.enabled : false)}";

        // ---------------- game objects ----------------

        Transform? car;
        Rigidbody? rb;
        MonoBehaviour? input, steerFsm, accelFsm, angleFsm, gameDrift, gameDriftEnter, raceManager;
        MonoBehaviour? bodyTilt, steerWheels, shake, crashFsm;
        object? crashingVar;
        PropertyInfo? crashingValue;

        // The game's "crash" FSM raises "crashing" while the car touches a wall or another car.
        bool Crashing() => crashingValue != null && crashingValue.GetValue(crashingVar) is bool b && b;
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
            crashFsm = Showcase.Fsm("race_car_01", "crash");
            crashingVar = Var(crashFsm, "GetFsmBool", "crashing");
            crashingValue = crashingVar?.GetType().GetProperty("Value");
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

        float accel, brake, steer, kmh, lastSteer;
        float t;                                   // fixed time
        float liftStart = -10f, liftEnd = -10f;    // throttle released (accel < 0.3) from / until
        bool lifted;
        float brakeAt = -10f, brakeOffAt = -10f;   // last brake press / release
        float flickAt = -10f; int flickDir;        // sharp steering movement ("coup de volant")
        float sideAt0 = -10f, sideAt1 = -10f;      // last time the wheel was turned left / right (feint)
        float punchAt = -10f;                      // throttle back on after a lift with a brake tap (lift, brake, gas)
        float liftedAt = -10f;                     // last time the throttle was released (even very briefly)
        bool punchArmed, wasOn;

        void ReadInputs(float dt)
        {
            if (ForceInput) { accel = ForceAccel; brake = ForceBrake; steer = ForceSteer; }
            else { accel = accelVar.Get(); brake = brakeVar.Get(); steer = steerVar.Get(); }
            kmh = kmhVar.Get();

            bool nowLifted = accel < 0.3f;
            if (nowLifted && !lifted) liftStart = t;
            if (!nowLifted && lifted) liftEnd = t;
            lifted = nowLifted;

            if (brake > 0.3f) brakeAt = t;
            else if (t - brakeAt < dt * 1.5f) brakeOffAt = t;

            // Lift / brake / gas, done as fast as the player likes: the throttle goes back up (past 0.6, no
            // need to floor it at once) after being released, with the brake touched during that short lift.
            if (accel < 0.4f) liftedAt = t;
            if (brake > 0.2f && t - liftedAt < 0.1f) punchArmed = true;
            bool on = accel > 0.6f;
            if (on && !wasOn && punchArmed && t - brakeAt < 0.6f) punchAt = t;
            if (on && !wasOn) punchArmed = false;
            if (t - liftedAt > 1.2f && t - brakeAt > 1.2f) punchArmed = false;
            wasOn = on;

            // A sharp turn of the wheel (fast movement ending well off centre).
            float rate = (steer - lastSteer) / Mathf.Max(dt, 1e-4f);
            if (Mathf.Abs(rate) > 3.5f && Mathf.Abs(steer) > 0.55f && Mathf.Sign(rate) == Mathf.Sign(steer)) { flickAt = t; flickDir = steer > 0f ? 1 : -1; }
            if (steer < -0.4f) sideAt0 = t;
            if (steer > 0.4f) sideAt1 = t;
            lastSteer = steer;
        }

        float LiftedFor => lifted ? t - liftStart : 0f;
        float LastLift => lifted ? t - liftStart : liftEnd - liftStart;

        // Gesture that starts a slide for each game, or 0. Returns the side (+1 right, -1 left).
        int Gesture(int style)
        {
            bool flick = t - flickAt < 0.12f;
            switch (style)
            {
                case 1: // Ridge Racer
                    // Off the throttle long enough, then a sharp turn of the wheel.
                    if (flick && (LiftedFor > 0.35f || (t - liftEnd < 0.25f && LastLift > 0.35f))) return flickDir;
                    // Off the throttle, a brake tap, then floored again while turning.
                    // The wheel may already be turned or come just after.
                    if (t - punchAt < 0.4f && Mathf.Abs(steer) > 0.35f) { punchAt = -10f; return steer > 0f ? 1 : -1; }
                    return 0;
                case 2: // OutRun 2: easier, the throttle can stay on
                    if (flick && (t - brakeAt < 0.5f || (LastLift > 0.08f && t - liftStart < 0.6f))) return flickDir;
                    if (t - punchAt < 0.4f && Mathf.Abs(steer) > 0.35f) { punchAt = -10f; return steer > 0f ? 1 : -1; }
                    return 0;
                default: // Daytona / Scud Race: feint (turn one way, then sharply the other) plus brake or lift
                    if (!flick) return 0;
                    float other = flickDir > 0 ? sideAt0 : sideAt1;
                    bool feint = t - other < 0.5f;
                    bool cut = t - brakeAt < 0.8f || (LastLift > 0.1f && t - liftStart < 0.9f);
                    return feint && cut ? flickDir : 0;
            }
        }

        // ---------------- drift ----------------
        // The slide is an angle of the car to its path (positive = nose to the right). The wheel opens or
        // closes it; with the wheel centred it stays. The car only grips again once it is straight
        // (angle back to ~0 and held there a moment): swinging it through straight to the other side keeps
        // the slide going, which chains the bends. The bigger the angle, the tighter the car turns.

        bool drifting;
        int dir;              // side of the slide: +1 right, -1 left
        float angle;          // degrees
        float driftTime, slip, straightTime;
        float exitedAt = -10f;

        // Per game: entry angle, steering rate (deg/s at full lock), self-straightening (deg/s),
        // turn rate at 45 degrees (rad/s), speed loss (share per second at 90 degrees), minimum speed.
        static readonly float[] EntryAngle = { 0f, 24f, 28f, 20f };
        static readonly float[] SteerRate = { 0f, 85f, 100f, 75f };
        static readonly float[] Straighten = { 0f, 0f, 2f, 18f };
        static readonly float[] TurnAt45 = { 0f, 1.35f, 1.45f, 1.15f };
        static readonly float[] Loss = { 0f, 0.10f, 0.05f, 0.30f };
        static readonly float[] MinKmh = { 0f, 100f, 90f, 110f };

        void FixedUpdate()
        {
            if (!Resolve()) return;
            float dt = Time.fixedDeltaTime;
            t += dt;
            int style = Settings.DriftStyle;
            if (style == 0) return;
            ReadInputs(dt);

            if (Time.timeScale <= 0f || State() != "racing")
            {
                if (drifting) Exit();
                angleVar.Set(0f);
                return;
            }

            var fwd = Vector3.ProjectOnPlane(car!.forward, Vector3.up).normalized;
            var vel = Vector3.ProjectOnPlane(rb!.linearVelocity, Vector3.up);
            slip = vel.magnitude > 3f ? Vector3.SignedAngle(vel, fwd, Vector3.up) : 0f;

            if (!drifting)
            {
                // The body angle FSM never brings its angle back to 0 by itself (the game's own drift FSM does
                // it every frame): without this the body stays turned after a slide, as if still sliding.
                angleVar.Set(0f);
                int side = Gesture(style);
                if (side != 0 && kmh > MinKmh[style] && t - exitedAt > 0.4f) Enter(side, EntryAngle[style]);
                return;
            }

            driftTime += dt;
            // A hit (wall, car): the slide is over. The game's crash FSM tells it, a sudden loss of speed too.
            if (Crashing() || (driftTime > 0.15f && kmh < kmhBefore * 0.75f)) { Exit(); kmhBefore = kmh; return; }
            kmhBefore = Mathf.Lerp(kmhBefore, kmh, 1f - Mathf.Exp(-dt / 0.15f));
            Slide(style, dt);
        }

        float savedDamping = -1f;

        // The car's strong angular drag would eat the rotation the slide sets: off while sliding.
        void FreeYaw(bool free)
        {
            if (free) { if (savedDamping < 0f) savedDamping = rb!.angularDamping; rb!.angularDamping = 0f; }
            else if (savedDamping >= 0f) { rb!.angularDamping = savedDamping; savedDamping = -1f; }
        }

        float kmhBefore;

        void Enter(int side, float startAngle)
        {
            drifting = true;
            kmhBefore = kmh;
            dir = side;
            angle = side * startAngle;
            driftTime = straightTime = 0f;
            FreeYaw(true);
            if (steerFsm) steerFsm!.enabled = false;     // the slide drives the rotation
            driftingVar.Set(true);                        // smoke, skid sound, body angle FSM
            BodyEvent(dir);
        }

        void Exit()
        {
            drifting = false;
            exitedAt = t;
            angle = 0f;
            // Hand the car back turning as the normal steering would, without a jolt.
            rb!.angularVelocity = new Vector3(0f, steer * 0.8f, 0f);
            FreeYaw(false);
            if (steerFsm) steerFsm!.enabled = true;
            grip.Set(DefaultGrip);
            driftingVar.Set(false);
            angleVar.Set(0f);
            BodyEvent(0);
        }

        void Slide(int style, float dt)
        {
            // The wheel turns the car's angle; Daytona's tyres also pull it back straight on their own.
            angle += steer * SteerRate[style] * dt;
            bool notInto = Mathf.Abs(steer) < 0.2f || Mathf.Sign(steer) != Mathf.Sign(angle);
            if (notInto) angle = Mathf.MoveTowards(angle, 0f, Straighten[style] * dt);
            angle = Mathf.Clamp(angle, -55f, 55f);

            // Straight again: grip back after a moment. Swung through to the other side: the slide goes on.
            if (Mathf.Abs(angle) < 4f) straightTime += dt; else straightTime = 0f;
            if (straightTime > (style == 3 ? 0.08f : 0.18f) || kmh < 60f) { Exit(); return; }
            int side = angle > 0f ? 1 : angle < 0f ? -1 : dir;
            if (side != dir && Mathf.Abs(angle) > 4f) { dir = side; BodyEvent(dir); }

            // The car turns with its angle (a little with the wheel too) and keeps to its line.
            float turn = angle / 45f * TurnAt45[style] + steer * 0.2f;
            rb!.angularVelocity = new Vector3(0f, turn, 0f);
            grip.Set(DefaultGrip);

            // Speed scrubbed with the angle.
            float loss = Loss[style] * Mathf.Abs(Mathf.Sin(angle * Mathf.Deg2Rad));
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * (1f - loss * dt), v.y, v.z * (1f - loss * dt));

            // The body shows the angle (the game's "car body drift angle" FSM).
            angleVar.Set(angle);
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

        public static float Angle => inst != null ? inst.angle : 0f;
    }
}
