using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots through a REMOTE client's own camera (#163, #164): what the second player sees of the host's gear,
    // shots, smoke and fire, and what a client that joins late sees. Run on purpose, with a graphics device:
    //   REPL_SHOTS=<folder> tools/unity.sh run-gfx ... (or test playmode Game.Tests.ReplicationCaptureTests)
    [Explicit, Category("Capture")]
    public class ReplicationCaptureTests : ReplicationTestBase
    {
        protected override string LogDir => "Logs/net-replication/capture";

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("REPL_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/replshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        // The 3D view of one player's camera (no overlay UI: the HUD of this process belongs to the host's player).
        static void Shoot(Camera camera, string path, float fov = 0f, int width = 1600, int height = 900)
        {
            float fovBefore = camera.fieldOfView;
            if (fov > 0f) camera.fieldOfView = fov;
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
            camera.fieldOfView = fovBefore;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator RemoteSees_HostGear_Shot_Smoke_AndFire_AndALateJoinerSeesTheCloud()
        {
            string folder = Folder();
            Arena(60f);
            // The host stands 5 m from the remote's camera; light poles are not needed: the TestBox sun reaches the arena.
            yield return PlaceInArena(2.6f);
            yield return GiveAndHold(HostPlayer, Rifle);
            foreach (int id in new[] { 11, 12, 15, 16, 5 }) HostPlayer.ServerGive(Optics.ToLoot(id));
            HostPlayer.GetComponent<CombatantGear>().ServerEquip(ArmorSlot.Helmet, 2);
            HostPlayer.GetComponent<CombatantGear>().ServerEquip(ArmorSlot.Vest, 2);
            HostPlayer.GetComponent<CombatantCarry>().ServerEquipBackpack(2);
            var host = HostPlayer.GetComponent<PlayerController>();
            // The host turns to face away a little so the remote sees its side: gun, can, mag, pack.
            host.Teleport(new Vector3(3000f, 0.1f, 2990f));
            host.LookAt(new Vector3(3008f, 1.6f, 2990.5f)); // turned so its right side faces the remote
            yield return Seconds(1.5f);
            var cam = Remote.Controller.Camera;
            Assert.IsNotNull(cam, "the remote player has a camera");
            Remote.Controller.LookAt(new Vector3(3000f, 1.25f, 2990f));
            yield return Seconds(0.6f);
            Shoot(cam, Path.Combine(folder, "remote_01_host_gear.png"), 24f); // zoomed in on the host: gun, can, magazine, grip, pack

            // The host fires: the remote catches the muzzle frame and the tracer.
            var view = HostOnRemote.GetComponent<CombatantView>();
            int flashes = view.MuzzleFlashes;
            Assert.IsTrue(ShootAt(HostPlayer, new Vector3(3008f, 1.6f, 3040f)));
            float giveUp = Time.time + 2f;
            while (view.MuzzleFlashes == flashes && Time.time < giveUp) yield return null;
            Assert.Greater(view.MuzzleFlashes, flashes, "the remote shows the muzzle flash");
            Shoot(cam, Path.Combine(folder, "remote_02_host_fires.png"), 24f);

            // A smoke grenade and a fire bottle between them; the remote looks at both.
            HostPlayer.ServerGive(new LootEntry { Kind = LootKind.Smoke, Amount = 1 });
            HostPlayer.ServerGive(new LootEntry { Kind = LootKind.Molotov, Amount = 1 });
            var user = HostPlayer.GetComponent<ThrowableUser>();
            yield return Seconds(1.1f);
            Assert.IsTrue(user.ServerThrow(new Vector3(2996f, 0.4f, 2996f), Vector3.zero, ThrowableKind.Smoke));
            yield return Seconds(1.1f);
            Assert.IsTrue(user.ServerThrow(new Vector3(3004f, 0.4f, 3001f), Vector3.down * 2f, ThrowableKind.Molotov));
            yield return Seconds(3.5f);
            Remote.Controller.LookAt(new Vector3(3000f, 1.5f, 3000f));
            yield return Seconds(0.3f);
            Shoot(cam, Path.Combine(folder, "remote_03_smoke_and_fire.png"));

            // A client that joins now sees the cloud and the fire that are already up.
            RemoteClient late = null;
            yield return Net.AddClient(c => late = c);
            yield return Seconds(1.5f);
            late.Controller.Teleport(new Vector3(3000f, 0.1f, 2985f));
            late.Controller.LookAt(new Vector3(3000f, 1.5f, 3000f));
            yield return Seconds(1f);
            Shoot(late.Controller.Camera, Path.Combine(folder, "late_04_joined_cloud_still_up.png"));
            Log("shots in " + folder);
        }
    }
}
