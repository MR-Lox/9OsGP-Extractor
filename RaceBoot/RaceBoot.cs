using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // Boots the hidden "Bonus_9OsGP" racing game directly and keeps the player inside it.
    public static class Boot
    {
        const int RaceScene = 27;        // Assets/TAKEOVER/scenes/Bonus_9OsGP.unity
        const int IntroScene = 0;
        const int TransitionScene = 1;   // T2_transition_scene (loading screen)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OptionsMenu.Create();
            Debug.Log("[RaceBoot] installed");
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log("[RaceBoot] loaded " + scene.buildIndex + " " + scene.name + " " + mode);
            int i = scene.buildIndex;
            if (i == RaceScene) { SceneDumper.Run(); Showcase.Run(); return; }
            if (mode == LoadSceneMode.Additive) return;
            if (i == IntroScene && !RaceBootConfig.SkipIntro) return;
            // Intro (Takeover 2 logos) and the end-of-race loading screen are skipped as well:
            // let them run a couple of frames so their global managers initialise, then jump to the race.
            int frames = i == IntroScene || i == TransitionScene ? 3 : 0;
            Redirect(frames);
        }

        static bool redirecting;

        static void Redirect(int frames)
        {
            if (redirecting) return;
            if (frames <= 0 || !OptionsMenu.Instance) { Load(); return; }
            redirecting = true;
            OptionsMenu.Instance!.StartCoroutine(Delayed(frames));
        }

        static System.Collections.IEnumerator Delayed(int frames)
        {
            for (int f = 0; f < frames; f++) yield return null;
            redirecting = false;
            if (SceneManager.GetActiveScene().buildIndex != RaceScene) Load();
        }

        static void Load()
        {
            Debug.Log("[RaceBoot] redirecting to race");
            SceneManager.LoadScene(RaceScene, LoadSceneMode.Single);
        }
    }

    static class RaceBootConfig
    {
        public static bool SkipIntro = true;
    }
}
