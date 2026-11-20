using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    // ADR-131: six emotes made in code: names, lengths, the pose starts and ends at rest, and the request limit.
    public class EmoteRulesTests
    {
        static readonly Emote[] All = { Emote.Wave, Emote.Cheer, Emote.Point, Emote.Bow, Emote.Dance, Emote.Clap };

        static float Size(EmotePose p) => System.Math.Abs(p.RightOut) + System.Math.Abs(p.LeftOut) + System.Math.Abs(p.RightFwd) + System.Math.Abs(p.LeftFwd)
            + System.Math.Abs(p.TorsoPitch) + System.Math.Abs(p.TorsoRoll) + System.Math.Abs(p.TorsoYaw) + System.Math.Abs(p.Bend);

        [Test]
        public void SixEmotes_HaveNamesAndLengths()
        {
            Assert.AreEqual(6, Emotes.Count);
            Assert.AreEqual(6, All.Select(Emotes.Name).Distinct().Count());
            foreach (var e in All)
            {
                Assert.IsTrue(Emotes.IsValid((int)e));
                Assert.IsNotEmpty(Emotes.Name(e));
                Assert.Greater(Emotes.Duration(e), 1f, e.ToString());
                Assert.Less(Emotes.Duration(e), 6f, e.ToString());
            }
            Assert.IsFalse(Emotes.IsValid(0));
            Assert.IsFalse(Emotes.IsValid(7));
            Assert.AreEqual(0f, Emotes.Duration(Emote.None));
        }

        [Test]
        public void Pose_StartsAndEndsAtRest_AndMovesInTheMiddle()
        {
            foreach (var e in All)
            {
                float d = Emotes.Duration(e);
                Assert.AreEqual(0f, Size(Emotes.PoseAt(e, 0f)), 1e-4f, e + " starts at rest");
                Assert.AreEqual(0f, Size(Emotes.PoseAt(e, d)), 1e-4f, e + " ends at rest");
                Assert.AreEqual(0f, Size(Emotes.PoseAt(e, d + 1f)), 1e-4f, e + " after its end");
                Assert.Greater(Size(Emotes.PoseAt(e, d * 0.5f)), 10f, e + " does something in the middle");
            }
        }

        [Test]
        public void Pose_KeepsTheAnglesHumanlyPossible()
        {
            foreach (var e in All)
                for (float t = 0f; t <= Emotes.Duration(e); t += 0.05f)
                {
                    var p = Emotes.PoseAt(e, t);
                    Assert.LessOrEqual(System.Math.Abs(p.RightOut), 180f);
                    Assert.LessOrEqual(System.Math.Abs(p.LeftOut), 180f);
                    Assert.LessOrEqual(System.Math.Abs(p.TorsoPitch), 45f);
                    Assert.LessOrEqual(p.Bend, 20f);
                }
        }

        [Test]
        public void Dance_NeverHoldsBothArmsOutToTheSide()
        {
            // #173: both arms raised sideways at once read as a T-pose.
            for (float t = 0f; t <= Emotes.Duration(Emote.Dance); t += 0.02f)
            {
                var p = Emotes.PoseAt(Emote.Dance, t);
                Assert.IsFalse(p.RightOut > 60f && p.LeftOut > 60f, "T-pose at " + t);
            }
            var mid = Emotes.PoseAt(Emote.Dance, Emotes.Duration(Emote.Dance) * 0.5f);
            Assert.Greater(System.Math.Abs(mid.RightFwd) + System.Math.Abs(mid.LeftFwd), 60f, "the arms pump in front");
        }

        [Test]
        public void FirstPersonView_PullsOutWhileEmoting()
        {
            // #173: the player sees their own emote; sights still go to the eyes.
            var rig = new Game.Gameplay.PlayerCameraRig();
            rig.ToggleMode();
            Assert.IsTrue(rig.IsEyeView(AimMode.Hip));
            rig.ShowSelf = true;
            Assert.IsFalse(rig.IsEyeView(AimMode.Hip));
            Assert.IsTrue(rig.IsEyeView(AimMode.Sights));
        }

        [Test]
        public void Envelope_FadesInAndOut()
        {
            Assert.AreEqual(0f, Emotes.Envelope(0f, 2f));
            Assert.AreEqual(1f, Emotes.Envelope(1f, 2f), 1e-5f);
            Assert.AreEqual(0f, Emotes.Envelope(2f, 2f));
            Assert.Greater(Emotes.Envelope(0.15f, 2f), 0f);
            Assert.Less(Emotes.Envelope(0.15f, 2f), 1f);
        }

        [Test]
        public void Gate_LimitsRequestsPerOwner()
        {
            var gate = new EmoteGate();
            Assert.IsTrue(gate.TryAccept(1, 5f, 0.4f));
            Assert.IsFalse(gate.TryAccept(1, 5.2f, 0.4f));
            Assert.IsTrue(gate.TryAccept(2, 5.2f, 0.4f));
            Assert.IsTrue(gate.TryAccept(1, 5.5f, 0.4f));
        }
    }
}
