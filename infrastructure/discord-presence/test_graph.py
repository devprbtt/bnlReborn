import io
import json
import unittest
import urllib.error
from PIL import Image
import player_graph as g


class FakeDiscord:
    def __init__(self, existing=None):
        self.calls = []
        self.message = existing
        self.fail = None

    def request(self, method, path, payload=None, png=None):
        self.calls.append((method, path, payload))
        if self.fail:
            raise urllib.error.HTTPError(path, self.fail, "failure", {}, None)
        if method == "GET":
            return [self.message] if self.message else []
        self.message = {"id": "123", "author": {"id": g.BOT}, "embeds": payload["embeds"]}
        return self.message


class GraphTests(unittest.TestCase):
    def test_sampling_prunes_and_replaces_but_preserves_zero(self):
        db = g.database(":memory:")
        for stamp, value in [(-4000000, 5), (120000, 0), (120001, -3), (120002, 0), (120060, True)]:
            g.record(db, stamp, value)
        self.assertEqual(g.window(db, 120060), [(120000, 0), (120060, None)])
        self.assertEqual(db.execute("SELECT count(*) FROM samples").fetchone()[0], 2)

    def test_history_last_sample_in_minute_wins_without_overwriting_live(self):
        db = g.database(":memory:")
        rows = [json.dumps({"__REALTIME_TIMESTAMP": str(t * 1000000), "MESSAGE": "poll player_count=" + n})
                for t, n in [(61, "4"), (65, "0"), (62, "8"), (121, "None")]]
        self.assertEqual(g.import_journal(db, rows), 2)
        self.assertEqual(g.window(db, 120), [(60, 0), (120, None)])
        g.record(db, 120, 9)
        g.import_journal(db, rows)
        self.assertEqual(g.window(db, 120)[-1], (120, 9))

    def test_missing_samples_break_lines(self):
        self.assertEqual(g.segments([(60, 0), (120, 1), (180, None), (240, 3), (420, 4)]),
                         [[(60, 0), (120, 1)], [(240, 3)], [(420, 4)]])

    def test_periods_keep_month_and_exclude_outside_window(self):
        db = g.database(":memory:")
        now = 4000000 // 60 * 60
        for days in [0, 2, 8, 29, 32]:
            g.record(db, now-days*86400, days)
        self.assertEqual(len(g.window(db, now, 1)), 1)
        self.assertEqual(len(g.window(db, now, 7)), 2)
        self.assertEqual(len(g.window(db, now, 30)), 4)
        with self.assertRaises(ValueError):
            g.window(db, now, 365)
        for period in [7, 30]:
            self.assertTrue(g.render(g.window(db, now, period), now, period).startswith(b'\x89PNG'))
        self.assertEqual(len(g.graph_controls()[0]['components']), 4)

    def test_render_empty_and_real_zero(self):
        for rows in [[], [(60, 0)], [(60, None)], [(60, 5000), (120, 3)]]:
            with Image.open(io.BytesIO(g.render([(t + 86400, n) for t, n in rows], 86520))) as image:
                self.assertEqual(image.size, (1200, 560))
                self.assertEqual(image.format, "PNG")

    def test_one_message_is_created_then_edited(self):
        db, api = g.database(":memory:"), FakeDiscord()
        g.publish(db, api, b"png", 120)
        g.publish(db, api, b"png", 180)
        self.assertEqual([c[0] for c in api.calls], ["GET", "POST", "PATCH"])
        self.assertTrue(api.calls[1][2]["enforce_nonce"])
        self.assertLessEqual(len(api.calls[1][2]["nonce"]), 25)
        self.assertEqual(api.calls[2][2]["attachments"], [{"id": 0, "filename": "players.png"}])

    def test_recovers_existing_message_after_lost_local_state(self):
        api = FakeDiscord({"id": "123", "author": {"id": g.BOT}, "embeds": [{"footer": {"text": g.MARKER}}]})
        g.publish(g.database(":memory:"), api, b"png", 120)
        self.assertEqual([c[0] for c in api.calls], ["GET", "PATCH"])

    def test_deleted_message_clears_id_and_permission_failure_does_not(self):
        for code in [404, 403]:
            db, api = g.database(":memory:"), FakeDiscord()
            g.publish(db, api, b"png", 120)
            api.fail = code
            with self.assertRaises(urllib.error.HTTPError):
                g.publish(db, api, b"png", 180)
            self.assertEqual(db.execute("SELECT count(*) FROM state").fetchone()[0], 0 if code == 404 else 1)


if __name__ == "__main__":
    unittest.main()
