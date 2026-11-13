using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The local player's scope handling (ADR-103, G10b): PageUp/PageDown zeroing per optic (remembered, sent to the
    // server), the sway of a magnified scope and Shift to hold the breath. Runs between ScopeView and PlayerController.
    [DefaultExecutionOrder(-40)]
    public class ScopeControl : MonoBehaviour
    {
        // View offset (degrees) the camera adds on top of the recoil; zero outside a scope.
        public static float SwayYaw { get; private set; }
        public static float SwayPitch { get; private set; }
        public static readonly BreathHold Breath = new BreathHold();
        // The zero (metres) of the optic on the weapon in hand.
        public static float ZeroMeters { get; private set; } = 100f;

        static readonly Dictionary<int, float> ZeroByOptic = new Dictionary<int, float>();
        int _optic = -1;
        CombatantOptics _optics;
        Combatant _owner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            SwayYaw = SwayPitch = 0f;
            Breath.Reset();
            ZeroMeters = 100f;
            ZeroByOptic.Clear();
        }

        void OnDestroy() => ResetStatics();

        void Update()
        {
            var rules = GameData.Rules.Ballistics;
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            if (me == null || pc == null) return;
            if (_owner != me) { _owner = me; _optics = me.GetComponent<CombatantOptics>(); _optic = -1; }
            int optic = _optics != null && me.ActiveWeaponId != 0 ? _optics.ActiveOptic : 0;
            var def = OpticRules.Current.Find(optic);
            if (optic != _optic)
            {
                _optic = optic;
                ZeroMeters = ZeroByOptic.TryGetValue(optic, out var kept) ? kept : rules.DefaultZeroMeters;
                SendZero();
            }
            // Zeroing is a scope's: PageUp raises the zero distance, PageDown lowers it.
            int steps = pc.LastInput.ZeroSteps;
            if (steps != 0 && Zeroing.Count(def, rules) > 1)
            {
                float next = Zeroing.Step(def, rules, ZeroMeters, steps);
                if (next != ZeroMeters)
                {
                    ZeroMeters = next;
                    ZeroByOptic[optic] = next;
                    SendZero();
                }
            }

            bool scoped = ScopeView.Scoped;
            Breath.Tick(Time.unscaledDeltaTime, scoped && pc.LastInput.Sprint, rules);
            if (!scoped) { SwayYaw = SwayPitch = 0f; return; }
            float amplitude = ScopeSway.Amplitude(rules, ScopeView.CurrentZoom, GameData.Rules.Recoil.StanceFactor(pc.BodyStance), Breath.Holding, Breath.Winded);
            ScopeSway.Offset(amplitude, Time.time, out float yaw, out float pitch);
            SwayYaw = yaw;
            SwayPitch = pitch;
        }

        void SendZero()
        {
            if (_optics != null) _optics.CmdSetZero(Mathf.RoundToInt(ZeroMeters));
        }
    }
}
