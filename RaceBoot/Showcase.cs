using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RaceBoot
{
    // Maintenance tool: takes the README screenshots automatically.
    // Enabled only when "showcase.txt" exists next to the executable. Starts the race, drives straight
    // for a few seconds, freezes the game, then captures each camera view at a different resolution,
    // one shot without HUD and one of the pause menu. Files go to "showcase/". The game quits afterwards.
    public class Showcase : MonoBehaviour
    {
        static string Dir => Path.Combine(Application.dataPath, "..");

        public static void Run()
        {
            if (!File.Exists(Path.Combine(Dir, "showcase.txt")) || FindObjectOfType<Showcase>()) return;
            var go = new GameObject("RaceBootShowcase");
            DontDestroyOnLoad(go);
            go.AddComponent<Showcase>();
        }

        internal static MonoBehaviour? Fsm(string owner, string name)
        {
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if (mb && mb.gameObject.name == owner && mb.GetType().Name == "PlayMakerFSM"
                    && mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string == name && mb.gameObject.scene.isLoaded)
                    return mb;
            return null;
        }

        internal static void Send(MonoBehaviour? fsm, string ev) => fsm?.GetType().GetMethod("SendEvent", new[] { typeof(string) })?.Invoke(fsm, new object[] { ev });

        internal static void SetFloat(MonoBehaviour fsm, string name, float v)
        {
            var vars = fsm.GetType().GetProperty("FsmVariables")?.GetValue(fsm);
            var f = vars?.GetType().GetMethod("GetFsmFloat", new[] { typeof(string) })?.Invoke(vars, new object[] { name });
            f?.GetType().GetProperty("Value")?.SetValue(f, v);
        }

        IEnumerator Start()
        {
            var outDir = Path.Combine(Dir, "showcase");
            Directory.CreateDirectory(outDir);
            yield return new WaitForSecondsRealtime(4f);

            // Leave the attract screen ("PRESS START").
            Send(Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(4.5f);   // countdown 3-2-1-GO

            // Drive straight at full throttle: the INPUT FSM is paused and fed directly.
            var input = Fsm("race_car_01", "INPUT");
            float until = Time.realtimeSinceStartup + 3.2f;
            if (input) input!.enabled = false;
            while (Time.realtimeSinceStartup < until)
            {
                if (input) { SetFloat(input!, "accelerate", 1f); SetFloat(input!, "brake", 0f); SetFloat(input!, "steer raw", 0f); }
                yield return null;
            }
            Time.timeScale = 0f;

            int view = Settings.CameraView;
            var hidden = new HashSet<string>(Settings.HudHidden);
            var shots = new (string file, int view, int w, int h, bool hud)[]
            {
                ("1_exterieur_1920x1080.png", CameraViews.Chase,  1920, 1080, true),
                ("2_interieur_2560x1080.png", CameraViews.Bumper, 2560, 1080, true),
                ("3_capot_3440x1080.png", CameraViews.Hood,   3440, 1080, true),
                ("4_sans_hud.png",       CameraViews.Chase,  1920, 1080, false),
            };
            foreach (var s in shots)
            {
                Settings.CameraView = s.view;
                Settings.HudHidden = s.hud ? new HashSet<string>(hidden) : new HashSet<string> { "Gauge", "Timer", "Position", "Laps", "LapTimes", "Messages", "Minimap" };
                Screen.SetResolution(s.w, s.h, FullScreenMode.Windowed);
                for (int i = 0; i < 20; i++) yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, s.file));
                for (int i = 0; i < 5; i++) yield return null;
                Debug.Log($"[RaceBoot] showcase {s.file} at {Screen.width}x{Screen.height}");
            }

            // Pause menu check.
            Settings.CameraView = view;
            Settings.HudHidden = hidden;
            Time.timeScale = 1f;
            var pause = Fsm("GameManager", "pause");
            if (pause) { pause!.enabled = true; Send(pause, "pause"); }
            for (int i = 0; i < 30; i++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "5_pause.png"));
            for (int i = 0; i < 5; i++) yield return null;
            Debug.Log("[RaceBoot] showcase done");
            Application.Quit();
        }
    }
}
