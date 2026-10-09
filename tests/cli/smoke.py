#!/usr/bin/env python3
"""Check a published CLI against a tiny local fake service. No external packages needed."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import argparse
import json
import os
import subprocess
import threading
import time

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--cli-directory', type=Path, default=root / 'artifacts/cli/linux-x64')
args = parser.parse_args()
binary = args.cli_directory / ('Runninghill.Cli.exe' if os.name == 'nt' else 'Runninghill.Cli')
reply = {'status': 200, 'body': b'{"message":"Ready"}'}


class FakeService(BaseHTTPRequestHandler):
    def do_GET(self):
        time.sleep(reply.get('delay', 0))
        self.send_response(reply['status'])
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(reply['body'])))
        self.send_header('X-Request-ID', 'smoke-reference')
        self.end_headers()
        try:
            self.wfile.write(reply['body'])
        except (ConnectionResetError, BrokenPipeError):
            # Rejecting an oversized reply may close the connection before we finish sending it.
            pass

    def log_message(self, *args):
        pass  # Test output should contain results, not a request log for every case.


# Port zero asks the OS for an unused port, avoiding clashes with a running development stack.
server = ThreadingHTTPServer(('127.0.0.1', 0), FakeService)
threading.Thread(target=server.serve_forever, daemon=True).start()
environment = dict(os.environ,
                   RUNNINGHILL_SERVICE_URL=f'http://127.0.0.1:{server.server_port}/',
                   RUNNINGHILL_ACCESS_TOKEN='test-only-token')
cases = [
    (200, b'{"message":"Ready"}', 0, 'Ready'),
    (401, b'private-server-detail', 1, 'new access token'),
    (403, b'private-server-detail', 1, 'administrator'),
    (429, b'private-server-detail', 1, 'wait a few seconds'),
    (503, b'private-server-detail', 1, 'try again shortly'),
    (500, b'private-server-detail', 1, 'try again'),
    (200, b'not-json', 1, 'cannot read'),
    (200, b'null', 1, 'cannot read'),
    (200, b'{}', 1, 'cannot read'),
    (200, b'{"message":""}', 1, 'cannot read'),
    (200, b'x' * 300000, 1, 'larger'),
]
try:
    for status, body, exit_code, expected in cases:
        reply.update(status=status, body=body)
        result = subprocess.run([str(binary.resolve())], env=environment, capture_output=True,
                                text=True, timeout=15)
        output = result.stdout + result.stderr
        assert result.returncode == exit_code and expected in output, (status, expected, output)
        events = [json.loads(line) for line in result.stderr.splitlines() if line.startswith('{')]
        assert any(event.get('State', {}).get('Outcome') == 'Started' for event in events), result.stderr
        assert any(event.get('State', {}).get('Outcome') in ('Completed', 'Failed') for event in events), result.stderr
        if body == b'{"message":"Ready"}':
            assert result.stdout.strip() == 'Ready', 'Diagnostic records must not pollute command output.'
        assert 'private-server-detail' not in output and 'test-only-token' not in output
        if status >= 400:
            assert 'smoke-reference' in output
    # A timeout is a service failure (exit 1), not a user's Ctrl+C cancellation (exit 130).
    reply.update(status=200, body=b'{"message":"Ready"}', delay=11)
    result = subprocess.run([str(binary.resolve())], env=environment, capture_output=True,
                            text=True, timeout=15)
    assert result.returncode == 1 and 'took too long' in result.stderr
    print('PASS native CLI: success, HTTP errors, invalid JSON, bounded reply size, timeout, safe request references')
finally:
    # Release the listening socket even when a test fails.
    server.shutdown()
    server.server_close()
