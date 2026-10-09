#!/usr/bin/env python3
"""Create a self-signed localhost certificate for the nginx development proxy."""
import argparse
from pathlib import Path
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    """Generate a matching certificate/key pair without replacing an existing pair by accident."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--renew', action='store_true', help='Replace the existing certificate; recreate nginx afterward')
    parser.add_argument('--output', type=Path, default=ROOT / '.run' / 'tls', help='Certificate directory (default: .run/tls)')
    args = parser.parse_args()
    if not shutil.which('openssl'):
        parser.exit(1, 'OpenSSL is required. Install it and make sure openssl is on your PATH.\n')
    directory = args.output.resolve()
    certificate, key = directory / 'localhost.crt', directory / 'localhost.key'
    if certificate.exists() or key.exists():
        if not args.renew:
            if certificate.is_file() and key.is_file():
                print('Existing certificate and key left unchanged. Use --renew to replace them.')
                return
            parser.exit(1, 'The certificate pair is incomplete. Run again with --renew to repair it.\n')
    try:
        directory.mkdir(parents=True, exist_ok=True, mode=0o700)
        # Only this host user can traverse the directory. Each individual file is mounted
        # read-only into nginx, whose unprivileged user must be able to read the key.
        # Do not make this directory public or mount the entire directory into nginx.
        directory.chmod(0o700)
        with tempfile.TemporaryDirectory(dir=directory) as temporary:
            staged_key = Path(temporary) / key.name
            staged_certificate = Path(temporary) / certificate.name
            subprocess.run([
                'openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-sha256', '-nodes',
                '-days', '365', '-subj', '/CN=localhost',
                '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1',
                '-addext', 'basicConstraints=critical,CA:FALSE',
                '-addext', 'keyUsage=critical,digitalSignature,keyEncipherment',
                '-addext', 'extendedKeyUsage=serverAuth',
                '-keyout', str(staged_key), '-out', str(staged_certificate)
            ], check=True, capture_output=True)
            staged_key.chmod(0o644)
            staged_certificate.chmod(0o644)
            os.replace(staged_key, key)
            os.replace(staged_certificate, certificate)
    except (OSError, subprocess.CalledProcessError):
        parser.exit(1, 'Could not create the certificate. Check OpenSSL, directory permissions and free space, then retry with --renew.\n')
    print(f'Created {certificate} and its private key (valid for 365 days).')
    print('Covers localhost, 127.0.0.1 and ::1. No system trust settings were changed.')
    print('Keep the private directory restricted; never share or commit localhost.key.')


if __name__ == '__main__':
    main()
