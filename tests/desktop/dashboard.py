#!/usr/bin/env python3
"""Exercise the real Photino window against isolated local endpoints, never the user's database.

Requires a graphical desktop (or xvfb-run on Linux). Test JavaScript is injected
into build OUTPUT only and removed in finally; application source is unchanged.
"""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import platform
from pathlib import Path
import subprocess
import ssl
import tempfile
import threading
from urllib.parse import urlparse, parse_qs

ROOT = Path(__file__).resolve().parents[2]


def main():
    """Start controlled endpoints, drive actual Blazor DOM events, and require a completion receipt."""
    parser = argparse.ArgumentParser(description=__doc__)
    host = {'Linux': 'linux', 'Windows': 'win', 'Darwin': 'osx'}[platform.system()]
    architecture = 'arm64' if platform.machine().lower() in ('aarch64', 'arm64') else 'x64'
    parser.add_argument('--app-directory', type=Path, default=ROOT / f'artifacts/Release/dashboard/{host}-{architecture}')
    parser.add_argument('--https', action='store_true', help='Check automatic trust using the existing .run/tls localhost certificate against isolated HTTPS fixtures')
    args = parser.parse_args()
    tls = None
    if args.https:
        tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        # Use the existing development pair without modifying it or changing system trust.
        # This server still serves fixtures only; it never contacts the user's database.
        tls.load_cert_chain(ROOT / '.run/tls/localhost.crt', ROOT / '.run/tls/localhost.key')
    directory = args.app_directory.resolve()
    html_path = directory / 'wwwroot/index.html'
    script_path = directory / 'wwwroot/dashboard-smoke.js'
    original = html_path.read_bytes()
    if (directory / 'Runninghill.Dashboard.staticwebassets.runtime.json').exists():
        raise SystemExit('Use published output: development builds serve source assets instead of the injected test files.')
    if script_path.exists():
        raise SystemExit('A dashboard smoke script already exists; finish the other test first.')
    completed = threading.Event()
    state = {'failure': False, 'load_requests': 0, 'result': None}

    class Handler(BaseHTTPRequestHandler):
        """Serve deterministic health, count and paginated log replies with normal token boundaries."""
        def log_message(self, *_):
            """Never log bearer headers or browser test payloads."""

        def reply(self, status, body, media='application/json'):
            """Allow only the local test page to report results without affecting application settings."""
            self.send_response(status)
            self.send_header('Content-Type', media)
            self.send_header('Access-Control-Allow-Origin', '*')
            self.send_header('Access-Control-Allow-Headers', 'Content-Type')
            self.end_headers()
            self.wfile.write(body.encode())

        def do_OPTIONS(self):
            """Accept the local WebView's test-result preflight."""
            self.reply(204, '')

        def do_POST(self):
            """Receive a test assertion result; production dashboard actions remain read-only."""
            if self.path != '/result':
                self.reply(405, '{}')
                return
            length = min(int(self.headers.get('Content-Length', '0')), 16384)
            state['result'] = json.loads(self.rfile.read(length))
            self.reply(200, '{}')
            completed.set()

        def do_GET(self):
            """Return test fixtures and count load requests without storing a collection."""
            parsed = urlparse(self.path)
            if parsed.path == '/control/fail':
                state['failure'] = True
                self.reply(200, '{}')
            elif parsed.path.startswith('/health/'):
                assert self.headers.get('Authorization') is None
                failed = state['failure'] and parsed.path.endswith('/ready')
                self.reply(503 if failed else 200, 'Unhealthy' if failed else 'Healthy', 'text/plain')
            elif self.headers.get('Authorization') != 'Bearer dashboard-test-token':
                self.reply(401, '{}')
            elif parsed.path == '/api/statistics':
                if state['failure']:
                    self.reply(503, '{}')
                else:
                    self.reply(200, json.dumps({'words': 37, 'sentences': 8, 'checkedAt': '2026-10-09T12:00:00Z'}))
            elif parsed.path == '/api/status':
                state['load_requests'] += 1
                self.reply(200, '{"message":"Test service ready"}')
            elif parsed.path == '/api/logs':
                older = parse_qs(parsed.query).get('before', ['0'])[0] != '0'
                self.reply(200, json.dumps({'items': [{'id': 1 if older else 2, 'timestamp': '2026-10-09T12:00:00Z',
                    'level': 'Information', 'category': 'Runninghill.Test', 'eventId': 0,
                    'message': 'Older service event' if older else 'Latest service event'}],
                    'nextBefore': None if older else 2, 'retainedCount': 2}))
            else:
                self.reply(404, '{}')

    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    base = f'http://127.0.0.1:{server.server_port}'
    secure_server = None
    api_base = base
    if tls:
        secure_server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        secure_server.socket = tls.wrap_socket(secure_server.socket, server_side=True)
        threading.Thread(target=secure_server.serve_forever, daemon=True).start()
        api_base = f'https://127.0.0.1:{secure_server.server_port}'
    script = (ROOT / 'tests/desktop/dashboard.js').read_text().replace('__TEST_BASE__', json.dumps(api_base)).replace('__TEST_CONTROL__', json.dumps(base))
    process = None
    temporary = tempfile.TemporaryDirectory(prefix='runninghill-dashboard-test-')
    output = tempfile.TemporaryFile()
    try:
        script_path.write_text(script)
        html_path.write_bytes(original.replace(b'</body>', b'<script src="dashboard-smoke.js"></script></body>'))
        environment = dict(os.environ)
        environment.pop('RUNNINGHILL_DASHBOARD_CA_CERT', None)
        # Keep the test's saved theme/language separate from the user's WebKit preferences.
        environment['XDG_DATA_HOME'] = temporary.name
        environment['XDG_CACHE_HOME'] = temporary.name
        process = subprocess.Popen(['dotnet', str(directory / 'Runninghill.Dashboard.dll')], cwd=directory,
                                   env=environment, stdout=output, stderr=subprocess.STDOUT)
        if not completed.wait(90):
            raise AssertionError(f'Dashboard did not report test completion (process exit: {process.poll()}).')
        result = state['result']
        assert result.get('passed'), result
        assert state['load_requests'] == 24, state
        output.seek(0)
        assert b'__bwv:' not in output.read(), 'Photino bridge tracing must remain disabled to protect credentials.'
        print('PASS native Photino dashboard: 5 languages, themes, polling, failures, current totals, load gauge, log paging and stop.')
        if tls:
            print('PASS HTTPS: automatic localhost certificate discovery without RUNNINGHILL_DASHBOARD_CA_CERT.')
    finally:
        if process and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        output.close()
        temporary.cleanup()
        html_path.write_bytes(original)
        script_path.unlink(missing_ok=True)
        server.shutdown()
        server.server_close()
        if secure_server:
            secure_server.shutdown()
            secure_server.server_close()


if __name__ == '__main__':
    main()
