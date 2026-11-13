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
    public class FeelCapture : MonoBehaviour, IPlayerInput
    {
        const float SpawnTimeoutSeconds = 30f;

        enum Watch { Game, Front, Side, Behind, Above }

        string _folder;
        bool _eleShots; // `-eleshots <folder>`: the elevator start of the outdoor map, frame by frame
        bool _worldShots; // `-worldshots <folder>`: a short tour of the outdoor map instead of the TestBox script
        PlayerInputFrame _frame = new PlayerInputFrame { SelectSlot = -1 };
        Watch _watch;
        Vector3 _home, _homeForward; // where the player spawned: out of the dummies' sight

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-feelcapture");
            int w = Array.IndexOf(args, "-worldshots");
            int e = Array.IndexOf(args, "-eleshots");
            if (e >= 0 && e + 1 < args.Length) { w = e; }
            if ((i < 0 || i + 1 >= args.Length) && (w < 0 || w + 1 >= args.Length)) return;
            bool world = i < 0;
            if (world) i = w;
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = Path.GetFullPath(args[i + 1]);
            capture._worldShots = world;
            capture._eleShots = e >= 0 && e + 1 < args.Length;
            PlayerController.InputOverride = capture;
        }

        public PlayerInputFrame Read()
        {
            var f = _frame;
            // One-frame presses.
            _frame.Jump = _frame.FirePressed = _frame.ToggleView = _frame.Crouch = _frame.Heal = _frame.Interact = false;
            _frame.ThrowPressed = _frame.ThrowReleased = false;
            _frame.Prone = _frame.Holster = _frame.FireMode = _frame.Map = false;
            _frame.Look = Vector2.zero;
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
            if (_eleShots)
            {
                yield return EleShots(me);
                Application.Quit();
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
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
            var pc = PlayerController.Local;
            if (_watch == Watch.Game || pc == null || pc.Camera == null) return;
            var player = pc.transform;
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
