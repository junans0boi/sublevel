using System;

namespace Game.Core
{
    // Hip = not aiming. Shoulder = right button held (third person stays, camera tucks in).
    // Sights = toggled by a short press (eye view, most accurate). ADR-045.
    public enum AimMode { Hip = 0, Shoulder = 1, Sights = 2 }

    // What aiming changes, per mode. Loaded from data.
    [Serializable]
    public class AimStance
    {
        public float MoveFactor = 1f;
        public float SpreadFactor = 1f;
        public float RecoilFactor = 1f;
        public float Fov = 60f;
    }

    [Serializable]
    public class AimRules
    {
        public float TapSeconds = 0.2f;        // a press shorter than this toggles sights; longer shoulders the weapon
        public float FovChangePerSecond = 10f; // how fast the camera eases to the stance's field of view
        public float ShoulderCameraDistance = 1.9f;
        public AimStance Hip = new AimStance();
        public AimStance Shoulder = new AimStance();
        public AimStance Sights = new AimStance();

        public AimStance For(AimMode mode) => mode == AimMode.Shoulder ? Shoulder : mode == AimMode.Sights ? Sights : Hip;
    }

    [Serializable]
    public class LeanRules
    {
        public float OffsetMeters = 0.25f;      // how far the eyes move sideways at full lean
        public float EyeDropMeters = 0.04f;
        public float EyeViewRollDeg = 6f;       // camera roll when it sits at the eyes (first person, sights)
        public float ThirdPersonRollDeg = 0f;   // the over-the-shoulder camera only follows the head sideways
        public float BodyRollDeg = 10f;
        public float ChangePerSecond = 8f;      // full lean takes 1 / this seconds
        public float MoveFactor = 0.85f;
    }

    [Serializable]
    public class RecoilRules
    {
        public float RecoverDegPerSecond = 14f;
        public float MaxPitchDeg = 12f;
        // ADR-089: the view starts settling back only this long after the last kick, so a spray climbs.
        public float RecoverDelaySeconds = 0.15f;
        // Recoil share by body stance (PUBG: crouch x0.8, prone x0.6).
        public float CrouchFactor = 0.8f;
        public float ProneFactor = 0.6f;
        // ADR-090 aim punch: a hit throws the view up to this much (up / sideways), times the vest factor below.
        public float AimPunchPitchDeg = 1.6f;
        public float AimPunchYawDeg = 0.9f;
        // Indexed by vest tier 0-3: a vest takes the same share off the punch as off the damage (30/40/55%).
        public float[] AimPunchVestFactor = { 1f, 0.7f, 0.6f, 0.45f };

        public float StanceFactor(Stance stance) => stance == Stance.Prone ? ProneFactor : stance == Stance.Crouch ? CrouchFactor : 1f;
        public float VestFactor(int tier) =>
            AimPunchVestFactor == null || AimPunchVestFactor.Length == 0 ? 1f : AimPunchVestFactor[Math.Max(0, Math.Min(AimPunchVestFactor.Length - 1, tier))];
        public float RecoverRate(WeaponDef weapon) =>
            weapon != null && weapon.RecoilRecoverDegPerSecond > 0f ? weapon.RecoilRecoverDegPerSecond : RecoverDegPerSecond;
    }

    // ADR-089: each weapon's recoil pattern. Every shot kicks the view up by the weapon's RecoilPitchDeg (+-10%)
    // and sideways by its bias plus a random share, both times `factor` (aim mode x body stance). ViewRecoil adds
    // them up within the vertical cap and the weapon's side cap, so a spray climbs and drifts the weapon's way.
    public static class RecoilPattern
    {
        public static void Next(WeaponDef weapon, Random rng, float factor, out float pitchDeg, out float yawDeg)
        {
            pitchDeg = weapon.RecoilPitchDeg * (0.9f + 0.2f * (float)rng.NextDouble()) * factor;
            yawDeg = (weapon.RecoilYawBias + (float)(rng.NextDouble() * 2.0 - 1.0)) * weapon.RecoilYawDeg * factor;
        }

        // Kicks `recoil` with the next shot of the weapon.
        public static void Kick(ViewRecoil recoil, WeaponDef weapon, Random rng, float factor, RecoilRules rules)
        {
            Next(weapon, rng, factor, out float pitch, out float yaw);
            recoil.Kick(pitch, yaw, rules.MaxPitchDeg, weapon.RecoilYawMaxDeg > 0f ? weapon.RecoilYawMaxDeg * factor : float.PositiveInfinity);
        }

        // ADR-090: being hit throws the view up and to a random side; a vest makes it smaller.
        public static void Punch(ViewRecoil recoil, Random rng, int vestTier, RecoilRules rules)
        {
            float f = rules.VestFactor(vestTier);
            float pitch = rules.AimPunchPitchDeg * (0.6f + 0.4f * (float)rng.NextDouble()) * f;
            float yaw = rules.AimPunchYawDeg * (float)(rng.NextDouble() * 2.0 - 1.0) * f;
            recoil.Kick(pitch, yaw, rules.MaxPitchDeg);
        }
    }

    public static class Crosshair
    {
        // Distance from the screen centre to the edge of the spread cone, in pixels: where the crosshair lines sit.
        // `spreadDeg` is the full cone angle after the stance factor; `fovDeg` is the camera's vertical field of view.
        public static float GapPixels(float spreadDeg, float fovDeg, float screenHeight)
        {
            const double rad = Math.PI / 180.0;
            return (float)(Math.Tan(spreadDeg * 0.5 * rad) / Math.Tan(fovDeg * 0.5 * rad) * screenHeight * 0.5);
        }
    }

    // Turns the aim button into an aim mode: hold = shoulder while held, tap = toggle sights.
    public class AimInput
    {
        readonly float _tapSeconds;
        bool _wasHeld;
        bool _ignorePress;      // this press was cancelled; wait for the button to come up
        bool _startedInSights;
        double _pressedAt;

        public AimMode Mode { get; private set; }

        public AimInput(float tapSeconds) => _tapSeconds = tapSeconds;

        // Call every frame. `canAim` is false when there is nothing to aim with (no weapon, dead).
        public AimMode Update(bool held, double time, bool canAim)
        {
            if (!canAim)
            {
                Mode = AimMode.Hip;
                _ignorePress = held;
                _wasHeld = held;
                return Mode;
            }
            if (_ignorePress)
            {
                if (!held) _ignorePress = false;
                _wasHeld = held;
                return Mode;
            }

            if (held && !_wasHeld)
            {
                _pressedAt = time;
                _startedInSights = Mode == AimMode.Sights;
            }
            else if (held)
            {
                if (Mode != AimMode.Sights && time - _pressedAt >= _tapSeconds) Mode = AimMode.Shoulder;
            }
            else if (_wasHeld)
            {
                if (time - _pressedAt < _tapSeconds) Mode = _startedInSights ? AimMode.Hip : AimMode.Sights;
                else if (Mode == AimMode.Shoulder) Mode = AimMode.Hip;
            }
            _wasHeld = held;
            return Mode;
        }
    }

    // View kick from firing: each shot pushes the view, then it settles back.
    public class ViewRecoil
    {
        public float Pitch { get; private set; } // degrees, up positive
        public float Yaw { get; private set; }
        float _sinceKick;

        // `maxYawDeg`: the side offset never goes past this either way.
        public void Kick(float pitchDeg, float yawDeg, float maxPitchDeg, float maxYawDeg = float.PositiveInfinity)
        {
            Pitch = Math.Min(maxPitchDeg, Pitch + pitchDeg);
            Yaw = Math.Max(-maxYawDeg, Math.Min(maxYawDeg, Yaw + yawDeg));
            _sinceKick = 0f;
        }

        // Settles back at `recoverDegPerSecond`, but only once `delaySeconds` have passed since the last kick.
        public void Tick(float dt, float recoverDegPerSecond, float delaySeconds = 0f)
        {
            _sinceKick += dt;
            if (_sinceKick < delaySeconds) return;
            float length = (float)Math.Sqrt(Pitch * Pitch + Yaw * Yaw);
            if (length < 1e-5f) { Pitch = 0f; Yaw = 0f; return; }
            float left = Math.Max(0f, length - recoverDegPerSecond * dt) / length;
            Pitch *= left;
            Yaw *= left;
        }
    }

    // How far the lean has got: -1 = fully left, 1 = fully right.
    public class LeanState
    {
        public float Value { get; private set; }

        public void Tick(int target, float dt, float changePerSecond)
        {
            float step = changePerSecond * dt;
            Value = Value < target ? Math.Min(target, Value + step) : Math.Max(target, Value - step);
        }
    }

    // Detects the moment a foot comes down from its height above the ground. Two thresholds so that a foot
    // hovering near the ground does not count as stepping.
    public class FootContact
    {
        readonly float _plantBelow, _liftAbove;
        bool _lifted;

        public FootContact(float plantBelow, float liftAbove)
        {
            if (liftAbove <= plantBelow) throw new ArgumentException("liftAbove must be higher than plantBelow");
            _plantBelow = plantBelow;
            _liftAbove = liftAbove;
        }

        // True on the frame the foot lands.
        public bool Update(float height)
        {
            if (!_lifted)
            {
                if (height > _liftAbove) _lifted = true;
                return false;
            }
            if (height >= _plantBelow) return false;
            _lifted = false;
            return true;
        }
    }
}
