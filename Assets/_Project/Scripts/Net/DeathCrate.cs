using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // What a combatant leaves behind when it dies: everything it carried (both weapons with their magazines, the
    // reserve ammo of each type, medkits, armour with the durability it had left).
    // Server: on the combatant's death the contents are taken out of its loadout and laid out as ordinary
    // LootItems in a small ring around the body, so picking them up works like any other loot.
    // Every side: a marker box stands over the body for a while. It follows the replicated Dead flag, so the
    // crate needs no network object or prefab of its own; it sits on the combatant prefab next to Combatant.
    [RequireComponent(typeof(Combatant))]
    public class DeathCrate : MonoBehaviour
    {
        // Optional: without it the marker gets a plain lit material made at run time.
        [SerializeField] Material _markerMaterial;

        const float WallProbeHeight = 0.5f;   // the ring is checked against walls at this height over the feet
        const float GroundProbeUp = 1.0f;     // the ground under an item is looked for from this far above the feet
        const float GroundProbeDown = 2.5f;   // ... down to this far below them
        static readonly Vector3 MarkerSize = new Vector3(0.7f, 0.45f, 0.5f);
        static readonly Color MarkerColor = new Color(0.32f, 0.27f, 0.2f);

        static readonly List<LootEntry> Contents = new List<LootEntry>(8);
        static Material _fallbackMaterial;

        Combatant _combatant;
        CombatantGear _gear;
        GameObject _marker;
        float _markerUntil;
        bool _seenDead, _started;

        // The marker box is standing over the body right now (any side).
        public bool MarkerVisible => _marker != null && _marker.activeSelf;
        // Server: the contents were laid out, and how many pickups that made.
        public bool ServerOpened { get; private set; }
        public int ServerItemCount { get; private set; }

        void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _gear = GetComponent<CombatantGear>();
        }

        // The event is static: every subscriber lets go of it itself.
        void OnEnable() => Combatant.ServerDied += OnServerDied;
        void OnDisable() => Combatant.ServerDied -= OnServerDied;

        void OnDestroy()
        {
            if (_marker != null) Destroy(_marker);
        }

        void Update()
        {
            bool dead = _combatant.Dead.Value;
            // Someone who was already dead when this side first saw them gets no marker: theirs has had its time.
            if (!_started) { _started = true; _seenDead = dead; }
            if (dead && !_seenDead) ShowMarker();
            _seenDead = dead;
            if (_marker != null && _marker.activeSelf && Time.time >= _markerUntil) _marker.SetActive(false);
        }

        void OnServerDied(Combatant victim, DamageInfo info)
        {
            if (victim != _combatant || ServerOpened || !_combatant.IsServerInitialized) return;
            // Nowhere to put the items: leave the loadout alone.
            if (MatchDirector.Instance == null) return;
            ServerOpen();
        }

        // ---- Server ----

        void ServerOpen()
        {
            ServerOpened = true;
            Contents.Clear();
            ServerTakeLoadout(Contents);
            if (_gear != null)
            {
                var helmet = _gear.ServerRemove(ArmorSlot.Helmet);
                if (helmet.Worn) Contents.Add(Armor.ToLoot(ArmorSlot.Helmet, helmet));
                var vest = _gear.ServerRemove(ArmorSlot.Vest);
                if (vest.Worn) Contents.Add(Armor.ToLoot(ArmorSlot.Vest, vest));
            }
            ServerItemCount = Contents.Count;
            for (int i = 0; i < Contents.Count; i++)
                MatchDirector.Instance.ServerSpawnLoot(Contents[i], ServerItemPosition(i, Contents.Count));
            Contents.Clear();
        }

        // Moves everything out of the victim's loadout into `into` and clears what clients see of it. Rounds a
        // non-player carried beyond Combatant.DroppedReserveCap per calibre are not dropped (a dummy holds 9999).
        void ServerTakeLoadout(List<LootEntry> into) => _combatant.ServerStripLoadout(into);

        // Item `index` of `count` on the ring around the body: pulled in where a wall crosses the ring, and set
        // on the ground under it (slopes, steps).
        Vector3 ServerItemPosition(int index, int count)
        {
            var rules = CrateRules.Current;
            var body = transform.position;
            LootScatter.RingOffset(index, count, rules.RingRadius, out float x, out float z);
            var offset = new Vector3(x, 0f, z);
            float reach = offset.magnitude;
            var position = body;
            if (reach > 1e-4f)
            {
                var direction = offset / reach;
                if (Physics.Raycast(body + Vector3.up * WallProbeHeight, direction, out var wall, reach + rules.WallClearance,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    reach = Mathf.Max(0f, wall.distance - rules.WallClearance);
                position += direction * reach;
            }
            if (Physics.Raycast(position + Vector3.up * GroundProbeUp, Vector3.down, out var ground, GroundProbeUp + GroundProbeDown,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && ground.collider.GetComponentInParent<Combatant>() == null)
                position.y = ground.point.y;
            position.y += rules.LiftMeters;
            return position;
        }

        // ---- Marker (every side) ----

        void ShowMarker()
        {
            if (_marker == null)
            {
                var material = _markerMaterial != null ? _markerMaterial : FallbackMaterial();
                if (material == null) return; // no shader to draw it with: the items are there all the same
                _marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _marker.name = "DeathCrateMarker";
                // Only to be seen: it never blocks movement or shots.
                var box = _marker.GetComponent<Collider>();
                if (box != null)
                {
                    box.enabled = false;
                    Destroy(box);
                }
                _marker.GetComponent<Renderer>().sharedMaterial = material;
                _marker.transform.SetParent(transform, false);
                _marker.transform.localPosition = new Vector3(0f, MarkerSize.y * 0.5f, 0f);
                _marker.transform.localScale = MarkerSize;
            }
            _marker.SetActive(true);
            _markerUntil = Time.time + CrateRules.Current.MarkerSeconds;
        }

        static Material FallbackMaterial()
        {
            // Unity-null after a scene change threw the unused material away.
            if (_fallbackMaterial != null) return _fallbackMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            _fallbackMaterial = new Material(shader) { name = "DeathCrateMarker" };
            _fallbackMaterial.SetColor("_BaseColor", MarkerColor);
            return _fallbackMaterial;
        }
    }
}
