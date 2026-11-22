using System;
using System.Linq;
using Rewired;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaceBoot
{
    // Racing wheel + pedals support.
    // The game only ships gamepad bindings, so a wheel gets no input at all. A small wizard detects
    // the steering axis, the pedals (rest / pressed values) and two buttons, then builds a Rewired map
    // for the wheel with a matching axis calibration. The car reads "steer", "handgun" (throttle) and
    // "run" (brake / drift) as analog values, so pedals are fully progressive.
    public static class Wheel
    {
        // ---- configuration (saved in the ini) ----
        public static string Device = "";
        public static int SteerAxis = -1, AccelAxis = -1, BrakeAxis = -1, ViewButton = -1, MenuButton = -1;
        public static float SteerCenter, AccelRest, AccelFull, BrakeRest, BrakeFull;
        public static bool SteerInvert;
        public static float SteerSens = 1.5f;    // 1 = full lock needs the whole wheel rotation
        public static float PedalDeadZone = 0.04f;

        public static bool Configured => !string.IsNullOrEmpty(Device) && SteerAxis >= 0;

        static bool hooked;
        static float reapplyAt = -1f;

        public static Joystick? Find()
        {
            if (!ReInput.isReady || string.IsNullOrEmpty(Device)) return null;
            return ReInput.controllers.Joysticks.FirstOrDefault(j => j.hardwareName == Device)
                ?? ReInput.controllers.Joysticks.FirstOrDefault(j => j.name == Device);
        }

        public static void Update()
        {
            if (!ReInput.isReady) return;
            if (!hooked)
            {
                hooked = true;
                ReInput.ControllerConnectedEvent += _ => reapplyAt = Time.unscaledTime + 0.5f;
                SceneManager.sceneLoaded += (s, m) => reapplyAt = Time.unscaledTime + 0.5f;
                reapplyAt = Time.unscaledTime + 0.5f;
            }
            // Applied with a small delay so it runs after Rewired / the user data store loaded their own maps.
            if (reapplyAt > 0 && Time.unscaledTime >= reapplyAt) { reapplyAt = -1f; Apply(); }
        }

        public static bool ViewPressed() { var j = Find(); return j != null && ViewButton >= 0 && ViewButton < j.buttonCount && j.GetButtonDown(ViewButton); }
        public static bool MenuPressed() { var j = Find(); return j != null && MenuButton >= 0 && MenuButton < j.buttonCount && j.GetButtonDown(MenuButton); }

        static int ActionId(string name) => ReInput.mapping.GetAction(name)?.id ?? -1;

        public static void Apply()
        {
            if (!Configured) return;
            var j = Find();
            if (j == null) return;
            var player = ReInput.players.GetPlayer(0);
            if (player == null) return;
            if (!player.controllers.ContainsController(j)) player.controllers.AddController(j, false);

            var maps = player.controllers.maps;
            var map = maps.GetMap(ControllerType.Joystick, j.id, 0, 0);
            if (map == null) { maps.AddEmptyMap(ControllerType.Joystick, j.id, 0, 0); map = maps.GetMap(ControllerType.Joystick, j.id, 0, 0); }
            if (map == null) return;
            map.ClearElementMaps();
            map.enabled = true;

            var cal = j.calibrationMap;
            for (int i = 0; i < j.axisCount; i++) cal.GetAxis(i).applyRangeCalibration = false;

            // Steering: centre captured by the wizard, range shortened by the sensitivity.
            if (SteerAxis < j.axisCount)
            {
                float range = 1f / Mathf.Max(1f, SteerSens);
                var c = cal.GetAxis(SteerAxis);
                c.applyRangeCalibration = true;
                c.calibratedZero = SteerCenter;
                c.calibratedMin = SteerCenter - range;
                c.calibratedMax = SteerCenter + range;
                c.invert = SteerInvert;
                c.deadZone = 0f;
                map.CreateElementMap(ActionId("steer"), Pole.Positive, j.AxisElementIdentifiers[SteerAxis].id, ControllerElementType.Axis, AxisRange.Full, false);
            }

            // Pedals: zero = released position; the pressed side is mapped to a positive action value.
            MapPedal(j, map, cal, AccelAxis, AccelRest, AccelFull, ActionId("handgun"));
            MapPedal(j, map, cal, BrakeAxis, BrakeRest, BrakeFull, ActionId("run"));
            Debug.Log("[RaceBoot] wheel map applied to " + j.hardwareName + " (" + map.elementMapCount + " bindings)");
        }

        static void MapPedal(Joystick j, ControllerMap map, CalibrationMap cal, int axis, float rest, float full, int action)
        {
            if (axis < 0 || axis >= j.axisCount || action < 0) return;
            var c = cal.GetAxis(axis);
            bool positive = full > rest;
            // Two pedals may share one axis (combined pedals): only set the side this pedal uses.
            if (!c.applyRangeCalibration)
            {
                c.applyRangeCalibration = true;
                c.calibratedZero = rest;
                c.calibratedMin = Mathf.Min(rest - 0.01f, -1f);
                c.calibratedMax = Mathf.Max(rest + 0.01f, 1f);
                c.invert = false;
            }
            if (positive) c.calibratedMax = full; else c.calibratedMin = full;
            c.deadZone = PedalDeadZone;
            map.CreateElementMap(action, Pole.Positive, j.AxisElementIdentifiers[axis].id, ControllerElementType.Axis,
                positive ? AxisRange.Positive : AxisRange.Negative, false);
        }

        // ---------------- wizard + UI ----------------

        static int step;            // 0 = idle, 1 rest, 2 steer, 3 throttle, 4 brake, 5 view button, 6 menu button
        static int selected = -1;   // joystick id
        static float[] rest = new float[0], extreme = new float[0];
        static int wSteer, wAccel, wBrake;
        static float wSteerSign, wAccelFull, wBrakeFull;

        static Joystick? Selected()
        {
            var list = ReInput.controllers.Joysticks;
            var j = list.FirstOrDefault(x => x.id == selected);
            if (j != null) return j;
            j = Find() ?? list.FirstOrDefault(x => x.GetTemplate<IGamepadTemplate>() == null) ?? list.FirstOrDefault();
            selected = j?.id ?? -1;
            return j;
        }

        static float Raw(Joystick j, int i) => i < j.axisCount ? j.Axes[i].valueRaw : 0f;

        public static void Draw(float s, GUIStyle header, GUIStyle label, GUIStyle small, GUIStyle button, GUIStyle on)
        {
            if (!ReInput.isReady) return;
            var joys = ReInput.controllers.Joysticks;
            if (joys.Count == 0) { GUILayout.Label(L.T("Aucun volant / contrôleur détecté. Branchez-le puis rouvrez ce menu."), label); return; }

            GUILayout.Label(L.T("Périphérique"), header);
            GUILayout.BeginHorizontal();
            foreach (var x in joys)
                if (GUILayout.Button(x.hardwareName, x.id == selected ? on : button, GUILayout.Height(36 * s))) { selected = x.id; step = 0; }
            GUILayout.EndHorizontal();
            var j = Selected();
            if (j == null) return;
            bool isConfigured = Configured && (j.hardwareName == Device || j.name == Device);
            GUILayout.Label(isConfigured ? L.T("Configuré pour ce périphérique.") : L.T("Pas encore configuré : lancez l'assistant."), small);
            GUILayout.Space(8 * s);

            if (step == 0)
            {
                if (GUILayout.Button(isConfigured ? L.T("Relancer l'assistant de configuration") : L.T("Lancer l'assistant de configuration"), on, GUILayout.Height(44 * s)))
                    step = 1;
            }
            else DrawWizard(j, s, header, label, small, button, on);

            GUILayout.Space(10 * s);
            GUILayout.Label(L.T("Axes en direct"), header);
            for (int i = 0; i < j.axisCount; i++)
            {
                GUILayout.BeginHorizontal();
                string role = i == SteerAxis && isConfigured ? L.T(" (direction)") : i == AccelAxis && isConfigured ? L.T(" (accélérateur)") : i == BrakeAxis && isConfigured ? L.T(" (frein)") : "";
                GUILayout.Label(j.AxisElementIdentifiers[i].name + role, small, GUILayout.Width(300 * s));
                GUILayout.HorizontalSlider(Raw(j, i), -1f, 1f, GUILayout.Height(24 * s));
                GUILayout.EndHorizontal();
            }

            if (isConfigured)
            {
                GUILayout.Space(10 * s);
                GUILayout.Label(L.T("Réglages"), header);
                float sens = SliderRow(L.T("Sensibilité de la direction"), SteerSens, 1f, 4f, label, s);
                float dz = SliderRow(L.T("Zone morte des pédales"), PedalDeadZone, 0f, 0.2f, label, s);
                if (Math.Abs(sens - SteerSens) > 1e-4f || Math.Abs(dz - PedalDeadZone) > 1e-4f) { SteerSens = sens; PedalDeadZone = dz; Apply(); }
                GUILayout.Label(L.T("Sensibilité 1 = braquage maximal à fond de volant ; 2 = à mi-course (utile pour les volants 900°)."), small);
                GUILayout.Space(6 * s);
                bool v = SteerInvert;
                if (ToggleRow(L.T("Inverser la direction"), ref v, label, button, on, s)) { SteerInvert = v; Apply(); }
                if (GUILayout.Button(L.T("Oublier ce volant"), button, GUILayout.Height(38 * s)))
                {
                    var player = ReInput.players.GetPlayer(0);
                    player?.controllers.maps.GetMap(ControllerType.Joystick, j.id, 0, 0)?.ClearElementMaps();
                    j.calibrationMap.Reset();
                    Device = ""; SteerAxis = AccelAxis = BrakeAxis = ViewButton = MenuButton = -1;
                    Settings.Save();
                }
            }

            GUILayout.Space(14 * s);
            GUILayout.Label(L.T("Retour de force"), header);
            bool ffb = Settings.Ffb;
            if (ToggleRow(L.T("Activer le retour de force"), ref ffb, label, button, on, s)) Settings.Ffb = ffb;
            Settings.FfbStrength = SliderRow(L.T("Force générale"), Settings.FfbStrength, 0f, 1f, label, s);
            Settings.FfbCentering = SliderRow(L.T("Rappel au centre"), Settings.FfbCentering, 0f, 1f, label, s);
            Settings.FfbEffects = SliderRow(L.T("Glisse et chocs"), Settings.FfbEffects, 0f, 1f, label, s);
            bool inv = Settings.FfbInvert;
            if (ToggleRow(L.T("Inverser le sens des forces"), ref inv, label, button, on, s)) Settings.FfbInvert = inv;
            GUILayout.Label(L.T("État : ") + Ffb.Status, small);
        }

        static void DrawWizard(Joystick j, float s, GUIStyle header, GUIStyle label, GUIStyle small, GUIStyle button, GUIStyle on)
        {
            int n = j.axisCount;
            if (rest.Length != n) { rest = new float[n]; extreme = new float[n]; }
            string title = "", hint = "";
            bool canValidate = false, canSkip = false;
            int best = -1;

            if (step == 1)
            {
                title = L.T("1/6 — Position de repos");
                hint = L.T("Volant au centre, pieds levés des pédales, puis cliquez sur Valider.");
                canValidate = true;
            }
            else if (step >= 2 && step <= 4)
            {
                title = step == 2 ? L.T("2/6 — Tournez le volant à fond vers la DROITE") : step == 3 ? L.T("3/6 — Enfoncez l'ACCÉLÉRATEUR à fond") : L.T("4/6 — Enfoncez le FREIN à fond");
                hint = L.T("Maintenez la position, puis cliquez sur Valider.");
                float bestDelta = 0f;
                for (int i = 0; i < n; i++)
                {
                    float d = Raw(j, i) - rest[i];
                    if (Mathf.Abs(d) > Mathf.Abs(extreme[i] - rest[i])) extreme[i] = Raw(j, i);
                    if (step >= 3 && i == wSteer) continue;
                    // Brake may share the throttle axis only in the opposite direction (combined pedals).
                    if (step == 4 && i == wAccel && Mathf.Sign(d) == Mathf.Sign(wAccelFull - rest[i])) continue;
                    if (Mathf.Abs(d) > bestDelta) { bestDelta = Mathf.Abs(d); best = i; }
                }
                canValidate = best >= 0 && bestDelta > 0.4f;
                canSkip = step == 4;
                hint += canValidate ? L.T("   Détecté : ") + j.AxisElementIdentifiers[best].name : L.T("   (en attente d'un mouvement…)");
            }
            else if (step == 5 || step == 6)
            {
                title = step == 5 ? L.T("5/6 — Bouton « changer de vue »") : L.T("6/6 — Bouton « menu des options »");
                hint = L.T("Appuyez sur le bouton du volant à utiliser, ou cliquez sur Passer.");
                canSkip = true;
                for (int b = 0; b < j.buttonCount; b++)
                    if (j.GetButtonDown(b))
                    {
                        if (step == 5) ViewButtonPending = b; else MenuButtonPending = b;
                        Next(j, -1);
                        return;
                    }
            }

            GUILayout.Label(title, header);
            GUILayout.Label(hint, label);
            GUILayout.BeginHorizontal();
            GUI.enabled = canValidate;
            if (step <= 4 && GUILayout.Button(L.T("Valider"), on, GUILayout.Height(42 * s))) Next(j, best);
            GUI.enabled = true;
            if (canSkip && GUILayout.Button(L.T("Passer"), button, GUILayout.Height(42 * s))) Next(j, -1);
            if (GUILayout.Button(L.T("Annuler"), button, GUILayout.Height(42 * s), GUILayout.Width(160 * s))) step = 0;
            GUILayout.EndHorizontal();
        }

        static int ViewButtonPending = -1, MenuButtonPending = -1;

        static void Next(Joystick j, int axis)
        {
            switch (step)
            {
                case 1:
                    for (int i = 0; i < rest.Length; i++) { rest[i] = Raw(j, i); extreme[i] = rest[i]; }
                    wSteer = wAccel = wBrake = -1; ViewButtonPending = MenuButtonPending = -1;
                    break;
                case 2: wSteer = axis; wSteerSign = Mathf.Sign(extreme[axis] - rest[axis]); break;
                case 3: wAccel = axis; wAccelFull = extreme[axis]; break;
                case 4: wBrake = axis; wBrakeFull = axis >= 0 ? extreme[axis] : 0f; break;
            }
            if (step == 3 || step == 4)
                for (int i = 0; i < rest.Length; i++) extreme[i] = rest[i];
            step++;
            if (step == 3 || step == 4) for (int i = 0; i < rest.Length; i++) extreme[i] = rest[i];
            if (step > 6) Finish(j);
        }

        static void Finish(Joystick j)
        {
            step = 0;
            Device = j.hardwareName;
            SteerAxis = wSteer; SteerCenter = rest[wSteer]; SteerInvert = wSteerSign < 0;
            AccelAxis = wAccel; AccelRest = wAccel >= 0 ? rest[wAccel] : 0f; AccelFull = wAccelFull;
            BrakeAxis = wBrake; BrakeRest = wBrake >= 0 ? rest[wBrake] : 0f; BrakeFull = wBrakeFull;
            ViewButton = ViewButtonPending; MenuButton = MenuButtonPending;
            Apply();
            Settings.Save();
        }

        static float SliderRow(string name, float v, float min, float max, GUIStyle label, float s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(34 * s), GUILayout.Width(300 * s));
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Height(34 * s));
            GUILayout.Label(v.ToString("0.00"), label, GUILayout.Width(80 * s));
            GUILayout.EndHorizontal();
            return v;
        }

        static bool ToggleRow(string name, ref bool v, GUIStyle label, GUIStyle button, GUIStyle on, float s)
        {
            bool old = v;
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Height(36 * s));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L.T("Oui"), v ? on : button, GUILayout.Width(100 * s), GUILayout.Height(36 * s))) v = true;
            if (GUILayout.Button(L.T("Non"), !v ? on : button, GUILayout.Width(100 * s), GUILayout.Height(36 * s))) v = false;
            GUILayout.EndHorizontal();
            return v != old;
        }
    }
}
