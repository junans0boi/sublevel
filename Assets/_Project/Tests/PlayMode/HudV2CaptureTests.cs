using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the HUD v2 bottom centre (ADR-563) against docs/research/ui_mockups/hud_v2.html: on foot (squad
    // and solo, 16:9 and 16:10), after a hit, healing, boosted, swimming and driving. The HUD canvases are drawn through
    // the player camera into a texture, no window. Run on purpose with
    //   HUD_V2_SHOTS=Logs/hud_v2 tools/unity.sh test playmode Game.Tests.HudV2CaptureTests
    [Explicit, Category("Capture")]
    public class HudV2CaptureTests
    {
        sealed class PlayerKeys : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };
            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.Look = Vector2.zero;
                return f;
            }
        }

        sealed class CarKeys : IVehicleInput
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

        PlayerKeys _keys;
        CarKeys _car;
        string _folder;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TeamMatch.Mode = TeamMode.Solo;
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        IEnumerator Load(TeamMode mode)
        {
            _folder = Environment.GetEnvironmentVariable("HUD_V2_SHOTS");
            if (string.IsNullOrEmpty(_folder)) _folder = "Logs/hud_v2";
            _folder = Path.GetFullPath(_folder);
            Directory.CreateDirectory(_folder);
            TeamMatch.Mode = mode;
            _keys = new PlayerKeys();
            _car = new CarKeys();
            PlayerController.InputOverride = _keys;
            VehicleInput.InputOverride = _car;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && Hud.Instance != null, 30f, "local player");
            yield return new WaitForSeconds(0.8f);
            var me = Combatant.LocalPlayer;
            var heal = me.GetComponent<CombatantHealing>();
            var gear = me.GetComponent<CombatantGear>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = InventoryBackend.AmmoValue(1), Amount = 120 });
            gear.ServerEquip(ArmorSlot.Helmet, 2, 0.8f * ArmorPiece.Fresh(ArmorSlot.Helmet, 2, ArmorRules.Current).Durability);
            gear.ServerEquip(ArmorSlot.Vest, 2, 0.25f * ArmorPiece.Fresh(ArmorSlot.Vest, 2, ArmorRules.Current).Durability);
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 });
            heal.ServerAdd(HealKind.Pill, 1);
            heal.ServerAdd(HealKind.Drink, 2);
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 3f, "rifle in hand");
            me.ServerTakeDamage(28f);
            yield return new WaitForSeconds(1.6f); // the damage trail has drained
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_OnFoot_Squad_Hit_Heal_Boost()
        {
            yield return Load(TeamMode.Squad);
            var me = Combatant.LocalPlayer;
            var hud = Hud.Instance;
            Snap("01_foot_squad_1920x1080.png");
            Snap("02_foot_squad_1728x1080.png", 1728, 1080);

            me.ServerTakeDamage(18f);
            yield return new WaitForSeconds(0.15f);
            Assert.Greater(hud.DamageTrailFill, hud.HealthFill + 0.1f, "the red trail stays behind the health");
            Snap("03_hit_trail.png");
            yield return new WaitForSeconds(1.6f);
            Assert.AreEqual(hud.HealthFill, hud.DamageTrailFill, 0.005f, "and drains");

            var heal = me.GetComponent<CombatantHealing>();
            heal.RequestUseKind(HealKind.Medkit);
            yield return WaitUntil(() => hud.HealingShown, 2f, "heal stripes");
            yield return new WaitForSeconds(2.5f);
            Assert.AreEqual(1f, hud.HealIncomingTo, 0.01f, "a medkit heals to full");
            Snap("04_medkit_healing.png");
            heal.RequestCancel();
            yield return WaitUntil(() => !hud.HealingShown, 2f, "stripes gone");

            heal.ServerAddGauge(70f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(hud.Status.HealEffectLit && hud.Status.RunEffectLit, "step 3: heal and run lit");
            Snap("05_boost_step3.png");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_OnFoot_Solo_AndSwimming()
        {
            yield return Load(TeamMode.Solo);
            var hud = Hud.Instance;
            Snap("06_foot_solo_1920x1080.png");
            Snap("07_foot_solo_1728x1080.png", 1728, 1080);

            var pc = PlayerController.Local;
            var c = new Vector3(3000f, 0f, 3000f);
            TestPool.Create(c, 12f, 2.0f, out _);
            Physics.SyncTransforms();
            pc.Teleport(c + new Vector3(0f, 0.1f, 0f));
            pc.LookAt(c + new Vector3(0f, 2f, 20f));
            yield return new WaitForSeconds(1.6f);
            _keys.Frame.SlowWalk = true; // dive
            yield return new WaitForSeconds(3.5f);
            Assert.IsTrue(hud.Status.LungShown, "the breath ring while swimming");
            Assert.Less(hud.Status.BreathShown, 0.999f, "the breath runs down under water");
            Snap("08_swimming_1920x1080.png");
            _keys.Frame.SlowWalk = false;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Capture_Driving()
        {
            yield return Load(TeamMode.Solo);
            Vehicle car = null;
            yield return WaitUntil(() => (car = FindCar()) != null, 10f, "the test box's car");
            var me = Combatant.LocalPlayer;
            PlayerController.Local.Teleport(car.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            _car.Frame.Interact = true;
            yield return WaitUntil(() => VehicleSeat.IsDriver(me), 3f, "in the driver's seat");
            yield return new WaitForSeconds(car.Rules.EnterSeconds + 0.3f);
            _car.Frame.Throttle = 1f;
            yield return new WaitForSeconds(1.8f);
            Assert.IsFalse(Hud.Instance.WeaponPanelVisible, "no ammo box while driving");
            Assert.IsFalse(Hud.Instance.Strip.Root.gameObject.activeSelf, "no weapon stack while driving");
            Snap("09_driving_1920x1080.png");
            Snap("10_driving_1728x1080.png", 1728, 1080);
            Snap("11_driving_2520x1080.png", 2520, 1080);
            _car.Frame.Throttle = 0f;
        }

        static Vehicle FindCar()
        {
            foreach (var v in Vehicle.All)
                if (v != null && v.Kind == VehicleKind.Car) return v;
            return null;
        }

        // The overlay canvases drawn through the player camera into a texture (as VehicleHandlingTests.Snap).
        void Snap(string name, int w = 1920, int h = 1080)
        {
            var cam = PlayerController.Local.Camera;
            var rt = new RenderTexture(w, h, 24);
            var before = cam.targetTexture;
            cam.targetTexture = rt;
            var moved = new System.Collections.Generic.List<Canvas>();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || !c.enabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f;
                Rescale(c);
                moved.Add(c);
            }
            Canvas.ForceUpdateCanvases();
            if (VehicleHud.Instance != null) VehicleHud.Instance.Relayout();
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(_folder, name), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = before;
            foreach (var c in moved) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; Rescale(c); }
            UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
        }

        static void Rescale(Canvas c)
        {
            var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) return;
            scaler.enabled = false;
            scaler.enabled = true;
        }
    }
}
