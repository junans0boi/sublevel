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
            if ((i < 0 || i + 1 >= args.Length) && (w < 0 || w + 1 >= args.Length)) return;
            bool world = i < 0;
            if (world) i = w;
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = Path.GetFullPath(args[i + 1]);
            capture._worldShots = world;
            PlayerController.InputOverride = capture;
        }

        public PlayerInputFrame Read()
        {
            var f = _frame;
            // One-frame presses.
            _frame.Jump = _frame.FirePressed = _frame.ToggleView = _frame.Crouch = _frame.Heal = _frame.Interact = false;
            _frame.ThrowPressed = _frame.ThrowReleased = false;
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
}
