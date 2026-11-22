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
        public Vector3 At; // the door's place: DoorSystem.Resolve finds it when the peers' lists disagree
        public byte State;
        public bool Quiet;
    }

    // Server -> clients: a door took damage (Hp in whole points). Quiet = a late joiner catching up (no sound).
    public struct DoorDamaged : IBroadcast
    {
        public int Index;
        public Vector3 At; // the door's place: DoorSystem.Resolve finds it when the peers' lists disagree
        public ushort Hp;
        public bool Quiet;
    }

    // Client -> server: the driver's car hit a door at this closing speed (the server checks the distance).
    public struct DoorRamRequest : IBroadcast
    {
        public int Index;
        public Vector3 At; // the door's place: DoorSystem.Resolve finds it when the peers' lists disagree
        public float Speed;
    }

    // Client -> server: F on a door (the server decides which way it swings from where the sender stands).
    public struct DoorToggleRequest : IBroadcast
    {
        public int Index;
        public Vector3 At; // the door's place: DoorSystem.Resolve finds it when the peers' lists disagree
    }

    // One hinged door in an opening of a building (ADR-104, G11). The panel is a box that turns about its hinge and
    // stops sight lines, shots and bodies while it is shut; bots' paths go through the opening, the bots open the door.
    public class Door : MonoBehaviour
    {
        public static readonly List<Door> All = new List<Door>();
        const float HandleLodHeight = 0.04f;

        public int Index { get; private set; }
        public DoorState State { get; private set; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        // The wall's normal, flat; the opening's centre at floor level.
        public Vector3 Forward => transform.forward;
        public Vector3 Right => transform.right;
        public float Angle => _angle;
        public bool Blocking => State != DoorState.Broken && Doors.Blocks(_angle, DoorRules.Current);
        public Collider Panel => _box;
        public float Hp { get; private set; } = DoorRules.Current.MaxHp;
        public int Stage => Doors.Stage(Hp, DoorRules.Current);
        public Transform Hinge => _pivot;
        public Material PanelLook => PanelMaterial();

        Transform _pivot;
        BoxCollider _box;
        AudioSource _audio;
        float _angle;
        float _droop;
        Transform _damage;
        int _shownStage;
        static Material _material, _dent, _splinter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            _material = null;
            Moved = null;
            Hit = null;
            Broke = null;
            _dent = _splinter = null;
        }

        static bool Headless => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        // `centre`: the middle of the opening at floor level; `facing`: the wall normal (z). Hinge on the left.
        public static Door Create(Transform parent, int index, Vector3 centre, Quaternion facing, float width, float height)
        {
            var rules = DoorRules.Current;
            var go = new GameObject("Door" + index.ToString("000"));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, facing);
            var door = go.AddComponent<Door>();
            door.Index = index;
            door.Hp = rules.MaxHp;
            door.Width = width;
            door.Height = height;
            var pivot = new GameObject("Hinge").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(-width / 2f, 0f, 0f);
            door._pivot = pivot;
            // The shader-less dedicated server gets the collider only: a primitive's renderer and the materials there log a
            // warning each, ~5500 for the map, and journald then drops the match start's lines (r5: no "[Doors]" in the journal).
            var panel = Headless ? new GameObject("Panel", typeof(BoxCollider)) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(pivot, false);
            panel.transform.localPosition = new Vector3(width / 2f, height / 2f, 0f);
            panel.transform.localScale = new Vector3(width - 0.04f, height - 0.03f, rules.PanelThickness);
            if (!Headless) panel.GetComponent<MeshRenderer>().sharedMaterial = PanelMaterial();
            door._box = panel.GetComponent<BoxCollider>();
            // A dark handle on the free edge, both faces.
            var handles = new List<Renderer>();
            if (!Headless) foreach (float face in new[] { -1f, 1f })
            {
                var knob = GameObject.CreatePrimitive(PrimitiveType.Cube);
                knob.name = "Handle";
                Destroy(knob.GetComponent<Collider>());
                knob.transform.SetParent(pivot, false);
                knob.transform.localPosition = new Vector3(width - 0.14f, height * 0.45f, face * (rules.PanelThickness / 2f + 0.025f));
                knob.transform.localScale = new Vector3(0.12f, 0.035f, 0.05f);
                var knobRenderer = knob.GetComponent<MeshRenderer>();
                knobRenderer.sharedMaterial = HandleMaterial();
                knobRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                handles.Add(knobRenderer);
            }
            // #452 (ADR-547): 3,668 handles were ~235 draws down a town street. Past ~45 m (door under 4% of the screen) only
            // the panel draws; the panel itself never culls (an open doorway in a facade would read as a hole).
            if (!Headless)
            {
                var panelRenderer = panel.GetComponent<MeshRenderer>();
                handles.Add(panelRenderer);
                go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(HandleLodHeight, handles.ToArray()), new LOD(0f, new Renderer[] { panelRenderer }) });
            }
            All.Add(door);
            return door;
        }

        void OnDestroy() => All.Remove(this);

        // Moves the door; plays its sound when the state really changes.
        public void Set(DoorState state, bool quiet = false)
        {
            if (state == State || State == DoorState.Broken) return;
            State = state;
            if (state == DoorState.Broken) { Break(quiet); return; }
            if (quiet) { _angle = Doors.TargetAngle(state, DoorRules.Current); Apply(); return; }
            PlaySound(state != DoorState.Closed);
        }

        // ---- Damage (ADR-147) ----

        // Raised on every peer when the door took a hit (not a quiet catch-up) and when it broke (with `quiet` false).
        public static event System.Action<Door> Hit;
        public static event System.Action<Door> Broke;

        // Sets the hit points the server reports; the look follows the stage.
        public void SetHp(float hp, bool quiet = false)
        {
            if (State == DoorState.Broken) return;
            Hp = hp;
            int stage = Stage;
            if (stage != _shownStage) { _shownStage = stage; ShowDamage(stage); }
            if (!quiet) Hit?.Invoke(this);
        }

        void Break(bool quiet)
        {
            Hp = 0f;
            _box.enabled = false;
            foreach (var r in _pivot.GetComponentsInChildren<Renderer>()) r.enabled = false;
            if (!quiet) Broke?.Invoke(this);
        }

        // Dents and splinters on both faces from stage 1, a dark crack and a sagging hinge from stage 2. Placed from the
        // door's index, so every peer shows the same marks.
        void ShowDamage(int stage)
        {
            if (_damage != null) Destroy(_damage.gameObject);
            _droop = stage >= 2 ? -3f : 0f;
            Apply();
            if (stage < 1 || stage > 2 || Headless) return;
            _damage = new GameObject("Damage").transform;
            _damage.SetParent(_pivot, false);
            var rng = new System.Random(Index * 7919 + 17);
            float R() => (float)rng.NextDouble();
            float half = DoorRules.Current.PanelThickness / 2f + 0.004f;
            int dents = stage == 1 ? 5 : 11;
            foreach (float face in new[] { -1f, 1f })
            {
                for (int i = 0; i < dents; i++)
                {
                    float x = 0.12f + (Width - 0.24f) * R(), y = 0.25f + (Height - 0.5f) * R();
                    Piece(_damage, DentMaterial(), new Vector3(x, y, face * half), new Vector3(0.04f + 0.06f * R(), 0.09f + 0.14f * R(), 0.01f), 30f * (R() - 0.5f));
                    // a pale sliver standing out of the dent
                    Piece(_damage, SplinterMaterial(), new Vector3(x + 0.04f * (R() - 0.5f), y, face * (half + 0.02f)), new Vector3(0.015f, 0.10f + 0.12f * R(), 0.015f), 40f * (R() - 0.5f));
                }
                if (stage >= 2)
                    Piece(_damage, DentMaterial(), new Vector3(Width * 0.5f, Height * 0.45f, face * half), new Vector3(0.015f, Height * 0.7f, 0.012f), 12f + 20f * R());
            }
        }

        static void Piece(Transform parent, Material material, Vector3 at, Vector3 size, float rollDeg)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Mark";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rollDeg);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void Update()
        {
            if (State == DoorState.Broken) return;
            float target = Doors.TargetAngle(State, DoorRules.Current);
            if (_angle == target) return;
            _angle = Doors.StepAngle(_angle, target, Time.deltaTime, DoorRules.Current);
            Apply();
        }

        void Apply() => _pivot.localRotation = Quaternion.Euler(0f, _angle, 0f) * Quaternion.Euler(0f, 0f, _droop);

        // Raised when a door starts to open (true) or shut (false); Game.Gameplay's DoorAudio plays the sound.
        public static event System.Action<Door, bool> Moved;
        // Made on the first sound (#484): ~3,700 idle sources, one per door, cost the audio update every frame.
        public AudioSource Audio
        {
            get
            {
                if (_audio != null) return _audio;
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.spatialBlend = 1f;
                _audio.playOnAwake = false;
                _audio.minDistance = 2f;
                _audio.maxDistance = 25f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
                return _audio;
            }
        }

        void PlaySound(bool opening) => Moved?.Invoke(this, opening);

        static Material PanelMaterial() => _material != null ? _material : _material = Make("Door", new Color(0.36f, 0.25f, 0.15f), 0.25f);
        static Material DentMaterial() => _dent != null ? _dent : _dent = Make("DoorDent", new Color(0.17f, 0.105f, 0.06f), 0.1f);
        static Material SplinterMaterial() => _splinter != null ? _splinter : _splinter = Make("DoorSplinter", new Color(0.72f, 0.55f, 0.35f), 0.1f);
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
