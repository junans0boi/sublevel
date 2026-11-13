using System;
using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #90, ADR-074 in the real TestBox scene as a host: shots flatten a tyre, crack the glass and hole the body; at no
    // health the car burns, can no longer be driven or entered, then explodes (hurting who stands next to it) into a wreck.
    public class VehicleDamagePlayTests
    {
        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        static Vehicle Car
        {
            get
            {
                foreach (var v in Vehicle.All)
                    if (v != null && v.Kind == VehicleKind.Car) return v;
                return null;
            }
        }

        static IEnumerator Load()
        {
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return WaitUntil(() => Car != null, 10f, "the test box's car");
            for (int n = 0; SpawnProtection.InputBlocked && n < 80; n++) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(0.5f);
        }

        // Fires at `target` until `done` or `shots` rounds have gone (the rifle's rate limits ServerFire).
        static IEnumerator ShootAt(Combatant me, Func<Vector3> target, Func<bool> done, int shots)
        {
            for (int fired = 0, tries = 0; fired < shots && !done() && tries < shots * 30; tries++)
            {
                if (me.ServerFire(target() - me.EyePosition)) fired++;
                yield return null;
            }
        }

        // DAMAGE_SHOTS=<folder>: one PNG per step, from a camera 7 m off the car's left front.
        static void Shot(Vehicle car, string name, float side = 5.5f, float ahead = 3.5f, float up = 1.2f)
        {
            string folder = Environment.GetEnvironmentVariable("DAMAGE_SHOTS");
            if (string.IsNullOrEmpty(folder)) return;
            System.IO.Directory.CreateDirectory(folder);
            var go = new GameObject("DamageShotCamera");
            var cam = go.AddComponent<Camera>();
            var t = car.transform;
            var at = t.position + Vector3.up * 0.9f;
            go.transform.position = at - t.right * side + t.forward * ahead + Vector3.up * up;
            go.transform.LookAt(at);
            cam.fieldOfView = 50f;
            const int w = 1280, h = 720;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(go);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Shots_FlattenATyre_CrackTheGlass_AndHoleTheBody()
        {
            yield return Load();
            var car = Car;
            var damage = car.GetComponent<VehicleDamage>();
            Assert.IsNotNull(damage, "the car prefab has VehicleDamage (VehicleBuilder)");
            Assert.IsNotNull(car.GetComponent<VehicleDamageVisual>(), "and its visual");
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 90 });
            yield return new WaitForSeconds(0.6f);

            // From the car's left, a few metres off.
            var t = car.transform;
            PlayerController.Local.Teleport(t.position - t.right * 5f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.5f);

            // The nearest wheel on this side.
            int wheel = -1;
            float best = float.MaxValue;
            for (int i = 0; i < car.WheelCount; i++)
            {
                float d = (car.WheelPivot(i).position - me.transform.position).sqrMagnitude;
                if (d < best) { best = d; wheel = i; }
            }
            yield return ShootAt(me, () => car.WheelPivot(wheel).position, () => (damage.FlatTyres.Value & (1 << wheel)) != 0, 12);
            Assert.AreNotEqual(0, damage.FlatTyres.Value & (1 << wheel), "the shot tyre is flat");
            var state = car.GetComponent<VehicleState>();
            Assert.Less(state.GripOf(wheel), 1f, "less grip on the flat tyre");
            Assert.Greater(state.DropOf(wheel), 0f, "that corner sits lower");
            var visual = car.GetComponent<VehicleDamageVisual>();
            yield return null;
            Assert.Less(car.WheelPivot(wheel).localScale.y, 1f, "the flat tyre is squashed");

            // The side window (between the belt line and the roof, at the cabin's middle).
            var b = damage.LocalBounds();
            Vector3 Window() => t.TransformPoint(new Vector3(0f, b.min.y + b.size.y * 0.75f, b.center.z));
            yield return ShootAt(me, Window, () => damage.Panes.Value != 0, 4);
            Assert.AreNotEqual(0, damage.Panes.Value, "a pane cracked");
            yield return null;
            Assert.Greater(visual.PaneDecalsShown, 0, "the crack is drawn on the pane");

            // The door below the window.
            int holes = 0;
            damage.Holed += (_, __, ___, ____) => holes++;
            float before = damage.Health.Value;
            Vector3 Door(float z01) => t.TransformPoint(new Vector3(0f, b.min.y + b.size.y * 0.32f, b.min.z + b.size.z * z01));
            yield return ShootAt(me, () => Door(0.5f), () => holes > 0 && damage.Health.Value < before, 6);
            Assert.Less(damage.Health.Value, before, "the body lost health");
            Assert.Greater(holes, 0, "a bullet hole was placed");
            yield return null;
            Assert.Greater(visual.HolesShown, 0, "drawn on the body's surface");
            // A spread of holes along the doors, and the windshield from the front, for the picture.
            for (int k = 0; k < 6; k++) { float z01 = 0.3f + 0.07f * k; yield return ShootAt(me, () => Door(z01), () => false, 1); }
            PlayerController.Local.Teleport(t.position + t.forward * 7f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.4f);
            yield return ShootAt(me, () => t.TransformPoint(new Vector3(-0.3f, b.min.y + b.size.y * 0.78f, b.max.z - 0.9f)), () => false, 2);
            yield return new WaitForSeconds(0.5f);
            Shot(car, "01_holes_glass_flat", 6f, 2.5f, 0.6f);
            Shot(car, "01b_door_close", 2.6f, 0.3f, 0.2f);
            Shot(car, "01c_front", 0.8f, 6.5f, 0.5f);

            // A blast at the left rear (the camera side; the right is against the wall): that side caves in, the glass shatters.
            int dentsBefore = visual.DentsShown;
            damage.ServerBlast(t.position - t.right * 2.4f - t.forward * 1.2f + Vector3.up * 0.6f, d => 40f, 1f);
            yield return null;
            Assert.Greater(visual.DentsShown, dentsBefore, "the blast dented the body");
            yield return new WaitForSeconds(0.3f);
            Shot(car, "01d_blast_dent", 4.5f, -2.5f, 0.2f);

            // Crack decals are cut from the glass itself: none reaches above the roof.
            float roofTop = float.NegativeInfinity;
            foreach (var c in car.Colliders) if (c.name == "Roof") roofTop = c.bounds.max.y;
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
                if (r.name == "GlassCrack") Assert.LessOrEqual(r.bounds.max.y, roofTop + 0.02f, "a crack decal sticks out over the roof");

            // A blast ahead of the nose (not close enough to wreck it): the front bumper and the hood come off and fall.
            int fallen = visual.PartsFallen;
            var bumper = damage.Part((int)VehiclePart.BumperFront);
            Assert.IsNotNull(bumper, "the sedan has a separate front bumper (VehicleModels)");
            damage.ServerBlast(t.TransformPoint(new Vector3(0f, 0.5f, b.max.z + 1.6f)), d => Mathf.Max(0f, 100f - 30f * (d - 1.5f)), 1f);
            yield return WaitUntil(() => visual.PartsFallen > fallen, 2f, "a part fell off");
            Assert.AreEqual(PartState.Fallen, damage.PartStateOf((int)VehiclePart.BumperFront), "the front bumper is gone");
            Assert.AreEqual(PartState.Fixed, damage.PartStateOf((int)VehiclePart.Trunk), "the trunk lid, far from it, stays");
            Assert.IsNotNull(bumper.GetComponent<Rigidbody>(), "it falls with physics");
            Assert.IsNull(bumper.parent, "let go of the car");
            yield return new WaitForSeconds(1.2f);
            Assert.Less(bumper.position.y, t.position.y + 0.6f, "on the ground");
            Shot(car, "01e_parts_off", 2.5f, 6.5f, 0.6f);
        }

        // Black smoke, fire (disabled), the explosion (hurting who stands beside it, with light and sound), the wreck.
        IEnumerator BurnToWreck(Vehicle v, string prefix)
        {
            var damage = v.GetComponent<VehicleDamage>();
            var visual = v.GetComponent<VehicleDamageVisual>();
            var state = v.GetComponent<VehicleState>();
            var me = Combatant.LocalPlayer;

            damage.ServerSetHealth01(0.2f);
            yield return WaitUntil(() => damage.CurrentStage == VehicleDamageStage.BlackSmoke, 2f, "black smoke");
            Assert.IsFalse(state.Disabled, "smoking vehicles still drive");
            Assert.AreEqual(0.2f, state.Health01, 1e-3f, "the HUD reads the condition");
            yield return new WaitForSeconds(2f);
            Shot(v, prefix + "02_black_smoke");

            // Standing beside it when it goes up.
            PlayerController.Local.Teleport(v.transform.position - v.transform.right * 3f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.4f);
            float hp = me.Hp.Value;
            damage.ServerSetHealth01(0f);
            yield return WaitUntil(() => damage.CurrentStage == VehicleDamageStage.Burning, 2f, "burning");
            Assert.IsTrue(state.Disabled, "a burning vehicle cannot be driven or entered");
            Assert.AreNotEqual(v, Vehicle.NearestEnterable(v.DoorPosition(0), 3f, out _), "not enterable");
            yield return new WaitForSeconds(1.5f);
            Shot(v, prefix + "03_burning");
            yield return WaitUntil(() => damage.CurrentStage == VehicleDamageStage.Wrecked, VehicleDamageRules.For(v.Kind).BurnSeconds + 3f, "the explosion");
            yield return null;
            Shot(v, prefix + "04a_explosion_flash");
            Assert.Greater(visual.ExplosionsShown, 0, "the explosion was shown");
            Assert.IsNotNull(visual.BangSource, "and heard");
            Assert.IsTrue(visual.BangSource.isPlaying, "the bang plays");
            yield return new WaitForSeconds(0.3f);
            Shot(v, prefix + "04b_explosion_fireball");
            Assert.Less(me.Hp.Value, hp, "the blast hurt the player beside it");
            Assert.IsTrue(state.Disabled, "the wreck stays disabled");
            yield return new WaitForSeconds(2.5f);
            Shot(v, prefix + "05_wreck");
        }

        [UnityTest]
        public IEnumerator NoHealth_Burns_Disables_ThenExplodesIntoAWreck()
        {
            yield return Load();
            yield return BurnToWreck(Car, "");
        }

        [UnityTest]
        public IEnumerator Motorcycle_Smokes_Burns_ExplodesIntoAWreck()
        {
            yield return Load();
            Vehicle bike = null;
            foreach (var v in Vehicle.All) if (v != null && v.IsMotorcycle) { bike = v; break; }
            Assert.IsNotNull(bike, "the test box's motorcycle");
            // Shots at the frame: holes only where the shot meets the drawn bike, all on its meshes.
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 90 });
            yield return new WaitForSeconds(0.6f);
            var t = bike.transform;
            PlayerController.Local.Teleport(t.position - t.right * 4f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.5f);
            var visual = bike.GetComponent<VehicleDamageVisual>();
            var b = bike.GetComponent<VehicleDamage>().LocalBounds();
            for (int k = 0; k < 6; k++)
            {
                // The tank and the side panels under the seat (the engine and the frame below are open tubes).
                var at = new Vector3(0f, b.min.y + b.size.y * (0.62f + 0.06f * (k % 3)), b.min.z + b.size.z * (0.35f + 0.06f * k));
                yield return ShootAt(me, () => t.TransformPoint(at), () => false, 1);
            }
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(visual.HolesShown, 3, "holes on the bike");
            foreach (Transform hole in bike.GetComponentsInChildren<Transform>())
                if (hole.name == "BulletHole" && hole.GetComponent<MeshFilter>() == null) Assert.IsTrue(hole.parent.GetComponent<MeshFilter>() != null, "a hole sits on a drawn mesh");
            Shot(bike, "bike_01_holes", 3f, 0.8f, 0.2f);
            me.ServerDamageFilter = null; // the explosion must hurt again
            yield return BurnToWreck(bike, "bike_");
        }
    }
}
