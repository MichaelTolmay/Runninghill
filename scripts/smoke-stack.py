#!/usr/bin/env python3
"""Exercise a running local stack without printing credentials. No external packages."""
from pathlib import Path
import argparse
import json
import os
import subprocess
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--service', default='http://127.0.0.1:5080')
parser.add_argument('--web', default='http://127.0.0.1:5082')
parser.add_argument('--cli', help='Optional path to the published native CLI')
parser.add_argument('--skip-web', action='store_true')
parser.add_argument('--expect-unavailable', action='store_true')
args = parser.parse_args()
token = subprocess.check_output([os.sys.executable, str(root / 'scripts/dev-token.py')], text=True).strip()

def get(url, expected, authenticated=False):
    request = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + token} if authenticated else {})
    try:
        with urllib.request.urlopen(request, timeout=12) as response:
            code, body = response.status, response.read()
    except urllib.error.HTTPError as error:
        code, body = error.code, error.read()
    assert code == expected, f'{url}: expected {expected}, received {code}'
    print(f'PASS {code} {url}')
    return body

get(args.service + '/health/live', 200)
get(args.service + '/health/ready', 503 if args.expect_unavailable else 200)
get(args.service + '/api/status', 401)
body = get(args.service + '/api/status', 503 if args.expect_unavailable else 200, True)
if not args.expect_unavailable:
    assert 'schema verified' in json.loads(body)['message']
    if not args.skip_web:
        get(args.web + '/', 200)
        get(args.web + '/_framework/blazor.webassembly.js', 200)
        assert json.loads(get(args.web + '/api/status', 200, True)) == json.loads(body)
    if args.cli:
        environment = dict(os.environ, RUNNINGHILL_SERVICE_URL=args.service + '/', RUNNINGHILL_ACCESS_TOKEN=token)
        output = subprocess.check_output([args.cli, 'status'], env=environment, text=True)
        assert 'schema verified' in output
        print('PASS native CLI -> service -> PostgreSQL')
