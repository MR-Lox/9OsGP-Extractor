using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RaceBoot
{
    // Force feedback for racing wheels through DirectInput 8 (Rewired has no FFB for wheels).
    // Effects: a centring spring that stiffens with speed, a damper that gives weight at low speed,
    // a constant force for sliding (lateral velocity) and short jolts on impacts.
    public static class Ffb
    {
        public static string Status { get; private set; } = "inactif";

        static IntPtr di, dev, spring, damper, constant;
        static float retryAt;
        
        static Rigidbody? car;
        static float carLookupAt;
        static Vector3 lastVel;
        static float joltUntil, joltForce;

        // ---------------- per-frame ----------------

        public static void Tick(bool paused)
        {
            if (!Settings.Ffb || Application.platform != RuntimePlatform.WindowsPlayer)
            {
                if (dev != IntPtr.Zero) Shutdown(L.T("désactivé"));
                return;
            }
            if (dev == IntPtr.Zero)
            {
                if (Time.unscaledTime < retryAt) return;
                retryAt = Time.unscaledTime + 5f;
                try { Init(); } catch (Exception e) { Status = L.T("erreur : ") + e.Message; Shutdown(Status); }
                if (dev == IntPtr.Zero) return;
            }

            float k = Settings.FfbStrength;
            float speed = 0f, lateral = 0f;
            if (!car && Time.unscaledTime >= carLookupAt)
            {
                carLookupAt = Time.unscaledTime + 2f;
                var go = GameObject.Find("race_car_01");
                car = go ? go.GetComponent<Rigidbody>() : null;
            }
            if (car && !paused && Time.timeScale > 0f)
            {
                var v = car!.linearVelocity;
                var local = car.transform.InverseTransformDirection(v);
                speed = v.magnitude * 3.6f;
                lateral = local.x;
                // Impacts: sudden velocity change.
                var dv = (v - lastVel).magnitude / Mathf.Max(Time.deltaTime, 1e-3f);
                if (dv > 120f && Time.unscaledTime > joltUntil)
                {
                    var impact = car.transform.InverseTransformDirection(v - lastVel);
                    joltForce = Mathf.Sign(impact.x == 0 ? UnityEngine.Random.value - 0.5f : impact.x) * Mathf.Clamp01(dv / 400f);
                    joltUntil = Time.unscaledTime + 0.18f;
                }
                lastVel = v;
            }

            float s01 = Mathf.Clamp01(speed / 180f);
            int springCoef = (int)(10000 * k * Settings.FfbCentering * (paused ? 0.3f : 0.25f + 0.75f * s01));
            int damperCoef = (int)(10000 * k * 0.35f * (1f - Mathf.Clamp01(speed / 80f)));
            float slide = -Mathf.Clamp(lateral / 12f, -1f, 1f) * Settings.FfbEffects;
            float force = slide + (Time.unscaledTime < joltUntil ? joltForce * Settings.FfbEffects : 0f);
            if (paused) force = 0f;
            if (Settings.FfbInvert) force = -force;
            int magnitude = (int)(Mathf.Clamp(force, -1f, 1f) * 10000 * k);

            try
            {
                SetCondition(spring, springCoef);
                SetCondition(damper, damperCoef);
                SetConstant(constant, magnitude);
            }
            catch (Exception e) { Shutdown(L.T("erreur : ") + e.Message); }
        }

        // ---------------- DirectInput setup ----------------

        const uint DIRECTINPUT_VERSION = 0x0800;
        const uint DI8DEVCLASS_GAMECTRL = 4, DIEDFL_ATTACHEDONLY = 1, DIEDFL_FORCEFEEDBACK = 0x100;
        const uint DISCL_EXCLUSIVE = 1, DISCL_FOREGROUND = 4, DISCL_BACKGROUND = 8;
        const uint INFINITE = 0xFFFFFFFF, DIEB_NOTRIGGER = 0xFFFFFFFF;
        const uint DIEFF_OBJECTOFFSETS = 0x2, DIEFF_CARTESIAN = 0x10;
        const uint DIEP_TYPESPECIFICPARAMS = 0x100;

        static readonly Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");
        static readonly Guid GUID_XAxis = new Guid("A36D02E0-C9F3-11CF-BFC7-444553540000");
        static readonly Guid GUID_ConstantForce = new Guid("13541C20-8E33-11D0-9AD0-00A0C9A06E35");
        static readonly Guid GUID_Spring = new Guid("13541C27-8E33-11D0-9AD0-00A0C9A06E35");
        static readonly Guid GUID_Damper = new Guid("13541C28-8E33-11D0-9AD0-00A0C9A06E35");

        [DllImport("dinput8.dll")] static extern int DirectInput8Create(IntPtr hinst, uint version, ref Guid riid, out IntPtr ppv, IntPtr outer);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        // COM vtable delegates.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateDeviceFn(IntPtr self, ref Guid guid, out IntPtr device, IntPtr outer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumDevicesFn(IntPtr self, uint type, EnumDevicesCb cb, IntPtr pvRef, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumDevicesCb(IntPtr instance, IntPtr pvRef);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetPropertyFn(IntPtr self, IntPtr guidProp, IntPtr header);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int NoArgFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetDataFormatFn(IntPtr self, IntPtr format);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetCoopFn(IntPtr self, IntPtr hwnd, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateEffectFn(IntPtr self, ref Guid guid, ref DIEFFECT eff, out IntPtr effect, IntPtr outer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetParametersFn(IntPtr self, ref DIEFFECT eff, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StartFn(IntPtr self, uint iterations, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SendCommandFn(IntPtr self, uint cmd);

        [StructLayout(LayoutKind.Sequential)]
        struct DIEFFECT
        {
            public uint dwSize, dwFlags, dwDuration, dwSamplePeriod, dwGain, dwTriggerButton, dwTriggerRepeatInterval, cAxes;
            public IntPtr rgdwAxes, rglDirection, lpEnvelope;
            public uint cbTypeSpecificParams;
            public IntPtr lpvTypeSpecificParams;
            public uint dwStartDelay;
        }

        static T Fn<T>(IntPtr com, int slot) where T : Delegate
        {
            var vtbl = Marshal.ReadIntPtr(com);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size), typeof(T));
        }

        static void Check(int hr, string what) { if (hr < 0) throw new Exception(what + " 0x" + hr.ToString("X8")); }

        static IntPtr axes, dirs, typeParams, dataFormat, objFormat, guidX;
        static Guid foundGuid;
        static string foundName = "";
        static EnumDevicesCb? keepCb;

        static void Init()
        {

            var iid = IID_IDirectInput8W;
            Check(DirectInput8Create(GetModuleHandle(null), DIRECTINPUT_VERSION, ref iid, out di, IntPtr.Zero), "DirectInput8Create");

            // Find a force-feedback game controller, preferring the configured wheel.
            foundGuid = Guid.Empty; foundName = "";
            keepCb = (inst, _) =>
            {
                var g = PtrToGuid(inst + 4);
                var name = Marshal.PtrToStringUni(inst + 4 + 16 + 16 + 4 + 520) ?? "";
                bool preferred = !string.IsNullOrEmpty(Wheel.Device) && (name.Contains(Wheel.Device) || Wheel.Device.Contains(name));
                if (foundGuid == Guid.Empty || preferred) { foundGuid = g; foundName = name; }
                return preferred ? 0 : 1; // DIENUM_STOP / DIENUM_CONTINUE
            };
            Check(Fn<EnumDevicesFn>(di, 4)(di, DI8DEVCLASS_GAMECTRL, keepCb, IntPtr.Zero, DIEDFL_ATTACHEDONLY | DIEDFL_FORCEFEEDBACK), "EnumDevices");
            if (foundGuid == Guid.Empty) { Shutdown(L.T("aucun volant à retour de force détecté")); return; }

            var guid = foundGuid;
            Check(Fn<CreateDeviceFn>(di, 3)(di, ref guid, out dev, IntPtr.Zero), "CreateDevice");

            // Minimal data format (only the X axis) so the device can be acquired.
            guidX = Marshal.AllocHGlobal(16); Marshal.Copy(GUID_XAxis.ToByteArray(), 0, guidX, 16);
            objFormat = Marshal.AllocHGlobal(24);
            Marshal.WriteIntPtr(objFormat, 0, guidX);
            Marshal.WriteInt32(objFormat, 8, 0);                                       // dwOfs
            Marshal.WriteInt32(objFormat, 12, unchecked((int)(0x80000000 | 0x00FFFF00 | 0x3))); // OPTIONAL | ANYINSTANCE | AXIS
            Marshal.WriteInt32(objFormat, 16, 0);
            dataFormat = Marshal.AllocHGlobal(32);
            Marshal.WriteInt32(dataFormat, 0, 32);   // dwSize
            Marshal.WriteInt32(dataFormat, 4, 24);   // dwObjSize
            Marshal.WriteInt32(dataFormat, 8, 1);    // DIDF_ABSAXIS
            Marshal.WriteInt32(dataFormat, 12, 4);   // dwDataSize
            Marshal.WriteInt32(dataFormat, 16, 1);   // dwNumObjs
            Marshal.WriteIntPtr(dataFormat, 24, objFormat);
            Check(Fn<SetDataFormatFn>(dev, 11)(dev, dataFormat), "SetDataFormat");

            var hwnd = GameWindow();
            int hr = Fn<SetCoopFn>(dev, 13)(dev, hwnd, DISCL_EXCLUSIVE | DISCL_BACKGROUND);
            if (hr < 0) Check(Fn<SetCoopFn>(dev, 13)(dev, hwnd, DISCL_EXCLUSIVE | DISCL_FOREGROUND), "SetCooperativeLevel");

            // Turn the wheel's own auto-centre off: we drive the centring ourselves.
            var prop = Marshal.AllocHGlobal(20);
            Marshal.WriteInt32(prop, 0, 20); Marshal.WriteInt32(prop, 4, 16); Marshal.WriteInt32(prop, 8, 0); Marshal.WriteInt32(prop, 12, 0);
            Marshal.WriteInt32(prop, 16, 0); // DIPROPAUTOCENTER_OFF
            Fn<SetPropertyFn>(dev, 6)(dev, (IntPtr)9, prop);                     // DIPROP_AUTOCENTER
            Marshal.FreeHGlobal(prop);

            Fn<NoArgFn>(dev, 7)(dev);                                             // Acquire
            Fn<SendCommandFn>(dev, 22)(dev, 0x00000001);                          // DISFFC_RESET
            Fn<SendCommandFn>(dev, 22)(dev, 0x00000010);                          // DISFFC_SETACTUATORSON

            axes = Marshal.AllocHGlobal(4); Marshal.WriteInt32(axes, 0);          // DIJOFS_X
            dirs = Marshal.AllocHGlobal(4); Marshal.WriteInt32(dirs, 0);
            typeParams = Marshal.AllocHGlobal(24);

            spring = CreateCondition(GUID_Spring);
            damper = CreateCondition(GUID_Damper);
            constant = CreateConstant();
            Status = L.T("actif sur ") + foundName;
            Debug.Log("[RaceBoot] FFB " + Status);
        }

        static Guid PtrToGuid(IntPtr p) { var b = new byte[16]; Marshal.Copy(p, b, 0, 16); return new Guid(b); }

        static DIEFFECT Effect(uint cb)
        {
            return new DIEFFECT
            {
                dwSize = (uint)Marshal.SizeOf<DIEFFECT>(), dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS, dwDuration = INFINITE,
                dwGain = 10000, dwTriggerButton = DIEB_NOTRIGGER, cAxes = 1, rgdwAxes = axes, rglDirection = dirs,
                cbTypeSpecificParams = cb, lpvTypeSpecificParams = typeParams,
            };
        }

        static IntPtr CreateCondition(Guid type)
        {
            WriteCondition(0);
            var e = Effect(24);
            Check(Fn<CreateEffectFn>(dev, 18)(dev, ref type, ref e, out var fx, IntPtr.Zero), "CreateEffect");
            Fn<StartFn>(fx, 7)(fx, 1, 0);
            return fx;
        }

        static IntPtr CreateConstant()
        {
            Marshal.WriteInt32(typeParams, 0, 0);
            var e = Effect(4);
            var type = GUID_ConstantForce;
            Check(Fn<CreateEffectFn>(dev, 18)(dev, ref type, ref e, out var fx, IntPtr.Zero), "CreateEffect");
            Fn<StartFn>(fx, 7)(fx, 1, 0);
            return fx;
        }

        static void WriteCondition(int coef)
        {
            coef = Mathf.Clamp(coef, 0, 10000);
            Marshal.WriteInt32(typeParams, 0, 0);        // lOffset
            Marshal.WriteInt32(typeParams, 4, coef);     // lPositiveCoefficient
            Marshal.WriteInt32(typeParams, 8, coef);     // lNegativeCoefficient
            Marshal.WriteInt32(typeParams, 12, 10000);   // dwPositiveSaturation
            Marshal.WriteInt32(typeParams, 16, 10000);   // dwNegativeSaturation
            Marshal.WriteInt32(typeParams, 20, 0);       // lDeadBand
        }

        static int lastSpring = -1, lastDamper = -1, lastConstant = int.MinValue;

        static void SetCondition(IntPtr fx, int coef)
        {
            if (fx == IntPtr.Zero) return;
            ref int last = ref (fx == spring ? ref lastSpring : ref lastDamper);
            if (Math.Abs(coef - last) < 50) return;
            last = coef;
            WriteCondition(coef);
            var e = Effect(24);
            Check(Fn<SetParametersFn>(fx, 6)(fx, ref e, DIEP_TYPESPECIFICPARAMS), "SetParameters");
        }

        static void SetConstant(IntPtr fx, int magnitude)
        {
            if (fx == IntPtr.Zero || Math.Abs(magnitude - lastConstant) < 50) return;
            lastConstant = magnitude;
            Marshal.WriteInt32(typeParams, 0, Mathf.Clamp(magnitude, -10000, 10000));
            var e = Effect(4);
            Check(Fn<SetParametersFn>(fx, 6)(fx, ref e, DIEP_TYPESPECIFICPARAMS), "SetParameters");
        }

        static IntPtr GameWindow()
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var sb = new System.Text.StringBuilder(64);
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != me || !IsWindowVisible(h)) return true;
                sb.Clear(); GetClassName(h, sb, 64);
                if (sb.ToString() == "UnityWndClass") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static void Release(ref IntPtr com)
        {
            if (com == IntPtr.Zero) return;
            try { Fn<ReleaseFn>(com, 2)(com); } catch { }
            com = IntPtr.Zero;
        }

        static void Free(ref IntPtr p) { if (p != IntPtr.Zero) { Marshal.FreeHGlobal(p); p = IntPtr.Zero; } }

        public static void Shutdown(string status)
        {
            if (dev != IntPtr.Zero)
            {
                try { Fn<SendCommandFn>(dev, 22)(dev, 0x00000001); } catch { }       // DISFFC_RESET: stop all forces
                try { Fn<NoArgFn>(dev, 8)(dev); } catch { }                          // Unacquire
            }
            Release(ref spring); Release(ref damper); Release(ref constant);
            Release(ref dev); Release(ref di);
            Free(ref axes); Free(ref dirs); Free(ref typeParams); Free(ref dataFormat); Free(ref objFormat); Free(ref guidX);
            lastSpring = lastDamper = -1; lastConstant = int.MinValue;
            Status = status;
        }
    }
}
