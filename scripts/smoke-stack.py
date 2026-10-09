#!/usr/bin/env python3
"""Exercise a running local stack without printing credentials. No external packages."""
from pathlib import Path
import argparse
import json
import os
import re
import subprocess
import ssl
import urllib.error
import urllib.request

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--service', default='http://127.0.0.1:5080')
parser.add_argument('--web', default='http://127.0.0.1:5082')
parser.add_argument('--ca-cert', type=Path, help='Trust this certificate for HTTPS checks (for example .run/tls/localhost.crt)')
parser.add_argument('--cli', help='Optional path to the published native CLI')
parser.add_argument('--skip-web', action='store_true')
parser.add_argument('--expect-unavailable', action='store_true')
args = parser.parse_args()
try:
    tls_context = ssl.create_default_context(cafile=str(args.ca_cert)) if args.ca_cert else None
except (OSError, ssl.SSLError):
    parser.error('Could not load the trusted certificate. Check --ca-cert and regenerate the development certificate if necessary.')
token = subprocess.check_output([os.sys.executable, str(root / 'scripts/dev-token.py')], text=True).strip()

def get(url, expected, authenticated=False, content_type=None):
    request = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + token} if authenticated else {})
    try:
        with urllib.request.urlopen(request, timeout=12, context=tls_context) as response:
            code, body = response.status, response.read()
            media_type = response.headers.get_content_type()
    except urllib.error.HTTPError as error:
        code, body = error.code, error.read()
        media_type = error.headers.get_content_type()
    assert code == expected, f'{url}: expected {expected}, received {code}'
    if content_type:
        assert media_type == content_type, f'{url}: expected {content_type}, received {media_type}'
    print(f'PASS {code} {url}')
    return body

get(args.service + '/health/live', 200)
get(args.service + '/health/ready', 503 if args.expect_unavailable else 200)
get(args.service + '/api/status', 401)
body = get(args.service + '/api/status', 503 if args.expect_unavailable else 200, True)
if not args.expect_unavailable:
    assert 'schema verified' in json.loads(body)['message']
    if not args.skip_web:
        html = get(args.web + '/', 200).decode('utf-8')
        # Follow the versioned runtime script actually shipped by this deployment.
        script = re.search(r'src="(_framework/blazor\.webassembly(?:\.[A-Za-z0-9]+)?\.js)"', html)
        assert script, 'The page does not reference a Blazor startup script.'
        get(args.web + '/' + script.group(1), 200)
        # A missing stylesheet can return the SPA's index.html with HTTP 200. Check its MIME type too.
        styles = re.findall(r'<link[^>]*href="([A-Za-z0-9_.-]+\.css)"', html)
        assert styles, 'The page does not reference its stylesheets.'
        for style in styles:
            get(args.web + '/' + style, 200, content_type='text/css')
        assert json.loads(get(args.web + '/api/status', 200, True)) == json.loads(body)
    if args.cli:
        environment = dict(os.environ, RUNNINGHILL_SERVICE_URL=args.service + '/', RUNNINGHILL_ACCESS_TOKEN=token)
        output = subprocess.check_output([args.cli, 'status'], env=environment, text=True)
        assert 'schema verified' in output
        print('PASS native CLI -> service -> PostgreSQL')
