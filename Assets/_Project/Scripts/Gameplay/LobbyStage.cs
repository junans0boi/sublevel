using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // The lobby's 3D scene (ADR-247): the elevator car built from the game's own materials (Editor/LobbyStageBuilder) with
    // the party's real roster characters standing in it in a low-ready rifle pose. Slot 0 is the local player and shows the
    // saved PlayerAppearance; the others show their PartyLobby member's Appearance. The menu UI is an overlay on top.
    public class LobbyStage : MonoBehaviour
    {
        public const int Slots = PartyLobby.SquadSize;
        // Local x of the four places across the car (slot 0, the local player, stands left and a little forward).
        static readonly Vector3[] SlotPos = { new Vector3(1.7f, 0f, 0f), new Vector3(0.58f, 0f, -0.1f), new Vector3(-0.58f, 0f, -0.1f), new Vector3(-1.7f, 0f, 0f) }; // the camera looks down -Z, so +X is on screen left (slot 0 = leftmost)

        // The slot of the local player (a step forward, so he is the biggest); set by the lobby panel.
        public int LocalSlot { get; set; }
        Vector3 PosFor(int slot) => _closeup && slot == LocalSlot ? new Vector3(0f, 0f, 0.35f) : SlotPos[slot] + (slot == LocalSlot ? new Vector3(0f, 0f, 0.35f) : Vector3.zero);
        float YawFor(int slot) => _closeup && slot == LocalSlot ? 20f : slot == LocalSlot ? 14f : slot < LocalSlot ? 6f : -8f;

        public GameObject[] RosterPrefabs;      // in PlayerAppearance.ModelIds order (set by the builder)
        public GameObject RifleTemplate;        // inactive model with its grip at its origin (set by the builder)
        public RuntimeAnimatorController IdleController; // the game's Character.controller: Humanoid rifle idle + breathing (set by the builder)
        public Camera Camera;
        public Transform CameraLobby, CameraCloseup;

        readonly GameObject[] _chars = new GameObject[Slots];
        readonly bool[] _animated = new bool[Slots];
        readonly PlayerAppearance?[] _looks = new PlayerAppearance?[Slots];
        bool _closeup;
        float _t;

        public bool Closeup => _closeup;
        public PlayerAppearance? LookOf(int slot) => _looks[slot];
        public GameObject CharacterOf(int slot) => _chars[slot];

        void Awake()
        {
            if (Camera == null) Camera = Camera.main;
            if (CameraLobby != null && Camera != null) Snap();
        }

        // Shows `look` in `slot`, or empties the slot (null). Rebuilds only when the look changed.
        public void SetSlot(int slot, PlayerAppearance? look)
        {
            if (slot < 0 || slot >= Slots) return;
            if (look.HasValue && _looks[slot].HasValue && _looks[slot].Value.Equals(look.Value) && _chars[slot] != null) return;
            _looks[slot] = look;
            if (_chars[slot] != null) { Destroy(_chars[slot]); _chars[slot] = null; }
            if (!look.HasValue) return;
            var a = look.Value.Normalized();
            var prefab = RosterPrefabs != null && a.ModelIndex < RosterPrefabs.Length ? RosterPrefabs[a.ModelIndex] : null;
            if (prefab == null) return;
            var go = Instantiate(prefab, transform);
            go.name = "Lobby_" + a.ModelId;
            go.transform.localPosition = PosFor(slot);
            go.transform.localRotation = Quaternion.Euler(0f, YawFor(slot), 0f);
            var def = go.GetComponent<CharacterDef>();
            if (def != null)
            {
                def.ApplyVariant(a.Variant);
                if (def.Animator != null && IdleController != null)
                {
                    // The game's own Humanoid rifle idle (shared clips, breathing layer), each person a little out of step.
                    def.Animator.runtimeAnimatorController = IdleController;
                    def.Animator.applyRootMotion = false;
                    def.Animator.speed = 0.92f + 0.05f * ((slot * 7) % 4);
                    def.Animator.Update(0f);
                    def.Animator.Play(def.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, (slot * 0.27f) % 1f);
                    _animated[slot] = true;
                }
                else
                {
                    if (def.Animator != null) def.Animator.enabled = false; // no controller: a held low-ready pose
                    PoseLowReady(def);
                    _animated[slot] = false;
                }
                AttachRifle(def, go);
            }
            if (a.Helmet > 0 || a.Vest > 0) go.AddComponent<ArmorVisual>().ShowTiers(a.Helmet, a.Vest);
            if (a.Pack > 0) go.AddComponent<BackpackVisual>().Show(a.Pack);
            _chars[slot] = go;
            go.SetActive(!_closeup || slot == LocalSlot);
        }

        // Closeup (외형 screen): only the local player, centred, the camera in close. Off = the whole party.
        public void SetCloseup(bool on, bool instant = false)
        {
            _closeup = on;
            for (int i = 0; i < Slots; i++)
            {
                if (_chars[i] == null) continue;
                _chars[i].SetActive(!on || i == LocalSlot);
                _chars[i].transform.localPosition = PosFor(i);
                _chars[i].transform.localRotation = Quaternion.Euler(0f, YawFor(i), 0f);
            }
            if (instant) Snap();
        }

        // Turns the local character on the spot (drag in the 외형 screen).
        public void Spin(float degrees)
        {
            if (_chars[LocalSlot] != null) _chars[LocalSlot].transform.Rotate(0f, degrees, 0f, Space.World);
        }

        void Snap()
        {
            var to = _closeup ? CameraCloseup : CameraLobby;
            if (Camera == null || to == null) return;
            Camera.transform.SetPositionAndRotation(to.position, to.rotation);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            var to = _closeup ? CameraCloseup : CameraLobby;
            if (Camera != null && to != null)
            {
                // A very slow drift (parallax) around the framing; none with reduced motion.
                var drift = UiFx.ReducedMotion ? Vector3.zero : new Vector3(Mathf.Sin(_t * 0.16f) * 0.14f, Mathf.Sin(_t * 0.11f) * 0.05f, 0f);
                var pos = to.position + to.rotation * drift;
                float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
                Camera.transform.SetPositionAndRotation(Vector3.Lerp(Camera.transform.position, pos, k), Quaternion.Slerp(Camera.transform.rotation, to.rotation, k));
            }
            // A slow breath so nobody stands like a statue: chest and head sway a few millimetres.
            for (int i = 0; i < Slots; i++)
            {
                if (_chars[i] == null) continue;
                var def = _chars[i].GetComponent<CharacterDef>();
                if (def == null || def.Sockets.Spine == null || _animated[i]) continue;
                float s = Mathf.Sin(_t * 1.6f + i * 1.3f) * 0.6f;
                def.Sockets.Spine.localRotation *= Quaternion.Euler(s * 0.02f, 0f, 0f);
            }
        }

        // Where a slot's feet and head are in the view, as 0..1 viewport coordinates for a view of `aspect` (the UI's own
        // canvas aspect, not the window's: the overlay labels are placed in canvas units). False when there is no camera.
        public bool ViewportPoints(int slot, float aspect, out Vector2 feet, out Vector2 head)
        {
            feet = head = default;
            if (Camera == null || aspect <= 0f) return false;
            var p = transform.TransformPoint(PosFor(slot));
            feet = Project(p, aspect);
            head = Project(p + Vector3.up * 1.85f, aspect);
            return true;
        }

        Vector2 Project(Vector3 world, float aspect)
        {
            var c = Camera.transform.InverseTransformPoint(world);
            float t = 2f * Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float z = Mathf.Max(0.01f, c.z);
            return new Vector2(0.5f + c.x / z / (t * aspect), 0.5f + c.y / z / t);
        }

        // Low-ready rifle pose from the T-pose by turning world-space bone rotations (same as Editor/CharacterRoster.PoseLowReady).
        public static void PoseLowReady(CharacterDef def)
        {
            var s = def.Sockets;
            void Rot(Transform t, Quaternion q) { if (t != null) t.rotation = q * t.rotation; }
            Rot(s.RightUpperArm, Quaternion.AngleAxis(-78f, Vector3.up)); Rot(s.RightUpperArm, Quaternion.AngleAxis(38f, Vector3.right));
            Rot(s.RightLowerArm, Quaternion.AngleAxis(-70f, Vector3.up));
            Rot(s.LeftUpperArm, Quaternion.AngleAxis(78f, Vector3.up)); Rot(s.LeftUpperArm, Quaternion.AngleAxis(38f, Vector3.right));
            Rot(s.LeftLowerArm, Quaternion.AngleAxis(48f, Vector3.up));
        }

        void AttachRifle(CharacterDef def, GameObject character)
        {
            if (RifleTemplate == null || def.Sockets.WeaponSocket == null) return;
            var w = Instantiate(RifleTemplate, def.Sockets.WeaponSocket);
            w.SetActive(true);
            // The template carries the muzzle-forward yaw of CharacterKit.InstantiateWeapon; point it forward and a little down.
            w.transform.rotation = character.transform.rotation * Quaternion.Euler(18f, -28f, 0f) * RifleTemplate.transform.localRotation;
            w.transform.position = def.Sockets.WeaponSocket.position + character.transform.forward * 0.18f;
        }
    }
}
