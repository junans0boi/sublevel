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
        PlayerInputFrame _frame = new PlayerInputFrame { SelectSlot = -1 };
        Watch _watch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-feelcapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("FeelCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<FeelCapture>();
            capture._folder = Path.GetFullPath(args[i + 1]);
            PlayerController.InputOverride = capture;
        }

        public PlayerInputFrame Read()
        {
            var f = _frame;
            // One-frame presses.
            _frame.Jump = _frame.FirePressed = _frame.ToggleView = false;
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
            me.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = AmmoType.Medium, Amount = 60 });
            me.ServerGive(new LootEntry { Kind = LootKind.Medkit, Amount = 2 });
            me.ServerTakeDamage(35f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("02_hud_damaged");
            UiFocus.Set(UiScreen.Inventory);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("02_inventory");
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

            Debug.Log("[FeelCapture] done: " + _folder);
            Application.Quit();
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
