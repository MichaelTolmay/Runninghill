"""Check certificate identity, safe reuse and nginx-readable files inside a private directory."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipUnless(shutil.which('openssl'), 'OpenSSL is required for certificate tests')
class CertificateTests(unittest.TestCase):
    """Exercise the real certificate generator in a disposable directory."""

    def test_localhost_identity_permissions_and_safe_reuse(self):
        """The certificate verifies, covers loopback, and remains unchanged on repeat setup."""
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / 'tls'
            command = [sys.executable, str(ROOT / 'scripts/dev-certificate.py'), '--output', str(directory)]
            subprocess.run(command, check=True, capture_output=True)
            certificate, key = directory / 'localhost.crt', directory / 'localhost.key'
            before = (certificate.read_bytes(), key.read_bytes())
            verification = subprocess.run(['openssl', 'verify', '-CAfile', str(certificate), str(certificate)], capture_output=True, text=True)
            self.assertEqual(verification.returncode, 0, verification.stdout + verification.stderr)
            for flag, value in [('-checkhost', 'localhost'), ('-checkip', '127.0.0.1'), ('-checkip', '::1')]:
                subprocess.run(['openssl', 'x509', '-in', str(certificate), '-noout', flag, value], check=True, capture_output=True)
            if os.name != 'nt':
                self.assertEqual(directory.stat().st_mode & 0o777, 0o700)
                self.assertEqual(key.stat().st_mode & 0o777, 0o644)
            subprocess.run(command, check=True, capture_output=True)
            self.assertEqual(before, (certificate.read_bytes(), key.read_bytes()))
            subprocess.run(command + ['--renew'], check=True, capture_output=True)
            self.assertNotEqual(before[0], certificate.read_bytes())
            self.assertNotEqual(before[1], key.read_bytes())

    def test_incomplete_pair_is_not_silently_replaced(self):
        """A missing half of the pair prompts explicit repair instead of overwriting credentials."""
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            (directory / 'localhost.key').write_text('existing-key')
            result = subprocess.run([sys.executable, str(ROOT / 'scripts/dev-certificate.py'), '--output', str(directory)], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('--renew', result.stderr)
            self.assertNotIn('existing-key', result.stderr)
            self.assertEqual((directory / 'localhost.key').read_text(), 'existing-key')


if __name__ == '__main__':
    unittest.main()
