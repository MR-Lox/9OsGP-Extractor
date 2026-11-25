using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RaceBoot
{
    // Audio options: master / music / engines / effects volumes, and the soundtrack.
    //  - Volumes: the game keeps setting the volume of its sources; whatever it writes becomes the base, and
    //    the chosen level is applied on top (same idea as the HUD).
    //  - Soundtrack: the original race music, or the player's own files (mp3 / ogg / wav) from the "Music"
    //    folder next to the executable, one of them or all at random. The game's music FSM is paused while
    //    another track plays.
    // Runs after every other script of the frame: the game writes the volume of some sources late in the
    // frame (and every frame); whatever runs last decides what the audio mixes for that frame.
    [DefaultExecutionOrder(32000)]
    public class GameAudio : MonoBehaviour
    {
        public const string Original = "", Random = "*random";
        static GameAudio? inst;

        public static void Install()
        {
            if (inst) return;
            var go = new GameObject("RaceBootAudio");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<GameAudio>();
            HookPlayerLoop();
        }

        // The volumes are applied right before Unity mixes the audio of the frame (PostLateUpdate.UpdateAudio):
        // some game scripts write the volume of their sources after every LateUpdate, which let a new sound
        // start at its full volume before being corrected.
        static void HookPlayerLoop()
        {
            var root = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var systems = root.subSystemList;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var list = new List<UnityEngine.LowLevel.PlayerLoopSystem>(systems[i].subSystemList);
                int at = list.FindIndex(x => x.type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAudio));
                if (at < 0) return;
                list.Insert(at + 1, new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(AfterAudio), updateDelegate = () => { if (inst) inst!.ProbeAfter(); } });
                list.Insert(at, new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(GameAudio), updateDelegate = () => { if (inst) inst!.Apply(); } });
                systems[i].subSystemList = list.ToArray();
                root.subSystemList = systems;
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(root);
                hooked = true;
                return;
            }
        }

        struct AfterAudio { }
        static bool hooked;

        // Test bench: volume of one source as the audio system used it.
        public static AudioSource? Watch;
        public static float WatchedVolume = -1f;
        void ProbeAfter() { if (Watch) WatchedVolume = Watch!.volume; }

        // ---------------- volumes ----------------

        class Vol { public float Base, Written, ReadBack; public int Frame; public Gain? Gain; }
        readonly Dictionary<AudioSource, Vol> vols = new Dictionary<AudioSource, Vol>();
        readonly List<AudioSource> sources = new List<AudioSource>();
        float nextScan;

        // Every source of the scenes, inactive ones included: the pooled sounds (crashes, skids…) are
        // known before they play, so their volume is set in the very frame they start. Scanning only the
        // active sources found them up to half a second later, at full volume meanwhile.
        void Scan()
        {
            sources.Clear();
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (s && s.gameObject.scene.IsValid()) sources.Add(s);
            if (vols.Count > 2000) foreach (var k in vols.Keys.Where(k => !k).ToList()) vols.Remove(k);
        }

        public static string Probe(AudioSource s) => inst == null ? "no instance" :
            $"listed={inst.sources.Contains(s)} count={inst.sources.Count} known={inst.vols.ContainsKey(s)} base={(inst.vols.TryGetValue(s, out var v) ? v.Base : -1f)} written={(inst.vols.TryGetValue(s, out var w) ? w.Written : -1f)} readback={(inst.vols.TryGetValue(s, out var x) ? x.ReadBack + "@" + x.Frame : "-")} now={s.volume} frame={Time.frameCount} last={inst.lastFrame}";
        int lastFrame;

        void LateUpdate() { if (!hooked) Apply(); }

        void Apply()
        {
            lastFrame = Time.frameCount;
            AudioListener.volume = 1f;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                Scan();
                FindMusic();
            }
            foreach (var src in sources)
            {
                if (!src) continue;
                float mult = Settings.VolMaster * (src == music ? Settings.VolMusic : IsEngine(src) ? Settings.VolEngine : Settings.VolFx);
                if (!vols.TryGetValue(src, out var v)) { v = new Vol { Base = src.volume, Written = -1f }; vols[src] = v; }
                else if (Mathf.Abs(src.volume - v.Written) > 0.0001f) v.Base = src.volume;   // written by the game
                // Above 100 %: the source is at full volume and a gain filter amplifies its signal.
                float want = v.Base * mult;
                float vol = Mathf.Min(want, 1f);
                float gain = want > 1f && vol > 0f ? want / vol : 1f;
                if (gain > 1.001f && !v.Gain) v.Gain = src.GetComponent<Gain>() ?? src.gameObject.AddComponent<Gain>();
                if (v.Gain) v.Gain!.Factor = gain;
                v.Written = vol;
                if (src.volume != vol) src.volume = vol;
                v.ReadBack = src.volume; v.Frame = Time.frameCount;
            }
            Soundtrack();
        }

        static bool IsEngine(AudioSource s) => s.outputAudioMixerGroup && s.outputAudioMixerGroup!.audioMixer && s.outputAudioMixerGroup.audioMixer.name.StartsWith("RES2");

        // ---------------- soundtrack ----------------

        AudioSource? music;
        MonoBehaviour? musicFsm;
        AudioClip? playing;
        string applied = Original;
        readonly Dictionary<string, AudioClip> files = new Dictionary<string, AudioClip>();
        readonly HashSet<string> loading = new HashSet<string>();

        static string MusicDir => Path.Combine(Application.dataPath, "..", "Music");

        void FindMusic()
        {
            if (music && music!.gameObject.activeInHierarchy) return;
            music = null; musicFsm = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (s && s.name == "music" && s.gameObject.scene.IsValid() && s.gameObject.activeInHierarchy && s.transform.parent && s.transform.parent.name == "loading object")
                {
                    music = s;
                    foreach (var mb in s.GetComponents<MonoBehaviour>())
                        if (mb.GetType().Name == "PlayMakerFSM" && mb.GetType().GetProperty("FsmName")?.GetValue(mb) as string == "music") musicFsm = mb;
                    break;
                }
        }

        public static List<string> UserFiles()
        {
            try
            {
                if (!Directory.Exists(MusicDir)) return new List<string>();
                return Directory.GetFiles(MusicDir).Where(f => f.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName).OrderBy(f => f).ToList()!;
            }
            catch { return new List<string>(); }
        }

        void Soundtrack()
        {
            if (!music) return;
            string want = Settings.Soundtrack;
            if (want == Original)
            {
                if (applied != Original)
                {
                    // Back to the game's own music: its FSM picks the race track again.
                    applied = Original;
                    playing = null;
                    if (musicFsm) { musicFsm!.enabled = false; musicFsm.enabled = true; }
                }
                return;
            }

            if (musicFsm && musicFsm!.enabled) musicFsm.enabled = false;
            bool finished = playing && music!.clip == playing && !music.isPlaying && music.time == 0f;
            if (want != applied || !playing || music!.clip != playing || finished)
            {
                var clip = Pick(want, finished);
                if (!clip) return;   // file still loading
                applied = want;
                playing = clip;
                music!.Stop();
                music.clip = clip;
                music.loop = want != Random;
                music.time = 0f;
                music.Play();
            }
            else if (!music!.isPlaying && music.loop) music.Play();
        }

        AudioClip? Pick(string want, bool next)
        {
            if (want == Random)
            {
                // Next random file (not the one that just played).
                if (randomNext == null)
                {
                    var all = UserFiles();
                    if (all.Count == 0) return null;
                    string pick;
                    int guard = 0;
                    do pick = all[UnityEngine.Random.Range(0, all.Count)]; while (all.Count > 1 && playing && pick == playing!.name && guard++ < 10);
                    randomNext = pick;
                }
                var c = FileClip(randomNext);
                if (c) randomNext = null;
                return c;
            }
            if (want.StartsWith("file:")) return FileClip(want.Substring(5));
            return null;
        }

        string? randomNext;

        AudioClip? FileClip(string name)
        {
            if (files.TryGetValue(name, out var c)) return c;
            if (loading.Add(name)) StartCoroutine(LoadFile(name));
            return null;
        }

        IEnumerator LoadFile(string name)
        {
            string path = Path.Combine(MusicDir, name);
            var type = name.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG
                : name.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(Path.GetFullPath(path)).AbsoluteUri, type))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    clip.name = name;
                    files[name] = clip;
                }
                else Debug.Log("[RaceBoot] audio: cannot load " + name + ": " + req.error);
            }
            loading.Remove(name);
        }
    }
}
