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
    // Review shots for the polish list of #143 (the grenade burst). Not part of the suite: run on purpose with
    //   tools/unity.sh test playmode Game.Tests.QualityCaptureTests   -> Logs/quality/*.png
    [Explicit, Category("Capture")]
    public class QualityCaptureTests
    {
        const string Folder = "Logs/quality";

        [UnityTest, Timeout(180000)]
        public IEnumerator GrenadeBurst()
        {
            Directory.CreateDirectory(Folder);
            SceneManager.LoadScene("TestBox");
            float end = Time.realtimeSinceStartup + 60f;
            while (PlayerController.Local == null || Combatant.LocalPlayer == null)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("no local player");
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            var me = Combatant.LocalPlayer;
            me.ServerDamageFilter = _ => 0f;
            foreach (var c in Combatant.All)
                if (!c.IsPlayer)
                    foreach (var part in c.GetComponents<MonoBehaviour>())
                        if (part.GetType().Name == "TrainingDummy") part.enabled = false;
            var user = me.GetComponent<ThrowableUser>();
            var fwd = PlayerController.Local.Camera.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            user.ServerAdd(ThrowableKind.Frag, 1);
            bool bang = false;
            ThrownItem.ClientExploded += (c, t) => bang = true;
            Assert.IsTrue(user.ServerThrow(me.transform.position + fwd * 7f + Vector3.up * 0.3f, Vector3.zero, ThrowableKind.Frag));
            end = Time.realtimeSinceStartup + 10f;
            while (!bang && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(bang, "the grenade went off");
            for (int n = 0; n < 4; n++)
            {
                yield return UiShot.Save(Path.Combine(Folder, $"grenade_burst_{n}.png"), 1280, 720);
                yield return new WaitForSeconds(0.04f);
            }
        }
    }
}
