using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // Development test bench, enabled by "testbench.txt" next to the executable (first line = mode).
    //  racedump : start a race, drive a little, dump the live scene + race manager transitions.
    public class TestBench : MonoBehaviour
    {
        static string Dir => Path.Combine(Application.dataPath, "..");
        string mode = "";

        public static void Run()
        {
            var f = Path.Combine(Dir, "testbench.txt");
            if (!File.Exists(f) || FindObjectOfType<TestBench>()) return;
            var go = new GameObject("RaceBootTestBench");
            DontDestroyOnLoad(go);
            var tb = go.AddComponent<TestBench>();
            tb.mode = File.ReadAllLines(f).Length > 0 ? File.ReadAllLines(f)[0].Trim() : "";
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(4f);
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(mode == "replay" ? 0.5f : 4.5f);
            var input = Showcase.Fsm("race_car_01", "INPUT");
            if (input) input!.enabled = false;
            float drive = mode == "replay" ? 45f : 4f;
            float until = Time.realtimeSinceStartup + drive;
            var car = GameObject.Find("race_car_01");
            while (Time.realtimeSinceStartup < until)
            {
                if (input && car)
                {
                    Autopilot(car!.transform, out float steer, out float throttle);
                    Showcase.SetFloat(input!, "accelerate", throttle); Showcase.SetFloat(input!, "brake", 0f); Showcase.SetFloat(input!, "steer raw", steer);
                }
                yield return null;
            }
            if (mode == "racedump") { RaceDump(); Application.Quit(); yield break; }
            if (mode == "replay") { yield return ReplayTest(); }
            Application.Quit();
        }

        // Follows the nearest AI car ahead (they drive the racing line).
        static Rigidbody[]? ai;
        static void Autopilot(Transform car, out float steer, out float throttle)
        {
            if (ai == null || Time.frameCount % 60 == 0)
                ai = Array.FindAll(FindObjectsOfType<Rigidbody>(), r => r.name.StartsWith("AI_car"));
            Vector3 p = car.position, f = car.forward;
            Transform? best = null; float bestD = float.MaxValue;
            foreach (var r in ai)
            {
                if (!r) continue;
                var d = r.position - p;
                float dist = d.magnitude;
                if (dist < 12f || dist > 120f) continue;
                if (Vector3.Dot(d / dist, f) < 0.5f) continue;
                if (dist < bestD) { bestD = dist; best = r.transform; }
            }
            if (!best) { steer = 0f; throttle = 1f; return; }
            float angle = Vector3.SignedAngle(f, best!.position - p, Vector3.up);
            steer = Mathf.Clamp(angle / 25f, -1f, 1f);
            throttle = Mathf.Abs(angle) > 35f ? 0.4f : 1f;
        }

        IEnumerator ReplayTest()
        {
            // Force the end of the race: the game goes through its normal end sequence and reloads the title screen.
            var rm = Showcase.Fsm("GameManager", "race manager");
            var fsm = rm?.GetType().GetProperty("Fsm")?.GetValue(rm);
            fsm?.GetType().GetMethod("SetState", new[] { typeof(string) })?.Invoke(fsm, new object[] { "finished!" });
            float wait = Time.realtimeSinceStartup + 40f;
            while (!Replay.Playing && Time.realtimeSinceStartup < wait) yield return null;
            Debug.Log("[RaceBoot] testbench: replay playing = " + Replay.Playing);
            var outDir = Path.Combine(Dir, "replaytest");
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < 24; i++)
            {
                if (i == 12) Replay.DebugForceShot = "Hood";
                yield return new WaitForSecondsRealtime(2.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"r{i:00}.png"));
            }
            Replay.DebugForceShot = null;

            // Start a new race from the replay: it must stop and give the normal race back.
            Showcase.Send(Showcase.Fsm("GameManager", "race manager"), "FINISHED");
            yield return new WaitForSecondsRealtime(6f);
            Debug.Log("[RaceBoot] testbench: after new race, replay playing = " + Replay.Playing);
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "z_newrace.png"));
            yield return new WaitForSecondsRealtime(1f);
        }

        static void RaceDump()
        {
            var sb = new StringBuilder();
            for (int s = 0; s < SceneManager.sceneCount; s++)
                foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects())
                    if (root.activeInHierarchy) Walk(root.transform, 0, sb);
            sb.AppendLine("=== DDOL ===");
            var probe = new GameObject("probe"); DontDestroyOnLoad(probe);
            foreach (var root in probe.scene.GetRootGameObjects()) Walk(root.transform, 0, sb);
            Destroy(probe);
            sb.AppendLine("=== ACTIVE RIGIDBODIES ===");
            Transform? firstAi = null;
            foreach (var rb in FindObjectsOfType<Rigidbody>())
            {
                sb.AppendLine($" {PathOf(rb.transform)} kin={rb.isKinematic} pos={rb.position:F1} v={rb.linearVelocity.magnitude:F1}");
                if (!firstAi && rb.name.StartsWith("AI_car")) firstAi = rb.transform;
            }
            if (firstAi)
            {
                var top = firstAi!;

                sb.AppendLine("=== FIRST POOLED CAR ===");
                Walk(top, 0, sb);
            }
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            sb.AppendLine("=== GLOBAL INT/FLOAT VARIABLES ===");
            var fsmVarsType = Type.GetType("HutongGames.PlayMaker.FsmVariables, PlayMaker");
            var globals = fsmVarsType?.GetProperty("GlobalVariables", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            foreach (var listName in new[] { "IntVariables", "FloatVariables", "BoolVariables" })
                if (globals?.GetType().GetProperty(listName)?.GetValue(globals) is Array arr)
                    foreach (var v in arr) sb.AppendLine($" {listName} {v.GetType().GetProperty("Name")?.GetValue(v)} = {v.GetType().GetProperty("Value")?.GetValue(v)}");
            foreach (var name in new[] { "race manager", "timer countdown" })
            {
            sb.AppendLine("=== TRANSITIONS " + name + " ===");
            var fsmComp = Showcase.Fsm("GameManager", name);
            var fsm = fsmComp?.GetType().GetProperty("Fsm")?.GetValue(fsmComp);
            if (fsm != null)
            {
                sb.AppendLine("active: " + fsm.GetType().GetProperty("ActiveStateName", F)?.GetValue(fsm));
                foreach (var gt in (Array)fsm.GetType().GetProperty("GlobalTransitions", F)!.GetValue(fsm))
                    sb.AppendLine("  GLOBAL " + gt.GetType().GetProperty("EventName", F)?.GetValue(gt) + " -> " + gt.GetType().GetProperty("ToState", F)?.GetValue(gt));
                foreach (var st in (Array)fsm.GetType().GetProperty("States", F)!.GetValue(fsm))
                {
                    sb.Append(" " + st.GetType().GetProperty("Name", F)?.GetValue(st) + ":");
                    foreach (var tr in (Array)st.GetType().GetProperty("Transitions", F)!.GetValue(st))
                        sb.Append("  " + tr.GetType().GetProperty("EventName", F)?.GetValue(tr) + "->" + tr.GetType().GetProperty("ToState", F)?.GetValue(tr));
                    sb.AppendLine();
                }
            }
            }
            File.WriteAllText(Path.Combine(Dir, "racedump.txt"), sb.ToString());
        }

        internal static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 7) return;
            sb.Append(' ', depth * 2).Append(t.gameObject.activeSelf ? "+ " : "- ").Append(t.name);
            sb.Append(" @").Append(t.position.ToString("F1")).Append(" [");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                var n = c.GetType().Name;
                if (n == "PlayMakerFSM") n += ":" + c.GetType().GetProperty("FsmName")?.GetValue(c);
                if (c is Rigidbody rb) n += rb.isKinematic ? "(kin)" : "";
                sb.Append(n).Append(',');
            }
            sb.AppendLine("]");
            // Skip the static track geometry.
            if (t.childCount > 60) { sb.Append(' ', depth * 2 + 2).AppendLine("… " + t.childCount + " children"); return; }
            foreach (Transform ch in t) Walk(ch, depth + 1, sb);
        }
    }
}
