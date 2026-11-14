using System;
using System.Collections;
using System.IO;
using FishNet.Managing;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of what a REMOTE client sees (#165): riders in a car driven by another remote player, the fire and the
    // explosion, a swimmer, a player hanging from a ledge, and the dead player's own screen (death cam, result panel,
    // spectating). Only the viewer's own copies of the objects are drawn (the other copies share this process).
    //   VEH_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.ReplicationVehicleCaptureTests
    [Explicit, Category("Capture")]
    public class ReplicationVehicleCaptureTests : ReplicationVehicleBase
    {
        protected override string LogDir => "Logs/net-replication/vehicle-capture";

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("VEH_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/vehshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        Camera _cam;
        SwimRules _swim;

        [UnitySetUp]
        public IEnumerator CaptureSetUp()
        {
            _swim = SwimRules.Current;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CaptureTearDown()
        {
            if (_swim != null) SwimRules.Current = _swim;
            yield return null;
        }

        // Only the copies that `viewer` holds are drawn: the server's and the other clients' copies of the same objects
        // sit at the same place in this process.
        void ShowOnly(NetworkManager viewer)
        {
            foreach (var nob in Net.Host.ServerManager.Objects.Spawned.Values)
                SetRenderers(nob, viewer == Net.Host);
            foreach (var c in Net.Clients)
                foreach (var nob in c.Manager.ClientManager.Objects.Spawned.Values)
                    if (nob != null && !nob.IsServerInitialized) SetRenderers(nob, c.Manager == viewer);
        }

        // What each renderer showed before: a renderer the game keeps off (a first-person head, say) stays off.
        readonly System.Collections.Generic.Dictionary<Renderer, bool> _original = new System.Collections.Generic.Dictionary<Renderer, bool>();

        void SetRenderers(FishNet.Object.NetworkObject nob, bool on)
        {
            if (nob == null || (nob.GetComponent<Vehicle>() == null && nob.GetComponent<Combatant>() == null)) return;
            foreach (var r in nob.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (!_original.TryGetValue(r, out bool was)) { was = r.enabled; _original[r] = was; }
                r.enabled = on && was;
            }
        }

        void Shot(string name, Vector3 from, Vector3 at, float fov = 55f, int width = 1600, int height = 900)
        {
            if (_cam == null)
            {
                var go = new GameObject("VehicleShotCamera");
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                if (Camera.main != null) _cam.CopyFrom(Camera.main);
            }
            _cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
            _cam.fieldOfView = fov;
            Render(_cam, Path.Combine(Folder(), name + ".png"), width, height);
        }

        static void Render(Camera camera, string path, int width, int height, bool withUi = false)
        {
            var overlays = new System.Collections.Generic.List<Canvas>();
            if (withUi)
                foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!c.isRootCanvas || !c.enabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = camera;
                    c.planeDistance = camera.nearClipPlane + 0.05f;
                    overlays.Add(c);
                }
            Canvas.ForceUpdateCanvases();
            var rt = new RenderTexture(width, height, 24);
            var before = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = before;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            foreach (var c in overlays) { c.renderMode = RenderMode.ScreenSpaceOverlay; }
            Debug.Log("[VehicleShot] " + path);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Vehicle_RidersDrivingFireAndExplosion_SeenByAThirdClient()
        {
            RemoteClient b = null, viewer = null;
            yield return Net.AddClient(c => b = c);
            yield return Net.AddClient(c => viewer = c);
            var car = FourSeatCar;
            var t = car.transform;
            yield return GetIn(Remote, car, 0, 0);
            yield return GetIn(b, car, 1, 1);
            HostPlayer.GetComponent<PlayerController>().Teleport(car.DoorPosition(2) + Vector3.up * 0.05f);
            yield return Seconds(0.3f);
            car.ServerEnter(HostPlayer);
            var sides = new[] { Net.Host, Remote.Manager, b.Manager, viewer.Manager };
            yield return SeatedEverywhere(car, Remote.Player, 0, sides);
            yield return SeatedEverywhere(car, b.Player, 1, sides);
            yield return Seconds(1f);
            ShowOnly(viewer.Manager);
            var roof = float.MinValue;
            foreach (var c in car.Colliders) roof = Mathf.Max(roof, c.bounds.max.y);
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.bounds.center.y < 1.3f || Vector3.Distance(new Vector3(r.bounds.center.x, 0f, r.bounds.center.z), new Vector3(t.position.x, 0f, t.position.z)) > 2.6f) continue;
                string path = r.name;
                for (var up = r.transform.parent; up != null; up = up.parent) path = up.name + "/" + path;
                Log($"above the roof ({roof:F2}): {path} at {r.bounds.center} size {r.bounds.size} {r.GetType().Name} shadows {(r is MeshRenderer mr ? mr.shadowCastingMode.ToString() : "-")}");
            }
            var middle = t.position + Vector3.up * 1.1f;
            // From the front left and from the right side, a little above: three riders, the driver's hands on the wheel.
            Shot("veh_01_riders_front_left", middle + t.forward * 3.6f - t.right * 2.6f + Vector3.up * 0.7f, middle + t.forward * 0.2f, 50f);
            Shot("veh_02_riders_left_side", middle - t.right * 4.2f + Vector3.up * 0.9f, middle, 45f);

            // A drives: the viewer sees the moving car from its side, at 100 ms / 2 % loss.
            SeparateTwins(car);
            Net.Simulate(Remote);
            Net.Simulate(viewer);
            SetLocal(Remote);
            var start = t.position;
            var ahead = start + t.forward * 9f;
            KeyFrames.Frame.Throttle = 1f;
            var onViewer = Replica(viewer.Manager, car);
            float end = Time.time + 6f;
            while (Time.time < end && Vector3.Dot(onViewer.transform.position - ahead, t.forward) < 0f) yield return null;
            Log($"driving shot: the viewer's copy has gone {Vector3.Distance(onViewer.transform.position, start):F1} m at {onViewer.SpeedKmh:F0} km/h");
            Shot("veh_03_driving_seen_by_viewer", ahead - onViewer.transform.right * 6f + Vector3.up * 1.4f, onViewer.transform.position + Vector3.up * 0.8f, 40f);
            KeyFrames.Frame.Throttle = -1f;
            yield return Seconds(3f);
            KeyFrames.Frame.Throttle = 0f;
            Net.ClearSimulation(Remote);
            Net.ClearSimulation(viewer);

            // Everybody out; then it smokes, burns and explodes.
            foreach (var who in new[] { Remote, b })
            {
                SetLocal(who);
                KeyFrames.Frame.Interact = true;
                yield return Until(() => !VehicleSeat.IsAttached(who.Player), who.Manager.name + " out");
                yield return Seconds(0.2f);
            }
            car.ServerExit(HostPlayer);
            yield return Seconds(1.2f);
            var damage = car.GetComponent<VehicleDamage>();
            var seen = onViewer.GetComponent<VehicleDamageVisual>();
            ShowOnly(viewer.Manager);
            var look = onViewer.transform.position + Vector3.up * 0.9f;
            var from = onViewer.transform.position - onViewer.transform.right * 6.5f + onViewer.transform.forward * -2.5f + Vector3.up * 1.6f;
            damage.ServerSetHealth01(0.2f);
            yield return Seconds(2.5f);
            Shot("veh_04_smoke", from, look, 50f);
            damage.ServerSetHealth01(0f);
            yield return Seconds(2.2f);
            Shot("veh_05_burning", from, look, 50f);
            yield return Until(() => seen.ExplosionsShown > 0, "the explosion", VehicleDamageRules.For(car.Kind).BurnSeconds + 3f);
            yield return null;
            Shot("veh_06_explosion", from, look, 50f);
            yield return Seconds(0.3f);
            Shot("veh_07_fireball", from, look, 50f);
            yield return Seconds(3f);
            Shot("veh_08_wreck", from, look, 50f);

            // A client that joins now sees the wreck.
            RemoteClient late = null;
            yield return Net.AddClient(c => late = c);
            yield return Seconds(1.5f);
            ShowOnly(late.Manager);
            Shot("veh_09_late_joiner_sees_the_wreck", from, look, 50f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Swim_AndLedge_SeenByAnotherClient()
        {
            SwimRules.Current = new SwimRules { DiveSeconds = 20f, RecoverSeconds = 9f };
            RemoteClient viewer = null;
            yield return Net.AddClient(c => viewer = c);
            var pool = TestPool.Create(new Vector3(3000f, 0f, 3000f), 12f, 2.0f, out _);
            Arena();
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.position = new Vector3(3000f, 1.15f, 3040f);
            block.transform.localScale = new Vector3(3f, 2.3f, 2f);
            Physics.SyncTransforms();
            foreach (var c in RemoteOnHost.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = 2;
            Remote.Controller.Teleport(new Vector3(3000f, 0.1f, 3000f));
            yield return Until(() => Remote.Controller.Swimming, "A swims");
            Remote.Controller.LookAt(new Vector3(3004f, 0.5f, 3000f));
            Remote.Input.Frame.Move = new Vector2(0f, 1f);
            yield return Seconds(1.2f);
            Remote.Input.Frame.Move = Vector2.zero;
            yield return Seconds(0.6f);
            ShowOnly(viewer.Manager);
            var swimmer = MultiHarness.ReplicaOf(viewer.Manager, Remote.Player).transform.position;
            // The water surface is at 2 m: a low look at the waterline, where the head and back of the swimmer are.
            var surface = new Vector3(swimmer.x, 2.05f, swimmer.z);
            Shot("swim_01_on_the_surface_side", new Vector3(swimmer.x, 2.6f, swimmer.z - 3.4f), surface, 45f);
            Shot("swim_02_on_the_surface_front", new Vector3(swimmer.x + 3.4f, 2.6f, swimmer.z + 0.3f), surface, 45f);
            Remote.Input.Frame.SlowWalk = true;
            yield return Seconds(1.8f);
            swimmer = MultiHarness.ReplicaOf(viewer.Manager, Remote.Player).transform.position;
            Shot("swim_03_diving_seen_from_above", new Vector3(swimmer.x, 3.3f, swimmer.z - 3.4f), new Vector3(swimmer.x, 1.0f, swimmer.z), 45f);
            Remote.Input.Frame.SlowWalk = false;
            UnityEngine.Object.Destroy(pool);

            // The ledge: A hangs, then pulls up; the viewer stands a little to the side.
            Remote.Controller.Teleport(new Vector3(3000f, 0.1f, 3038.4f));
            Remote.Controller.LookAt(new Vector3(3000f, 1.6f, 3100f));
            yield return Seconds(0.6f);
            float end = Time.time + 5f;
            while (!Remote.Controller.OnLedge && Time.time < end)
            {
                Remote.Input.Frame.Jump = true;
                yield return null;
            }
            Assert.IsTrue(Remote.Controller.OnLedge, "A hangs");
            yield return Seconds(0.7f);
            var at = MultiHarness.ReplicaOf(viewer.Manager, Remote.Player).transform.position;
            Shot("ledge_01_hang_side", at + new Vector3(3.2f, 1.5f, -1.8f), at + new Vector3(0f, 1.2f, 0.2f), 50f);
            Shot("ledge_02_hang_behind", at + new Vector3(1.2f, 1.5f, -2.6f), at + new Vector3(0f, 1.5f, 0.3f), 50f);
            Remote.Input.Frame.Jump = true;
            yield return null;
            yield return Seconds(0.35f);
            at = MultiHarness.ReplicaOf(viewer.Manager, Remote.Player).transform.position;
            Shot("ledge_03_climbing_side", at + new Vector3(3.6f, 1.2f, -1.9f), at + new Vector3(0f, 1.0f, 0.4f), 50f);
            while (Remote.Controller.InParkour) yield return null;
            yield return Seconds(0.6f);
            at = MultiHarness.ReplicaOf(viewer.Manager, Remote.Player).transform.position;
            Shot("ledge_04_on_top", at + new Vector3(3.6f, 0.9f, -3.5f), at + Vector3.up * 0.9f, 50f);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Death_OnTheRemotePlayersOwnScreen_DeathCamResultAndSpectate()
        {
            RemoteClient b = null;
            yield return Net.AddClient(c => b = c);
            LocalSwitch.To(Remote);
            ShowOnly(Remote.Manager);
            yield return GiveAndHold(HostPlayer, Rifle);
            yield return PlaceInArena(9f);
            // A hurts the host first for the damage number, the host kills A from where it stands.
            yield return GiveAndHold(RemoteOnHost, Rifle);
            Remote.Player.CmdFire((HostPlayer.transform.position + Vector3.up * 1.1f - Remote.Player.EyePosition).normalized);
            yield return Seconds(1.2f);
            b.Controller.Teleport(new Vector3(3010f, 0.1f, 3010f));
            RemoteOnHost.ServerTakeDamage(new DamageInfo { Amount = 1000f, Attacker = HostPlayer, Zone = HitZone.Body, Cause = DamageCause.Shot, WeaponId = Rifle });
            var screen = ResultScreen.Instance;
            yield return Until(() => screen.Current == ResultScreen.Phase.DeathCam && screen.DeathCamText.Length > 0, "death cam");
            yield return Seconds(1.6f);
            var main = Camera.main;
            screen.Spectate.Place();
            Render(main, Path.Combine(Folder(), "death_01_death_cam_on_killer.png"), 1600, 900, true);
            yield return Until(() => screen.Current == ResultScreen.Phase.Panel, "the panel", ResultScreen.DeathCamSeconds + 3f);
            yield return Seconds(0.5f);
            screen.Spectate.Place();
            Render(main, Path.Combine(Folder(), "death_02_result_panel.png"), 1600, 900, true);
            screen.StartSpectating();
            yield return Seconds(0.8f);
            screen.Spectate.Place();
            Render(main, Path.Combine(Folder(), "death_03_spectating_the_killer.png"), 1600, 900, true);
            for (int i = 0; i < 12 && screen.Spectate.Target.ObjectId != b.Player.ObjectId; i++) screen.Spectate.Next();
            yield return Seconds(0.6f);
            screen.Spectate.Place();
            Render(main, Path.Combine(Folder(), "death_04_spectating_the_other_remote.png"), 1600, 900, true);
            Log("target " + screen.Spectate.Target.ObjectId + " b " + b.Player.ObjectId);
        }
    }
}
