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
        public float OffsetMeters = 0.45f;   // how far the eyes move sideways at full lean
        public float EyeDropMeters = 0.08f;
        public float CameraRollDeg = 12f;
        public float BodyRollDeg = 18f;
        public float ChangePerSecond = 7f;   // full lean takes 1 / this seconds
        public float MoveFactor = 0.85f;
    }

    [Serializable]
    public class RecoilRules
    {
        public float RecoverDegPerSecond = 14f;
        public float MaxPitchDeg = 12f;
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

        public void Kick(float pitchDeg, float yawDeg, float maxPitchDeg)
        {
            Pitch = Math.Min(maxPitchDeg, Pitch + pitchDeg);
            Yaw += yawDeg;
        }

        public void Tick(float dt, float recoverDegPerSecond)
        {
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
