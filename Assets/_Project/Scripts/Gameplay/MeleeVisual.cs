using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The melee weapon in first person (ADR-073). WeaponVisual moves the held model in front of the camera like a
    // gun; this runs after it and holds a melee weapon low on the right with the head up instead, and plays the swing
    // there: a slash sweeps the blade across from right to left, a chop comes down from above. Same swing timing and
    // pose numbers as the third-person bone turns (Melee.SwingAt / Melee.PoseAt). In the hand (third person)
    // nothing is done here: the model hangs from the hand bone that CombatantAnimation turns.
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(Combatant), typeof(WeaponVisual))]
    public class MeleeVisual : MonoBehaviour
    {
        // Camera space: x right, y up, z forward. The grip's spot and the model's turn when nothing is swung.
        // Low on the right with the head tipped forward (#219: held upright at the face, the blade filled half the view as a flat plate).
        [SerializeField] Vector3 _restPosition = new Vector3(0.28f, -0.2f, 0.45f);
        [SerializeField] Vector3 _restEuler = new Vector3(55f, 32f, 0f);
        // At rest the blade is also turned about its own length so its edge, not its flat, faces the eye (the flat read as a plate).
        [SerializeField] float _restSpinDeg = 45f;
        // The body turns with a swing (#219): weapon and arms are carried round a point at the chest by the third-person torso twist and pitch.
        [SerializeField] Vector3 _chestPivot = new Vector3(0f, -0.45f, 0f);
        [SerializeField] float _torsoShare = 1f;
        // Slash: sideways travel per unit of sweep (x) and lift / push forward at full swing (y, z); the blade is
        // tipped forward and rolled over toward level while swinging, and turned with the sweep.
        [SerializeField] Vector3 _slashTravel = new Vector3(0.2f, 0.1f, 0.08f);
        [SerializeField] float _slashPitchDeg = 70f;
        [SerializeField] float _slashRollDeg = -75f;
        [SerializeField] float _slashYawPerSweep = 0.9f;
        // A slashing blade is turned so its flat faces the camera and its edge leads to the left.
        [SerializeField] float _slashEdgeYawDeg = -90f;
        // Chop: how high the grip rises at full wind-up and how far forward it reaches at the end of the strike.
        [SerializeField] float _chopLift = 0.28f;
        [SerializeField] float _chopReach = 0.12f;

        Combatant _combatant;
        WeaponVisual _weaponVisual;
        int _swingWeapon;
        float _swingStart, _swingDuration;

        // True on frames the melee weapon was placed at the eye; the swing phase shown then.
        public bool PlacedAtEye { get; private set; }
        public MeleeSwingPhase Phase { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _weaponVisual = GetComponent<WeaponVisual>();
            _combatant.Punched += OnPunch;
        }

        void OnDestroy() => _combatant.Punched -= OnPunch;

        void OnPunch(in Combatant.PunchResult punch)
        {
            var weapon = punch.WeaponId != 0 ? GameData.Weapons.Find(punch.WeaponId) : null;
            if (weapon == null || !weapon.IsMelee) return;
            _swingWeapon = punch.WeaponId;
            _swingStart = Time.time;
            _swingDuration = Melee.SwingDuration(weapon, MeleeRules.Current);
        }

        void LateUpdate()
        {
            PlacedAtEye = false;
            var cam = _weaponVisual.EyeCamera;
            int id = _combatant.ActiveWeaponId;
            var weapon = id != 0 ? GameData.Weapons.Find(id) : null;
            if (cam == null || weapon == null || !weapon.IsMelee) return;
            var model = _weaponVisual.ModelFor(id);
            if (model == null) return;

            var rules = MeleeRules.Current;
            float elapsed = Time.time - _swingStart;
            var phase = _swingWeapon == id && elapsed < _swingDuration ? Melee.SwingAt(elapsed, _swingDuration, rules) : default;
            Phase = phase;
            var pose = Melee.PoseAt(weapon.Swing, in phase, rules);
            float w = phase.Weight;
            var position = _restPosition;
            var euler = _restEuler;
            if (weapon.Swing == MeleeSwingStyle.Chop)
            {
                float span = Mathf.Max(1f, rules.ChopWindUpDeg - rules.ReadyArmDeg);
                float lift = Mathf.Clamp((pose.ArmRaiseDeg - rules.ReadyArmDeg) / span, -0.2f, 1f);
                position += new Vector3(0f, _chopLift * lift, _chopReach * Mathf.Max(0f, phase.Swing) * w);
                // Over the head the head of the weapon points back; at the end of the strike it points ahead and down.
                euler.x = Mathf.Lerp(_restEuler.x, 90f - pose.ArmRaiseDeg, w);
            }
            else
            {
                float sweep = pose.ArmSweepDeg / Mathf.Max(1f, rules.SlashSweepDeg); // +1 wound up right, -1 followed through left
                position += new Vector3(sweep * _slashTravel.x, w * _slashTravel.y, w * _slashTravel.z);
                euler = new Vector3(Mathf.Lerp(_restEuler.x, _slashPitchDeg, w), _restEuler.y + _slashEdgeYawDeg - pose.ArmSweepDeg * _slashYawPerSweep,
                    Mathf.Lerp(_restEuler.z, _slashRollDeg, w));
            }
            // The torso's turn this frame (positive twist = to the right, positive pitch = forward), about the chest.
            var torso = Quaternion.AngleAxis(pose.TorsoTwistDeg * _torsoShare * w, Vector3.up) * Quaternion.AngleAxis(pose.TorsoPitchDeg * _torsoShare * w, Vector3.right);
            position = _chestPivot + torso * (position - _chestPivot);
            var t = model.transform;
            t.rotation = cam.rotation * torso * Quaternion.Euler(euler) * Quaternion.AngleAxis(_restSpinDeg * (1f - w), Vector3.up);
            t.position = cam.position + cam.rotation * position;
            PlacedAtEye = true;
            _weaponVisual.PlaceMeleeArms(t, id, w, cam.rotation * torso);
        }
    }
}
