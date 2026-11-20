using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // #375 review shots of the peek cycle (ADR-311) in the real World scene: one bot with a rifle against the (unhurt, still) player
    // 16-20 m away, next to a wall it can hide behind. Shots: hiding behind the cover, out at the peek spot firing, reloading behind it.
    // The other bots are switched off and parked. Run on purpose with
    //   BOTCOVER_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.BotCoverCaptureTests
    [Explicit, Category("Capture")]
    public class BotCoverCaptureTests
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Rifle = 3;
        static readonly Type BotType = Type.GetType("Game.AI.BotController, Game.AI");

        sealed class StillInput : IPlayerInput
        {
            public PlayerInputFrame Read() => new PlayerInputFrame { SelectSlot = -1 };
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        BotRules _peaceRules;
        float _peaceSeconds, _peaceSpread, _farmSeconds;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerController.InputOverride = null;
            if (_peaceRules != null) { _peaceRules.EarlyPeaceSeconds = _peaceSeconds; _peaceRules.EarlyPeaceSpreadSeconds = _peaceSpread; _peaceRules.FarmSeconds = _farmSeconds; }
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static bool Clear(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        static bool OpenSky(Vector3 p) => !Physics.Raycast(p + Vector3.up * 0.5f, Vector3.up, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        // Nothing within `meters` at chest height all round: the player stands in the open, so whatever cover there is lies at the bot's end.
        static bool OpenAround(Vector3 p, float meters)
        {
            for (int d = 0; d < 360; d += 30)
                if (Physics.Raycast(p + Vector3.up * 1.2f, Quaternion.Euler(0f, d, 0f) * Vector3.forward, meters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        static bool OnMesh(Vector3 p, float radius, out Vector3 at)
        {
            at = default;
            if (!NavMesh.SamplePosition(p, out var hit, radius, NavMesh.AllAreas)) return false;
            at = hit.position;
            return true;
        }

        // A spot for the player in the open and one for the bot 16-20 m away in plain sight of it, with a hiding place 3 m from the bot
        // whose crouched eye line to the player is cut by a wall within 1.8 m, and a peek spot beside it (2-3.5 m) that sees the player.
        static bool FindStage(Vector3 around, out Vector3 player, out Vector3 bot, out Vector3 hide)
        {
            player = bot = hide = default;
            for (float r = 0f; r <= 160f; r += 9f)
            {
                int steps = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.PI * r / 9f));
                for (int s = 0; s < steps; s++)
                {
                    var off = Quaternion.Euler(0f, 360f * s / steps, 0f) * Vector3.forward * r;
                    if (!OnMesh(around + off, 2f, out var p) || !OpenSky(p) || !OpenAround(p, 8f)) continue;
                    var eye = p + Vector3.up * 1.6f;
                    for (int a = 0; a < 360; a += 20)
                        for (float dist = 16f; dist <= 20f; dist += 4f)
                        {
                            var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                            if (!OnMesh(p + dir * dist, 1f, out var b) || Mathf.Abs(b.y - p.y) > 1f || !OpenSky(b)) continue;
                            if (!Clear(b + Vector3.up * 1.1f, eye) || !Clear(b + Vector3.up * 1.6f, eye)) continue;
                            for (int d = 0; d < 8; d++)
                            {
                                var hdir = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
                                if (!OnMesh(b + hdir * 3f, 1f, out var h) || Mathf.Abs(h.y - b.y) > 0.6f || !OpenSky(h)) continue;
                                if (!Physics.Linecast(h + Vector3.up * 0.95f, eye, out var wall, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                                if (wall.distance > 1.8f || wall.collider.GetComponentInParent<Combatant>() != null) continue;
                                if (!Physics.Linecast(h + Vector3.up * 1.6f, eye, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue; // tall enough to hide a standing head too
                                var toP = eye - h; toP.y = 0f;
                                var side = Vector3.Cross(Vector3.up, toP.normalized);
                                bool peek = false;
                                foreach (float sgn in new[] { 1f, -1f })
                                    foreach (float o in new[] { 2f, 3.5f })
                                        if (OnMesh(h + side * sgn * o, 1f, out var k) && Clear(k + Vector3.up * 1.6f, eye)) peek = true;
                                if (!peek) continue;
                                player = p; bot = b; hide = h;
                                return true;
                            }
                        }
                }
            }
            return false;
        }

        static void Save(Camera cam, string path, Vector3 at, Vector3 lookAt)
        {
            cam.fieldOfView = 55f;
            cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(lookAt - at));
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[BotCoverCapture] SHOT " + path);
        }

        // Two views of the bot: over its shoulder toward the player, and a high side view with bot, cover and player in one frame.
        // Each view takes the first camera spot that is not inside anything and sees the bot's chest.
        static void Shoot(Camera cam, string folder, string name, Vector3 bot, Vector3 player)
        {
            var chest = bot + Vector3.up * 1.0f;
            var t = player - bot; t.y = 0f; t.Normalize();
            var side = Vector3.Cross(Vector3.up, t);
            // a: close, behind the bot (away from the player) so the cover sits between it and the player; b: high and wider.
            var close = new System.Collections.Generic.List<Vector3>();
            var high = new System.Collections.Generic.List<Vector3>();
            foreach (float deg in new[] { 30f, -30f, 60f, -60f, 0f, 90f, -90f })
            {
                var away = Quaternion.Euler(0f, deg, 0f) * -t;
                close.Add(away * 3.2f + Vector3.up * 1.9f); close.Add(away * 2.4f + Vector3.up * 2.4f);
                high.Add(away * 6.5f + Vector3.up * 5f); high.Add(away * 4.5f + Vector3.up * 6f);
            }
            var views = new[] { ("a", close, chest + t * 2.5f), ("b", high, bot + t * 5f) };
            foreach (var (tag, spots, look) in views)
            {
                var at = bot + spots[0];
                foreach (var s in spots)
                {
                    var c = bot + s;
                    if (!Physics.CheckSphere(c, 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && Clear(c, chest) && Clear(c, bot + Vector3.up * 1.6f)) { at = c; break; }
                }
                Save(cam, Path.Combine(folder, $"{name}_{tag}.png"), at, look);
            }
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator BotHides_PeeksAndFires_ReloadsBehindCover()
        {
            if (BotType == null) Assert.Ignore("Game.AI.BotController is not in this build");
            string folder = Environment.GetEnvironmentVariable("BOTCOVER_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/botcover";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);

            PlayerController.InputOverride = new StillInput();
            SceneManager.LoadScene("World");
            yield return WaitUntil(() => PlayerController.Local != null && Combatant.LocalPlayer != null, 60f, "the local player in World");
            float doors = Time.realtimeSinceStartup + 60f;
            while (!SpawnProtection.InputBlocked && Time.realtimeSinceStartup < doors) yield return null;
            yield return WaitUntil(() => !SpawnProtection.InputBlocked, 60f, "the elevator doors to open");
            yield return new WaitForSeconds(1f);
            var pc = PlayerController.Local;
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            if (me.TryGetComponent<SpawnProtection>(out var myGuard)) myGuard.ServerEnd();
            // Armed, or the bot leaves it alone beyond 12 m (ADR-314).
            me.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 30 });
            yield return WaitUntil(() => me.ActiveWeaponId == Rifle, 5f, "the player's rifle");

            // One bot does the fighting; every other bot leaves the match (switched off, moved under the map, killed) as in BotTests.
            Combatant fighter = null;
            int parked = 0;
            var all = Combatant.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var c = all[i];
                if (c == null || c.IsPlayer || c.Dead.Value) continue;
                if (fighter == null && c.GetComponent(BotType) != null && c.GetComponentInChildren<Animator>(true) != null) { fighter = c; continue; }
                if (c.GetComponent(BotType) is Behaviour b) b.enabled = false;
                if (c.TryGetComponent<NavMeshAgent>(out var a)) a.enabled = false;
                c.transform.position = new Vector3(3f * parked++, -200f, 0f);
                c.ServerTakeDamage(100000f);
            }
            Assert.IsNotNull(fighter, "a living bot in World");
            var ctrl = (Behaviour)fighter.GetComponent(BotType);
            var brainField = BotType.GetField("_brain", Priv);
            var hideField = BotType.GetField("_hidePoint", Priv);
            var coverField = BotType.GetField("_coverOk", Priv);
            // The opening peace (ADR-315, also held until the farm starts) would keep it from fighting for minutes; this bot's rules skip both (put back in TearDown).
            _peaceRules = (BotRules)BotType.GetField("_rules", Priv).GetValue(ctrl);
            _peaceSeconds = _peaceRules.EarlyPeaceSeconds; _peaceSpread = _peaceRules.EarlyPeaceSpreadSeconds; _farmSeconds = _peaceRules.FarmSeconds;
            _peaceRules.EarlyPeaceSeconds = 0f; _peaceRules.EarlyPeaceSpreadSeconds = 0f; _peaceRules.FarmSeconds = 0f;

            Assert.IsTrue(FindStage(me.transform.position, out var playerAt, out var botAt, out var hideAt), "a staging spot with cover in World");
            Debug.Log($"[BotCoverCapture] stage player {playerAt} bot {botAt} hide {hideAt}, {parked} bots parked");

            ctrl.enabled = false;
            pc.Teleport(playerAt + Vector3.up * 0.1f);
            pc.LookAt(botAt + Vector3.up * 1.2f);
            if (fighter.TryGetComponent<NavMeshAgent>(out var agent) && agent.enabled) agent.Warp(botAt);
            fighter.transform.SetPositionAndRotation(botAt, Quaternion.LookRotation(Flat(playerAt - botAt)));
            Physics.SyncTransforms();
            fighter.ServerDamageFilter = _ => 0f;
            if (fighter.TryGetComponent<SpawnProtection>(out var guard)) guard.ServerEnd();
            // A short first magazine: the first exposure ends empty, so the bot reloads behind the cover; the reload fills a full one.
            Assert.IsTrue(fighter.ServerGive(new LootEntry { Kind = LootKind.Weapon, WeaponId = Rifle, Amount = 6 }), "rifle for the bot");
            var weapon = GameData.Weapons.Find(Rifle);
            fighter.ServerGive(new LootEntry { Kind = LootKind.Ammo, Ammo = weapon.Ammo, Amount = 300 });
            yield return WaitUntil(() => fighter.ActiveWeaponId == Rifle, 5f, "the rifle in the bot's hands");
            ctrl.enabled = true;
            yield return null;
            if (fighter.TryGetComponent<NavMeshAgent>(out var agent2) && agent2.enabled) agent2.Warp(botAt);

            var cam = new GameObject("BotCoverCam").AddComponent<Camera>();
            cam.enabled = false;
            cam.CopyFrom(pc.Camera);

            bool cover = false, peek = false, reload = false;
            float atHideSince = -1f, reloadSince = -1f;
            int lastMag = fighter.ActiveMag;
            float end = Time.time + 200f, nextLog = 0f;
            while (Time.time < end && !(cover && peek && reload))
            {
                yield return null;
                var brain = (BotBrain)brainField.GetValue(ctrl);
                if (brain == null) continue;
                var pos = fighter.transform.position;
                var hide = (Vector3)hideField.GetValue(ctrl);
                bool atHide = (bool)coverField.GetValue(ctrl) && Vector3.Distance(Flat(pos), Flat(hide)) <= 1.5f;
                var playerEye = me.transform.position + Vector3.up * 1.6f;
                bool blocked = Physics.Linecast(pos + Vector3.up * 0.95f, playerEye, out var block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && block.collider.GetComponentInParent<Combatant>() == null;
                bool firing = fighter.ActiveMag < lastMag;
                // Strict: the wall has to be right at the bot (or at its hiding place for the peek), not somewhere down the line of fire.
                bool hugging = blocked && block.distance <= 1.6f;
                bool hideHugs = Physics.Linecast(hide + Vector3.up * 0.95f, playerEye, out var hb, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hb.distance <= 1.6f && hb.collider.GetComponentInParent<Combatant>() == null;
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 3f;
                    Debug.Log($"[BotCoverCapture] t phase {brain.Phase} cycles {brain.PeekCycles} coverOk {coverField.GetValue(ctrl)} hide {hide} bot {pos} dist {Vector3.Distance(pos, me.transform.position):F1} mag {fighter.ActiveMag} reloading {fighter.Reloading.Value} blocked {blocked} {(blocked ? block.distance : -1f):F1} m, hide hugs {hideHugs} {(hideHugs ? hb.collider.name : "")}");
                }
                lastMag = fighter.ActiveMag;

                if (brain.Phase == PeekPhase.Hide && atHide) { if (atHideSince < 0f) atHideSince = Time.time; } else atHideSince = -1f;
                if (fighter.Reloading.Value) { if (reloadSince < 0f) reloadSince = Time.time; } else reloadSince = -1f;

                if (!reload && brain.Phase == PeekPhase.Hide && atHide && hugging && reloadSince > 0f && Time.time - reloadSince > 0.5f)
                {
                    reload = true;
                    Debug.Log($"[BotCoverCapture] reload at {pos}, cover {block.collider.name} {block.distance:F2} m in front, mag {fighter.ActiveMag}");
                    Shoot(cam, folder, "3_reload_behind_cover", pos, me.transform.position);
                }
                else if (!cover && brain.Phase == PeekPhase.Hide && atHide && hugging && !fighter.Reloading.Value && Time.time - atHideSince > 0.5f)
                {
                    cover = true;
                    Debug.Log($"[BotCoverCapture] cover at {pos}, cover {block.collider.name} {block.distance:F2} m in front, peek cycles {brain.PeekCycles}");
                    Shoot(cam, folder, "1_behind_cover", pos, me.transform.position);
                }
                else if (!peek && brain.Phase == PeekPhase.Expose && brain.PeekCycles > 0 && firing && !blocked && hideHugs && Vector3.Distance(Flat(pos), Flat(hide)) <= 4.5f)
                {
                    peek = true;
                    Debug.Log($"[BotCoverCapture] peek fire at {pos}, {Vector3.Distance(Flat(pos), Flat(hide)):F1} m from the hiding place {hide}, cycle {brain.PeekCycles}");
                    Shoot(cam, folder, "2_peek_firing", pos, me.transform.position);
                }
            }
            var last = (BotBrain)brainField.GetValue(ctrl);
            Debug.Log($"[BotCoverCapture] done cover {cover} peek {peek} reload {reload}, phase {last?.Phase}, cycles {last?.PeekCycles}, bot {fighter.transform.position}, player hp {me.Hp.Value}");
            Assert.IsTrue(cover && peek && reload, $"shots taken: cover {cover} peek {peek} reload {reload}");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
