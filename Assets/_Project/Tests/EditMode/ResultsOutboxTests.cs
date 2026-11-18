using System.Collections.Generic;
using System.IO;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class ResultsOutboxTests
    {
        string _dir;
        [SetUp] public void Up() { _dir = Path.Combine(Path.GetTempPath(), "outbox-" + System.Guid.NewGuid().ToString("N")); }
        [TearDown] public void Down() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        const long Ended = 1_800_000_000_000;
        static string J(string id) => ResultsRules.Json(id, "battle-royale", Ended, new List<PlayerResult> { new PlayerResult(7, 1, 3, 412.9f) });

        [Test]
        public void Json_HasBackendShape_AndClamps()
        {
            var j = ResultsRules.Json("m-1", "battle-royale", Ended, new List<PlayerResult> { new PlayerResult(7, 0, -2, 999999f) });
            Assert.AreEqual("{\"match_id\":\"m-1\",\"mode\":\"battle-royale\",\"ended_at\":1800000000000,\"players\":[{\"account_id\":7,\"rank\":1,\"kills\":0,\"survival_seconds\":86400}]}", j);
        }

        [Test]
        public void Outcome_ClassifiesStatus()
        {
            Assert.AreEqual(SendOutcome.Ok, ResultsRules.Outcome(200));
            Assert.AreEqual(SendOutcome.Drop, ResultsRules.Outcome(400));
            Assert.AreEqual(SendOutcome.Drop, ResultsRules.Outcome(401));
            Assert.AreEqual(SendOutcome.Retry, ResultsRules.Outcome(503));
            Assert.AreEqual(SendOutcome.Retry, ResultsRules.Outcome(0));
            Assert.AreEqual(1, ResultsRules.BackoffSeconds(1)); Assert.AreEqual(4, ResultsRules.BackoffSeconds(3)); Assert.AreEqual(60, ResultsRules.BackoffSeconds(20));
        }

        [Test]
        public void BackendDown_KeepsFile_ThenSendsAfterBackoff_AndDeletes()
        {
            int calls = 0; var outcome = SendOutcome.Retry;
            var box = new ResultsOutbox(_dir, _ => { calls++; return outcome; });
            box.Enqueue("m-1", J("m-1"));
            double t = Ended / 1000.0 + 10;
            box.Flush(t); Assert.AreEqual(1, calls); Assert.AreEqual(1, box.Pending);
            box.Flush(t + 0.5); Assert.AreEqual(1, calls, "still backing off");
            outcome = SendOutcome.Ok;
            box.Flush(t + 1.5); Assert.AreEqual(2, calls); Assert.AreEqual(0, box.Pending);
        }

        [Test]
        public void SurvivesRestart_NewOutboxSendsOldFile()
        {
            new ResultsOutbox(_dir, _ => SendOutcome.Retry).Enqueue("m-2", J("m-2"));
            int calls = 0;
            var again = new ResultsOutbox(_dir, _ => { calls++; return SendOutcome.Ok; });
            again.Flush(Ended / 1000.0 + 1);
            Assert.AreEqual(1, calls); Assert.AreEqual(0, again.Pending);
        }

        [Test]
        public void Rejected400_IsDropped_AndOldFilesExpire()
        {
            var box = new ResultsOutbox(_dir, _ => SendOutcome.Drop);
            box.Enqueue("m-3", J("m-3")); box.Flush(Ended / 1000.0 + 1);
            Assert.AreEqual(0, box.Pending);
            int calls = 0;
            var old = new ResultsOutbox(_dir, _ => { calls++; return SendOutcome.Retry; });
            old.Enqueue("m-4", J("m-4")); old.Flush(Ended / 1000.0 + 25 * 3600);
            Assert.AreEqual(0, calls); Assert.AreEqual(0, old.Pending);
        }
    }
}
