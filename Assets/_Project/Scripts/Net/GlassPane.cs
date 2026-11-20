using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // A pane that shattered, sent by the server to every client; `Quiet` for a late joiner catching up (no effect).
    public struct GlassShattered : IBroadcast
    {
        public int Id;
        public bool Quiet;
    }

    // A pane of glass in a window (ADR-065 (4)): a thin see-through box that blocks movement until a bullet or a
    // body going through the window breaks it. It sits on the Ignore Raycast layer, so sight lines, the camera and
    // ordinary shots pass through it (a shot breaks it on the way through, see GlassPanes.ServerBreakAlong).
    // Not a network object: every peer has the same panes from the scene, identified by Id.
    public class GlassPane : MonoBehaviour
    {
        public const int Layer = 2; // built-in Ignore Raycast
        public const int LayerMask = 1 << Layer;

        [SerializeField] int _id = -1;

        static readonly List<GlassPane> AllPanes = new List<GlassPane>();
        public static IReadOnlyList<GlassPane> All => AllPanes;

        // Raised on every peer that shows a pane breaking (not for a quiet catch-up), for the effects.
        public static event Action<GlassPane> Shattered;

        Collider[] _colliders;
        Renderer[] _renderers;

        public int Id => _id;
        public bool Broken { get; private set; }
        // The pane's look, for the shards.
        public Material Material => _renderers != null && _renderers.Length > 0 ? _renderers[0].sharedMaterial : null;
        // Where the pane is, taken while it was whole (a hidden renderer's bounds are not to be trusted).
        public Bounds Bounds
        {
            get
            {
                if (!_hasBounds && !Broken && _renderers != null && _renderers.Length > 0)
                {
                    _bounds = _renderers[0].bounds;
                    _hasBounds = true;
                }
                return _hasBounds ? _bounds : new Bounds(transform.position, Vector3.one * 0.5f);
            }
        }
        Bounds _bounds;
        bool _hasBounds;

        void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
            _renderers = GetComponentsInChildren<Renderer>();
            gameObject.layer = Layer;
            AllPanes.Add(this);
        }

        void OnDestroy() => AllPanes.Remove(this);

        // Panes made in code (tests) get their id here.
        public void SetId(int id) => _id = id;

        public static GlassPane Find(int id)
        {
            foreach (var pane in AllPanes)
                if (pane != null && pane.Id == id) return pane;
            return null;
        }

        // Out of the way for good (a restart reloads the scene). True when it was whole until now.
        public bool Break()
        {
            if (Broken) return false;
            _ = Bounds; // remember where it was before hiding it
            Broken = true;
            foreach (var c in _colliders) if (c != null) c.enabled = false;
            foreach (var r in _renderers) if (r != null) r.enabled = false;
            return true;
        }

        // Shows the shattering (shards, sound) on this peer. Breaks it too if the message got here first.
        internal void ShowShatter()
        {
            Break();
            Shattered?.Invoke(this);
        }
    }

    // The scene's window panes over the network: the server breaks them and tells everyone (and each client that
    // loads later). Added next to MatchDirector; works without one too (then only this process knows).
    public class GlassPanes : MonoBehaviour
    {
        public static GlassPanes Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            ServerBreaks = 0;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        NetworkManager _network;
        bool _bound;
        public static int ServerBroadcasts; // GlassShattered sent to everyone (ServerPerfProbe, #331 gate)
        readonly HashSet<int> _broken = new HashSet<int>();

        // Server: panes broken so far (tests).
        public static int ServerBreaks { get; private set; }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (!_bound || _network == null) return;
            _network.ClientManager.UnregisterBroadcast<GlassShattered>(OnShattered);
            _network.SceneManager.OnClientLoadedStartScenes -= OnClientLoaded;
        }

        public void Bind(NetworkManager network)
        {
            if (_bound || network == null) return;
            _bound = true;
            _network = network;
            _network.ClientManager.RegisterBroadcast<GlassShattered>(OnShattered);
            _network.SceneManager.OnClientLoadedStartScenes += OnClientLoaded;
        }

        bool ServerActive => _network != null && _network.IsServerStarted;

        // Server: breaks a pane, tells every client and lets the bots hear it. `sourceId`: who broke it (a bot does
        // not investigate its own noise).
        public static void ServerBreak(GlassPane pane, int sourceId)
        {
            if (pane == null || pane.Broken) return;
            pane.Break();
            ServerBreaks++;
            var at = pane.transform.position;
            BotNoise.Emit(at.x, at.y, at.z, BotRules.Current.GlassHearingRadius, sourceId, NoiseKind.Other);
            var self = Instance;
            if (self == null || !self.ServerActive)
            {
                // No network to carry it (a scene without a match director): show it here.
                pane.ShowShatter();
                return;
            }
            self._broken.Add(pane.Id);
            ServerBroadcasts++;
            self._network.ServerManager.Broadcast(new GlassShattered { Id = pane.Id });
        }

        // Server: a body going through a window breaks the pane the owner named, if it is anywhere near.
        public static void ServerBreakNear(int id, Vector3 near, float meters, int sourceId)
        {
            var pane = GlassPane.Find(id);
            if (pane == null || pane.Broken) return;
            if ((pane.Bounds.ClosestPoint(near) - near).sqrMagnitude > meters * meters) return;
            ServerBreak(pane, sourceId);
        }

        // Server: a shot from `origin` along `direction` for `distance` metres breaks every pane on the way.
        public static void ServerBreakAlong(Vector3 origin, Vector3 direction, float distance, int sourceId)
        {
            if (GlassPane.All.Count == 0 || !(distance > 0f)) return;
            int n = Physics.RaycastNonAlloc(origin, direction, Hits, distance, GlassPane.LayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var pane = Hits[i].collider.GetComponentInParent<GlassPane>();
                if (pane != null) ServerBreak(pane, sourceId);
            }
        }

        void OnClientLoaded(NetworkConnection connection, bool asServer)
        {
            if (!asServer || !ServerActive || !connection.IsAuthenticated) return;
            foreach (int id in _broken) _network.ServerManager.Broadcast(connection, new GlassShattered { Id = id, Quiet = true });
        }

        void OnShattered(GlassShattered message, Channel channel)
        {
            var pane = GlassPane.Find(message.Id);
            if (pane == null) return;
            if (message.Quiet) pane.Break();
            else pane.ShowShatter();
        }
    }
}
