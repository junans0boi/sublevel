using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Hands with things in them (#220 #221 #212): a throwable held at the chest with the left hand pulling the pin
    // and an overhand throw, healing and drinking with the item in hand, and the reach for loot or a door. The poses
    // are ItemMotion's pure functions; here they are tracked from the replicated state (throwable count and cooking,
    // the healing use) and put on the arms with IK over whatever else poses them. First person reads ItemPose too.
    public partial class CombatantAnimation
    {
        const float ItemWeightPerSecond = 9f;
        ThrowableUser _throwUser;
        CombatantHealing _healUser;
        HandPropView _props;
        int _lastThrowTotal = -1;
        float _throwStart = -10f, _windup, _gestureStart = -10f, _itemW, _flinch, _flinchSide = 1f;
        HandGesture _gesture;
        bool _itemHidesWeapon;

        // The hands pose this frame (zero weight when none) and how far it is on, 0..1.
        public HandsPose ItemPose { get; private set; }
        public float ItemWeight => _itemW;
        public bool ItemActive => _itemW > 0.001f;
        // Hits shown so far (a flinch each).
        public int FlinchesShown { get; private set; }

        public void StartGesture(HandGesture g) { _gesture = g; _gestureStart = Time.time; }

        public HandPropView Props()
        {
            if (_props != null) return _props;
            var body = _animator != null ? _animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (body == null || body.sharedMaterial == null) return null;
            return _props = new HandPropView(body.sharedMaterial);
        }

        void TrackItems(bool dead, bool hit)
        {
            if (_throwUser == null) _throwUser = GetComponent<ThrowableUser>();
            if (_healUser == null) _healUser = GetComponent<CombatantHealing>();
            float dt = Time.deltaTime;
            if (hit)
            {
                _flinch = 1f; _flinchSide = -_flinchSide; FlinchesShown++;
                _kick = Mathf.Max(_kick, 0.6f); // the weapon jolts with the body (#224)
            }
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt / 0.4f);

            HandsPose pose = default;
            bool active = false;
            _itemHidesWeapon = false;
            if (dead) { _lastThrowTotal = -1; }
            else
            {
                if (_healUser != null && _healUser.Using)
                {
                    pose = ItemMotion.Heal(_healUser.Kind, _healUser.Progress01);
                    active = true; _itemHidesWeapon = true;
                }
                else if (_throwUser != null)
                {
                    int total = _throwUser.TotalCount;
                    bool inHand = _combatant.ActiveSlot.Value == Loadout.ThrowableSlot && !_combatant.Holstered.Value;
                    if (_lastThrowTotal >= 0 && total < _lastThrowTotal && inHand) _throwStart = Time.time;
                    _lastThrowTotal = total;
                    float release = (Time.time - _throwStart) / ItemMotion.ThrowSeconds;
                    bool throwing = release >= 0f && release < 1f;
                    var local = PlayerController.Local;
                    bool held = _combatant == Combatant.LocalPlayer && local != null && local.LastInput.ThrowHeld;
                    _windup = Mathf.MoveTowards(_windup, held ? 1f : 0f, dt / 0.18f);
                    if (inHand && (total > 0 || throwing))
                    {
                        pose = ItemMotion.Throw(ItemMotion.PropOf(_throwUser.SelectedKind), _windup, throwing ? release : -1f, _throwUser.Cooking.Value);
                        active = true;
                    }
                }
                if (!active && Time.time - _gestureStart < (_gesture == HandGesture.Door ? ItemMotion.DoorSeconds : ItemMotion.PickupSeconds))
                {
                    float len = _gesture == HandGesture.Door ? ItemMotion.DoorSeconds : ItemMotion.PickupSeconds;
                    pose = ItemMotion.Gesture(_gesture, (Time.time - _gestureStart) / len);
                    active = _gesture != HandGesture.None;
                }
            }
            _itemW = Mathf.MoveTowards(_itemW, active ? 1f : 0f, ItemWeightPerSecond * dt);
            if (active) ItemPose = pose;
            else if (_itemW <= 0.001f) ItemPose = default;
        }

        // LateUpdate, after the torso and the weapon hold: the item hands and the body's flinch.
        void PoseItems()
        {
            if (_flinch > 0.001f)
            {
                // A hit throws the chest back and to one side for a moment.
                Turn(Torso, -15f * _flinch, transform.right);
                Turn(Torso, 12f * _flinch * _flinchSide, transform.up);
                Turn(Torso, 8f * _flinch * _flinchSide, transform.forward);
            }
            var props = Props();
            if (_itemW <= 0.001f || _upperArmR == null || _wrist[0] == null)
            {
                if (props != null && (_weapons == null || !_weapons.AtEye)) props.Hide();
                return;
            }
            var pose = ItemPose;
            float w = _itemW;
            Turn(Torso, pose.TorsoPitchDeg * w, transform.right);
            Turn(Torso, pose.TorsoTwistDeg * w, transform.up);
            var frame = transform.rotation;
            // Armed with a long gun, the free left hand does the reaching.
            bool leftReach = _rifle > 0.5f && (_gesture != HandGesture.None) && pose.LeftW <= 0f && Time.time - _gestureStart < 1f;
            if (leftReach)
            {
                Reach(0, ArmLeft, _bones[ArmLeft].position + frame * new Vector3(-pose.RX, pose.RY, pose.RZ), pose.RightW * w);
                CloseHand(0, GripCurlDeg * pose.Curl * pose.RightW * w);
                return;
            }
            if (pose.RightW > 0f)
            {
                Reach(1, ArmRight, _upperArmR.position + frame * new Vector3(pose.RX, pose.RY, pose.RZ), pose.RightW * w);
                CloseHand(1, GripCurlDeg * pose.Curl * pose.RightW * w);
            }
            if (pose.LeftW > 0f)
            {
                Reach(0, ArmLeft, _bones[ArmLeft].position + frame * new Vector3(pose.LX, pose.LY, pose.LZ), pose.LeftW * w);
                CloseHand(0, GripCurlDeg * 0.7f * pose.LeftW * w);
            }
            if (props != null && (_weapons == null || !_weapons.AtEye))
            {
                if (pose.Prop != HandProp.None && _wrist[1] != null)
                {
                    var tip = _fingerTip[1] != null ? _fingerTip[1].position : _wrist[1].position + frame * Vector3.forward * 0.1f;
                    // Held in the palm, standing on its end (a bottle tips toward the mouth).
                    var turn = Quaternion.LookRotation(frame * Vector3.forward, Vector3.up);
                    props.Show(pose.Prop, Vector3.Lerp(_wrist[1].position, tip, 0.55f), turn);
                }
                else props.Hide();
            }
        }
    }
}
