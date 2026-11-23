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
        [TearDown] public void Down() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); if (File.Exists(_dir)) File.Delete(_dir); }
        string[] Rejected() => Directory.Exists(Path.Combine(_dir, "rejected")) ? Directory.GetFiles(Path.Combine(_dir, "rejected"), "*.json") : new string[0];

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
            Assert.AreEqual(SendOutcome.Retry, ResultsRules.Outcome(401), "a rotated key gets fixed, the results wait (#495)");
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

        // #495: the backend refuses the whole match when one account is unknown; the others still get their results, the refused row is kept.
        [Test]
        public void Rejected400_SendsTheOthersAgain_AndShelvesOnlyTheBadRow()
        {
            var ok = new List<string>();
            var box = new ResultsOutbox(_dir, json =>
            {
                if (json.Contains("\"account_id\":13,")) return SendOutcome.Drop;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"account_id\":(\\d+)")) ok.Add(m.Groups[1].Value);
                return SendOutcome.Ok;
            });
            var rows = new List<PlayerResult> { new PlayerResult(7, 1, 3, 400), new PlayerResult(13, 2, 0, 300), new PlayerResult(21, 3, 1, 200), new PlayerResult(5, 4, 0, 100) };
            Assert.IsTrue(box.Enqueue("m-3", ResultsRules.Json("m-3", "battle-royale", Ended, rows)));
            for (int i = 0; i < 10; i++) box.Flush(Ended / 1000.0 + 1 + i);
            CollectionAssert.AreEquivalent(new[] { "7", "21", "5" }, ok, "everyone else sent exactly once");
            Assert.AreEqual(0, box.Pending);
            var shelved = Rejected();
            Assert.AreEqual(1, shelved.Length);
            StringAssert.Contains("\"account_id\":13,", File.ReadAllText(shelved[0]));
            StringAssert.Contains("\"match_id\":\"m-3\"", File.ReadAllText(shelved[0]));
        }

        [Test]
        public void OldFiles_AreShelvedNotDeleted()
        {
            int calls = 0;
            var old = new ResultsOutbox(_dir, _ => { calls++; return SendOutcome.Retry; });
            old.Enqueue("m-4", J("m-4")); old.Flush(Ended / 1000.0 + 25 * 3600);
            Assert.AreEqual(0, calls); Assert.AreEqual(0, old.Pending); Assert.AreEqual(1, Rejected().Length);
        }

        // #495: a full disk must not throw into the match's end (which would skip the end-of-match broadcast).
        [Test]
        public void Enqueue_ThatCannotWrite_ReturnsFalse_InsteadOfThrowing()
        {
            var box = new ResultsOutbox(_dir, _ => SendOutcome.Ok);
            Directory.Delete(_dir, true); File.WriteAllText(_dir, "not a folder");
            Assert.IsFalse(box.Enqueue("m-5", J("m-5")));
        }
    }
}
