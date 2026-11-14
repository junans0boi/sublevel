using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FishNet.Managing;
using FishNet.Transporting;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #172: one whole multiplayer match in the real World scene, end to end, through the real lobby: the menu's host +
    // 3 remote clients, bots fill the start size, synchronized elevator deployment, looting (guns lie unloaded, ammo apart),
    // firefights human vs human and human vs bot at 100 ms / 2 % loss, a remote driver, lockdown stages and a supply drop,
    // a remote player dying (death cam, result panel, spectate), the last survivor winning, result data on every client,
    // and the way back to the menu's lobby. Milestones and per-client numbers go to Logs/net-match/.
    // Shots are taken through the remote clients' own cameras (MATCH_SHOTS=<folder>, default Logs/matchshots).
    // The harness keeps every client in this one process; what the test cannot prove is listed in the report/log header.
    [Category("Long"), Explicit("about 3-5 minutes with 16 combatants; run on purpose: test playmode Game.Tests.MatchEndToEndTests")]
    public class MatchEndToEndTests
    {
        const string LogDir = "Logs/net-match";

        readonly StringBuilder _log = new StringBuilder();
        readonly Stopwatch _clock = new Stopwatch();
        MultiHarness _net;
        FrameSampler _frames;
        Keys _keys;
        BotRules _bots;
        string _shots;
        readonly List<LobbyNet> _rooms = new List<LobbyNet>();
        readonly List<Wire> _wires = new List<Wire>();
        // [0] host, [1..3] remote clients A, B, C: the combatant as that client holds it.
        readonly List<string> _names = new List<string>();
        int _humanHitsOnHumans, _humanHitsOnBots, _botHitsOnHumans;

        sealed class Stopwatch
        {
            float _t0;
            public void Start() => _t0 = Time.realtimeSinceStartup;
            public float Seconds => Time.realtimeSinceStartup - _t0;
        }

        // What one client's own NetworkManager heard (a stand-in for that client's UI data) and how much it received.
        sealed class Wire
        {
            public string Name;
            public NetworkManager Manager;
            public long Bytes;
            public double BytesFrom;
            public readonly List<ElevatorDoorStatus> Doors = new List<ElevatorDoorStatus>();
            public readonly List<double> DoorTimes = new List<double>();
            public readonly List<ZoneUpdate> Zones = new List<ZoneUpdate>();
            public readonly List<SupplyDropped> Supply = new List<SupplyDropped>();
            public readonly List<KillEntry> Kills = new List<KillEntry>();
            public readonly List<MatchStatus> Status = new List<MatchStatus>();
            public readonly List<PlayerStats> Stats = new List<PlayerStats>();
            public MatchStatus LastStatus;

            public void Attach(NetworkManager nm)
            {
                Manager = nm;
                var c = nm.ClientManager;
                c.RegisterBroadcast<ElevatorDoorStatus>((m, ch) => { Doors.Add(m); DoorTimes.Add(Time.timeAsDouble); });
                c.RegisterBroadcast<ZoneUpdate>((m, ch) => Zones.Add(m));
                c.RegisterBroadcast<SupplyDropped>((m, ch) => Supply.Add(m));
                c.RegisterBroadcast<KillEntry>((m, ch) => Kills.Add(m));
                c.RegisterBroadcast<MatchStatus>((m, ch) => { Status.Add(m); LastStatus = m; });
                c.RegisterBroadcast<PlayerStats>((m, ch) => Stats.Add(m));
                nm.TransportManager.Transport.OnClientReceivedData += (ClientReceivedDataArgs a) => Bytes += a.Data.Count;
            }
        }

        // Every frame's time, by the phase the scenario is in.
        sealed class FrameSampler : MonoBehaviour
        {
            public string Phase = "start";
            public readonly Dictionary<string, List<float>> Ms = new Dictionary<string, List<float>>();
            public readonly List<string> Order = new List<string>();

            void Update()
            {
                if (!Ms.TryGetValue(Phase, out var list)) { Ms[Phase] = list = new List<float>(); Order.Add(Phase); }
                list.Add(Time.unscaledDeltaTime * 1000f);
            }
        }

        sealed class Keys : IVehicleInput
        {
            public VehicleInputFrame Frame = new VehicleInputFrame { Seat = -1 };

            public VehicleInputFrame Read()
            {
                var f = Frame;
                Frame.Interact = Frame.EngineToggle = Frame.ToggleView = Frame.Jerrycan = false;
                Frame.Seat = -1;
                return f;
            }
        }

        void Log(string line)
        {
            string stamped = $"[{_clock.Seconds,7:F1}s] {line}";
            Debug.Log("[Match] " + stamped);
            _log.AppendLine(stamped);
        }

        void Milestone(string name)
        {
            if (_frames != null) _frames.Phase = name;
            Log("MILESTONE " + name);
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        IEnumerator Until(Func<bool> cond, string what, float timeout = 20f) => NetHarness.WaitUntil(cond, what, Log, timeout);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _log.Clear();
            _clock.Start();
            _wires.Clear();
            _names.Clear();
            _humanHitsOnHumans = _humanHitsOnBots = _botHitsOnHumans = 0;
            MoveGuard.ResetStats();
            ServerGuard.ResetStats();
            NetSession.Reset();
            _bots = BotRules.Current;
            _shots = Environment.GetEnvironmentVariable("MATCH_SHOTS");
            if (string.IsNullOrEmpty(_shots)) _shots = "Logs/matchshots";
            _shots = Path.GetFullPath(_shots);
            Directory.CreateDirectory(_shots);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Combatant.ServerDamaged -= OnServerDamaged;
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "match-" + TestContext.CurrentContext.Test.Name + ".log"), _log.ToString());
            BotRules.Current = _bots;
            SupplyRules.Current = new SupplyRules();
            EnvironmentView.ForceTime = null;
            EnvironmentView.ForceWeather = null;
            SmokeZones.Clear();
            VehicleInput.InputOverride = null;
            foreach (var r in _rooms) if (r != null) { r.Shutdown(); UnityEngine.Object.Destroy(r.gameObject); }
            _rooms.Clear();
            if (_frames != null) UnityEngine.Object.Destroy(_frames.gameObject);
            if (_net != null) yield return _net.Teardown();
            NetSession.Reset();
        }

        // ---- picture taking: through a remote client's own camera ----

        void SetLocal(Combatant who, PlayerController controller)
        {
            typeof(PlayerController).GetProperty("Local").GetSetMethod(true).Invoke(null, new object[] { controller });
            typeof(Combatant).GetProperty("LocalPlayer").GetSetMethod(true).Invoke(null, new object[] { who });
        }

        void SetLocal(RemoteClient who) => SetLocal(who.Player, who.Controller);

        // The 3D view of `who`'s camera plus, if `overlay`, the HUD/screens drawn for that player (this process shows one player's UI at a time).
        IEnumerator Shot(string name, RemoteClient who, bool overlay = true, Vector3? focus = null, Vector3? from = null, bool eye = false)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Log("no graphics device: no shot " + name); yield break; }
            SetLocal(who);
            yield return Seconds(0.4f); // the camera rig follows the player it is made local
            var cam = who.Controller.Camera;
            if (cam == null) { Log("SHOT FAILED (no camera) " + name); yield break; }
            // In this one process the camera of a client that was not "local" for a while stays where it last was (seen: B's stayed at
            // the gun it picked up); a real client always follows its own body. Put it behind the body (or looking at `focus`) then.
            var bodyT = who.Player.transform;
            if (Vector3.Distance(cam.transform.position, bodyT.position) > 6f && (ResultScreen.Instance == null || ResultScreen.Instance.Current == ResultScreen.Phase.None))
            {
                float away = Vector3.Distance(cam.transform.position, bodyT.position);
                var at = from ?? (eye ? bodyT.position + Vector3.up * 1.6f + bodyT.forward * 0.25f : bodyT.position - bodyT.forward * 3.2f + Vector3.up * 2.2f);
                var look = focus ?? (eye ? at + bodyT.forward * 5f : bodyT.position + Vector3.up * 1.3f);
                cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look - at));
                Log($"shot {name}: the client's camera was {away:F0} m from its body; placed behind the body");
            }
            string path = Path.Combine(_shots, name + ".png");
            Log($"shot {name}: body {who.Player.transform.position}, camera {cam.transform.position}, camera enabled {cam.enabled}, main {(Camera.main != null ? Camera.main.name : "none")}");
            // The client's own 3D view first (camera.Render): proven in the replication captures; then, with the screens it shows.
            Capture(cam, overlay ? Path.Combine(_shots, name + "_3d.png") : path);
            if (overlay)
            {
                // UiShot copies Camera.main: make it this client's camera for the one frame.
                var others = new List<(Camera cam, string tag)>();
                foreach (var c in Camera.allCameras)
                    if (c != cam && c.CompareTag("MainCamera")) { others.Add((c, c.tag)); c.tag = "Untagged"; }
                string before = cam.tag;
                cam.tag = "MainCamera";
                yield return UiShot.Save(path, 1600, 900);
                cam.tag = before;
                foreach (var o in others) o.cam.tag = o.tag;
            }
            Log("SHOT " + path);
        }

        static void Capture(Camera camera, string path, int width = 1600, int height = 900)
        {
            var rt = new RenderTexture(width, height, 24);
            var before = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            camera.targetTexture = before;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }

        // ---- helpers ----

        Combatant Server(Combatant clientCopy) => MultiHarness.ReplicaOf(_net.Host, clientCopy);

        IEnumerable<Combatant> ServerCombatants()
        {
            foreach (var nob in _net.Host.ServerManager.Objects.Spawned.Values)
            {
                var c = nob != null ? nob.GetComponent<Combatant>() : null;
                if (c != null) yield return c;
            }
        }

        void OnServerDamaged(Combatant victim, DamageInfo info, float applied)
        {
            if (info.Attacker == null || victim == null) return;
            if (info.Attacker.IsPlayer && victim.IsPlayer) _humanHitsOnHumans++;
            else if (info.Attacker.IsPlayer) _humanHitsOnBots++;
            else if (victim.IsPlayer) _botHitsOnHumans++;
        }

        // The ground point under (x, z) or null.
        static Vector3? GroundAt(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.9f) return hit.point;
            return null;
        }

        // Teleports `who` next to `to` and waits a moment (the server takes teleports in tests, MoveGuard.TrustTeleports).
        IEnumerator Go(RemoteClient who, Vector3 to)
        {
            who.Controller.Teleport(to);
            yield return Seconds(0.3f);
        }

        // Picks up the gun `weapon` (a server LootItem) and the ammo item beside it with `who`'s own request, checks the
        // gun came unloaded, then reloads with the reload key. `onClient`: that client's copy of a server object.
        IEnumerator Arm(string label, Combatant clientBody, PlayerController controller, HarnessInput input, NetworkManager manager, LootItem weapon, LootItem ammo)
        {
            var server = Server(clientBody);
            int wid = weapon.WeaponId.Value; // read now: a despawned loot item clears its values
            if (controller != null) controller.Teleport(weapon.transform.position + Vector3.up * 0.1f);
            yield return Seconds(0.5f);
            LootItem Copy(LootItem l)
            {
                return manager.ClientManager.Objects.Spawned.TryGetValue(l.ObjectId, out var nob) ? nob.GetComponent<LootItem>() : null;
            }
            var wc = Copy(weapon);
            Assert.IsNotNull(wc, label + ": the gun is on the client's side");
            var def = GameData.Weapons.Find(wid);
            int ammoAmount = ammo.Amount.Value;
            int reserveBefore = server.ReserveFor(def.Ammo);
            clientBody.CmdPickup(wc.NetworkObject);
            int SlotOf() { for (int i = 0; i < Combatant.SlotCount; i++) if (server.WeaponInSlot(i) == wid) return i; return -1; }
            yield return Until(() => SlotOf() >= 0, label + " holds the picked gun on the server");
            int slot = SlotOf();
            Assert.AreEqual(0, server.MagInSlot(slot), label + ": a picked-up gun is unloaded (ADR-136)");
            // The ammo item is its own pickup. Walking over it with a matching gun also collects it by itself (auto pickup):
            // either way it must end up in the reserve; if nothing took it in a second, ask for it like the F key does.
            float wait = Time.time + 1.2f;
            while (ammo != null && ammo.IsSpawned && Time.time < wait) yield return null;
            if (ammo != null && ammo.IsSpawned)
            {
                var ac = Copy(ammo);
                Assert.IsNotNull(ac, label + ": the ammo is on the client's side");
                clientBody.CmdPickup(ac.NetworkObject);
            }
            yield return Until(() => server.ReserveFor(def.Ammo) >= reserveBefore + Mathf.Min(ammoAmount, 1), label + " got the ammo as reserve");
            // Hold the gun, reload by key.
            input.Frame.SelectSlot = slot;
            float giveUp = Time.time + 2f;
            while (server.ActiveWeaponId != wid && Time.time < giveUp) { input.Frame.SelectSlot = slot; yield return null; }
            if (server.ActiveWeaponId != wid)
                Log($"WARN {label}: slot key {slot + 1} did not bring the gun out (active slot {server.ActiveSlot.Value}, holstered {server.Holstered.Value}, dead {server.Dead.Value}, input blocked {SpawnProtection.InputBlocked}, result blocks {ResultScreen.BlocksGameplay}, local {PlayerController.Local != null && PlayerController.Local.gameObject == controller?.gameObject})");
            yield return Until(() => server.ActiveWeaponId == wid, label + " has the gun in hand (slot key " + (slot + 1) + ")", 5f);
            input.Frame.Reload = true;
            yield return Until(() => server.MagInSlot(slot) > 0, label + " loaded the gun with R", 10f);
            Log($"{label}: {def.Name} unloaded on pickup, ammo separate (+{server.ReserveFor(def.Ammo) - reserveBefore}), mag {server.MagInSlot(slot)} after R");
        }

        // Rifles/SMGs with an ammo item lying at the same spot: pairs of server loot the lobby's map rolled.
        List<(LootItem weapon, LootItem ammo)> GunPairs()
        {
            var pairs = new List<(LootItem, LootItem)>();
            foreach (var w in LootItem.All)
            {
                if (w == null || !w.IsServerInitialized || w.Kind.Value != (int)LootKind.Weapon) continue;
                var def = GameData.Weapons.Find(w.WeaponId.Value);
                if (def == null || def.IsMelee) continue;
                foreach (var a in LootItem.All)
                    if (a != null && a.IsServerInitialized && a.Kind.Value == (int)LootKind.Ammo && a.Ammo.Value == (int)def.Ammo && (a.transform.position - w.transform.position).sqrMagnitude < 1f)
                    { pairs.Add((w, a)); break; }
            }
            return pairs;
        }

        // ---- the scenario ----

        [UnityTest, Timeout(1200000)]
        public IEnumerator WholeMatch_Lobby_To_LastSurvivor_ResultsOnEveryClient_BackToTheLobby()
        {
            var frameGo = new GameObject("MatchFrameSampler");
            _frames = frameGo.AddComponent<FrameSampler>();
            UnityEngine.Object.DontDestroyOnLoad(frameGo);
            _net = new MultiHarness { Log = Log };
            _net.PrepareHostInput();
            // Scenario scaffolding moves people between phases by teleport (a real match never does, #170); play itself is not teleported.
            MoveGuard.TrustTeleports = true;
            // The server picks the sky: a clear day, so the review shots show something.
            EnvironmentView.ForceTime = DayTimeChoice.Day;
            EnvironmentView.ForceWeather = WeatherChoice.Clear;
            _keys = new Keys();
            VehicleInput.InputOverride = _keys;

            // ===== 1. lobby: the menu's host, three guests join through the real lobby, bots fill the start size =====
            Milestone("lobby");
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return Until(() => MainMenu.Instance != null, "main menu");
            yield return null;
            MainMenu.Instance.OpenHost();
            var hostRoom = MainMenu.Instance.Lobby.Lobby;
            yield return Until(() => hostRoom.Phase == LobbyNet.PhaseKind.InLobby, "menu host in its room");
            var guests = new List<LobbyNet>();
            for (int i = 0; i < 3; i++)
            {
                var g = new GameObject("MatchGuest" + i).AddComponent<LobbyNet>();
                _rooms.Add(g);
                guests.Add(g);
                Assert.IsTrue(g.Join("127.0.0.1", hostRoom.Port));
                yield return Until(() => g.Phase == LobbyNet.PhaseKind.InLobby, "guest " + i + " in the room");
            }
            yield return Until(() => hostRoom.Roster.Count == 4, "four people in the room");
            int startSize = (MatchRules.Current ?? new MatchRules()).ClampedBotCount + 1;
            int bots = LobbyRules.DefaultBots(4, startSize);
            hostRoom.SetBots(bots);
            foreach (var g in guests) g.SetReady(true);
            yield return Until(() => hostRoom.CanStart && guests.TrueForAll(g => g.Bots == bots), "everybody ready, bot count synced");
            Log($"lobby: 4 people + {bots} bots = start size {startSize}");
            Assert.AreEqual(startSize, 4 + bots, "bots fill to the configured start size");
            yield return Shot_Lobby();
            Assert.IsTrue(hostRoom.StartMatch());
            yield return Until(() => guests.TrueForAll(g => g.HasStarted), "every guest got the start");
            yield return Until(() => SceneManager.GetActiveScene().name == MainMenu.MatchScene && MatchDirector.Instance != null, "World loaded", 90f);
            yield return _net.AdoptHost();
            var director = MatchDirector.Instance;
            Assert.AreEqual(4, director.ExpectedPlayers);
            Assert.AreEqual(bots, director.BotCount);
            Combatant.ServerDamaged += OnServerDamaged;

            // ===== 2. the three remote clients connect to the match port the lobby told them =====
            Milestone("deployment");
            var host = new Wire { Name = "host" };
            host.Attach(_net.Host);
            _wires.Add(host);
            _names.Add("host");
            var clients = new List<RemoteClient>();
            for (int i = 0; i < 3; i++)
            {
                var wire = new Wire { Name = "client" + (char)('A' + i) };
                _wires.Add(wire);
                RemoteClient rc = null;
                yield return _net.AddClient(c => rc = c, null, nm => wire.Attach(nm));
                _net.Simulate(rc);
                clients.Add(rc);
                _names.Add(wire.Name);
            }
            RemoteClient A = clients[0], B = clients[1], C = clients[2];
            foreach (var p in new[] { _net.HostPlayer, A.Player, B.Player, C.Player })
                Server(p).ServerDamageFilter = info => info.Amount * 0.2f; // survivable test: no one dies by chance, only on purpose
            // Everybody is in its elevator: the doors are scheduled, the same moment for all clients.
            yield return Until(() => director.Elevators.ServerScheduled, "doors scheduled once the four stand in their elevators", 60f);
            yield return Until(() => _wires.TrueForAll(w => w.Doors.Count > 0 || w.Name == "host"), "every remote client heard the door schedule", 10f);
            // Where each human stands: in an elevator station of its own assignment.
            var sites = director.Elevators.Sites;
            foreach (var who in new[] { _net.HostPlayer, A.Player, B.Player, C.Player })
            {
                var p = Server(who).transform.position;
                float best = float.MaxValue;
                foreach (var s in sites) best = Mathf.Min(best, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.Spawn.x, s.Spawn.z)));
                Assert.Less(best, 3.5f, who.name + " starts in an elevator");
            }
            var openAt = new List<double>();
            foreach (var w in _wires)
                if (w.Doors.Count > 0) openAt.Add(w.DoorTimes[w.DoorTimes.Count - 1] + w.Doors[w.Doors.Count - 1].OpensIn);
            double spread = openAt.Count > 1 ? Max(openAt) - Min(openAt) : 0;
            Log($"deployment: doors open for {openAt.Count} remote clients within {spread * 1000:F0} ms of each other (server {director.Elevators.ServerOpenAt - Time.timeAsDouble:F2} s from now)");
            Assert.Less(spread, 0.6, "the elevator doors open at the same moment for every client");
            yield return Shot("01_remoteA_in_elevator", A, true, null, null, true);
            yield return Until(() => !director.Elevators.ServerDoorsClosed, "doors open", 30f);
            yield return Seconds(1.5f);
            yield return Shot("02_remoteA_deployed", A, true, null, null, true);
            yield return Shot("03_remoteB_deployed", B, true, null, null, true);
            Assert.AreEqual(4 + bots, MatchState.Instance.TotalCount, "everyone is counted");
            Log($"match state: {MatchState.Instance.AliveCount} alive of {MatchState.Instance.TotalCount}");

            // ===== 3. looting: guns unloaded, ammo separate =====
            Milestone("loot");
            var pairs = GunPairs();
            Log($"loot: {pairs.Count} gun+ammo pairs on the map");
            Assert.GreaterOrEqual(pairs.Count, 4, "the map has guns with ammo beside them");
            // The fights need rounds: take the pairs with the biggest ammo items (a rolled rifle may lie with only 5 rounds).
            pairs.Sort((x, y) => y.ammo.Amount.Value.CompareTo(x.ammo.Amount.Value));
            yield return Arm("host", _net.HostPlayer, _net.HostPlayer.GetComponent<PlayerController>(), _net.HostInput, _net.Host, pairs[0].weapon, pairs[0].ammo);
            yield return Arm("A", A.Player, A.Controller, A.Input, A.Manager, pairs[1].weapon, pairs[1].ammo);
            yield return Arm("B", B.Player, B.Controller, B.Input, B.Manager, pairs[2].weapon, pairs[2].ammo);
            yield return Arm("C", C.Player, C.Controller, C.Input, C.Manager, pairs[3].weapon, pairs[3].ammo);
            yield return Shot("04_remoteA_armed", A);

            // ===== 4. firefights =====
            Milestone("firefight");
            // 4a. human vs human on an open floor (the world's streets are not flat enough to line two people up):
            // A shoots the host from 20 m while the host strafes, both at 100 ms.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "MatchArenaFloor";
            floor.transform.position = new Vector3(-4000f, -0.5f, -3940f);
            floor.transform.localScale = new Vector3(80f, 1f, 220f);
            Physics.SyncTransforms();
            var hostCtl = _net.HostPlayer.GetComponent<PlayerController>();
            hostCtl.Teleport(new Vector3(-4000f, 0.1f, -4000f));
            A.Controller.Teleport(new Vector3(-4000f, 0.1f, -3980f));
            // B watches from the side; the arena lies far outside the map (west of it), without its lighting, so it gets a lamp for the shot.
            var lamp = new GameObject("MatchArenaLamp").AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.intensity = 1.6f;
            lamp.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            B.Controller.Teleport(new Vector3(-3994f, 0.1f, -3990f));
            yield return Seconds(1f);
            B.Controller.LookAt(new Vector3(-4000f, 1.4f, -3990f));
            hostCtl.LookAt(new Vector3(-4000f, 1.6f, -3980f));
            int hitsBefore = _humanHitsOnHumans;
            int shots = 0;
            float nextDbg = 0f, nextTapA = 0f, nextTapB = 0f;
            float end = Time.time + 8f;
            while (Time.time < end)
            {
                var target = Server(_net.HostPlayer).transform.position + Vector3.up * 1.3f;
                A.Input.Frame.FireHeld = false; // one frame down per tap
                A.Controller.LookAt(target);
                // Strafes only in the last third: lag compensation under movement has its own tests (#162); here standing targets must be hit.
                _net.HostInput.Frame.Move = Time.time > end - 3f ? new Vector2(Mathf.Sin(Time.time * 2f), 0f) : Vector2.zero;
                if (Time.time > nextDbg)
                {
                    nextDbg = Time.time + 1f;
                    var sa = Server(A.Player); var sh = Server(_net.HostPlayer);
                    bool los = Physics.Raycast(sa.EyePosition, (target - sa.EyePosition).normalized, out var lh, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Log($"  firefight dbg: A mag {sa.ActiveMag} reload {sa.Reloading.Value} weapon {sa.ActiveWeaponId} dist {Vector3.Distance(sa.transform.position, sh.transform.position):F1} first hit {(los ? lh.collider.name : "none")} host hp {sh.Hp.Value:F0} hits {_humanHitsOnHumans - hitsBefore} guard {ServerGuard.Summary()}");
                }
                if (Server(A.Player).ActiveMag <= 0 && !Server(A.Player).Reloading.Value) A.Input.Frame.Reload = true; // R on an empty magazine
                else if (Time.time >= nextTapA) { A.Input.Frame.FirePressed = A.Input.Frame.FireHeld = true; nextTapA = Time.time + 0.25f; shots++; } // aimed taps: sustained fire only climbs the spread
                yield return null;
            }
            A.Input.Frame.FireHeld = false;
            _net.HostInput.Frame.Move = Vector2.zero;
            yield return Seconds(0.5f);
            int hHits = _humanHitsOnHumans - hitsBefore;
            Log($"firefight human vs human: A pressed fire {shots} times, {hHits} hits registered on the host's body (100 ms, 2 % loss)");
            Assert.Greater(hHits, 0, "a remote client's shots hit another human under 100 ms");
            // One more short burst, B's view taken while the muzzle flashes (the picture blocks this loop for a moment, so it runs apart from the counted one).
            A.Input.Frame.FireHeld = true;
            yield return Seconds(0.25f);
            yield return Shot("05_remoteB_sees_firefight", B, true, new Vector3(-4000f, 1.4f, -3990f), new Vector3(-3984f, 2.6f, -3990f));
            A.Input.Frame.FireHeld = false;
            UnityEngine.Object.Destroy(lamp.gameObject);

            // 4b. human vs bot in the real map: B finds a clear line to a bot and shoots it (a few bots are tried: they walk away).
            int botHitsBefore = _humanHitsOnBots, hitOnHumansBefore = _botHitsOnHumans;
            var tried = new HashSet<Combatant>();
            Combatant lastBot = null;
            for (int attempt = 0; attempt < 5 && _humanHitsOnBots == botHitsBefore; attempt++)
            {
                Combatant bot = null;
                Vector3 spot = default;
                foreach (var cand in ServerCombatants())
                {
                    if (cand.IsPlayer || cand.Dead.Value || tried.Contains(cand)) continue;
                    var chest = cand.transform.position + Vector3.up * 1.3f;
                    for (int ring = 0; ring < 2 && bot == null; ring++)
                        for (int k = 0; k < 16 && bot == null; k++)
                        {
                            float ang = k * Mathf.PI / 8f, r = 9f + ring * 5f;
                            var flat = cand.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                            var ground = GroundAt(flat);
                            if (ground == null || Mathf.Abs(ground.Value.y - cand.transform.position.y) > 1.5f) continue;
                            if (!NavMesh.SamplePosition(ground.Value, out _, 0.6f, NavMesh.AllAreas)) continue;
                            var eye = ground.Value + Vector3.up * 1.6f;
                            if (Physics.Raycast(eye, (chest - eye).normalized, out var hit, r + 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Combatant>() == cand)
                            { bot = cand; spot = ground.Value; }
                        }
                    if (bot != null) break;
                }
                Assert.IsNotNull(bot, "found a bot with a clear line of fire");
                tried.Add(bot);
                lastBot = bot;
                float botHpBefore = bot.Hp.Value;
                B.Controller.Teleport(spot + Vector3.up * 0.1f);
                yield return Seconds(0.5f);
                end = Time.time + 8f;
                shots = 0;
                while (Time.time < end && !bot.Dead.Value)
                {
                    B.Input.Frame.FireHeld = false;
                    B.Controller.LookAt(bot.transform.position + Vector3.up * 1.3f);
                    if (Server(B.Player).ActiveMag <= 0 && !Server(B.Player).Reloading.Value) B.Input.Frame.Reload = true;
                    else if (Time.time >= nextTapB) { B.Input.Frame.FirePressed = B.Input.Frame.FireHeld = true; nextTapB = Time.time + 0.2f; shots++; }
                    yield return null;
                }
                B.Input.Frame.FireHeld = false;
                Log($"firefight human vs bot (attempt {attempt + 1}): B fired ~{shots}, bot Hp {botHpBefore:F0} -> {bot.Hp.Value:F0} (dead {bot.Dead.Value}), human hits on bots {_humanHitsOnBots - botHitsBefore}, bot hits on humans so far {_botHitsOnHumans - hitOnHumansBefore}");
            }
            Assert.Greater(_humanHitsOnBots - botHitsBefore, 0, "a remote client's shots hit a bot");
            yield return Shot("06_remoteB_vs_bot", B, true, lastBot != null ? lastBot.transform.position + Vector3.up * 1.2f : (Vector3?)null);

            // ===== 5. a remote driver =====
            Milestone("vehicle");
            ReplicaLayers.SeparateVehicles = true;
            // Cars with 16 m of clear road ahead, nearest to B first; a car that will not go (parked against something) is left and the next tried.
            var candidates = new List<(float d, Vehicle v)>();
            foreach (var nob in _net.Host.ServerManager.Objects.Spawned.Values)
            {
                var v = nob != null ? nob.GetComponent<Vehicle>() : null;
                if (v == null || !v.HasFreeSeat || v.IsMotorcycle) continue;
                var from = v.transform.position + Vector3.up * 1f;
                bool clear = !Physics.SphereCast(from, 1.1f, v.transform.forward, out var wall, 16f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || wall.collider.GetComponentInParent<Vehicle>() == v;
                if (clear) candidates.Add((Vector3.Distance(v.transform.position, Server(B.Player).transform.position), v));
            }
            candidates.Sort((x, y) => x.d.CompareTo(y.d));
            Log($"vehicle: {candidates.Count} cars with clear road ahead");
            Assert.Greater(candidates.Count, 0, "the map has a car that can be driven away");
            Vehicle car = null;
            float moved = 0f, topSpeed = 0f, errMax = 0f;
            double errSum = 0; int errN = 0;
            Vehicle asSeenByA = null, asSeenByC = null;
            Vector3 startPos = default;
            for (int attempt = 0; attempt < Math.Min(4, candidates.Count) && moved <= 8f; attempt++)
            {
                car = candidates[attempt].v;
                // The server's copy stays where the wheels cast: the replicas each hold their own (in-process only).
                foreach (var col in car.Colliders) col.gameObject.layer = 2;
                Log($"vehicle attempt {attempt + 1}: {car.name}, {candidates[attempt].d:F0} m from B");
                SetLocal(B);
                B.Controller.Teleport(car.DoorPosition(0) + Vector3.up * 0.05f);
                yield return Seconds(0.4f);
                _keys.Frame.Interact = true;
                float enterBy = Time.time + 1.5f;
                while (car.OccupantId(0) != B.Player.ObjectId && Time.time < enterBy) yield return null;
                if (car.OccupantId(0) != B.Player.ObjectId)
                {
                    var sb = Server(B.Player);
                    Log($"WARN F did not board B (server body {Vector3.Distance(sb.transform.position, car.DoorPosition(0)):F1} m from the door, nearby loot {B.Controller.NearbyLoot != null}, guard {ServerGuard.Summary()}); asking the vehicle directly like the key does");
                    MultiHarness_Vehicle(B.Manager, car).RequestEnter(B.Player);
                }
                yield return Until(() => car.OccupantId(0) == B.Player.ObjectId, "B is the driver on the server", 5f);
                yield return Seconds(1.5f);
                Assert.IsTrue(VehicleSeat.IsAttached(B.Player), "B sits in the car on its own screen");
                var fuel = car.GetComponent<VehicleFuel>();
                if (fuel != null && fuel.Fuel01 < 0.3f) { fuel.ServerSetFuel(1f); Log("vehicle: the map's car was nearly empty; filled it for the ride"); }
                var bCopy = MultiHarness_Vehicle(B.Manager, car);
                Log($"vehicle: engine {car.EngineOn.Value}, fuel {(fuel != null ? fuel.Fuel01.ToString("F2") : "n/a")}; B's copy owner {bCopy.IsOwner} simulating {bCopy.Simulating}; server copy owner client {car.Owner.ClientId} (B is {B.Player.Owner.ClientId}); seated {VehicleSeat.SeatedAmount(B.Player):F2}");
                var rb = bCopy.GetComponent<Rigidbody>();
                Log($"vehicle diag: up.y {bCopy.transform.up.y:F2}, B copy kinematic {(rb != null ? rb.isKinematic.ToString() : "n/a")} sleeping {(rb != null ? rb.IsSleeping().ToString() : "n/a")}, server copy kinematic {(car.GetComponent<Rigidbody>() != null ? car.GetComponent<Rigidbody>().isKinematic.ToString() : "n/a")}, B local {PlayerController.Local == B.Controller}, ui {UiFocus.Current}, blocked {SpawnProtection.InputBlocked}, B attached {VehicleSeat.IsAttached(B.Player)}, driver input {Replica_DriverInput(bCopy)}");
                startPos = car.transform.position;
                asSeenByA = MultiHarness_Vehicle(A.Manager, car);
                asSeenByC = MultiHarness_Vehicle(C.Manager, car);
                topSpeed = 0f; errSum = 0; errN = 0; errMax = 0f;
                float drive = Time.time + 8f, nextLog = 0f;
                bool shot = false;
                while (Time.time < drive)
                {
                    _keys.Frame.Throttle = 1f;
                    _keys.Frame.Steer = 0.25f * Mathf.Sin(Time.time * 0.8f);
                    var owner = MultiHarness_Vehicle(B.Manager, car);
                    topSpeed = Mathf.Max(topSpeed, owner != null ? owner.SpeedKmh : 0f);
                    if (asSeenByA != null) { float e = Vector3.Distance(asSeenByA.transform.position, car.transform.position); errSum += e; errN++; errMax = Mathf.Max(errMax, e); }
                    if (Time.time > nextLog) { nextLog = Time.time + 1f; Log($"  vehicle t+{8f - (drive - Time.time):F0}s: owner copy {(owner != null ? owner.SpeedKmh : 0f):F0} km/h at {car.transform.position}"); }
                    // The picture of the ride once it is up to speed.
                    if (!shot && Time.time > drive - 5f && topSpeed > 12f) { shot = true; yield return Shot("08_remoteB_driving", B); }
                    yield return null;
                }
                _keys.Frame.Throttle = -1f;
                yield return Seconds(2.5f);
                _keys.Frame.Throttle = 0f;
                moved = Vector3.Distance(startPos, car.transform.position);
                Log($"vehicle attempt {attempt + 1}: top speed {topSpeed:F0} km/h, moved {moved:F1} m; client A's copy vs the server's copy: mean {errSum / Math.Max(1, errN):F2} m, max {errMax:F2} m");
                if (moved <= 8f)
                {
                    _keys.Frame.Interact = true;
                    yield return Until(() => car.OccupantId(0) != B.Player.ObjectId, "B got out of the stuck car", 8f);
                    yield return Seconds(1f);
                }
            }
            Assert.Greater(moved, 8f, "the remote driver drove a car");
            Assert.Greater(Vector3.Distance(asSeenByC.transform.position, startPos), 4f, "another client sees it moved");
            Assert.Less(errMax, 6f, "another client's copy never strays far from the server's");
            _keys.Frame.Interact = true;
            yield return Until(() => car.OccupantId(0) != B.Player.ObjectId, "B got out", 8f);
            ReplicaLayers.SeparateVehicles = false;

            // ===== 6. lockdown stages and a supply drop =====
            Milestone("lockdown_supply");
            SupplyRules.Current = new SupplyRules { FirstStage = 1, EveryStages = 1, DescentSeconds = 3f, OpenSeconds = 0.6f, SmokeSeconds = 60f };
            var zone = UnityEngine.Object.FindFirstObjectByType<ZoneDirector>();
            Assert.IsNotNull(zone);
            var rules = new LockdownRules
            {
                FirstWarningSeconds = 0.5f, WarningSeconds = 1.5f, LockdownSeconds = 3f, BombardmentSeconds = 2f,
                MinSecondsBetweenClosings = 5f, RadiationDps = 0f, RadiationGraceSeconds = 100f, RadiationTickSeconds = 0.1f, ClosedDps = 0f,
                BlastIntervalSeconds = 5f, BlastWarningSeconds = 0.5f, BlastDamage = 0f, KeysPerRegion = 0, KeyOpenSeconds = 2f, SubZoneStartDelaySeconds = 1000f,
            };
            zone.ServerConfigure(rules, 0);
            foreach (var w in _wires) { w.Zones.Clear(); w.Supply.Clear(); }
            yield return Until(() => _wires.TrueForAll(w => CountClosedRegions(w) >= 2), "two regions reached lockdown or closed on every client", 60f);
            yield return Until(() => _wires.TrueForAll(w => w.Supply.Count >= 1), "a supply drop reached every client", 30f);
            var crate = SupplyDirector.Instance.Crates.Count > 0 ? SupplyDirector.Instance.Crates[0] : null;
            Log($"lockdown: regions closing seen by clients: {string.Join("/", _wires.ConvertAll(w => CountClosedRegions(w).ToString()))}; supply drops seen: {string.Join("/", _wires.ConvertAll(w => w.Supply.Count.ToString()))}");
            if (crate != null)
            {
                var stand = crate.Position + new Vector3(7f, 0f, 0f);
                C.Controller.Teleport((GroundAt(stand) ?? stand) + Vector3.up * 0.1f);
                C.Controller.LookAt(crate.Position + Vector3.up * 1.5f);
                yield return Seconds(1f);
                yield return Shot("09_remoteC_supply_drop", C, false);
            }
            yield return Shot("10_remoteA_zone_ui", A);

            // ===== 7. a remote player dies: death cam, result panel, spectate =====
            Milestone("death");
            SetLocal(C);
            yield return Until(() => ResultScreen.Instance != null, "result screen installed");
            Assert.AreEqual(ResultScreen.Phase.None, ResultScreen.Instance.Current);
            var cServer = Server(C.Player);
            cServer.ServerTakeDamage(new DamageInfo { Amount = 1000f, Attacker = _net.HostPlayer.IsServerInitialized ? Server(_net.HostPlayer) : null, Cause = DamageCause.Shot, WeaponId = Server(_net.HostPlayer).ActiveWeaponId });
            yield return Until(() => C.Player.Dead.Value, "C's death reaches C", 5f);
            yield return Until(() => ResultScreen.Instance.Current == ResultScreen.Phase.DeathCam, "death cam", 5f);
            yield return Seconds(0.8f);
            Log("death cam text: " + ResultScreen.Instance.DeathCamText);
            yield return Shot("11_remoteC_death_cam", C);
            yield return Until(() => ResultScreen.Instance.Current == ResultScreen.Phase.Panel, "result panel after the death cam", 8f);
            yield return Seconds(0.5f);
            Log($"result panel (C): '{ResultScreen.Instance.TitleText}' {ResultScreen.Instance.PlacementText}, kills {ResultScreen.Instance.KillsText}, damage {ResultScreen.Instance.DamageText}, time {ResultScreen.Instance.TimeText}, by {ResultScreen.Instance.KilledByText}");
            Assert.AreEqual(MatchOutcome.Eliminated, ResultScreen.Instance.Outcome);
            yield return Shot("12_remoteC_result_panel", C);
            ResultScreen.Instance.StartSpectating();
            yield return Until(() => ResultScreen.Instance.Current == ResultScreen.Phase.Spectate, "spectating", 5f);
            yield return Seconds(1f);
            Log("spectating: " + ResultScreen.Instance.Spectate.TargetText + " target " + (ResultScreen.Instance.Spectate.Target != null ? ResultScreen.Instance.Spectate.Target.name : "none"));
            Assert.IsNotNull(ResultScreen.Instance.Spectate.Target, "spectate follows someone alive");
            Assert.IsFalse(ResultScreen.Instance.Spectate.Target.Dead.Value);
            yield return Shot("13_remoteC_spectate", C);

            // ===== 8. the last survivor =====
            Milestone("last_survivor");
            var hostServer = Server(_net.HostPlayer);
            DamageInfo Kill() => new DamageInfo { Amount = 1000f, Attacker = hostServer, Cause = DamageCause.Shot, WeaponId = hostServer.ActiveWeaponId };
            // A and B are knocked out in turn, each seen through its own client's result screen (death cam, panel).
            foreach (var (who, tag) in new[] { (A, "A"), (B, "B") })
            {
                SetLocal(who);
                yield return Seconds(0.5f);
                Server(who.Player).ServerTakeDamage(Kill());
                yield return Until(() => ResultScreen.Instance.Current == ResultScreen.Phase.DeathCam, tag + "'s death cam", 5f);
                yield return Seconds(0.8f);
                yield return Shot("14_remote" + tag + "_death_cam", who);
                yield return Until(() => ResultScreen.Instance.Current == ResultScreen.Phase.Panel, tag + "'s result panel", 8f);
                yield return Seconds(0.5f);
                Log($"result panel ({tag}): '{ResultScreen.Instance.TitleText}' {ResultScreen.Instance.PlacementText}, kills {ResultScreen.Instance.KillsText}, damage {ResultScreen.Instance.DamageText}, by {ResultScreen.Instance.KilledByText}");
                Assert.AreEqual(MatchOutcome.Eliminated, ResultScreen.Instance.Outcome);
                yield return Shot("15_remote" + tag + "_result_panel", who);
            }
            foreach (var cand in new List<Combatant>(ServerCombatants()))
                if (cand != hostServer && !cand.Dead.Value) cand.ServerTakeDamage(Kill());
            yield return Until(() => MatchState.Instance.ServerOver, "the server decides the match", 10f);
            yield return Until(() => _wires.TrueForAll(w => w.LastStatus.Over), "every client heard the match is over", 10f);
            int winner = hostServer.ObjectId;
            foreach (var w in _wires)
            {
                Log($"{w.Name}: status alive {w.LastStatus.Alive}/{w.LastStatus.Total} over {w.LastStatus.Over} winner {w.LastStatus.WinnerId} '{w.LastStatus.WinnerName}', kills heard {w.Kills.Count}, stats heard {w.Stats.Count}");
                Assert.AreEqual(winner, w.LastStatus.WinnerId, w.Name + " knows the winner");
                Assert.AreEqual(1, w.LastStatus.Alive);
            }
            for (int i = 1; i < _wires.Count; i++) Assert.AreEqual(_wires[0].Kills.Count, _wires[i].Kills.Count, "every client heard every kill");
            Assert.AreEqual(startSize - 1, _wires[0].Kills.Count);
            var placements = new HashSet<int>();
            foreach (var k in _wires[1].Kills) placements.Add(k.VictimPlacement);
            Assert.AreEqual(startSize - 1, placements.Count, "every dead combatant has its own placement");
            Assert.IsTrue(_wires[1].Kills.Exists(k => k.VictimId == C.Player.ObjectId));

            // The result screens: the winner (host), the dead remote clients as they would see it.
            SetLocal(_net.HostPlayer, _net.HostPlayer.GetComponent<PlayerController>());
            yield return Until(() => ResultScreen.Instance.Outcome == MatchOutcome.Won && ResultScreen.Instance.Current == ResultScreen.Phase.Panel, "winner's panel", 10f);
            yield return Seconds(0.6f);
            Log($"result panel (host): '{ResultScreen.Instance.TitleText}' {ResultScreen.Instance.PlacementText}, kills {ResultScreen.Instance.KillsText}, damage {ResultScreen.Instance.DamageText}");
            Assert.AreEqual(MatchOutcome.Won, ResultScreen.Instance.Outcome);
            yield return Shot_Host("16_host_winner_panel");

            // ===== 9. back to the lobby =====
            Milestone("back_to_lobby");
            ReportNumbers(A, B, C);
            foreach (var c in clients) c.Manager.ClientManager.StopConnection();
            yield return Seconds(0.5f);
            MainMenu.LeaveMatch();
            yield return Until(() => MainMenu.Instance != null && SceneManager.GetActiveScene().name == MainMenu.SceneName, "main menu again");
            yield return null;
            MainMenu.Instance.OpenHost();
            var again = MainMenu.Instance.Lobby.Lobby;
            yield return Until(() => again.Phase == LobbyNet.PhaseKind.InLobby, "a new lobby opens after the match");
            Log("back in a fresh lobby on port " + again.Port);
            Milestone("done");
            Log("MATCH COMPLETE");
        }

        IEnumerator Shot_Lobby()
        {
            // The host's own lobby screen (the real UI of this process).
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            // The menu camera draws nothing itself (culling mask 0); the shot must still draw the UI layer.
            Camera.main.cullingMask = -1;
            yield return Seconds(0.5f);
            yield return UiShot.Save(Path.Combine(_shots, "00_lobby_host_room.png"), 1600, 900);
        }

        IEnumerator Shot_Host(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var cam = _net.HostPlayer.GetComponent<PlayerController>().Camera;
            var others = new List<(Camera cam, string tag)>();
            foreach (var c in Camera.allCameras)
                if (c != cam && c.CompareTag("MainCamera")) { others.Add((c, c.tag)); c.tag = "Untagged"; }
            string before = cam.tag;
            cam.tag = "MainCamera";
            yield return UiShot.Save(Path.Combine(_shots, name + ".png"), 1600, 900);
            cam.tag = before;
            foreach (var o in others) o.cam.tag = o.tag;
            Log("SHOT " + Path.Combine(_shots, name + ".png"));
        }

        static string Replica_DriverInput(Vehicle v) { var f = typeof(Vehicle).GetField("_input", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); if (f == null) return "?"; var i = (DriverInput)f.GetValue(v); return $"throttle {i.Throttle:F1} steer {i.Steer:F2} handbrake {i.Handbrake}"; }

        static Vehicle MultiHarness_Vehicle(NetworkManager manager, Vehicle server) =>
            manager.ClientManager.Objects.Spawned.TryGetValue(server.ObjectId, out var nob) ? nob.GetComponent<Vehicle>() : null;

        static double Max(List<double> l) { double m = double.MinValue; foreach (var x in l) m = Math.Max(m, x); return m; }
        static double Min(List<double> l) { double m = double.MaxValue; foreach (var x in l) m = Math.Min(m, x); return m; }

        static int CountClosedRegions(Wire w)
        {
            var seen = new HashSet<int>();
            foreach (var z in w.Zones)
                if (z.Kind == ZoneUpdate.KindRegion && z.State >= (byte)ZoneState.Lockdown) seen.Add(z.Region);
            return seen.Count;
        }

        void ReportNumbers(RemoteClient A, RemoteClient B, RemoteClient C)
        {
            Log("==== per-client numbers (all clients share this process: frame times are process-wide per phase) ====");
            var who = new[] { ("host", _net.HostPlayer), ("A", A.Player), ("B", B.Player), ("C", C.Player) };
            foreach (var (name, body) in who)
            {
                var server = Server(body);
                Log($"{name}: server refusals {ServerGuard.CountFor(server)} (movement corrections are among them)");
            }
            Log($"all clients: reconciliation corrections {MoveGuard.Corrections} (max error {MoveGuard.MaxError:F2} m), server guard refusals {ServerGuard.Total} [{ServerGuard.Summary()}]");
            double seconds = _clock.Seconds;
            foreach (var w in _wires)
                Log($"{w.Name}: received {w.Bytes / 1024.0:F0} KiB over {seconds:F0} s = {w.Bytes / seconds / 1024.0:F1} KiB/s, kills heard {w.Kills.Count}, zone updates {w.Zones.Count}");
            foreach (var phase in _frames.Order)
            {
                var ms = _frames.Ms[phase].ToArray();
                var fs = FrameStats.Compute(ms, Math.Min(10, ms.Length / 4));
                Log($"frame time [{phase}]: {fs.Count} frames, avg {fs.AvgMs:F1} ms ({fs.Fps:F0} fps), p99 {fs.P99Ms:F1} ms, max {fs.MaxMs:F1} ms");
            }
            Log($"damage events: human->human {_humanHitsOnHumans}, human->bot {_humanHitsOnBots}, bot->human {_botHitsOnHumans}");
        }
    }
}
