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
    // ADR-058 in the real TestBox scene with injected input: five weapon slots, fire mode (B), unarmed (X) with its
    // faster movement, and lying down (Z) with shooting and the body pose.
    public class SlotsAndProneTests
    {
        const int Pistol = 1, Smg = 2, Rifle = 3, Shotgun = 4;

        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.FirePressed = Frame.Jump = Frame.Crouch = Frame.Prone = Frame.Map = Frame.Holster = Frame.FireMode = Frame.ToggleView = false;
                Frame.SelectSlot = -1;
                return f;
            }
        }

        ScriptedInput _input;

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        IEnumerator LoadTestBox()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene("TestBox");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 15f, "local player spawned");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static void Give(Combatant me, int weaponId, int mag) =>
            Assert.IsTrue(me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = weaponId, Amount = mag }), "weapon " + weaponId + " accepted");

        // Along the south wall, heading east past the parked car (z = -27): out of every dummy's sight range.
        static void GoToTheQuietLane(PlayerController pc)
        {
            pc.Teleport(new Vector3(-25f, 0.1f, -29f));
            pc.LookAt(new Vector3(25f, 1.7f, -29f));
        }

        static float EyeHeight(Combatant c) => c.EyePosition.y - c.transform.position.y;

        static CombatantStance StanceOf(Combatant combatant)
        {
            var stance = combatant.GetComponent<CombatantStance>();
            Assert.IsNotNull(stance, "the combatant prefab needs a CombatantStance component");
            return stance;
        }

        static void Box(string name, Vector3 center, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = center;
            box.transform.localScale = size;
            Physics.SyncTransforms();
        }

        // Metres per second along x over `seconds`, after letting the speed settle.
        IEnumerator MeasureEastSpeed(PlayerController pc, float seconds, Action<float> result)
        {
            yield return new WaitForSeconds(0.3f);
            float x0 = pc.transform.position.x, t0 = Time.time;
            yield return new WaitForSeconds(seconds);
            result((pc.transform.position.x - x0) / (Time.time - t0));
        }

        // ---- Slots ----

        [UnityTest]
        public IEnumerator ThePistol_GoesToTheSidearmSlot_AndTheNumberKeysSelectAllFiveSlots()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Assert.AreEqual(5, Combatant.SlotCount);
            Give(me, Pistol, 12);
            Give(me, Rifle, 30);
            Give(me, Shotgun, 6);
            yield return WaitUntil(() => me.Slot0.Value == Rifle && me.Slot1.Value == Shotgun && me.Slot2.Value == Pistol, 2f, "weapons routed");
            Assert.AreEqual(Pistol, me.WeaponInSlot(2));
            Assert.AreEqual(12, me.MagInSlot(2));
            Assert.AreEqual(0, me.WeaponInSlot(3), "the melee slot stays empty");
            Assert.AreEqual(0, me.WeaponInSlot(4), "and so does the throwable slot");
            Assert.AreEqual(Loadout.SidearmSlot, me.ActiveSlot.Value, "the first weapon taken up is in hand");

            // Keys 1 to 5 give slots 0 to 4: rifle, shotgun, pistol, then empty hands twice.
            int[] expected = { Rifle, Shotgun, Pistol, 0, 0 };
            for (int slot = 0; slot < 5; slot++)
            {
                int s = slot;
                _input.Frame.SelectSlot = s;
                yield return WaitUntil(() => me.ActiveSlot.Value == s, 2f, "slot " + s + " selected");
                Assert.AreEqual(expected[s], me.ActiveWeaponId, "weapon in hand at slot " + s);
            }
        }

        [UnityTest]
        public IEnumerator ServerRefusesAPistolInAPrimarySlot_AndSwapsPrimariesOnly()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            Give(me, Rifle, 30);
            Give(me, Shotgun, 6);
            yield return WaitUntil(() => me.Slot0.Value == Rifle && me.Slot1.Value == Shotgun, 2f, "two primaries");

            // A pistol on the ground, right by the player: slot 1 refuses it, the loot stays.
            int before = LootItem.All.Count;
            MatchDirector.Instance.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = Pistol, Amount = 7 }, me.transform.position + me.transform.forward + Vector3.up * 0.3f);
            yield return WaitUntil(() => LootItem.All.Count == before + 1, 2f, "pistol on the ground");
            var pistol = LootItem.All[LootItem.All.Count - 1];
            me.CmdPickupToSlot(pistol.NetworkObject, 0);
            me.CmdPickupToSlot(pistol.NetworkObject, 3);
            me.CmdPickupToSlot(pistol.NetworkObject, 4);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(pistol.IsSpawned, "no primary, melee or throwable slot takes a pistol");
            Assert.AreEqual(Rifle, me.Slot0.Value);
            Assert.AreEqual(0, me.Slot2.Value);

            // The sidearm slot takes it.
            me.CmdPickupToSlot(pistol.NetworkObject, 2);
            yield return WaitUntil(() => me.Slot2.Value == Pistol, 2f, "pistol in the sidearm slot");
            Assert.AreEqual(7, me.Mag2.Value);
            Assert.AreEqual(Rifle, me.Slot0.Value, "the primaries are untouched");
            Assert.AreEqual(Shotgun, me.Slot1.Value);

            // Swaps: a pistol cannot trade with a primary slot; two primaries can.
            me.CmdSwapSlots(0, 2);
            me.CmdSwapSlots(2, 1);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(Rifle, me.Slot0.Value);
            Assert.AreEqual(Shotgun, me.Slot1.Value);
            Assert.AreEqual(Pistol, me.Slot2.Value);
            me.CmdSwapSlots(0, 1);
            yield return WaitUntil(() => me.Slot0.Value == Shotgun && me.Slot1.Value == Rifle, 2f, "primaries traded");
            Assert.AreEqual(Pistol, me.Slot2.Value);

            // Dropping the sidearm leaves a pistol on the ground and an empty slot.
            int onGround = LootItem.All.Count;
            me.CmdDropWeapon(2);
            yield return WaitUntil(() => me.Slot2.Value == 0, 2f, "sidearm slot emptied");
            // ADR-081: unloaded, its 7 rounds beside it as ammo.
            yield return WaitUntil(() => LootItem.All.Count == onGround + 2, 2f, "the pistol and its rounds lie on the ground");
            Assert.AreEqual(Pistol, LootItem.All[onGround].WeaponId.Value);
            Assert.AreEqual(0, LootItem.All[onGround].Amount.Value);
            Assert.AreEqual((int)LootKind.Ammo, LootItem.All[onGround + 1].Kind.Value);
            Assert.AreEqual(7, LootItem.All[onGround + 1].Amount.Value);
        }

        // ---- Fire mode ----

        [UnityTest]
        public IEnumerator B_SwitchesAutomaticWeaponsToSingleShots_AndOnlyThose()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            GoToTheQuietLane(pc);
            Give(me, Rifle, 30);
            Give(me, Pistol, 12);
            Give(me, Shotgun, 6);
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            Assert.AreEqual("연사", me.FireModeOf(0));
            Assert.AreEqual("단발", me.FireModeOf(1), "the shotgun is semi-automatic");
            Assert.AreEqual("단발", me.FireModeOf(2), "so is the pistol");
            Assert.AreEqual("", me.FireModeOf(3), "empty slot");
            Assert.IsTrue(me.ActiveAutoFire);
            var rifle = GameData.Weapons.Find(Rifle);
            Assert.AreEqual(rifle.FireInterval, (float)me.FireInterval(rifle), 1e-6f);

            // Automatic: holding the trigger keeps firing.
            _input.Frame.FireHeld = true;
            yield return new WaitForSeconds(0.5f);
            _input.Frame.FireHeld = false;
            Assert.Greater(30 - me.Mag0.Value, 2, "held trigger, automatic: several shots");
            yield return WaitUntil(() => !me.Reloading.Value, 2f, "settled");

            _input.Frame.FireMode = true;
            yield return WaitUntil(() => me.FireModeOf(0) == "단발", 2f, "single shots");
            Assert.IsFalse(me.ActiveAutoFire);
            Assert.AreEqual(GameData.Rules.Slots.SingleModeMinInterval, (float)me.FireInterval(rifle), 1e-6f, "held to the single-mode interval");
            int mag = me.Mag0.Value;
            _input.Frame.FireHeld = true;
            yield return new WaitForSeconds(0.5f);
            _input.Frame.FireHeld = false;
            Assert.AreEqual(mag, me.Mag0.Value, "a held trigger fires nothing in single mode");
            yield return new WaitForSeconds(0.3f);
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => me.Mag0.Value == mag - 1, 2f, "one click, one shot");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(mag - 1, me.Mag0.Value, "and only one");

            // The server holds the rate even for a client that asks every frame.
            int shots = 0, asked = 0;
            float until = Time.time + 1.2f;
            while (Time.time < until)
            {
                asked++;
                if (me.ServerFire(pc.transform.forward)) shots++;
                yield return null;
            }
            Assert.Greater(asked, shots);
            Assert.LessOrEqual(shots, 11, "at most one shot per 0.12 s (plus the first, plus the early allowance)");

            // ADR-089: B goes on to burst; one click fires the whole burst (3 for the rifle) and no more.
            yield return new WaitForSeconds(0.5f);
            _input.Frame.FireMode = true;
            yield return WaitUntil(() => me.FireModeOf(0) == "점사", 2f, "burst");
            Assert.IsFalse(me.ActiveAutoFire);
            mag = me.Mag0.Value;
            _input.Frame.FirePressed = true;
            yield return WaitUntil(() => me.Mag0.Value == mag - rifle.Burst, 2f, "one click, one burst");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(mag - rifle.Burst, me.Mag0.Value, "and only one burst");

            // Toggling back, and a weapon with no choice.
            _input.Frame.FireMode = true;
            yield return WaitUntil(() => me.FireModeOf(0) == "연사", 2f, "automatic again");
            _input.Frame.SelectSlot = 2;
            yield return WaitUntil(() => me.ActiveWeaponId == Pistol, 2f, "pistol in hand");
            _input.Frame.FireMode = true;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual("단발", me.FireModeOf(2), "a pistol only ever fires single shots");
            Assert.AreEqual(0, me.SingleFireSlots.Value & (1 << 2), "and nothing was recorded for it");
        }

        // ---- Unarmed ----

        [UnityTest]
        public IEnumerator X_PutsTheWeaponAway_TheServerBlocksFiringReloadingAndAiming_AndTheKeysTakeItOutAgain()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var visual = me.GetComponent<WeaponVisual>();
            GoToTheQuietLane(pc);
            Give(me, Rifle, 20);
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 60 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            yield return WaitUntil(() => visual.ShownWeaponId == Rifle, 2f, "rifle model shown");
            Assert.IsFalse(me.Holstered.Value);

            _input.Frame.Holster = true;
            yield return WaitUntil(() => me.Holstered.Value, 2f, "put away");
            Assert.AreEqual(0, me.ActiveWeaponId, "empty hands as far as everything else is concerned");
            Assert.IsNull(me.ActiveWeapon);
            Assert.AreEqual(0, me.ActiveMag);
            Assert.AreEqual(Rifle, me.WeaponInSlot(0), "the weapon is still in its slot");
            Assert.AreEqual(0, me.ActiveSlot.Value);
            yield return WaitUntil(() => visual.ShownWeaponId == 0, 2f, "no weapon model");

            // The server refuses, whatever the client sends.
            Assert.IsFalse(me.ServerFire(pc.transform.forward), "no shot");
            Assert.AreEqual(20, me.Mag0.Value);
            me.ServerReload();
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(me.Reloading.Value, "no reload");
            _input.Frame.FireHeld = true;
            _input.Frame.AimHeld = true;
            _input.Frame.FirePressed = true;
            yield return new WaitForSeconds(0.5f);
            _input.Frame.FireHeld = false;
            Assert.AreEqual(20, me.Mag0.Value, "the trigger does nothing");
            Assert.AreEqual(AimMode.Hip, me.AimMode, "and nothing to aim");
            _input.Frame.AimHeld = false;
            yield return new WaitForSeconds(0.3f);

            // X again takes it out, from the same slot.
            _input.Frame.Holster = true;
            yield return WaitUntil(() => !me.Holstered.Value, 2f, "taken out");
            Assert.AreEqual(Rifle, me.ActiveWeaponId);
            Assert.AreEqual(0, me.ActiveSlot.Value);
            yield return WaitUntil(() => visual.ShownWeaponId == Rifle, 2f, "rifle model shown again");
            yield return new WaitForSeconds(GameData.Weapons.Find(Rifle).DrawSeconds); // ADR-089: drawing first
            Assert.IsTrue(me.ServerFire(pc.transform.forward), "fires again");

            // A number key takes it out as well, and chooses the slot.
            _input.Frame.Holster = true;
            yield return WaitUntil(() => me.Holstered.Value, 2f, "put away again");
            _input.Frame.SelectSlot = 0;
            yield return WaitUntil(() => !me.Holstered.Value && me.ActiveWeaponId == Rifle, 2f, "the number key draws it");
        }

        [UnityTest]
        public IEnumerator EmptyHands_AreFaster_WalkingAndSprinting()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            GoToTheQuietLane(pc);
            Give(me, Rifle, 30);
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            float rifle = GameData.Rules.Locomotion.RifleMoveFactor; // ADR-095: empty hands 6.3 / 4.7, a rifle 4.6% slower

            _input.Frame.Move = new Vector2(0, 1);
            _input.Frame.Sprint = true;
            float armedSprint = 0f;
            yield return MeasureEastSpeed(pc, 1.0f, s => armedSprint = s);
            Assert.IsTrue(pc.Sprinting);
            Assert.AreEqual(6.3f * rifle, armedSprint, 0.3f, "armed sprint");

            _input.Frame.Holster = true;
            yield return WaitUntil(() => me.Holstered.Value, 2f, "put away");
            float unarmedSprint = 0f;
            yield return MeasureEastSpeed(pc, 1.0f, s => unarmedSprint = s);
            Assert.IsTrue(pc.Sprinting);
            Assert.GreaterOrEqual(unarmedSprint, armedSprint * 1.03f, "unarmed sprint is faster");
            Assert.AreEqual(6.3f, unarmedSprint, 0.3f);

            _input.Frame.Sprint = false;
            pc.Teleport(new Vector3(-25f, 0.1f, -29f));
            float unarmedWalk = 0f;
            yield return MeasureEastSpeed(pc, 1.0f, s => unarmedWalk = s);
            Assert.AreEqual(4.7f, unarmedWalk, 0.25f, "unarmed walk");

            _input.Frame.SelectSlot = 0;
            yield return WaitUntil(() => !me.Holstered.Value, 2f, "rifle out again");
            pc.Teleport(new Vector3(-25f, 0.1f, -29f));
            float armedWalk = 0f;
            yield return MeasureEastSpeed(pc, 1.0f, s => armedWalk = s);
            Assert.AreEqual(4.7f * rifle, armedWalk, 0.25f, "armed walk");
            Assert.GreaterOrEqual(unarmedWalk, armedWalk * 1.03f);
        }

        // ---- Prone ----

        [UnityTest]
        public IEnumerator Z_LiesDown_TheServersEyesDrop_TheCapsuleShrinks_AndASlowCrawlFollows()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            var body = me.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.4f);
            GoToTheQuietLane(pc);
            Give(me, Rifle, 30);
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            Assert.AreEqual(1.6f, EyeHeight(me), 0.01f);

            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Prone && stance.ProneAmount >= 0.999f, 2f, "prone on the server");
            Assert.AreEqual(Stance.Prone, pc.BodyStance);
            Assert.Less(EyeHeight(me), 0.6f, "the server's eyes are close to the ground");
            Assert.AreEqual(0.4f, EyeHeight(me), 0.03f);
            Assert.AreEqual(0.7f, body.height, 1e-3f);
            Assert.GreaterOrEqual(body.height, 2f * body.radius, "the controller capsule is never lower than its width");
            Assert.AreEqual(0.2f, me.StanceSpreadFactor, 1e-3f);
            Assert.AreEqual(0.1f, stance.FootstepVolumeFactor, 1e-3f);
            Assert.AreEqual(0f, stance.CrouchAmount, 1e-3f);

            // Looking from the eyes: the first-person camera is where the server's eyes are.
            _input.Frame.ToggleView = true;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(0f, Vector3.Distance(pc.Camera.transform.position, me.EyePosition), 0.05f, "camera and server eyes agree");
            _input.Frame.ToggleView = true;
            yield return null;

            // A slow crawl.
            _input.Frame.Move = new Vector2(0, 1);
            float crawl = 0f;
            yield return MeasureEastSpeed(pc, 1.0f, s => crawl = s);
            _input.Frame.Move = Vector2.zero;
            Assert.AreEqual(1.2f * GameData.Rules.Locomotion.RifleMoveFactor, crawl, 0.2f, "1.2 m/s times the rifle's factor (ADR-095)");

            // Space and sprinting stand up; Z toggles back down and up.
            _input.Frame.Jump = true;
            yield return WaitUntil(() => stance.Current == Stance.Stand, 2f, "Space stands up");
            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Prone, 2f, "down again");
            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Stand, 2f, "Z gets up again");
            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Prone, 2f, "down once more");
            _input.Frame.Crouch = true;
            yield return WaitUntil(() => stance.Current == Stance.Crouch && stance.CrouchAmount >= 0.999f && stance.ProneAmount <= 0.001f, 2f, "C from prone crouches");
            Assert.AreEqual(1.0f, body.height, 1e-3f);
            Assert.AreEqual(0.95f, EyeHeight(me), 0.03f);
        }

        [UnityTest]
        public IEnumerator FromTheGround_AShotHitsTheDummy_ThroughTheServer_AndThroughRealInput()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            Combatant dummy = null;
            yield return WaitUntil(() =>
            {
                foreach (var c in Combatant.All)
                    if (!c.IsPlayer) { dummy = c; return true; }
                return false;
            }, 5f, "a training dummy in the test box");
            yield return WaitUntil(() => dummy.ActiveWeaponId != 0, 5f, "the dummy armed itself");
            foreach (var c in Combatant.All)
            {
                if (c.IsPlayer) continue;
                // By name: the test assembly does not reference the AI assembly.
                foreach (var part in c.GetComponents<MonoBehaviour>())
                    if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            }
            pc.Teleport(dummy.transform.position + dummy.transform.forward * 5f + Vector3.up * 0.1f);
            Give(me, Rifle, 30);
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Prone && stance.ProneAmount >= 0.999f, 2f, "prone");
            pc.LookAt(dummy.transform.position + Vector3.up * 1.0f);
            yield return new WaitForSeconds(0.5f);
            float hp = dummy.Hp.Value;

            // The server's shot from the lying eyes, spread closed.
            float spread = me.StanceSpreadFactor;
            me.StanceSpreadFactor = 0f;
            bool fired = me.ServerFire(dummy.transform.position + Vector3.up * 1.0f - me.EyePosition);
            me.StanceSpreadFactor = spread;
            Assert.IsTrue(fired, "prone: the server lets the shot go");
            Assert.Less(dummy.Hp.Value, hp, "and it hits");

            // The same through the input: first person from the eyes, the trigger pulled.
            yield return new WaitForSeconds(0.3f);
            _input.Frame.ToggleView = true;
            yield return new WaitForSeconds(0.3f);
            pc.LookAt(dummy.transform.position + Vector3.up * 1.0f);
            yield return null;
            hp = dummy.Hp.Value;
            int mag = me.Mag0.Value;
            _input.Frame.FireHeld = true;
            yield return new WaitForSeconds(0.25f);
            _input.Frame.FireHeld = false;
            Assert.Less(me.Mag0.Value, mag, "shots left the weapon");
            Assert.Less(dummy.Hp.Value, hp, "and hit the dummy from the ground");
            Assert.AreEqual(Stance.Prone, stance.Current, "still lying");
        }

        [UnityTest]
        public IEnumerator Prone_BodyLiesOnItsBelly_FacingTheAim_WithTheWeaponLevel()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            var animation = me.GetComponent<CombatantAnimation>();
            var model = me.GetComponentInChildren<Animator>().transform;
            Transform hips = null;
            Renderer[] rifleParts = null;
            Give(me, Rifle, 30);
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 2f, "rifle in hand");
            foreach (var t in me.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Hips") hips = t;
                else if (t.name == "Weapon3") rifleParts = t.GetComponentsInChildren<Renderer>();
            }
            Assert.IsNotNull(hips, "Hips bone");
            Assert.IsNotNull(rifleParts, "rifle renderers");
            GoToTheQuietLane(pc);
            yield return new WaitForSeconds(0.5f);
            float floor = pc.transform.position.y;

            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Prone && stance.ProneAmount >= 0.999f, 2f, "prone");
            _input.Frame.AimHeld = false;
            yield return new WaitForSeconds(0.8f); // the upper layer's weapon pose has settled
            Assert.AreEqual(1f, animation.PronePose, 0.01f);

            // The model is pitched about the right axis: its up lies along the facing, its front faces the ground.
            float pitch = StanceRules.Current.ProneModelPitchDeg;
            Assert.AreEqual(pitch, Vector3.Angle(model.up, Vector3.up), 1.5f, "lying down");
            Assert.Greater(Vector3.Dot(model.up, pc.transform.forward), 0.95f, "head toward where the player looks");
            Assert.Greater(Vector3.Dot(model.forward, Vector3.down), 0.95f, "belly down");
            Assert.Less(hips.position.y - floor, 0.45f, "the hips are near the ground");
            Assert.Less(Vector3.Dot(hips.position - pc.transform.position, pc.transform.forward), -0.2f, "the body trails behind the capsule");

            // The weapon is level and points along the facing (east): its long side is along x.
            var bounds = rifleParts[0].bounds;
            foreach (var part in rifleParts) bounds.Encapsulate(part.bounds);
            Assert.Greater(bounds.size.x, bounds.size.y * 1.4f, "the weapon is level, not pointing at the ground");
            Assert.Greater(bounds.size.x, bounds.size.z * 1.4f, "and points the way the player looks");
        }

        [UnityTest]
        public IEnumerator UnderALowCeiling_ProneCannotStandUp_ButCanCrouchWhenThereIsRoom()
        {
            yield return LoadTestBox();
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            var stance = StanceOf(me);
            // A slab north of the player, its underside 1.2 m above the floor (the same one the crouch test uses).
            Box("LowSlab", new Vector3(3f, 1.7f, -19f), new Vector3(4f, 1f, 2f));
            pc.Teleport(new Vector3(3f, 0.1f, -22f));
            pc.LookAt(new Vector3(3f, 1.7f, 0f));
            yield return new WaitForSeconds(0.3f);

            _input.Frame.Prone = true;
            yield return WaitUntil(() => stance.Current == Stance.Prone, 2f, "prone");
            pc.Teleport(new Vector3(3f, 0.1f, -19f));
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(stance.CanStand(), "the slab is 1.2 m up: not room for 1.8");
            Assert.IsTrue(stance.CanReach(1.0f), "but room for the crouch");

            _input.Frame.Prone = true;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(Stance.Prone, stance.Current, "Z to stand: refused under the slab");
            _input.Frame.Jump = true;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(Stance.Prone, stance.Current, "Space: refused");
            Assert.AreEqual(0.7f, me.GetComponent<CharacterController>().height, 1e-3f);

            // The server refuses too, whatever the owner asks.
            stance.ServerSetStance(Stance.Stand);
            Assert.AreEqual(Stance.Prone, stance.Current, "the server: no head room");

            _input.Frame.Crouch = true;
            yield return WaitUntil(() => stance.Current == Stance.Crouch, 2f, "C: the crouch fits under the slab");
        }
    }
}
