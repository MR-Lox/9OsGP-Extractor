using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaceBoot
{
    // Tweaks the race's pause menu (90sGP_canvas/PAUSE):
    //  - removes the OPTIONS entry (Takeover 2's options screen is not useful here: F1 replaces it),
    //  - makes the black background see-through so the paused race stays visible.
    // Menu layout is horizontal: OPTIONS | CONTINUE | QUIT, driven by the PlayMaker FSM "continue".
    public static class PauseMenu
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static RectTransform? root;
        static Image? background;
        static object? fsm;            // HutongGames.PlayMaker.Fsm
        static PropertyInfo? activeStateName;
        static MethodInfo? setState;
        static string lastItem = "continue";
        static bool hooked;

        public static void Tick()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => root = null; }
            if (!Resolve()) return;

            // See-through background: the paused race stays visible behind the menu.
            if (background && background!.color.a != 0f) { var c = background.color; c.a = 0f; background.color = c; }

            // Safety net in case the transitions could not be re-targeted: never stay on OPTIONS.
            if (fsm != null && activeStateName != null && setState != null && root!.gameObject.activeInHierarchy)
            {
                var st = activeStateName.GetValue(fsm) as string;
                if (st == "continue" || st == "quit") lastItem = st;
                else if (st == "options" || st == "go_options")
                    setState.Invoke(fsm, new object[] { lastItem == "continue" ? "quit" : "continue" });
            }
        }

        static bool Resolve()
        {
            if (root) return true;
            fsm = null; background = null;
            foreach (var t in Resources.FindObjectsOfTypeAll<RectTransform>())
                if (t.name == "PAUSE" && t.parent && t.parent.name == "90sGP_canvas" && t.gameObject.scene.isLoaded) { root = t; break; }
            if (!root) return false;

            background = root!.GetComponent<Image>();

            var options = root.Find("OPTIONS") as RectTransform;
            var cont = root.Find("CONTINUE") as RectTransform;
            var quit = root.Find("QUIT") as RectTransform;
            if (options)
            {
                options!.gameObject.SetActive(false);
                // Close the gap: centre the two remaining entries where the three used to be.
                if (cont && quit)
                {
                    // Keep the original spacing, just slide both entries by half a slot to the left.
                    float shift = (cont!.anchoredPosition.x - options.anchoredPosition.x) / 2f;
                    cont.anchoredPosition -= new Vector2(shift, 0f);
                    quit!.anchoredPosition -= new Vector2(shift, 0f);
                }
            }

            foreach (var mb in root.GetComponents<MonoBehaviour>())
            {
                if (!mb || mb.GetType().Name != "PlayMakerFSM") continue;
                if (mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string != "continue") continue;
                fsm = mb.GetType().GetProperty("Fsm")?.GetValue(mb);
                if (fsm == null) break;
                activeStateName = fsm.GetType().GetProperty("ActiveStateName", F);
                setState = fsm.GetType().GetMethod("SetState", F, null, new[] { typeof(string) }, null);
                Retarget("continue", "quit");   // CONTINUE + left  -> QUIT (wrap)
                Retarget("quit", "continue");   // QUIT + right     -> CONTINUE (wrap)
                break;
            }
            Debug.Log("[RaceBoot] pause menu patched (options hidden, fsm " + (fsm != null) + ")");
            return true;
        }

        // In state `from`, the transition fired by the "options" event now goes to `to`.
        static void Retarget(string from, string to)
        {
            try
            {
                if (!(fsm!.GetType().GetProperty("States", F)?.GetValue(fsm) is System.Array states)) return;
                object? fromState = null, toState = null;
                foreach (var s in states)
                {
                    var n = s.GetType().GetProperty("Name", F)?.GetValue(s) as string;
                    if (n == from) fromState = s;
                    if (n == to) toState = s;
                }
                if (fromState == null || toState == null) return;
                if (!(fromState.GetType().GetProperty("Transitions", F)?.GetValue(fromState) is System.Array transitions)) return;
                foreach (var tr in transitions)
                {
                    if (tr.GetType().GetProperty("EventName", F)?.GetValue(tr) as string != "options") continue;
                    tr.GetType().GetProperty("ToState", F)?.SetValue(tr, to);
                    tr.GetType().GetProperty("ToFsmState", F)?.SetValue(tr, toState);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[RaceBoot] pause retarget failed: " + e.Message); }
        }

        // Debug helper (used by the diagnostic dump): opens the pause menu like the Start button.
        public static void OpenForTest()
        {
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if (mb && mb.gameObject.name == "GameManager" && mb.GetType().Name == "PlayMakerFSM"
                    && mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string == "pause")
                { mb.enabled = true; mb.GetType().GetMethod("SendEvent", new[] { typeof(string) })?.Invoke(mb, new object[] { "pause" }); }
        }
    }
}
