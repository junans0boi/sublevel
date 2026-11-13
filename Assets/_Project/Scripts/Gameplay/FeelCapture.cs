using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Player-build review shots: launch with `-feelcapture <folder>`. Plays a fixed script of inputs (aim, lean,
    // jump, move) and saves what the screen shows at each step, so the result can be checked by eye without a
    // person at the keyboard. Development aid only; it does nothing without the argument, and only the host (the
    // server of its own solo match) can hand itself the weapons. Strip before a multiplayer release (RISKS).
    // Late: the outside views (LateUpdate) replace the camera after the player has placed it.
    [DefaultExecutionOrder(200)]
    public class FeelCapture : MonoBehaviour, IPlayerInput, IVehicleInput
    {
        const float SpawnTimeoutSeconds = 30f;

        enum Watch { Game, Front, Side, Behind, Above, Optic, Fixed, Vehicle }

        string _folder;
        bool _eleShots; // `-eleshots <folder>`: the elevator start of the outdoor map, frame by frame
        bool _w5Shots; // `-w5shots <folder>` (TestBox): settings tabs at two sizes, a punch on a dummy, first-aid icon
        bool _w4Shots; // `-w4shots <folder>` (with -fastzones): scopes, healing, smoke and a region lockdown in the World
        bool _scopeShots; // `-scopeshots <folder>` (World): every optic through the sights, on the weapon, on the ground
        bool _worldShots; // `-worldshots <folder>`: a short tour of the outdoor map instead of the TestBox script
        bool _integShots; // `-integshots <folder>` (World): elevator rise, ping, slow walk, free look, a vault, car and motorcycle
        VehicleInputFrame _vehicleFrame = new VehicleInputFrame { Seat = -1 };
        Vector3 _fixFrom, _fixAt; // Watch.Fixed: a camera standing still
        Vehicle _watched;         // Watch.Vehicle: a camera riding along, _fixFrom in the vehicle's axes
        PlayerInputFrame _frame = new PlayerInputFrame { SelectSlot = -1 };
        Watch _watch;
        Vector3 _home, _homeForward; // where the player spawned: out of the dummies' sight
        Combatant _pinned; // scope shots: a bot held at _pinAt every frame
        Vector3 _pinAt;
        Quaternion _pinRotation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-feelcapture");
            int w = Array.IndexOf(args, "-worldshots");
            int e = Array.IndexOf(args, "-eleshots");
            if (e >= 0 && e + 1 < args.Length) { w = e; }
            int w4 = Array.IndexOf(args, "-w4shots");
            if (w4 >= 0 && w4 + 1 < args.Length) { w = w4; }
            int sc = Array.IndexOf(args, "-scopeshots");
            if (sc >= 0 && sc + 1 < args.Length) { w = sc; }
            int w5 = Array.IndexOf(args, "-w5shots");
            if (w5 >= 0 && w5 + 1 < args.Length) { w = w5; }
            int ig = Array.IndexOf(args, "-integshots");
            if (ig >= 0 && ig + 1 < args.Length) { w = ig; }
            if ((i < 0 || i + 1 >= args.Length) && (w < 0 || w + 1 >= args.Length)) return;
            bool world = i < 0;
            if (world) i = w;
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = Path.GetFullPath(args[i + 1]);
            capture._worldShots = world;
            capture._eleShots = e >= 0 && e + 1 < args.Length;
            capture._w4Shots = w4 >= 0 && w4 + 1 < args.Length;
            capture._w5Shots = w5 >= 0 && w5 + 1 < args.Length;
            capture._scopeShots = sc >= 0 && sc + 1 < args.Length;
            capture._integShots = ig >= 0 && ig + 1 < args.Length;
            PlayerController.InputOverride = capture;
            if (capture._integShots) VehicleInput.InputOverride = capture;
        }

        public PlayerInputFrame Read()
        {
            var f = _frame;
            // One-frame presses.
            _frame.Jump = _frame.FirePressed = _frame.ToggleView = _frame.Crouch = _frame.Heal = _frame.Interact = false;
            _frame.ThrowPressed = _frame.ThrowReleased = false;
            _frame.Prone = _frame.Holster = _frame.FireMode = _frame.Map = _frame.Ping = _frame.AutoRun = false;
            _frame.ZoomSteps = 0;
            _frame.Look = Vector2.zero;
            return f;
        }

        VehicleInputFrame IVehicleInput.Read()
        {
            var f = _vehicleFrame;
            _vehicleFrame.Interact = _vehicleFrame.EngineToggle = _vehicleFrame.ToggleView = false;
            _vehicleFrame.Seat = -1;
            return f;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float giveUp = Time.realtimeSinceStartup + SpawnTimeoutSeconds;
            while (Combatant.LocalPlayer == null || PlayerController.Local == null)
            {
                if (Time.realtimeSinceStartup > giveUp)
                {
                    Debug.LogError("[FeelCapture] no local player appeared");
                    Application.Quit(1);
                    yield break;
                }
                yield return null;
            }
            var me = Combatant.LocalPlayer;
            if (_integShots)
            {
                yield return IntegShots(me);
                Debug.Log("[FeelCapture] integration shots done: " + _folder);
                Application.Quit();
                yield break;
            }
            if (_eleShots)
            {
                yield return EleShots(me);
                Application.Quit();
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            if (_w5Shots)
            {
                yield return Wave5Shots(me);
                Debug.Log("[FeelCapture] wave 5 shots done: " + _folder);
                Application.Quit();
                yield break;
            }
            if (_scopeShots)
            {
                yield return ScopeShots(me);
                Debug.Log("[FeelCapture] scope shots done: " + _folder);
                Application.Quit();
                yield break;
            }
            if (_w4Shots)
            {
                yield return Wave4Shots(me);
                Debug.Log("[FeelCapture] wave 4 shots done: " + _folder);
                Application.Quit();
                yield break;
            }
            if (_worldShots)
            {
                yield return WorldShots(me);
                Debug.Log("[FeelCapture] world shots done: " + _folder);
                Application.Quit();
                yield break;
            }
            _home = me.transform.position;
            _homeForward = me.transform.forward;

            yield return Shot("01_unarmed_tpp");
            if (!me.IsServerInitialized)
            {
                Debug.LogError("[FeelCapture] only works when this process hosts the match");
                Application.Quit(1);
                yield break;
            }
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 4, Amount = 6 });
            yield return new WaitForSeconds(0.6f);
            yield return Shot("02_hip_tpp");

            // Automatic fire, several frames in a row to catch the flash and the tracers.
            _frame.FireHeld = true;
            yield return new WaitForSeconds(0.25f);
            yield return Burst("02_fire_tpp", 5, 0.02f);
            _frame.FireHeld = false;
            yield return new WaitForSeconds(0.4f);

            // The screens.
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = GameData.Weapons.Find(3).Ammo, Amount = 60 });
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 });
            me.ServerTakeDamage(35f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("02_hud_damaged");
            // The script walks into the dummies' sight later on; nothing hurts the player until the scripted death.
            me.ServerDamageFilter = _ => 0f;
            yield return InventoryShots(me);
            UiFocus.Set(UiScreen.Menu);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("02_menu");
            if (PauseMenu.Instance != null) PauseMenu.Instance.ShowSettings();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("02_menu_settings");
            if (PauseMenu.Instance != null) PauseMenu.Instance.Back();
            UiFocus.Close();
            yield return new WaitForSeconds(0.3f);

            _frame.AimHeld = true;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("03_shoulder_tpp");
            _frame.AimHeld = false;
            yield return new WaitForSeconds(0.5f);

            yield return Tap();
            yield return new WaitForSeconds(0.8f);
            yield return Shot("04_sights");
            _frame.LeanRight = true;
            yield return new WaitForSeconds(0.4f);
            yield return Shot("05_sights_lean_right");
            _frame.LeanRight = false;
            yield return Tap();
            yield return new WaitForSeconds(0.6f);

            foreach (var (name, left) in new[] { ("lean_left", true), ("lean_right", false) })
            {
                _frame.LeanLeft = left;
                _frame.LeanRight = !left;
                yield return new WaitForSeconds(0.4f);
                yield return Shot("06_" + name + "_tpp");
                _watch = Watch.Front;
                yield return Shot("07_" + name + "_front");
                _watch = Watch.Game;
                _frame.LeanLeft = _frame.LeanRight = false;
                yield return new WaitForSeconds(0.4f);
            }

            _frame.ToggleView = true;
            yield return new WaitForSeconds(0.6f);
            yield return Shot("08_hip_fpp");
            _frame.LeanLeft = true;
            yield return new WaitForSeconds(0.4f);
            yield return Shot("09_lean_left_fpp");
            _frame.LeanLeft = false;
            _frame.SelectSlot = 1;
            yield return null;
            _frame.SelectSlot = -1;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("10_shotgun_fpp");
            _frame.SelectSlot = 0;
            yield return null;
            _frame.SelectSlot = -1;
            _frame.ToggleView = true;
            yield return new WaitForSeconds(0.6f);

            // A standing jump seen from the side, then a running jump seen from the game camera.
            _watch = Watch.Behind;
            yield return JumpShots("11_jump_behind");
            yield return new WaitForSeconds(0.5f);
            _watch = Watch.Game;
            yield return JumpShots("11_jump_game");
            yield return new WaitForSeconds(0.5f);
            _watch = Watch.Side;
            yield return JumpShots("11_jump_side");
            _watch = Watch.Game;
            // Turn to face along the long side of the spawn area so there is room to run.
            _frame.Look = new Vector2(90f, 0f);
            yield return null;
            _frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.5f);
            yield return JumpShots("12_jump_running");

            _watch = Watch.Side;
            yield return Burst("13_walk_side", 4, 0.07f);
            _frame.Sprint = true;
            yield return new WaitForSeconds(0.4f);
            yield return Burst("14_sprint_side", 4, 0.07f);
            _frame.Sprint = false;
            _watch = Watch.Behind;
            _frame.Move = new Vector2(1f, 1f);
            yield return new WaitForSeconds(0.5f);
            yield return Burst("15_forward_right_behind", 4, 0.09f);
            _frame.Move = new Vector2(-1f, 0f);
            yield return new WaitForSeconds(0.5f);
            yield return Burst("16_left_behind", 4, 0.09f);
            _watch = Watch.Above;
            yield return Burst("17_left_above", 2, 0.09f);
            _frame.AimHeld = true;
            yield return new WaitForSeconds(0.5f);
            yield return Burst("17_left_shouldered_above", 2, 0.09f);
            _frame.Move = Vector2.zero;
            yield return new WaitForSeconds(0.6f);
            yield return Shot("17_standing_shouldered_above");
            _frame.AimHeld = false;
            _watch = Watch.Side;
            _frame.Move = new Vector2(0f, -1f);
            yield return new WaitForSeconds(0.5f);
            yield return Burst("18_back_side", 4, 0.09f);
            _frame.Move = Vector2.zero;
            yield return new WaitForSeconds(0.5f);

            yield return CrouchShots();
            yield return Wave3Shots(me);
            yield return GearShots(me);
            yield return MatchShots(me);

            Debug.Log("[FeelCapture] done: " + _folder);
            Application.Quit();
        }

        // The four-zone inventory with items on the ground, a vest on, two calibres and two weapons, at the two
        // resolutions that matter, plus a drag in progress.
        IEnumerator InventoryShots(Combatant me)
        {
            var director = MatchDirector.Instance;
            var gear = me.GetComponent<CombatantGear>();
            if (gear != null)
            {
                gear.ServerEquip(ArmorSlot.Vest, 2);
                gear.ServerEquip(ArmorSlot.Helmet, 1);
            }
            var thrower = me.GetComponent<ThrowableUser>();
            if (thrower != null) thrower.ServerAdd(2);
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Shotgun12, Amount = 18 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Pistol9, Amount = 36 });
            if (director != null)
            {
                var at = me.transform.position;
                var fwd = me.transform.forward;
                var right = me.transform.right;
                director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Weapon, WeaponId = 2, Amount = 30 }, at + fwd * 1.6f - right * 0.8f + Vector3.up * 0.2f);
                director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Vest, Tier = 3, Amount = 1 }, at + fwd * 1.6f + Vector3.up * 0.2f);
                director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Helmet, Tier = 2, Amount = 1 }, at + fwd * 1.6f + right * 0.8f + Vector3.up * 0.2f);
                director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 45 }, at + fwd * 2.2f - right * 0.4f + Vector3.up * 0.2f);
                director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Medkit, Amount = 1 }, at + fwd * 2.2f + right * 0.4f + Vector3.up * 0.2f);
                director.ServerSpawnLoot(new LootEntry { Kind = LootKind.Grenade, Amount = 1 }, at + fwd * 1.0f + right * 1.0f + Vector3.up * 0.2f);
            }
            yield return new WaitForSeconds(0.8f);
            foreach (var (w, h) in new[] { (1280, 720), (1920, 1080) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSeconds(1.0f);
                UiFocus.Set(UiScreen.Inventory);
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"02_inventory_{h}p");
                var screen = InventoryScreen.Instance;
                if (screen != null && screen.VicinityCount > 0)
                {
                    // Dragging a ground item toward the bag, and a weapon card toward the other slot.
                    screen.BeginDrag(screen.VicinitySource(0));
                    screen.MoveDrag(new Vector2(w * 0.5f, h * 0.55f), null);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"02_inventory_{h}p_drag");
                    screen.CancelDrag();
                }
                UiFocus.Close();
                yield return new WaitForSeconds(0.3f);
                if (h == 1080) yield return Shot("02_hud_1080p");
            }
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSeconds(1.0f);
        }

        // From the elevator: a run to the nearest loot (through its building), the loot on the floor, a bot far away,
        // then a fight with it. The player cannot be hurt for the tour.
        IEnumerator WorldShots(Combatant me)
        {
            var pc = PlayerController.Local;
            me.ServerDamageFilter = _ => 0f;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 90 });
            yield return new WaitForSeconds(0.5f);
            yield return Shot("w01_elevator");
            yield return HudResolutions("h_world");
            UiFocus.Set(UiScreen.Map);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("w10_map");
            UiFocus.Close();
            yield return new WaitForSeconds(0.3f);

            // The nearest loot that can be walked to.
            LootItem best = null;
            UnityEngine.AI.NavMeshPath path = null;
            float bestLen = float.MaxValue;
            foreach (var item in LootItem.All)
            {
                if (item == null) continue;
                var p = new UnityEngine.AI.NavMeshPath();
                if (!UnityEngine.AI.NavMesh.SamplePosition(item.transform.position, out var target, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(me.transform.position, out var start, 3f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (!UnityEngine.AI.NavMesh.CalculatePath(start.position, target.position, UnityEngine.AI.NavMesh.AllAreas, p) || p.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                float len = 0f;
                for (int c = 1; c < p.corners.Length; c++) len += Vector3.Distance(p.corners[c - 1], p.corners[c]);
                if (len < 25f) continue; // far enough to be a run, near enough to be quick
                if (len < bestLen) { bestLen = len; best = item; path = p; }
            }
            if (best != null)
            {
                Debug.Log($"[FeelCapture] running {bestLen:F0} m to loot {best.name}");
                _frame.Sprint = true;
                _frame.Move = new Vector2(0f, 1f);
                int corner = 1;
                float giveUp = Time.realtimeSinceStartup + 25f;
                int shots = 0;
                float nextShot = Time.realtimeSinceStartup + 2f;
                while (corner < path.corners.Length && Time.realtimeSinceStartup < giveUp)
                {
                    var c = path.corners[corner];
                    var flat = new Vector3(c.x - me.transform.position.x, 0f, c.z - me.transform.position.z);
                    if (flat.magnitude < 0.9f) { corner++; continue; }
                    _frame.Sprint = flat.magnitude > 6f;
                    pc.LookAt(new Vector3(c.x, me.EyePosition.y - 0.2f, c.z));
                    if (Time.realtimeSinceStartup > nextShot && shots < 2) { shots++; nextShot += 2.5f; yield return Shot("w02_running_" + shots); }
                    yield return null;
                }
                _frame.Move = Vector2.zero;
                _frame.Sprint = false;
                float left = Vector3.Distance(me.transform.position, best.transform.position);
                Debug.Log($"[FeelCapture] after the run: {left:F1} m from the loot, corner {corner}/{path.corners.Length}");
                if (left > 4f)
                {
                    // Stuck on something: stand in the room instead, facing the loot, and say so.
                    var back = (path.corners[path.corners.Length - 1] - path.corners[Mathf.Max(0, path.corners.Length - 2)]).normalized;
                    var stand = path.corners[path.corners.Length - 1] - back * 2.5f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(stand, out var hitStand, 2f, UnityEngine.AI.NavMesh.AllAreas)) stand = hitStand.position;
                    pc.Teleport(stand + Vector3.up * 0.1f);
                    Debug.LogWarning("[FeelCapture] the run did not reach the loot; teleported into the room");
                }
                yield return new WaitForSeconds(0.5f);
                pc.LookAt(best.transform.position);
                yield return new WaitForSeconds(0.5f);
                yield return Shot("w03_inside_loot");
                _frame.Interact = true;
                yield return new WaitForSeconds(0.3f);
                _frame.Interact = false;
                // A look around the room.
                _frame.Look = new Vector2(70f, 0f);
                yield return null;
                yield return new WaitForSeconds(0.4f);
                yield return Shot("w03_inside_look");
            }
            else Debug.LogWarning("[FeelCapture] no loot to run to");

            // A bot in the distance: stand about 40 m from it on the navmesh with a clear line.
            Combatant bot = null;
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value) { bot = c; break; }
            if (bot == null) { Debug.LogWarning("[FeelCapture] no bot in the match"); yield break; }
            Vector3 spot = default;
            bool found = false;
            for (int a = 0; a < 36 && !found; a++)
            {
                float ang = a * 10f * Mathf.Deg2Rad;
                var probe = bot.transform.position + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 28f;
                if (!UnityEngine.AI.NavMesh.SamplePosition(probe, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                var from = hit.position + Vector3.up * 1.6f;
                var to = bot.transform.position + Vector3.up * 1.3f;
                if (Physics.Linecast(from, to, out var block) && block.collider.GetComponentInParent<Combatant>() != bot) continue;
                spot = hit.position;
                found = true;
            }
            if (!found) { Debug.LogWarning("[FeelCapture] no clear line to the bot"); yield break; }
            pc.Teleport(spot + Vector3.up * 0.1f);
            yield return null;
            pc.LookAt(bot.transform.position + Vector3.up * 1.3f);
            yield return new WaitForSeconds(1.0f);
            yield return Shot("w04_bot_distance");
            // Sights up and fire; the bot fights back.
            _frame.AimHeld = true;
            yield return new WaitForSeconds(0.7f);
            for (int n = 0; n < 6 && !bot.Dead.Value; n++)
            {
                pc.LookAt(bot.transform.position + Vector3.up * 1.3f);
                _frame.FirePressed = _frame.FireHeld = true;
                yield return null;
                Debug.Log($"[FeelCapture] fight {n}: bot at {Vector3.Distance(bot.transform.position, me.transform.position):F1} m, hp {bot.Hp.Value}, mag {me.ActiveMag}");
                yield return Shot("w05_bot_fight_" + n);
                yield return new WaitForSeconds(0.25f);
            }
            _frame.AimHeld = false;
            _frame.FireHeld = false;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("w06_after");
            Debug.Log($"[FeelCapture] bot hp {bot.Hp.Value}, dead {bot.Dead.Value}");

            // Leave the bots alone for a minute and report what they do (the player stays out of it, immune).
            var began = new System.Collections.Generic.Dictionary<Combatant, Vector3>();
            foreach (var c in Combatant.All) if (c != null && !c.IsPlayer) began[c] = c.transform.position;
            pc.Teleport(_home + Vector3.up * 0.1f);
            for (int t = 1; t <= 6; t++)
            {
                yield return new WaitForSeconds(10f);
                int idx = 0;
                var line = new System.Text.StringBuilder($"[FeelCapture] bots at {t * 10} s:");
                foreach (var kv in began)
                {
                    if (kv.Key == null) continue;
                    line.Append($" #{idx++}[moved {Vector3.Distance(kv.Key.transform.position, kv.Value):F0} m, hp {kv.Key.Hp.Value:F0}{(kv.Key.Dead.Value ? " dead" : "")}, weapon {kv.Key.ActiveWeaponId}]");
                }
                Debug.Log(line.ToString());
            }
        }

        // Standing against crouched from the side and from behind, then crouch-walking.
        IEnumerator CrouchShots()
        {
            // Back at the spawn, out of the dummies' sight, facing the wall.
            var pc = PlayerController.Local;
            if (pc != null)
            {
                pc.Teleport(_home);
                yield return null;
                pc.LookAt(_home + _homeForward * 10f + Vector3.up * 1.6f);
                yield return new WaitForSeconds(0.5f);
            }
            _watch = Watch.Side;
            yield return Shot("20_stand_side");
            _watch = Watch.Behind;
            yield return Shot("20_stand_behind");
            _frame.Crouch = true;
            yield return new WaitForSeconds(0.7f);
            _watch = Watch.Side;
            yield return Shot("21_crouch_side");
            _watch = Watch.Behind;
            yield return Shot("21_crouch_behind");
            _watch = Watch.Front;
            yield return Shot("21_crouch_front");
            _watch = Watch.Game;
            yield return new WaitForSeconds(0.3f);
            yield return Shot("21_crouch_tpp");
            _frame.AimHeld = true;
            yield return new WaitForSeconds(0.7f);
            yield return Shot("21_crouch_shoulder_tpp");
            _frame.AimHeld = false;
            yield return new WaitForSeconds(0.4f);
            _frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.5f);
            _watch = Watch.Side;
            yield return Burst("22_crouchwalk_side", 4, 0.09f);
            _watch = Watch.Behind;
            yield return Burst("22_crouchwalk_behind", 3, 0.09f);
            _frame.Move = Vector2.zero;
            _watch = Watch.Game;
            yield return new WaitForSeconds(0.3f);
            _frame.Crouch = true;
            yield return new WaitForSeconds(0.6f);
        }

        // Armour on the HUD and a grenade thrown and going off, from the spawn corner where nothing else is in sight.
        // The player is immune by now (see the damage filter above), so the blast only has to be seen.
        IEnumerator GearShots(Combatant me)
        {
            var pc = PlayerController.Local;
            if (pc != null)
            {
                pc.Teleport(_home);
                yield return null;
                pc.LookAt(_home + _homeForward * 10f + Vector3.up * 1.6f);
            }
            _watch = Watch.Game;
            var gear = me.GetComponent<CombatantGear>();
            if (gear != null)
            {
                gear.ServerEquip(ArmorSlot.Vest, 2);
                gear.ServerEquip(ArmorSlot.Helmet, 1);
            }
            else Debug.LogWarning("[FeelCapture] the player has no CombatantGear");
            var thrower = me.GetComponent<ThrowableUser>();
            if (thrower != null) thrower.ServerAdd(3);
            else Debug.LogWarning("[FeelCapture] the player has no ThrowableUser");
            yield return new WaitForSeconds(0.6f);
            yield return Shot("40_armor_hud");

            // Aim down so it lands a few metres ahead, in view, instead of flying over the wall.
            if (pc != null) pc.LookAt(_home + _homeForward * 4f);
            yield return new WaitForSeconds(0.3f);
            // Aiming (the preview arc), then let go.
            _frame.ThrowPressed = true;
            _frame.ThrowHeld = true;
            yield return new WaitForSeconds(0.4f);
            yield return Shot("41_grenade_aim");
            _frame.ThrowHeld = false;
            _frame.ThrowReleased = true;
            yield return new WaitForSeconds(0.3f);
            yield return Shot("41_grenade_thrown");
            // The fuse is counted from the throw: shots every 0.2 s around the bang, and the moment it is reported.
            float fuse = ThrowableRules.Current.FuseSeconds;
            float thrownAt = Time.time - 0.3f;
            Action<Vector3, FishNet.Object.NetworkObject> logBang = (c, t) => Debug.Log($"[FeelCapture] grenade exploded {Time.time - thrownAt:F2} s after the throw at {c}");
            ThrownItem.ClientExploded += logBang;
            yield return new WaitForSeconds(Mathf.Max(0f, fuse - 0.9f));
            for (int n = 0; n < 9; n++)
            {
                yield return Shot($"42_grenade_blast_{n}");
                yield return new WaitForSeconds(0.12f);
            }
            ThrownItem.ClientExploded -= logBang;
        }

        // The HUD at the three resolutions that matter.
        IEnumerator Wave5Shots(Combatant me)
        {
            var pause = PauseMenu.Instance;
            foreach (var (w, h) in new[] { (1280, 720), (1920, 1080) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSeconds(1.2f);
                if (pause == null) { Debug.LogError("[FeelCapture] no pause menu"); break; }
                UiFocus.Set(UiScreen.Menu);
                yield return null;
                pause.ShowSettings();
                foreach (SettingsTab tab in Enum.GetValues(typeof(SettingsTab)))
                {
                    pause.Settings.SelectTab(tab);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"s_{h}p_{(int)tab}_{tab}");
                    if (tab == SettingsTab.Graphics)
                    {
                        pause.Settings.SelectPage(SettingsPage.Advanced);
                        yield return new WaitForSeconds(0.3f);
                        yield return Shot($"s_{h}p_{(int)tab}_{tab}_advanced");
                        pause.Settings.SelectPage(SettingsPage.Basic);
                    }
                }
                pause.Back();
                UiFocus.Close();
                yield return new WaitForSeconds(0.3f);
            }
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSeconds(1.0f);

            // A punch on the first dummy, unarmed, from a metre away.
            var pc = PlayerController.Local;
            var dummies = Dummies();
            if (dummies.Length > 0)
            {
                var target = dummies[0];
                pc.Teleport(target.transform.position + target.transform.forward * 1.1f + Vector3.up * 0.1f);
                yield return null;
                pc.LookAt(target.transform.position + Vector3.up * 1.3f);
                yield return new WaitForSeconds(0.3f);
                float hpBefore = target.Hp.Value;
                _watch = Watch.Side;
                _frame.FirePressed = _frame.FireHeld = true;
                yield return null;
                _frame.FireHeld = false;
                yield return new WaitForSeconds(0.08f);
                yield return Shot("p_punch_0");
                yield return new WaitForSeconds(0.12f);
                yield return Shot("p_punch_1");
                _watch = Watch.Game;
                yield return new WaitForSeconds(0.4f);
                Debug.Log($"[FeelCapture] punch: dummy hp {hpBefore} -> {target.Hp.Value}");
            }

            // The first-aid kit, medkit and bandage icons in the inventory.
            if (me.IsServerInitialized)
            {
                me.ServerGive(new LootEntry { Kind = LootKind.FirstAidKit, Amount = 1 });
                me.ServerGive(new LootEntry { Kind = LootKind.Bandage, Amount = 3 });
                me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 1 });
            }
            yield return new WaitForSeconds(0.5f);
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("i_inventory_firstaid");
            UiFocus.Close();
        }

        IEnumerator HudResolutions(string prefix)
        {
            foreach (var (w, h) in new[] { (1280, 720), (1920, 1080), (2560, 1440) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSeconds(1.2f);
                Debug.Log($"[FeelCapture] {prefix}: asked {w}x{h}, screen is {Screen.width}x{Screen.height}");
                yield return Shot($"{prefix}_{h}p");
            }
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSeconds(1.0f);
        }

        // Elevator start: the first seconds of the match, then stepping out. Logs where the player was put.
        IEnumerator EleShots(Combatant me)
        {
            var director = MatchDirector.Instance;
            Debug.Log($"[FeelCapture] elevator {(director != null && director.ElevatorAssignment.Count > 0 ? director.ElevatorAssignment[0] : -1)} at {me.transform.position}, seed {(director != null ? director.DeploymentSeed : 0)}");
            var info = WorldInfo.Current;
            if (info != null && director != null && director.ElevatorAssignment.Count > 0)
                Debug.Log("[FeelCapture] region of the player's elevator: " + info.Elevators[director.ElevatorAssignment[0]].Region);
            _watch = Watch.Game;
            float t0 = Time.time;
            for (int n = 0; n < 24; n++)
            {
                Debug.Log($"[FeelCapture] t={Time.time - t0:F1} blocked={SpawnProtection.InputBlocked} doorLeft={SpawnProtection.DoorSecondsLeft:F1} protect={SpawnProtection.LocalSecondsLeft:F1}");
                yield return Shot($"e{n:00}");
                if (!SpawnProtection.InputBlocked && n > 6) _frame.Move = new Vector2(0f, 1f);
                yield return new WaitForSeconds(0.35f);
            }
            _frame.Move = Vector2.zero;
            Debug.Log("[FeelCapture] after stepping out: " + me.transform.position);
        }

        // Integration review in the World (#84-#87): the car rising out of its shaft (through the door window and from
        // above the slab opening), a ping, slow walk and free look, a vault over a box, then a car and a motorcycle:
        // outside, driven in third and first person, and the seated poses seen from beside them.
        IEnumerator IntegShots(Combatant me)
        {
            var pc = PlayerController.Local;
            me.ServerDamageFilter = _ => 0f;
            ElevatorCar ride = null;
            foreach (var car in ElevatorCar.All)
                if (car != null && (ride == null || (car.Top - me.transform.position).sqrMagnitude < (ride.Top - me.transform.position).sqrMagnitude)) ride = car;
            for (int n = 0; SpawnProtection.InputBlocked && n < 40; n++)
            {
                bool rising = ride != null && ride.Offset > 0.01f;
                Debug.Log($"[FeelCapture] rise t{n}: phase {(ride != null ? ride.Phase.ToString() : "-")}, offset {(ride != null ? ride.Offset : 0f):F2}, door {SpawnProtection.DoorSecondsLeft:F1}");
                if (rising && n % 2 == 0)
                {
                    // Looking down into the slab opening while the car is still below it.
                    _watch = ride.Offset > 2.8f ? Watch.Fixed : Watch.Game;
                    _fixFrom = ride.Top + Vector3.up * 2.2f - ride.transform.forward * 0.6f;
                    _fixAt = ride.Top - Vector3.up * 8f + ride.transform.forward * 0.3f;
                    yield return Shot($"r{n:00}_rise_above");
                    _watch = Watch.Game;
                }
                yield return Shot($"r{n:00}_rise_window");
                yield return new WaitForSeconds(0.4f);
            }
            _watch = Watch.Game;
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 2, Amount = 12 });
            yield return new WaitForSeconds(0.5f);

            // Out of the station: the first car on the map is the place for the rest; stand 12 m from it.
            Vehicle carV = null, bike = null;
            foreach (var v in Vehicle.All)
            {
                if (v == null) continue;
                if (v.IsMotorcycle) { if (bike == null) bike = v; }
                else if (carV == null) carV = v;
            }
            if (carV == null || bike == null) { Debug.LogError("[FeelCapture] integ: no car or motorcycle in the World"); yield break; }
            Debug.Log($"[FeelCapture] integ: car at {carV.transform.position} up {carV.transform.up}, motorcycle at {bike.transform.position} up {bike.transform.up}");
            var open = carV.transform.position - carV.transform.right * 12f;
            if (UnityEngine.AI.NavMesh.SamplePosition(open, out var openHit, 6f, UnityEngine.AI.NavMesh.AllAreas)) open = openHit.position;
            pc.Teleport(open + Vector3.up * 0.1f);
            yield return null;
            pc.LookAt(carV.transform.position + Vector3.up * 0.5f + carV.transform.forward * 20f);
            yield return new WaitForSeconds(0.8f);

            // Ping: a point on the ground ahead; the compass and the world marker show it.
            pc.LookAt(me.EyePosition + (carV.transform.position - me.transform.position).normalized * 30f - Vector3.up * 3f);
            yield return new WaitForSeconds(0.3f);
            _frame.Ping = true;
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[FeelCapture] integ ping: has {MapMarker.Has} at {MapMarker.Position}");
            pc.LookAt(me.EyePosition + Quaternion.Euler(0f, 30f, 0f) * (carV.transform.position - me.transform.position).normalized * 30f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("p01_ping_compass");
            UiFocus.Set(UiScreen.Map);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("p02_ping_map");
            UiFocus.Close();
            yield return new WaitForSeconds(0.3f);

            // Slow walk (Ctrl) from the side, then free look (Alt) turned round to look back at the face.
            pc.LookAt(me.EyePosition - carV.transform.right * 20f);
            _frame.SlowWalk = true;
            _frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.8f);
            _watch = Watch.Side;
            yield return Burst("m01_slowwalk_side", 2, 0.15f);
            _watch = Watch.Game;
            _frame.FreeLook = true;
            for (int n = 0; n < 15; n++) { _frame.Look = new Vector2(10f, 0f); yield return null; }
            yield return new WaitForSeconds(0.2f);
            yield return Shot("m02_freelook_150");
            _frame.Move = Vector2.zero;
            _frame.SlowWalk = false;
            yield return new WaitForSeconds(0.2f);
            _frame.FreeLook = false;
            yield return new WaitForSeconds(0.5f);

            // A vault over a 0.9 m box placed ahead, seen from the side. The box borrows a world material (the
            // primitive's default one is not in the build).
            var feet = me.transform.position;
            var ahead = Vector3.ProjectOnPlane(me.transform.forward, Vector3.up).normalized;
            pc.LookAt(feet + ahead * 20f + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.3f);
            feet = me.transform.position;
            ahead = Vector3.ProjectOnPlane(me.transform.forward, Vector3.up).normalized;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "IntegVaultBox";
            box.transform.SetPositionAndRotation(feet + ahead * 0.9f + Vector3.up * 0.45f, Quaternion.LookRotation(ahead));
            box.transform.localScale = new Vector3(3f, 0.9f, 0.5f);
            var look = carV.GetComponentInChildren<MeshRenderer>();
            if (look != null) box.GetComponent<Renderer>().sharedMaterial = look.sharedMaterial;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);
            _watch = Watch.Side;
            _frame.Jump = true;
            for (int n = 0; n < 8; n++)
            {
                yield return Shot($"v{n:00}_vault_side");
                yield return new WaitForSeconds(0.1f);
            }
            Debug.Log($"[FeelCapture] integ vault: {pc.LastParkourPlan.Move}, moved {Vector3.Distance(feet, me.transform.position):F2} m");
            _watch = Watch.Game;
            Destroy(box);
            yield return new WaitForSeconds(0.5f);

            yield return VehicleShots(carV, "c");
            yield return VehicleShots(bike, "b");
        }

        // One vehicle: outside from the front quarter, then in the driver's seat, driven a little, in third and first
        // person, and seen from beside the seat; then out again.
        IEnumerator VehicleShots(Vehicle v, string prefix)
        {
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            pc.Teleport(v.DoorPosition(0) - v.transform.right * 3f + Vector3.up * 0.1f);
            yield return null;
            pc.LookAt(v.transform.position + Vector3.up * 0.8f);
            yield return new WaitForSeconds(0.6f);
            yield return Shot(prefix + "01_outside_tpp");
            _watched = v;
            _watch = Watch.Vehicle;
            float size = v.IsMotorcycle ? 0.6f : 1f;
            _fixFrom = new Vector3(-3.5f, 1.4f, 4.5f) * size;
            _fixAt = new Vector3(0f, 0.6f, 0f);
            yield return Shot(prefix + "02_outside_front");
            _fixFrom = new Vector3(-4.5f, 0.6f, 0f) * size;
            _fixAt = new Vector3(0f, 0.5f, 0f);
            yield return Shot(prefix + "02_outside_side_low");
            _watch = Watch.Game;

            pc.Teleport(v.DoorPosition(0) + Vector3.up * 0.05f);
            yield return null;
            yield return null;
            _vehicleFrame.Interact = true;
            float until = Time.realtimeSinceStartup + 3f;
            while (!VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(v.Rules.EnterSeconds + 0.4f);
            Debug.Log($"[FeelCapture] integ {v.Kind}: seated {VehicleSeat.SeatedAmount(me):F2}, astride {VehicleSeat.Astride(me)}, engine {v.EngineOn.Value}");
            pc.LookAt(v.transform.position + v.transform.forward * 30f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot(prefix + "03_seated_tpp");
            _watched = v;
            _watch = Watch.Vehicle;
            _fixFrom = new Vector3(-2.6f, 1.5f, 0.6f) * size;
            _fixAt = v.transform.InverseTransformPoint(me.transform.position + Vector3.up * 0.9f);
            yield return Shot(prefix + "04_seated_side");
            _watch = Watch.Game;

            _vehicleFrame.Throttle = 1f;
            yield return new WaitForSeconds(1.8f);
            pc.LookAt(v.transform.position + v.transform.forward * 30f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot(prefix + "05_driving_tpp");
            Debug.Log($"[FeelCapture] integ {v.Kind}: {v.SpeedKmh:F0} km/h, lean {v.LeanShown:F1}, up {v.transform.up}");
            _watched = v;
            _watch = Watch.Vehicle;
            _fixFrom = new Vector3(-3.2f, 1.2f, 1.2f) * size;
            yield return Shot(prefix + "06_driving_side");
            _watch = Watch.Game;
            _vehicleFrame.ToggleView = true;
            yield return new WaitForSeconds(0.4f);
            yield return Shot(prefix + "07_driving_fpp");
            pc.LookAt(me.EyePosition - v.transform.right * 10f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot(prefix + "08_fpp_look_left");
            _vehicleFrame.Throttle = -1f;
            yield return new WaitForSeconds(1.5f);
            _vehicleFrame.Throttle = 0f;
            _vehicleFrame.ToggleView = true;
            yield return new WaitForSeconds(0.5f);
            _vehicleFrame.Interact = true;
            until = Time.realtimeSinceStartup + 3f;
            while (VehicleSeat.IsAttached(me) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(0.6f);
            Debug.Log($"[FeelCapture] integ {v.Kind}: out {!VehicleSeat.IsAttached(me)}, at {me.transform.position}");
        }

        // Lying down, empty hands, optics: the wave-3 additions to the script.
        IEnumerator Wave3Shots(Combatant me)
        {
            var pc = PlayerController.Local;
            pc.Teleport(_home);
            yield return null;
            pc.LookAt(_home + _homeForward * 10f + Vector3.up * 1.6f);
            _frame.Move = Vector2.zero;
            _watch = Watch.Game;
            yield return new WaitForSeconds(0.6f);
            float eyeStand = me.EyePosition.y - me.transform.position.y;

            // Prone, from the four views.
            _frame.Prone = true;
            yield return new WaitForSeconds(1.2f);
            float eyeProne = me.EyePosition.y - me.transform.position.y;
            Debug.Log($"[FeelCapture] eye height standing {eyeStand:F2}, prone {eyeProne:F2}");
            _watch = Watch.Side; yield return Shot("50_prone_side");
            _watch = Watch.Behind; yield return Shot("50_prone_behind");
            _watch = Watch.Front; yield return Shot("50_prone_front");
            _watch = Watch.Above; yield return Shot("50_prone_above");
            _watch = Watch.Game; yield return Shot("50_prone_tpp");
            _frame.AimHeld = true;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("51_prone_shoulder_tpp");
            _frame.FireHeld = true;
            yield return new WaitForSeconds(0.2f);
            Vector3 originSeen = default;
            Action<Vector3, Vector3> grab = (o, e2) => originSeen = o;
            me.ShotFired += grab;
            yield return Burst("51_prone_fire_tpp", 3, 0.05f);
            me.ShotFired -= grab;
            Debug.Log($"[FeelCapture] prone shot origin height above feet {originSeen.y - me.transform.position.y:F2} (eye {eyeProne:F2})");
            _frame.FireHeld = false;
            _watch = Watch.Side; yield return Shot("51_prone_fire_side");
            _watch = Watch.Game;
            _frame.AimHeld = false;
            yield return new WaitForSeconds(0.4f);
            _frame.Move = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.6f);
            Vector3 p0 = me.transform.position;
            _watch = Watch.Side; yield return Burst("52_prone_crawl_side", 3, 0.12f);
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[FeelCapture] prone crawl speed {(me.transform.position - p0).magnitude / 1.0f:F2} m/s (approx)");
            _frame.Move = Vector2.zero;
            _watch = Watch.Game;
            _frame.Prone = true; // up again
            yield return new WaitForSeconds(1.0f);

            // Empty hands (X) against armed, running side by side.
            pc.Teleport(_home);
            yield return null;
            pc.LookAt(_home + _homeForward * 10f + Vector3.up * 1.6f);
            _frame.Look = new Vector2(90f, 0f);
            yield return null;
            foreach (var unarmed in new[] { false, true })
            {
                if (unarmed != me.Holstered.Value) { _frame.Holster = true; yield return new WaitForSeconds(0.5f); }
                pc.Teleport(_home); yield return null;
                _frame.Move = new Vector2(0f, 1f);
                yield return new WaitForSeconds(0.4f);
                Vector3 a = me.transform.position;
                _watch = Watch.Side;
                yield return Burst(unarmed ? "53_unarmed_run_side" : "53_armed_run_side", 3, 0.1f);
                float dt = 3 * 0.1f;
                yield return new WaitForSeconds(0.3f);
                Debug.Log($"[FeelCapture] {(unarmed ? "unarmed" : "armed")} walking speed ~{(me.transform.position - a).magnitude / (dt + 0.3f + 0.1f):F2} m/s");
                _frame.Move = Vector2.zero;
                _watch = Watch.Game;
                yield return new WaitForSeconds(0.3f);
            }
            if (me.Holstered.Value) { _frame.Holster = true; yield return new WaitForSeconds(0.5f); }
            pc.Teleport(_home); yield return null;
            pc.LookAt(_home + _homeForward * 10f + Vector3.up * 1.6f);
            _frame.SelectSlot = 0; yield return null; _frame.SelectSlot = -1;
            yield return new WaitForSeconds(0.4f);

            // Optics: attach, see through them, change, drop.
            var optics = me.GetComponent<CombatantOptics>();
            int slot = me.ActiveSlot.Value;
            Debug.Log($"[FeelCapture] optics component {(optics != null)}, weapon in hand {me.ActiveWeaponId}, slot {slot}");
            foreach (var (name, id) in new[] { ("4x", 5), ("reddot", 1), ("2x", 3), ("6x", 6) })
            {
                bool ok = optics != null && optics.ServerAttach(slot, id);
                Debug.Log($"[FeelCapture] attach {name}: {ok}");
                yield return new WaitForSeconds(0.5f);
                if (name == "4x")
                {
                    _watch = Watch.Side; yield return Shot("60_optic_on_weapon_side");
                    _watch = Watch.Behind; yield return Shot("60_optic_on_weapon_behind");
                    _watch = Watch.Game; yield return Shot("60_optic_hip_tpp");
                }
                yield return Tap();
                yield return new WaitForSeconds(1.0f);
                yield return Shot("61_sights_" + name);
                Debug.Log($"[FeelCapture] {name}: fov {PlayerController.Local.Camera.fieldOfView:F1}, ScopeView.Active {ScopeView.Active}, sens factor {ScopeView.SensitivityFactor:F2}");
                yield return Tap();
                yield return new WaitForSeconds(0.6f);
            }
            _frame.AimHeld = true;
            yield return new WaitForSeconds(0.8f);
            yield return Shot("62_shoulder_with_6x_tpp");
            _frame.AimHeld = false;
            yield return new WaitForSeconds(0.4f);
            int before = LootItem.All.Count;
            if (optics != null) optics.CmdDropOptic(slot);
            yield return new WaitForSeconds(0.6f);
            Debug.Log($"[FeelCapture] after dropping the optic: on weapon {(optics != null ? optics.OpticInSlot(slot) : -1)}, loot {before} -> {LootItem.All.Count}");

            // The inventory with an optic attached and one on the ground.
            if (optics != null) optics.ServerAttach(slot, 5);
            var director = MatchDirector.Instance;
            if (director != null)
                director.ServerSpawnLoot(Optics.ToLoot(3), me.transform.position + me.transform.forward * 1.6f + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.8f);
            foreach (var (w, h) in new[] { (1280, 720), (1920, 1080) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSeconds(1.0f);
                UiFocus.Set(UiScreen.Inventory);
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"63_inventory_optic_{h}p");
                UiFocus.Close();
                yield return new WaitForSeconds(0.3f);
            }
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSeconds(1.0f);
            if (optics != null) optics.ServerDetach(slot);
            yield return new WaitForSeconds(0.3f);
        }

        Combatant[] Dummies()
        {
            var list = new System.Collections.Generic.List<Combatant>();
            foreach (var c in Combatant.All)
                if (c != null && !c.IsPlayer && !c.Dead.Value) list.Add(c);
            return list.ToArray();
        }

        static DamageInfo Lethal(Combatant attacker, HitZone zone, int weaponId) =>
            new DamageInfo { Amount = 999f, Attacker = attacker, Zone = zone, WeaponId = weaponId, Cause = DamageCause.Shot };

        // A real headshot on a dummy, the kill feed, the eliminated screen, and after a restart the win screen.
        // Kills go through the server's damage path directly, as the weapons above did.
        IEnumerator MatchShots(Combatant me)
        {
            var pc = PlayerController.Local;
            var dummies = Dummies();
            if (dummies.Length < 3)
            {
                Debug.LogError("[FeelCapture] expected three dummies, found " + dummies.Length);
                yield break;
            }
            _frame.Heal = true;
            yield return new WaitForSeconds(0.2f);

            // Seven metres in front of the first dummy, sights on its head, one shot.
            var target = dummies[0];
            pc.Teleport(target.transform.position + target.transform.forward * 7f + Vector3.up * 0.1f);
            yield return null;
            var head = target.transform.position + Vector3.up * 1.68f;
            pc.LookAt(head);
            yield return Tap();
            yield return new WaitForSeconds(0.5f);
            pc.LookAt(head);
            yield return null;
            var view = me.GetComponent<CombatantView>();
            int headshotsBefore = view != null ? view.HeadshotConfirms : 0;
            float hpBefore = target.Hp.Value;
            _frame.FirePressed = _frame.FireHeld = true;
            yield return null;
            _frame.FireHeld = false;
            yield return Burst("30_headshot", 4, 0.02f);
            Debug.Log($"[FeelCapture] headshot confirms: {(view != null ? view.HeadshotConfirms - headshotsBefore : -1)}, dummy hp {hpBefore} -> {target.Hp.Value}");
            yield return Tap();
            yield return new WaitForSeconds(0.4f);

            target.ServerTakeDamage(Lethal(me, HitZone.Head, 3));
            yield return new WaitForSeconds(0.4f);
            yield return Shot("31_killfeed_1");
            dummies[1].ServerTakeDamage(Lethal(me, HitZone.Body, 4));
            yield return new WaitForSeconds(0.4f);
            yield return Shot("31_killfeed_2");
            yield return HudResolutions("h_testbox");

            // The last dummy gets the player.
            me.ServerDamageFilter = null;
            me.ServerTakeDamage(Lethal(dummies[2], HitZone.Body, 1));
            yield return new WaitForSeconds(1.0f);
            yield return Shot("32_eliminated");

            // A fresh match, then every dummy down: the win screen.
            if (MatchState.Instance == null)
            {
                Debug.LogError("[FeelCapture] no match state");
                yield break;
            }
            MatchState.Instance.RequestRestart();
            yield return null;
            float giveUp = Time.realtimeSinceStartup + SpawnTimeoutSeconds;
            while (Combatant.LocalPlayer == null || Combatant.LocalPlayer == me || PlayerController.Local == null || Dummies().Length < 3)
            {
                if (Time.realtimeSinceStartup > giveUp)
                {
                    Debug.LogError("[FeelCapture] the restarted match did not come up");
                    yield break;
                }
                yield return null;
            }
            me = Combatant.LocalPlayer;
            yield return new WaitForSeconds(1.5f);
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            yield return new WaitForSeconds(0.5f);
            dummies = Dummies();
            for (int i = 0; i < dummies.Length; i++)
            {
                dummies[i].ServerTakeDamage(Lethal(me, i == 0 ? HitZone.Head : HitZone.Body, 3));
                yield return new WaitForSeconds(0.3f);
                if (i == dummies.Length - 2) yield return Shot("33_killfeed_before_win");
            }
            yield return new WaitForSeconds(0.8f);
            yield return Shot("33_win");
        }

        // Wave 4 review in the World: fixed scope views, the sniper's 8x, healing, smoke, then a region lockdown
        // (run with -fastzones so it comes within a minute).
        IEnumerator Wave4Shots(Combatant me)
        {
            var pc = PlayerController.Local;
            float began = Time.realtimeSinceStartup;
            // Only zone damage (and the scripted fall below) reaches the player.
            me.ServerDamageFilter = info => info.Cause == DamageCause.Zone || info.Cause == DamageCause.Fall ? info.Amount : 0f;
            yield return new WaitForSeconds(4f); // the elevator door
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 5, Amount = 5 });
            yield return new WaitForSeconds(0.5f);
            // Out of the elevator, a few metres into the open, looking at the horizon.
            pc.Teleport(me.transform.position + me.transform.forward * 4f + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.3f);
            var optics = me.GetComponent<CombatantOptics>();
            Debug.Log($"[FeelCapture] w4: weapon {me.ActiveWeaponId} slot {me.ActiveSlot.Value}");
            for (int s = 0; s < 2; s++)
            {
                _frame.SelectSlot = s; yield return null; _frame.SelectSlot = -1;
                yield return new WaitForSeconds(0.6f);
                int slot = me.ActiveSlot.Value;
                int weapon = me.ActiveWeaponId;
                var list = weapon == 5 ? new[] { ("sniper_8x", 7) } : new[] { ("warden_4x", 5), ("warden_reddot", 1), ("warden_holo", 2) };
                foreach (var (name, id) in list)
                {
                    bool ok = optics != null && optics.ServerAttach(slot, id);
                    yield return new WaitForSeconds(0.5f);
                    yield return Tap();
                    yield return new WaitForSeconds(1.0f);
                    yield return Shot("s_" + name);
                    Debug.Log($"[FeelCapture] w4 {name} (weapon {weapon}, attached {ok}): fov {pc.Camera.fieldOfView:F1}, scope hides weapon {ScopeView.HidesWeapon}");
                    yield return Tap();
                    yield return new WaitForSeconds(0.6f);
                }
            }

            // Healing: hurt, then a bandage; the ring shows the progress.
            me.ServerTakeDamage(new DamageInfo { Amount = 45f, Cause = DamageCause.Fall, Zone = HitZone.Body });
            var healing = me.GetComponent<CombatantHealing>();
            if (healing != null)
            {
                healing.ServerAdd(HealKind.Bandage, 3);
                yield return new WaitForSeconds(0.3f);
                bool ok = healing.ServerBegin(HealKind.Bandage);
                yield return new WaitForSeconds(1.2f);
                yield return Shot("s_heal_ring");
                float hp0 = me.Hp.Value;
                yield return new WaitForSeconds(Mathf.Max(0.5f, healing.RemainingSeconds + 0.5f));
                Debug.Log($"[FeelCapture] w4 heal began {ok}: hp {hp0:F0} -> {me.Hp.Value:F0}, completed {healing.ServerCompleted}");
            }

            // Smoke: thrown ahead, seen as it spreads.
            var thrower = me.GetComponent<ThrowableUser>();
            if (thrower != null)
            {
                thrower.ServerAdd(ThrowableKind.Smoke, 1);
                bool thrown = thrower.ServerThrowAimed((me.transform.forward - Vector3.up * 0.1f).normalized, ThrowableKind.Smoke);
                yield return new WaitForSeconds(SmokeRules.Current.FuseSeconds + 5f);
                yield return Shot("s_smoke");
                Debug.Log($"[FeelCapture] w4 smoke thrown {thrown}");
            }

            // Lockdown: wait for the first announcement, then stand inside that region next to one of its passes.
            var zones = ZoneDirector.Instance;
            if (zones == null) { Debug.LogWarning("[FeelCapture] w4: no ZoneDirector"); yield break; }
            int region = -1;
            float giveUp = Time.realtimeSinceStartup + 90f;
            while (region < 0 && Time.realtimeSinceStartup < giveUp)
            {
                for (int r = 0; r < zones.RegionCount; r++) if (zones.StateOf(r) != ZoneState.Safe) region = r;
                yield return null;
            }
            if (region < 0) { Debug.LogWarning("[FeelCapture] w4: no region was announced"); yield break; }
            var geo = zones.Geometry;
            Debug.Log($"[FeelCapture] w4 region {zones.RegionIdOf(region)} {zones.StateOf(region)} at {Time.realtimeSinceStartup - began:F0} s");
            for (int p = 0; p < geo.PassCount; p++)
            {
                if (geo.PassA[p] != region && geo.PassB[p] != region) continue;
                var at = new Vector3(geo.PassX[p], geo.PassY[p], geo.PassZ[p]);
                float yaw = geo.PassYawDeg[p] * Mathf.Deg2Rad;
                var along = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var inside = at + along * 7f;
                if (zones.RegionIndexAt(inside) != region) inside = at - along * 7f;
                if (UnityEngine.AI.NavMesh.SamplePosition(inside, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) inside = hit.position;
                pc.Teleport(inside + Vector3.up * 0.2f);
                yield return null;
                pc.LookAt(at + Vector3.up * 1.4f);
                break;
            }
            yield return new WaitForSeconds(1.0f);
            yield return Shot("s_lock_warning");
            giveUp = Time.realtimeSinceStartup + 40f;
            while (zones.StateOf(region) == ZoneState.Warning && Time.realtimeSinceStartup < giveUp) yield return null;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("s_lock_barrier");
            float hpA = me.Hp.Value;
            pc.LookAt(me.EyePosition - me.transform.forward * 10f + Vector3.up * 0.5f);
            yield return new WaitForSeconds(4f);
            yield return Shot("s_lock_fog");
            Debug.Log($"[FeelCapture] w4 lockdown {zones.StateOf(region)}: hp {hpA:F0} -> {me.Hp.Value:F0} over 4 s, pass locked {zones.IsPassLocked(0)}");
            giveUp = Time.realtimeSinceStartup + 40f;
            while (zones.StateOf(region) == ZoneState.Lockdown && Time.realtimeSinceStartup < giveUp) yield return null;
            me.ServerDamageFilter = _ => 0f;
            yield return new WaitForSeconds(4f);
            yield return Shot("s_lock_bombard");
            Debug.Log($"[FeelCapture] w4 then {zones.StateOf(region)}, markers {zones.Markers.Count}, hp {me.Hp.Value:F0}");
        }

        // Every optic (ADR-060, ADR-062) at 1920x1080 in the World: through the sights at a still bot ~40 m away with
        // a building near it, then each optic on the weapon from the side in third person, then all of them on the
        // ground and in the inventory's ground list.
        IEnumerator ScopeShots(Combatant me)
        {
            var pc = PlayerController.Local;
            me.ServerDamageFilter = _ => 0f;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            // The bots stand still for the pictures.
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer) continue;
                if (c.GetComponent("BotController") is Behaviour brain) brain.enabled = false;
                var agent = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) agent.enabled = false;
            }
            yield return new WaitForSeconds(4f); // the elevator door
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 3, Amount = 30 });
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = 5, Amount = 5 });
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Rifle556, Amount = 90 });
            yield return new WaitForSeconds(0.5f);

            // A spot ~40 m from a bot with a clear line to it, preferring one where something solid stands behind it.
            Combatant bot = null;
            Vector3 spot = me.transform.position;
            float bestScore = float.MinValue;
            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsPlayer || c.Dead.Value) continue;
                var target = c.transform.position + Vector3.up * 1.3f;
                for (int a = 0; a < 36; a++)
                {
                    float ang = a * 10f * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    if (!UnityEngine.AI.NavMesh.SamplePosition(c.transform.position + dir * 40f, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    var from = hit.position + Vector3.up * 1.6f;
                    if (Physics.Linecast(from, target, out var block) && block.collider.GetComponentInParent<Combatant>() != c) continue;
                    // Something 5-40 m behind the bot, and to its sides, makes the view less bare.
                    float score = 0f;
                    var look = (target - from).normalized;
                    var side = Vector3.Cross(Vector3.up, look).normalized;
                    foreach (float off in new[] { -6f, -2f, 0f, 2f, 6f })
                        if (Physics.Raycast(target + side * off + look * 1f, look, out var behind, 40f) && behind.distance > 4f) score += 1f;
                    if (score > bestScore) { bestScore = score; bot = c; spot = hit.position; }
                }
            }
            if (bot == null) Debug.LogWarning("[FeelCapture] scopes: no bot in sight; shooting from the elevator");
            pc.Teleport(spot + Vector3.up * 0.1f);
            yield return null;
            var eye = spot + Vector3.up * 1.6f;
            var ahead = bot != null ? bot.transform.position - spot : me.transform.forward;
            ahead.y = 0f;
            ahead.Normalize();
            // The bot itself may stand behind cover: bring it out into the open, 35-50 m ahead, in clear view.
            Vector3 aimAt = eye + ahead * 40f;
            if (bot != null)
            {
                var across = Vector3.Cross(Vector3.up, ahead);
                bool placed = false;
                foreach (float dist in new[] { 40f, 36f, 45f, 50f, 32f })
                {
                    foreach (float off in new[] { 0f, -3f, 3f, -6f, 6f, -10f, 10f })
                    {
                        if (!UnityEngine.AI.NavMesh.SamplePosition(spot + ahead * dist + across * off, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                        var chest = hit.position + Vector3.up * 1.2f;
                        var feet = hit.position + Vector3.up * 0.3f;
                        if (Physics.Linecast(eye, chest, out var b1) && b1.collider.GetComponentInParent<Combatant>() == null) continue;
                        if (Physics.Linecast(eye, feet, out var b2) && b2.collider.GetComponentInParent<Combatant>() == null) continue;
                        var controller = bot.GetComponent<CharacterController>();
                        if (controller != null) controller.enabled = false;
                        bot.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(-ahead));
                        if (controller != null) controller.enabled = true;
                        aimAt = hit.position + Vector3.up * 1.3f;
                        _pinned = bot;
                        _pinAt = hit.position;
                        _pinRotation = Quaternion.LookRotation(-ahead);
                        placed = true;
                        Debug.Log($"[FeelCapture] scopes: bot placed {Vector3.Distance(spot, hit.position):F0} m ahead, {off} m aside");
                        break;
                    }
                    if (placed) break;
                }
                if (!placed) Debug.LogWarning("[FeelCapture] scopes: no clear spot for the bot");
            }
            yield return new WaitForSeconds(0.5f);

            var optics = me.GetComponent<CombatantOptics>();
            var visual = me.GetComponent<OpticVisual>();
            var plan = new[] { (0, 1, "w_reflex"), (0, 2, "w_projector"), (0, 3, "w_2x"), (0, 4, "w_3x"), (0, 5, "w_4x"), (0, 6, "w_6x"), (1, 7, "s_8x"), (1, 8, "s_15x") };
            int selected = -1;
            foreach (var (slotIndex, id, name) in plan)
            {
                if (selected != slotIndex)
                {
                    _frame.SelectSlot = slotIndex; yield return null; _frame.SelectSlot = -1;
                    yield return new WaitForSeconds(0.8f);
                    selected = slotIndex;
                }
                int slot = me.ActiveSlot.Value;
                optics?.ServerRemove(slot);
                bool ok = optics != null && optics.ServerAttach(slot, id);
                yield return new WaitForSeconds(0.5f);
                pc.LookAt(aimAt);
                yield return new WaitForSeconds(0.3f);
                yield return Tap();
                yield return new WaitForSeconds(1.2f);
                pc.LookAt(aimAt);
                yield return new WaitForSeconds(0.2f);
                yield return Shot("o_" + name);
                var sv = ScopeView.Instance;
                if (_pinned != null)
                {
                    int shown = 0;
                    foreach (var r in _pinned.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff) shown++;
                    Debug.Log($"[FeelCapture] bot at {_pinned.transform.position} (pinned {_pinAt}), dead {_pinned.Dead.Value}, renderers shown {shown}");
                }
                Debug.Log($"[FeelCapture] scopes {name} (weapon {me.ActiveWeaponId}, optic {id}, attached {ok}): fov {pc.Camera.fieldOfView:F1}, hides weapon {ScopeView.HidesWeapon}, hides hud weapon {ScopeView.HidesHudWeapon}, housing {(sv != null && sv.Housing != null && sv.Housing.activeInHierarchy)}, stacked {(sv != null && sv.HousingCamera != null)}");
                // Variable scopes (ADR-063): wheel down to the lowest zoom and look again.
                int down = id == 6 ? 3 : id == 8 ? 7 : 0;
                if (down > 0)
                {
                    for (int n = 0; n < down; n++)
                    {
                        _frame.ZoomSteps = -1;
                        yield return null;
                        yield return null;
                    }
                    yield return new WaitForSeconds(1.0f);
                    yield return Shot($"o_{name}_at{Mathf.RoundToInt(ScopeView.CurrentZoom)}");
                    Debug.Log($"[FeelCapture] scopes {name} wheeled down {down}: zoom {ScopeView.CurrentZoom}, fov {pc.Camera.fieldOfView:F2}");
                }
                yield return Tap();
                yield return new WaitForSeconds(0.6f);

                // The same optic on the weapon from the side, third person (the body and the weapon in the hand).
                bool wasFirst = pc.Rig.Mode == ViewMode.FirstPerson;
                if (wasFirst) pc.Rig.ToggleMode();
                pc.LookAt(aimAt);
                yield return new WaitForSeconds(0.6f);
                _watch = Watch.Optic;
                yield return new WaitForSeconds(0.3f);
                yield return Shot("t_" + name);
                _watch = Watch.Game;
                if (wasFirst) pc.Rig.ToggleMode();
                yield return new WaitForSeconds(0.5f);
            }

            // All optics on the ground in front, then in the inventory's ground list.
            var director = MatchDirector.Instance;
            if (director != null)
            {
                var at = me.transform.position;
                var fwd = me.transform.forward;
                var right = me.transform.right;
                for (int id = 1; id <= 8; id++)
                {
                    int i = id - 1;
                    director.ServerSpawnLoot(Optics.ToLoot(id), at + fwd * (2.6f + (i / 4) * 0.6f) + right * ((i % 4) - 1.5f) * 0.5f + Vector3.up * 0.15f);
                }
            }
            yield return new WaitForSeconds(1.2f);
            foreach (var item in LootItem.All)
            {
                if (item == null || item.Kind.Value != (int)LootKind.Optic) continue;
                var view = item.GetComponent<LootView>();
                var box = item.transform.Find("Cube")?.GetComponent<Renderer>();
                Debug.Log($"[FeelCapture] loot optic {item.Optic.Value}: model {(view != null && view.OpticModel != null)}, box drawn {(box != null && box.enabled && !box.forceRenderingOff)}");
            }
            pc.LookAt(me.transform.position + me.transform.forward * 2.9f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("g_loot_ground");
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("i_inventory_optics");
            UiFocus.Close();
        }

        IEnumerator Tap()
        {
            _frame.AimHeld = true;
            yield return null;
            yield return null;
            _frame.AimHeld = false;
            yield return null;
        }

        IEnumerator JumpShots(string name)
        {
            _frame.Jump = true;
            float start = Time.time;
            foreach (float at in new[] { 0.08f, 0.2f, 0.34f, 0.5f, 0.62f, 0.72f, 0.82f, 1.0f })
            {
                while (Time.time - start < at) yield return null;
                yield return Shot($"{name}_{Mathf.RoundToInt(at * 100):000}");
            }
        }

        IEnumerator Burst(string name, int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                yield return Shot($"{name}_{i}");
                yield return new WaitForSeconds(interval);
            }
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_folder, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
        }

        // Runs after the player has placed the camera: the outside views replace it for this frame.
        void LateUpdate()
        {
            if (_pinned != null && (_pinned.transform.position - _pinAt).sqrMagnitude > 0.01f)
                _pinned.transform.SetPositionAndRotation(_pinAt, _pinRotation);
            var pc = PlayerController.Local;
            if (_watch == Watch.Game || pc == null || pc.Camera == null) return;
            var player = pc.transform;
            if (_watch == Watch.Fixed || _watch == Watch.Vehicle)
            {
                var from = _fixFrom;
                var at = _fixAt;
                if (_watch == Watch.Vehicle && _watched != null)
                {
                    from = _watched.transform.TransformPoint(_fixFrom);
                    at = _watched.transform.TransformPoint(_fixAt);
                }
                pc.Camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
                pc.Camera.fieldOfView = 50f;
                return;
            }
            if (_watch == Watch.Optic)
            {
                // Close on the optic on the weapon in hand, from the weapon's right side (away from the body), a little
                // above and behind, in the optic's own axes (+z toward the muzzle, +y up).
                var optic = pc.GetComponent<OpticVisual>();
                var parts = optic != null ? optic.Parts : null;
                var window = optic != null ? optic.Window : null;
                var focus = window != null ? Vector3.Lerp(window.position, parts.transform.position, 0.5f) : player.position + Vector3.up * 1.4f;
                var right = parts != null ? parts.transform.right.normalized : player.right;
                var up = parts != null ? parts.transform.up.normalized : Vector3.up;
                var fwd = parts != null ? parts.transform.forward.normalized : player.forward;
                var camT = pc.Camera.transform;
                camT.position = focus + right * 0.30f + up * 0.10f - fwd * 0.06f;
                camT.rotation = Quaternion.LookRotation(focus - camT.position, Vector3.up);
                pc.Camera.fieldOfView = 40f;
                return;
            }
            var offset = _watch == Watch.Front ? player.forward * 3.2f
                : _watch == Watch.Side ? player.right * 3.6f
                : -player.forward * 3.2f + Vector3.up * 0.6f;
            var cam = pc.Camera.transform;
            if (_watch == Watch.Above)
            {
                // Straight down, the player's facing toward the top of the picture.
                cam.position = player.position + Vector3.up * 5f;
                cam.rotation = Quaternion.LookRotation(Vector3.down, player.forward);
                pc.Camera.fieldOfView = 35f;
                return;
            }
            cam.position = player.position + Vector3.up * 1.1f + offset;
            cam.rotation = Quaternion.LookRotation(player.position + Vector3.up * 1.0f - cam.position);
            pc.Camera.fieldOfView = 45f;
        }
    }

    // `-menushots <folder>`: the main menu, its settings, a match, back to the menu and a second match (ADR-044).
    public class MenuCapture : MonoBehaviour
    {
        string _folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-menushots");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("MenuCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuCapture>()._folder = Path.GetFullPath(args[i + 1]);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            float until = Time.realtimeSinceStartup + 20f;
            while (MainMenu.Instance == null && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("m01_menu");
            MainMenu.Instance.ShowSettings();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("m02_settings");
            foreach (var preset in new[] { GraphicsPreset.Low, GraphicsPreset.High, GraphicsPreset.Medium })
            {
                GraphicsOptions.Preset = preset;
                GraphicsOptions.Apply();
                Debug.Log($"[MenuCapture] preset {preset}: SSAO feature found {GraphicsOptions.SsaoToggleAvailable}, active {GraphicsOptions.SsaoActive}, shadow {QualitySettings.shadowDistance}");
            }
            MainMenu.Instance.Back();
            for (int round = 1; round <= 2; round++)
            {
                MainMenu.Instance.StartGame();
                until = Time.realtimeSinceStartup + 60f;
                while ((Combatant.LocalPlayer == null || PlayerController.Local == null) && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSeconds(5f);
                int players = 0;
                foreach (var c in Combatant.All) if (c != null && c.IsPlayer) players++;
                Debug.Log($"[MenuCapture] match {round}: scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, local player {(Combatant.LocalPlayer != null)}, players {players}, combatants {Combatant.All.Count}");
                yield return Shot($"m0{2 + round}_match{round}");
                MainMenu.LeaveMatch();
                until = Time.realtimeSinceStartup + 20f;
                yield return null;
                while (MainMenu.Instance == null && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSeconds(1.5f);
                Debug.Log($"[MenuCapture] back in the menu {round}: menu {(MainMenu.Instance != null)}, local player {(Combatant.LocalPlayer != null)}, combatants {Combatant.All.Count}");
                yield return Shot($"m0{4 + round}_menu_again{round}");
            }
            Application.Quit();
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_folder, name + ".png"), texture.EncodeToPNG());
            Destroy(texture);
        }
    }
}
