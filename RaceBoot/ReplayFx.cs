using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RaceBoot
{
    // Effects of the replay: tyre smoke, dirt, sparks, backfires, brake lights…
    // The game drives them three ways, all recorded as change events per car:
    //  - objects switched on / off (dirt emitters, exhaust flames, landing sparks…);
    //  - particle systems started / stopped, their emission rate, and bursts emitted by script;
    //  - the emission of a material (brake lights).
    public partial class Replay
    {
        const int FxActive = 0, FxParticles = 1, FxEmission = 2;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        class Fx
        {
            public string Path = "";
            public int Kind;
            // Events: frame index + value. Particles: x = emitting, y = rate, z = 0 (state) / 1 (burst of w particles).
            public readonly List<int> Frames = new List<int>();
            public readonly List<Vector4> Values = new List<Vector4>();

            // Recording side.
            public GameObject? SrcGo;
            public ParticleSystem? SrcPs;
            public Renderer? SrcR;
            public Vector4 Last;
            public int LastCount;
            public bool Started;

            // Playback side.
            public GameObject? DstGo;
            public ParticleSystem? DstPs;
            public Material? DstMat;
            public int Next;

            public void Add(int frame, Vector4 v) { Frames.Add(frame); Values.Add(v); Last = v; }
        }

        // ---------------- recording ----------------

        static void AddFx(Actor a, Transform body)
        {
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                string path = t == body ? "" : RelPath(body, t);
                if (t != body) a.Fx.Add(new Fx { Path = path, Kind = FxActive, SrcGo = t.gameObject });
                var ps = t.GetComponent<ParticleSystem>();
                if (ps) a.Fx.Add(new Fx { Path = path, Kind = FxParticles, SrcPs = ps });
                var r = t.GetComponent<Renderer>();
                if (r && !(r is ParticleSystemRenderer) && r.sharedMaterial && r.sharedMaterial.HasProperty(EmissionColor))
                    a.Fx.Add(new Fx { Path = path, Kind = FxEmission, SrcR = r });
            }
        }

        static void SampleFx(Actor a, int frame)
        {
            foreach (var fx in a.Fx)
            {
                switch (fx.Kind)
                {
                    case FxActive:
                    {
                        if (!fx.SrcGo) break;
                        var v = new Vector4(fx.SrcGo!.activeSelf ? 1f : 0f, 0, 0, 0);
                        if (!fx.Started || v.x != fx.Last.x) fx.Add(frame, v);
                        break;
                    }
                    case FxParticles:
                    {
                        var ps = fx.SrcPs;
                        if (!ps) break;
                        var em = ps!.emission;
                        bool emitting = ps.isEmitting && em.enabled && ps.gameObject.activeInHierarchy;
                        float rate = em.rateOverTimeMultiplier;
                        var v = new Vector4(emitting ? 1f : 0f, rate, 0f, 0f);
                        if (!fx.Started || v.x != fx.Last.x || Mathf.Abs(rate - fx.Last.y) > Mathf.Max(0.5f, Mathf.Abs(fx.Last.y) * 0.15f))
                            fx.Add(frame, v);
                        int count = ps.particleCount;
                        // Particles emitted by script (Emit) while the system itself is not emitting.
                        if (fx.Started && !emitting && count > fx.LastCount) { var last = fx.Last; fx.Add(frame, new Vector4(0f, 0f, 1f, count - fx.LastCount)); fx.Last = last; }
                        fx.LastCount = count;
                        break;
                    }
                    case FxEmission:
                    {
                        if (!fx.SrcR || !fx.SrcR!.sharedMaterial) break;
                        Color c = fx.SrcR.sharedMaterial.GetColor(EmissionColor);
                        var v = new Vector4(c.r, c.g, c.b, c.a);
                        if (!fx.Started || (v - fx.Last).sqrMagnitude > 0.0025f) fx.Add(frame, v);
                        break;
                    }
                }
                fx.Started = true;
            }
        }

        // Keeps only what actually changed during the race.
        static void TrimFx(Actor a)
        {
            a.Fx.RemoveAll(fx => fx.Kind == FxParticles ? fx.Frames.Count == 0 || (fx.Frames.Count == 1 && fx.Values[0].x == 0f) : fx.Frames.Count <= 1);
            foreach (var fx in a.Fx) { fx.SrcGo = null; fx.SrcPs = null; fx.SrcR = null; }
        }

        // ---------------- playback ----------------

        static void LinkFx(Actor a, GameObject clone)
        {
            foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.playOnAwake = false;   // only the recorded events start them
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var fx in a.Fx)
            {
                var t = fx.Path == "" ? clone.transform : clone.transform.Find(fx.Path);
                if (!t) continue;
                fx.DstGo = t!.gameObject;
                if (fx.Kind == FxParticles) fx.DstPs = t.GetComponent<ParticleSystem>();
                if (fx.Kind == FxEmission) { var r = t.GetComponent<Renderer>(); if (r) fx.DstMat = r!.material; }
            }
        }

        int fxFrame = -1;

        // Brings every effect to `frame`: a jump back (loop, new playback) resets them, otherwise only the
        // events since the last frame are played (bursts included).
        void StepFx(Recording r, int frame)
        {
            bool reset = frame < fxFrame || fxFrame < 0;
            foreach (var a in r.Actors)
                foreach (var fx in a.Fx)
                {
                    if (reset)
                    {
                        fx.Next = 0;
                        if (fx.DstPs) fx.DstPs!.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        // State at `frame` (last state event), without the bursts of the past.
                        int stateIdx = -1;
                        while (fx.Next < fx.Frames.Count && fx.Frames[fx.Next] <= frame)
                        {
                            if (!(fx.Kind == FxParticles && fx.Values[fx.Next].z > 0.5f)) stateIdx = fx.Next;
                            fx.Next++;
                        }
                        if (stateIdx >= 0) ApplyFx(fx, fx.Values[stateIdx]);
                        continue;
                    }
                    while (fx.Next < fx.Frames.Count && fx.Frames[fx.Next] <= frame) { ApplyFx(fx, fx.Values[fx.Next]); fx.Next++; }
                }
            fxFrame = frame;
        }

        static void ApplyFx(Fx fx, Vector4 v)
        {
            switch (fx.Kind)
            {
                case FxActive:
                    if (fx.DstGo && fx.DstGo!.activeSelf != v.x > 0.5f) fx.DstGo.SetActive(v.x > 0.5f);
                    break;
                case FxParticles:
                {
                    var ps = fx.DstPs;
                    if (!ps) break;
                    if (v.z > 0.5f) { if (ps!.gameObject.activeInHierarchy) ps.Emit(Mathf.Clamp((int)v.w, 1, 200)); break; }
                    var em = ps!.emission;
                    if (v.x > 0.5f)
                    {
                        em.enabled = true;
                        em.rateOverTimeMultiplier = v.y;
                        if (!ps.isPlaying) ps.Play(false);
                    }
                    else ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    break;
                }
                case FxEmission:
                    if (fx.DstMat) fx.DstMat!.SetColor(EmissionColor, new Color(v.x, v.y, v.z, v.w));
                    break;
            }
        }

        // ---------------- file ----------------

        static void WriteFx(BinaryWriter w, Actor a)
        {
            w.Write(a.Fx.Count);
            foreach (var fx in a.Fx)
            {
                w.Write(fx.Path); w.Write(fx.Kind); w.Write(fx.Frames.Count);
                for (int i = 0; i < fx.Frames.Count; i++)
                {
                    w.Write(fx.Frames[i]);
                    var v = fx.Values[i]; w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w);
                }
            }
        }

        static void ReadFx(BinaryReader rd, Actor a)
        {
            int n = rd.ReadInt32();
            for (int k = 0; k < n; k++)
            {
                var fx = new Fx { Path = rd.ReadString(), Kind = rd.ReadInt32() };
                int e = rd.ReadInt32();
                for (int i = 0; i < e; i++)
                {
                    fx.Frames.Add(rd.ReadInt32());
                    fx.Values.Add(new Vector4(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle()));
                }
                a.Fx.Add(fx);
            }
        }
    }
}
