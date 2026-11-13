using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Server -> clients: a door changed (also sent to a client that finishes loading, Quiet = no sound).
    public struct DoorChanged : IBroadcast
    {
        public int Index;
        public byte State;
        public bool Quiet;
    }

    // Client -> server: F on a door (the server decides which way it swings from where the sender stands).
    public struct DoorToggleRequest : IBroadcast
    {
        public int Index;
    }

    // One hinged door in an opening of a building (ADR-104, G11). The panel is a box that turns about its hinge and
    // stops sight lines, shots and bodies while it is shut; bots' paths go through the opening, the bots open the door.
    public class Door : MonoBehaviour
    {
        public static readonly List<Door> All = new List<Door>();

        public int Index { get; private set; }
        public DoorState State { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        // The wall's normal, flat; the opening's centre at floor level.
        public Vector3 Forward => transform.forward;
        public Vector3 Right => transform.right;
        public float Angle => _angle;
        public bool Blocking => Doors.Blocks(_angle, DoorRules.Current);
        public Collider Panel => _box;

        Transform _pivot;
        BoxCollider _box;
        AudioSource _audio;
        float _angle;
        static Material _material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            _material = null;
            Moved = null;
        }

        // `centre`: the middle of the opening at floor level; `facing`: the wall normal (z). Hinge on the left.
        public static Door Create(Transform parent, int index, Vector3 centre, Quaternion facing, float width, float height)
        {
            var rules = DoorRules.Current;
            var go = new GameObject("Door" + index.ToString("000"));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, facing);
            var door = go.AddComponent<Door>();
            door.Index = index;
            door.Width = width;
            door.Height = height;
            var pivot = new GameObject("Hinge").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(-width / 2f, 0f, 0f);
            door._pivot = pivot;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(pivot, false);
            panel.transform.localPosition = new Vector3(width / 2f, height / 2f, 0f);
            panel.transform.localScale = new Vector3(width - 0.04f, height - 0.03f, rules.PanelThickness);
            var renderer = panel.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = PanelMaterial();
            door._box = panel.GetComponent<BoxCollider>();
            // A dark handle on the free edge, both faces.
            foreach (float face in new[] { -1f, 1f })
            {
                var knob = GameObject.CreatePrimitive(PrimitiveType.Cube);
                knob.name = "Handle";
                Destroy(knob.GetComponent<Collider>());
                knob.transform.SetParent(pivot, false);
                knob.transform.localPosition = new Vector3(width - 0.14f, height * 0.45f, face * (rules.PanelThickness / 2f + 0.025f));
                knob.transform.localScale = new Vector3(0.12f, 0.035f, 0.05f);
                knob.GetComponent<MeshRenderer>().sharedMaterial = HandleMaterial();
            }
            door._audio = go.AddComponent<AudioSource>();
            door._audio.spatialBlend = 1f;
            door._audio.playOnAwake = false;
            door._audio.minDistance = 2f;
            door._audio.maxDistance = 25f;
            door._audio.rolloffMode = AudioRolloffMode.Linear;
            All.Add(door);
            return door;
        }

        void OnDestroy() => All.Remove(this);

        // Moves the door; plays its sound when the state really changes.
        public void Set(DoorState state, bool quiet = false)
        {
            if (state == State) return;
            State = state;
            if (quiet) { _angle = Doors.TargetAngle(state, DoorRules.Current); Apply(); return; }
            PlaySound(state != DoorState.Closed);
        }

        void Update()
        {
            float target = Doors.TargetAngle(State, DoorRules.Current);
            if (_angle == target) return;
            _angle = Doors.StepAngle(_angle, target, Time.deltaTime, DoorRules.Current);
            Apply();
        }

        void Apply() => _pivot.localRotation = Quaternion.Euler(0f, _angle, 0f);

        // Raised when a door starts to open (true) or shut (false); Game.Gameplay's DoorAudio plays the sound.
        public static event System.Action<Door, bool> Moved;
        public AudioSource Audio => _audio;

        void PlaySound(bool opening) => Moved?.Invoke(this, opening);

        static Material PanelMaterial() => _material != null ? _material : _material = Make("Door", new Color(0.36f, 0.25f, 0.15f), 0.25f);
        static Material _handle;
        static Material HandleMaterial() => _handle != null ? _handle : _handle = Make("DoorHandle", new Color(0.08f, 0.08f, 0.09f), 0.6f);

        static Material Make(string name, Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }
    }
}
