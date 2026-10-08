#!/usr/bin/env python3
"""Real HTTP + PostgreSQL contract checks. Uses a running debug or development Docker stack."""
import argparse
import concurrent.futures
import json
from pathlib import Path
import subprocess
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--debug', action='store_true')
parser.add_argument('--url')
args = parser.parse_args()
base = args.url or ('http://localhost:5180' if args.debug else 'http://localhost:5080')
token = subprocess.check_output(['python3', 'scripts/dev.py', 'token'] if args.debug else ['python3', 'scripts/dev-token.py'], cwd=ROOT, text=True).strip()
headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'}
created = []
# Only letters are valid words. Every run gets its own prefix so cleanup cannot touch user words.
prefix = 'contract' + ''.join(chr(97 + int(c, 16)) for c in uuid.uuid4().hex)


def request(method, path, payload=None, expected=200, auth=True):
    req = urllib.request.Request(base + '/api/' + path, method=method,
        headers=headers if auth else {'Content-Type': 'application/json'},
        data=None if payload is None else payload if isinstance(payload, bytes) else json.dumps(payload).encode())
    try:
        response = urllib.request.urlopen(req, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        body = response.read()
        acceptable = expected if isinstance(expected, tuple) else (expected,)
        assert response.status in acceptable, (method, path, expected, response.status, body.decode())
        assert response.headers.get('X-Request-ID'), 'Missing support reference'
        return json.loads(body) if body else None


try:
    request('GET', 'words', expected=401, auth=False)
    request('POST', 'words', b'{', 400)
    request('POST', 'words', {'word': 'a' * 18000, 'type': 'Noun'}, 413)
    request('POST', 'words', {'word': 'two words', 'type': 'Noun'}, 400)
    request('POST', 'words', {'word': 'hello', 'type': 'Unknown'}, 400)
    request('POST', 'sentences', {'wordIds': [], 'requestId': str(uuid.uuid4())}, 400)
    request('POST', 'sentences', {'wordIds': [1]}, 400)
    request('GET', 'words?after=-1', expected=400)
    request('GET', 'words?types=Unknown', expected=400)
    # Test the page boundary using real rows and more than one word type.
    for i in range(53):
        text = prefix + chr(97 + i // 26) + chr(97 + i % 26)
        created.append(request('POST', 'words', {'word': text, 'type': 'Noun' if i % 2 == 0 else 'Verb'}, 201))
    page = request('GET', 'words?search=' + prefix)
    assert len(page['items']) == 50 and page['nextAfter'] == created[49]['id']
    tail = request('GET', f"words?search={prefix}&after={page['nextAfter']}")
    assert [w['id'] for w in tail['items']] == [w['id'] for w in created[50:]]
    assert tail['nextAfter'] is None
    verbs = request('GET', f'words?search={prefix}&types=Verb')
    assert len(verbs['items']) == 26 and all(w['type'] == 'Verb' for w in verbs['items'])
    # Unicode normalization must work in the native binary too, not only under the test host.
    accent = request('POST', 'words', {'word': prefix + 'cafe\u0301', 'type': 'Noun'}, 201)
    created.append(accent)
    assert accent['word'] == prefix + 'café', accent
    request('POST', 'words', {'word': prefix + 'café', 'type': 'Noun'}, 409)
    assert not request('GET', 'words?search=' + urllib.parse.quote(prefix + '%'))['items']
    first, second = created[:2]
    assert set(request('GET', f"words/{first['id']}")) == {'id', 'word', 'type'}
    request('POST', 'words', {'word': first['word'].upper(), 'type': first['type']}, 409)
    request('PUT', f"words/{first['id']}", {'word': first['word'], 'type': 'Adjective'})
    assert request('GET', f"words/{first['id']}")['type'] == 'Adjective'
    # Concurrent retries must converge on one persisted sentence, preserving repeated words.
    payload = {'wordIds': [first['id'], second['id'], first['id']], 'requestId': str(uuid.uuid4())}
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        sentences = list(executor.map(lambda _: request('POST', 'sentences', payload), range(6)))
    assert len({s['id'] for s in sentences}) == 1
    sentence = sentences[0]
    assert sentence['text'] == f"{first['word']} {second['word']} {first['word']}"
    request('POST', 'sentences', dict(payload, wordIds=[second['id']]), 409)
    request('DELETE', f"words/{first['id']}", expected=204)
    request('GET', f"words/{first['id']}", expected=404)
    request('PUT', f"words/{first['id']}", {'word': 'absent', 'type': 'Noun'}, 404)
    request('POST', 'sentences', dict(payload, requestId=str(uuid.uuid4())), 409)
    retry = request('POST', 'sentences', payload)
    assert retry == sentence, 'Retry after word deletion lost the saved snapshot'
    history = request('GET', 'sentences')
    assert sentence in history['items']
    assert all(a['id'] > b['id'] for a, b in zip(history['items'], history['items'][1:])), 'History must show newest first'
    # A new HTTP connection still reads database state; no client-memory-only collection.
    assert request('GET', f"words/{second['id']}") == second
    print('PostgreSQL collection checks passed: auth, validation, CRUD, paging, filtering, concurrent retries, immutable sentence history.')
finally:
    # Delete only this test's own words. Sentence history deliberately has no delete endpoint.
    for word in created:
        request('DELETE', f"words/{word['id']}", expected=(204, 404))
