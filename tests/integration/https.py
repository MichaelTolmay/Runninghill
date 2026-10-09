#!/usr/bin/env python3
"""Verify localhost TLS, redirects and native gRPC through nginx without changing collection data."""
import argparse
from pathlib import Path
import ssl
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--certificate', type=Path, default=ROOT / '.run/tls/localhost.crt')
args = parser.parse_args()
context = ssl.create_default_context(cafile=str(args.certificate))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Expose the redirect itself so the test checks its destination and preserved query."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        """Prevent this test request from following HTTP into HTTPS automatically."""
        return None


request = urllib.request.Request('http://localhost:5082/logs?test=redirect', headers={'Host': 'untrusted.invalid'})
try:
    urllib.request.build_opener(NoRedirect()).open(request, timeout=10)
    raise AssertionError('HTTP did not redirect')
except urllib.error.HTTPError as error:
    assert error.code == 308
    assert error.headers['Location'] == 'https://localhost:5443/logs?test=redirect'
print('PASS fixed HTTPS redirect preserves route/query and ignores untrusted Host')

for host in ('localhost', '127.0.0.1'):
    with urllib.request.urlopen(f'https://{host}:5443/', context=context, timeout=10) as response:
        assert response.status == 200
        assert response.headers['X-Content-Type-Options'] == 'nosniff'
        assert b'_framework/blazor.webassembly' in response.read()
print('PASS trusted certificate and hostname validation for localhost and IPv4 loopback')

# A token stays in a private temporary file, never in curl's process arguments or output.
token = subprocess.check_output([sys.executable, str(ROOT / 'scripts/dev-token.py')], text=True).strip()
with tempfile.TemporaryDirectory(prefix='runninghill-tls-test-') as temporary:
    directory = Path(temporary)
    config = directory / 'curl.conf'
    config.write_text(f'header = "Authorization: Bearer {token}"\n')
    config.chmod(0o600)
    headers, body = directory / 'headers', directory / 'body'
    result = subprocess.run([
        'curl', '--silent', '--show-error', '--fail', '--http2', '--max-time', '15',
        '--cacert', str(args.certificate), '--config', str(config),
        '--header', 'Content-Type: application/grpc', '--header', 'TE: trailers',
        '--data-binary', '@-', '--dump-header', str(headers), '--output', str(body),
        '--write-out', '%{http_version}',
        'https://localhost:5443/runninghill.v1.Application/GetStatus'
    ], input=b'\0\0\0\0\0', capture_output=True, check=True)
    assert result.stdout == b'2', 'gRPC must negotiate HTTP/2'
    assert 'grpc-status: 0' in headers.read_text().lower(), 'gRPC response was not successful'
    assert b'schema verified' in body.read_bytes(), 'gRPC did not reach the application/database'
print('PASS authenticated native gRPC over TLS/HTTP2 through nginx to PostgreSQL')
