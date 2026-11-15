using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Melee weapons in third person (ADR-073). No clip: like the jump and the crouch, the pose is bone turns on top
    // of whatever the base layer plays (the upper layer stays off, so legs and the free arm move as with empty hands).
    // Holding one, the weapon arm is turned a little forward (the ready pose). A swing (Combatant.Punched with a
    // weapon id) winds up and strikes over a fraction of a second: a slash raises the arm to about level and sweeps
    // it from right to left with the torso twisting along; a chop takes the arm over the head and brings it down in
    // front with the torso bending into it. The weapon model hangs from the hand, so it follows the arm.
    // The numbers are in rules.json "Melee"; Melee.SwingAt / Melee.PoseAt work them out.
    public partial class CombatantAnimation
    {
        const float MeleeHoldPerSecond = 6f; // how fast the ready pose comes and goes when the weapon is taken or put away

        int _meleeLookupId = -1;  // weapon id HoldsMelee last looked at
        WeaponDef _meleeInHand;   // the melee weapon in hand, or null
        int _swingWeapon;         // weapon of the swing being shown (0 = none)
        float _swingStart, _swingDuration;
        float _meleeHold;         // 0..1: how much of the melee pose is shown

        // Swings shown so far.
        public int SwingsShown { get; private set; }
        // Where the swing being shown is (rest when none), and the pose shown for it this frame.
        public MeleeSwingPhase SwingPhase { get; private set; }
        public MeleePose MeleePoseShown { get; private set; }
        // How much of the melee pose (ready or swinging) is shown, 0..1.
        public float MeleeHold => _meleeHold;

        // A melee weapon is in hand (not put away).
        public bool HoldsMelee
        {
            get
            {
                int id = _combatant.ActiveWeaponId;
                if (id != _meleeLookupId)
                {
                    _meleeLookupId = id;
                    var weapon = id != 0 ? GameData.Weapons.Find(id) : null;
                    _meleeInHand = weapon != null && weapon.IsMelee ? weapon : null;
                }
                return _meleeInHand != null;
            }
        }

        void StartSwing(int weaponId)
        {
            var weapon = GameData.Weapons.Find(weaponId);
            if (weapon == null || !weapon.IsMelee) return;
            _swingWeapon = weaponId;
            _swingStart = Time.time;
            _swingDuration = Melee.SwingDuration(weapon, MeleeRules.Current);
            SwingsShown++;
        }

        // Called from LateUpdate after the jump, lean and parkour turns.
        void PoseMelee()
        {
            bool dead = _combatant.Dead.Value;
            bool holding = !dead && HoldsMelee;
            var rules = MeleeRules.Current;
            float elapsed = Time.time - _swingStart;
            if (_swingWeapon != 0 && (dead || elapsed >= _swingDuration)) _swingWeapon = 0;
            // Seated or vaulting, the arms are busy elsewhere.
            float wanted = holding || _swingWeapon != 0 ? 1f - Mathf.Max(_seated, _parkour) : 0f;
            _meleeHold = Mathf.MoveTowards(_meleeHold, wanted, MeleeHoldPerSecond * Time.deltaTime);
            var phase = _swingWeapon != 0 ? Melee.SwingAt(elapsed, _swingDuration, rules) : default;
            SwingPhase = phase;
            if (_meleeHold <= 1e-3f)
            {
                MeleePoseShown = default;
                return;
            }
            var weapon = _swingWeapon != 0 ? GameData.Weapons.Find(_swingWeapon) : _meleeInHand;
            var style = weapon != null ? weapon.Swing : MeleeSwingStyle.Slash;
            var pose = Melee.PoseAt(style, in phase, rules);
            MeleePoseShown = pose;
            float k = _meleeHold;
            // Positive about the up axis turns toward the right; positive about the right axis tips forward.
            Turn(Torso, pose.TorsoTwistDeg * k, transform.up);
            Turn(Torso, pose.TorsoPitchDeg * k, transform.right);
            // A negative turn about the right axis swings a hanging arm forward (and on up over the head).
            Turn(ArmRight, -pose.ArmRaiseDeg * k, transform.right);
            Turn(ArmRight, pose.ArmSweepDeg * k, transform.up);
            // The hand closes round the grip instead of hanging open (#219).
            if (!_gripBonesLooked) FindGripBones();
            CloseHand(1, GripCurlDeg * 0.9f * k);
        }
    }
}
