using System;
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #153: the generated training range loads as a hosted match, has what it promises (targets at the five distances,
    // the item table, doors, a pit of water, two vehicles) and its checklist ticks off real actions of the local player
    // through the scripted input.
    public class TrainingRangePlayTests
    {
        sealed class ScriptedInput : IPlayerInput
        {
            public PlayerInputFrame Frame = new PlayerInputFrame { SelectSlot = -1 };

            public PlayerInputFrame Read()
            {
                var f = Frame;
                Frame.ZeroSteps = 0;
                Frame.Heal = Frame.ThrowPressed = Frame.ThrowReleased = false;
                Frame.FirePressed = Frame.Jump = Frame.Interact = Frame.Reload = Frame.Crouch = Frame.Prone = false;
                return f;
            }
        }

        sealed class ScriptedVehicleInput : IVehicleInput
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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input = new ScriptedInput();
            PlayerController.InputOverride = _input;
            SceneManager.LoadScene(TrainingRange.SceneName);
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null && TrainingRange.Instance != null, 30f, "local player in the range");
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            VehicleInput.InputOverride = null;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Range_HasTargetsAtFiveDistances_TheTable_Doors_Water_AndVehicles()
        {
            var meters = UnityEngine.Object.FindObjectsByType<TrainingTarget>(FindObjectsSortMode.None).Select(t => t.Meters).Distinct().OrderBy(m => m).ToArray();
            CollectionAssert.AreEqual(new[] { 10, 25, 50, 100, 200 }, meters);
            yield return WaitUntil(() => LootItem.All.Count >= TrainingRange.Instance.Loot.Length, 10f, "the table's items");
            yield return WaitUntil(() => DoorSystem.Instance != null && DoorSystem.Instance.Count == 2, 5f, "two doors");
            Assert.AreEqual(1, WaterVolume.All.Count, "the swimming pit's water");
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None).Length == 2, 10f, "a car and a bike");
            var me = Combatant.LocalPlayer;
            Assert.AreEqual(TrainingRange.StartHp, me.Hp.Value, 0.5f, "starts hurt so healing has something to do");
            var ticked = Enum.GetValues(typeof(TrainingStep)).Cast<TrainingStep>().Where(TrainingRange.Instance.Checklist.IsDone).ToArray();
            Assert.AreEqual(0, ticked.Length, "nothing is ticked before the player does anything: " + string.Join(",", ticked));
        }

        [UnityTest]
        public IEnumerator Checklist_TicksMoveSprintCrouchProne_ShootReload_PickupAndDoor()
        {
            var list = TrainingRange.Instance.Checklist;
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;

            _input.Frame.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => list.IsDone(TrainingStep.Move), 3f, "move ticked");
            _input.Frame.Sprint = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Sprint), 3f, "sprint ticked");
            _input.Frame.Move = Vector2.zero;
            _input.Frame.Sprint = false;
            yield return new WaitForSeconds(0.3f);
            _input.Frame.Crouch = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Crouch), 3f, "crouch ticked");
            yield return new WaitForSeconds(0.3f);
            _input.Frame.Prone = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Prone), 3f, "prone ticked");
            _input.Frame.Prone = true;   // back up
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(list.IsDone(TrainingStep.Shoot), "not shot yet");

            // Shoot the 10 m dummy.
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 90 });
            yield return WaitUntil(() => me.ActiveWeaponId == 3, 2f, "rifle in hand");
            pc.Teleport(new Vector3(0f, 0.1f, 2f));
            yield return null;
            _input.Frame.Move = Vector2.zero;
            for (int i = 0; i < 40 && !list.IsDone(TrainingStep.Shoot); i++)
            {
                pc.LookAt(new Vector3(0f, 1.2f, 10f));
                _input.Frame.FireHeld = true;
                yield return null;
            }
            _input.Frame.FireHeld = false;
            Assert.IsTrue(list.IsDone(TrainingStep.Shoot), "shot ticked");
            yield return WaitUntil(() => TrainingRange.Instance.TargetHits > 0, 3f, "the dummy was hit");
            yield return new WaitForSeconds(0.5f);
            _input.Frame.Reload = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Reload), 3f, "reload ticked");
            Assert.IsFalse(list.IsDone(TrainingStep.Attach), "taking a gun is not fitting a part");

            // Pick something up at the table.
            pc.Teleport(new Vector3(-12.2f, 0.1f, -4f));
            pc.LookAt(new Vector3(-14.5f, 1.2f, -4f));
            yield return WaitUntil(() => pc.NearbyLoot != null, 3f, "loot in reach");
            _input.Frame.Interact = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Pickup), 5f, "pickup ticked");

            // Open the north door of the house.
            var door = DoorSystem.Instance[0];
            pc.Teleport(door.transform.position + door.transform.forward * 1.2f);
            pc.LookAt(door.transform.position + Vector3.up * 1.2f);
            yield return null;
            yield return null;
            _input.Frame.Interact = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Door), 3f, "door ticked");
            Assert.AreNotEqual(DoorState.Closed, door.State);
        }

        [UnityTest]
        public IEnumerator Checklist_TicksAttach_Aim_AndZero_OnAScopedRifle()
        {
            var list = TrainingRange.Instance.Checklist;
            var me = Combatant.LocalPlayer;
            var optics = me.GetComponent<CombatantOptics>();
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 5, Amount = 5 });
            yield return WaitUntil(() => me.ActiveWeaponId == 5, 2f, "sniper in hand");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(list.IsDone(TrainingStep.Attach), "a bare gun has no part fitted");
            Assert.IsTrue(optics.ServerAttach(me.ActiveSlot.Value, 8), "15x fitted");
            yield return WaitUntil(() => list.IsDone(TrainingStep.Attach), 3f, "attach ticked");
            _input.Frame.AimHeld = true;
            yield return null;
            yield return null;
            _input.Frame.AimHeld = false;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Aim), 3f, "aim ticked (sights)");
            Assert.IsFalse(list.IsDone(TrainingStep.Zero), "swapping the scope alone is not the player zeroing it");
            _input.Frame.ZeroSteps = 1;
            yield return null;
            _input.Frame.ZeroSteps = 0;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Zero), 3f, "zero ticked");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Course_CrawlGapNeedsCrouching_VaultAndLedgeWork()
        {
            var list = TrainingRange.Instance.Checklist;
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;

            // The gap in the wall at x = 34 is 1.2 m high: standing is stopped, crouching goes through.
            pc.Teleport(new Vector3(31f, 0.1f, -14f));
            pc.LookAt(new Vector3(40f, 1.6f, -14f));
            yield return new WaitForSeconds(0.3f);
            _input.Frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(2f);
            Assert.Less(pc.transform.position.x, 34f, "standing cannot pass under the lintel");
            _input.Frame.Crouch = true;
            yield return new WaitForSeconds(2.5f);
            Assert.Greater(pc.transform.position.x, 35f, "crouched, the player passes the gap");
            _input.Frame.Move = Vector2.zero;

            // The 0.9 m box at x = 24.
            pc.Teleport(new Vector3(20f, 0.1f, -14f));
            pc.LookAt(new Vector3(30f, 1.6f, -14f));
            yield return new WaitForSeconds(0.5f);
            _input.Frame.Crouch = pc.BodyStance == Stance.Crouch;
            yield return null;
            _input.Frame.Move = new Vector2(0f, 1f);
            float end = Time.realtimeSinceStartup + 6f;
            while (!list.IsDone(TrainingStep.Vault) && Time.realtimeSinceStartup < end)
            {
                // Space once the box is within the probe (the near face is at x = 23.75).
                if (pc.transform.position.x > 22.9f) _input.Frame.Jump = true;
                yield return null;
            }
            _input.Frame.Move = Vector2.zero;
            Assert.IsTrue(list.IsDone(TrainingStep.Vault), "vault ticked at the low box");

            // The 2.3 m wall at x = 43.5: jump at it, catch the edge, pull up.
            yield return new WaitForSeconds(0.8f);
            pc.Teleport(new Vector3(42.9f, 0.1f, -14f));
            pc.LookAt(new Vector3(50f, 1.6f, -14f));
            yield return new WaitForSeconds(0.5f);
            _input.Frame.Jump = true;
            end = Time.realtimeSinceStartup + 3f;
            while (!pc.OnLedge && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(pc.OnLedge, "caught the edge of the 2.3 m wall");
            yield return new WaitForSeconds(0.4f);
            _input.Frame.Jump = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Ledge), 3f, "ledge ticked");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Course_StairsAreWalkable_AndThePitHasAWayOut()
        {
            var pc = PlayerController.Local;
            Combatant.LocalPlayer.ServerDamageFilter = _ => 0f;
            pc.Teleport(new Vector3(50.5f, 0.1f, -14f));
            pc.LookAt(new Vector3(60f, 1.6f, -14f));
            yield return new WaitForSeconds(0.3f);
            _input.Frame.Move = new Vector2(0f, 1f);
            yield return WaitUntil(() => pc.transform.position.y > 1.4f, 6f, "up the stairs onto the platform");

            pc.Teleport(new Vector3(75f, 0.5f, -14f));
            pc.LookAt(new Vector3(95f, 1.6f, -14f));
            yield return WaitUntil(() => pc.Swimming, 4f, "swimming in the pit");
            // Swim to the ramp and walk out onto the ground.
            yield return WaitUntil(() => !pc.Swimming && pc.transform.position.x > 93f && pc.transform.position.y > -0.3f, 25f, "out of the pit by the ramp");
            _input.Frame.Move = Vector2.zero;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Checklist_TicksHeal_Throw_AndVehicle()
        {
            var list = TrainingRange.Instance.Checklist;
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            me.ServerGive(new LootEntry { Kind = LootKind.Bandage, Amount = 3 });
            me.ServerGive(new LootEntry { Kind = LootKind.Grenade, Amount = 2 });
            yield return new WaitForSeconds(0.5f);

            var hp = me.Hp.Value;
            _input.Frame.Heal = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Heal), 12f, "heal ticked (bandage finished)");
            Assert.Greater(me.Hp.Value, hp, "and it healed");

            // Grenade: select the throwable slot, hold G, let go.
            // ADR-144: key 5 is a press (a second press cycles the kind), so send exactly one frame of it.
            _input.Frame.SelectSlot = 4;
            yield return null;
            _input.Frame.SelectSlot = -1;
            yield return WaitUntil(() => me.ActiveSlot.Value == Loadout.ThrowableSlot && !me.Holstered.Value, 3f, "throwable in hand");
            pc.LookAt(new Vector3(0f, 1.5f, 30f));
            _input.Frame.ThrowPressed = true;
            _input.Frame.ThrowHeld = true;
            yield return new WaitForSeconds(0.4f);
            _input.Frame.ThrowHeld = false;
            _input.Frame.ThrowReleased = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Throw), 4f, "throw ticked");

            // Get into the car with F beside it.
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None).Length == 2, 10f, "vehicles");
            var car = UnityEngine.Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None).First(v => !v.IsMotorcycle);
            var keys = new ScriptedVehicleInput();
            VehicleInput.InputOverride = keys;
            pc.Teleport(car.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            keys.Frame.Interact = true;
            yield return WaitUntil(() => list.IsDone(TrainingStep.Vehicle), 5f, "vehicle ticked (seated)");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator MainMenu_TrainingButton_OpensTheRange()
        {
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return WaitUntil(() => MainMenu.Instance != null, 10f, "the main menu");
            yield return null;
            var labels = MainMenu.Instance.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text).ToArray();
            CollectionAssert.Contains(labels, "훈련장");
            MainMenu.Instance.StartTraining();
            Assert.IsTrue(MainMenu.Instance.IsLoading);
            yield return WaitUntil(() => PlayerController.Local != null && TrainingRange.Instance != null, 30f, "the range after the menu button");
        }

        [UnityTest]
        public IEnumerator Range_KeepsThePlayerAlive_AndSwimmingInThePitTicksSwim()
        {
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            me.ServerTakeDamage(500f);
            yield return null;
            Assert.IsFalse(me.Dead.Value, "no way to die in the range");
            Assert.GreaterOrEqual(me.Hp.Value, 1f);

            pc.Teleport(new Vector3(78f, 0.5f, -14f));
            yield return WaitUntil(() => TrainingRange.Instance.Checklist.IsDone(TrainingStep.Swim), 6f, "swim ticked in the pit");
        }
    }
}
