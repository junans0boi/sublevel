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



class NavmeshGate(unittest.TestCase):
    def test_size_is_added_in_mb(self):
        import os, tempfile
        from navmesh_gate import with_navmesh_mb
        with tempfile.NamedTemporaryFile(delete=False) as f:
            f.write(b"x" * 2 * 1024 * 1024)
        try:
            self.assertEqual(with_navmesh_mb({"bot_ms": 1}, f.name), {"bot_ms": 1, "navmesh_mb": 2.0})
        finally:
            os.unlink(f.name)


if __name__ == "__main__":
    unittest.main()
