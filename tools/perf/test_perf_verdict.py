import unittest
from perf_verdict import judge, LIMITS


class Verdict(unittest.TestCase):
    def test_all_at_limit_passes(self):
        self.assertTrue(all(r[3] == "PASS" for r in judge(dict(LIMITS))))

    def test_over_limit_fails_and_missing_is_unmeasured(self):
        rows = {r[0]: r[3] for r in judge({"bot_ms": 4.1, "server_avg_ms": 3.0})}
        self.assertEqual(rows["bot_ms"], "FAIL")
        self.assertEqual(rows["server_avg_ms"], "PASS")
        self.assertEqual(rows["load_s"], "UNMEASURED")


if __name__ == "__main__":
    unittest.main()
