using System.Collections.Generic;
using Rewired;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // F8: free camera, flies anywhere on the map without collisions.
    // Mouse: look around · WASD (ZQSD on AZERTY) / arrows: move · Space / E: up · Ctrl / Q (A on AZERTY): down
    // Shift: fast · Alt: slow · mouse wheel: base speed. The race is frozen while it is on.
    public class FreeCam : MonoBehaviour
    {
        public const string CameraName = "RaceBootFreeCam";
        public static bool Active => inst != null && inst.cam != null;
        public static void ToggleForTest() => inst?.Toggle();   // test bench only
        public static void MoveForTest(Vector3 d, float yawDeg) { if (inst != null && inst.cam) { inst.yaw += yawDeg; inst.cam!.transform.position += d; inst.cam.transform.rotation = Quaternion.Euler(inst.pitch, inst.yaw, 0f); } }

        static FreeCam? inst;
        Camera? cam;
        float yaw, pitch, speed = 20f, savedTimeScale = 1f;
        bool froze;
        CursorLockMode savedLock;
        bool savedVisible;
        readonly HashSet<Camera> muted = new HashSet<Camera>();
        readonly List<Canvas> hiddenHud = new List<Canvas>();

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootFreeCamHost");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<FreeCam>();
            SceneManager.sceneLoaded += (s, m) => { if (inst && inst!.cam) inst.Toggle(); };
        }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F8) { Toggle(); e.Use(); }
        }

        void Toggle()
        {
            if (cam) { Stop(); return; }
            Camera? from = Camera.main;
            foreach (var c in Camera.allCameras)
                if (c.enabled && c.targetTexture == null && (!from || c.depth > from!.depth)) from = c;
            var go = new GameObject(CameraName);
            DontDestroyOnLoad(go);
            cam = go.AddComponent<Camera>();
            if (from)
            {
                cam.CopyFrom(from);
                Replay.CopyUrpData(from!, cam);
                go.transform.SetPositionAndRotation(from!.transform.position, from.transform.rotation);
            }
            cam.depth = 100;
            cam.targetTexture = null;
            cam.nearClipPlane = 0.05f;
            var ang = go.transform.eulerAngles;
            yaw = ang.y; pitch = ang.x > 180f ? ang.x - 360f : ang.x;

            // Freeze the race (not the title screen and its replay, which run on unscaled time anyway).
            froze = Time.timeScale > 0f;
            savedTimeScale = Time.timeScale;
            if (froze) Time.timeScale = 0f;

            // No race HUD while flying around.
            foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>())
                if (cv.name == "90sGP_canvas" && cv.gameObject.scene.isLoaded && cv.enabled) { cv.enabled = false; hiddenHud.Add(cv); }

            savedLock = Cursor.lockState; savedVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void Stop()
        {
            if (cam) Destroy(cam!.gameObject);
            cam = null;
            foreach (var c in muted) if (c) c.enabled = true;
            muted.Clear();
            foreach (var cv in hiddenHud) if (cv) cv.enabled = true;
            hiddenHud.Clear();
            if (froze && Time.timeScale == 0f) Time.timeScale = savedTimeScale;
            froze = false;
            Cursor.lockState = savedLock; Cursor.visible = savedVisible;
        }

        void LateUpdate()
        {
            if (!cam) return;
            // Only this camera renders.
            foreach (var c in Camera.allCameras)
                if (c != cam && c.targetTexture == null) { c.enabled = false; muted.Add(c); }
            if (OptionsMenu.IsOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; return; }
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            if (!ReInput.isReady) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var kb = ReInput.controllers.Keyboard;
            var mouse = ReInput.controllers.Mouse;

            yaw += mouse.GetAxisRaw(0) * 0.12f;
            pitch = Mathf.Clamp(pitch - mouse.GetAxisRaw(1) * 0.12f, -89f, 89f);
            float wheel = mouse.GetAxisRaw(2);
            if (wheel != 0f) speed = Mathf.Clamp(speed * (wheel > 0f ? 1.25f : 0.8f), 1f, 400f);

            // Gamepad: left stick moves, right stick looks, triggers go up / down.
            Vector3 move = Vector3.zero;
            float look = 0f, lookY = 0f;
            foreach (var j in ReInput.controllers.Joysticks)
            {
                var g = j.GetTemplate<IGamepadTemplate>();   // gamepads only (not a wheel and its pedals)
                if (g == null) continue;
                move.x += Dz(g.leftStick.horizontal.value); move.z += Dz(g.leftStick.vertical.value);
                look += Dz(g.rightStick.horizontal.value); lookY -= Dz(g.rightStick.vertical.value);
                move.y += g.rightTrigger.value - g.leftTrigger.value;
            }
            yaw += look * 120f * dt;
            pitch = Mathf.Clamp(pitch - lookY * 90f * dt, -89f, 89f);

            // Physical keys: WASD is ZQSD on an AZERTY keyboard.
            if (kb.GetKey(KeyCode.W) || kb.GetKey(KeyCode.UpArrow)) move.z += 1f;
            if (kb.GetKey(KeyCode.S) || kb.GetKey(KeyCode.DownArrow)) move.z -= 1f;
            if (kb.GetKey(KeyCode.D) || kb.GetKey(KeyCode.RightArrow)) move.x += 1f;
            if (kb.GetKey(KeyCode.A) || kb.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
            if (kb.GetKey(KeyCode.Space) || kb.GetKey(KeyCode.E)) move.y += 1f;
            if (kb.GetKey(KeyCode.LeftControl) || kb.GetKey(KeyCode.RightControl) || kb.GetKey(KeyCode.Q)) move.y -= 1f;

            float mult = kb.GetKey(KeyCode.LeftShift) || kb.GetKey(KeyCode.RightShift) ? 4f : kb.GetKey(KeyCode.LeftAlt) ? 0.25f : 1f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var t = cam!.transform;
            var dir = rot * new Vector3(move.x, 0f, move.z) + Vector3.up * move.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            t.SetPositionAndRotation(t.position + dir * speed * mult * dt, rot);
        }

        static float Dz(float v) => Mathf.Abs(v) < 0.15f ? 0f : v;
    }
}
