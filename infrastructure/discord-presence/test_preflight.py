import importlib.util
from pathlib import Path
import unittest
import urllib.error

spec = importlib.util.spec_from_file_location("run_bot", Path(__file__).with_name("run-bot.py"))
bot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bot)


class PreflightTests(unittest.TestCase):
    def fetch(self, path, token):
        return ({"id": bot.APPLICATION_ID, "bot": True} if path == "users/@me"
                else [{"id": bot.GUILD_ID}])

    def test_valid_identity_and_membership(self):
        self.assertEqual(bot.validate("test", self.fetch)[0], 0)

    def test_missing_token_does_not_contact_discord(self):
        self.assertEqual(bot.validate("", lambda *_: self.fail("unexpected request"))[0], 78)

    def test_wrong_identity(self):
        self.assertEqual(bot.validate("test", lambda *_: {"id": "other", "bot": True})[0], 78)

    def test_removed_from_community(self):
        def fetch(path, token):
            return self.fetch(path, token) if path == "users/@me" else []
        self.assertEqual(bot.validate("test", fetch)[0], 78)

    def test_invalid_credential_stops_but_transient_errors_retry(self):
        for status, expected in ((401, 78), (403, 78), (429, 1), (500, 1)):
            def fetch(*_):
                raise urllib.error.HTTPError("https://discord.com", status, "secret-body", {}, None)
            code, message = bot.validate("secret-token", fetch)
            self.assertEqual(code, expected)
            self.assertNotIn("secret", message)

    def test_network_failure_is_retryable(self):
        def fetch(*_):
            raise urllib.error.URLError("secret-in-error")
        code, message = bot.validate("test", fetch)
        self.assertEqual(code, 1)
        self.assertNotIn("secret", message)


if __name__ == "__main__":
    unittest.main()
