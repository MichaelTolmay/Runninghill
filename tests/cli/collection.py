#!/usr/bin/env python3
"""Test the published CLI's real word and sentence commands against a development stack."""
import argparse
import os
from pathlib import Path
import re
import subprocess
import uuid

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--debug', action='store_true')
parser.add_argument('--url')
parser.add_argument('--cli-directory', type=Path, default=root / 'artifacts/Release/cli/linux-x64')
args = parser.parse_args()
binary = args.cli_directory / ('Runninghill.Cli.exe' if os.name == 'nt' else 'Runninghill.Cli')
token = subprocess.check_output(['python3', 'scripts/dev.py', 'token'] if args.debug else ['python3', 'scripts/dev-token.py'], cwd=root, text=True).strip()
environment = dict(os.environ, RUNNINGHILL_SERVICE_URL=args.url or ('http://localhost:5180/' if args.debug else 'http://localhost:5080/'), RUNNINGHILL_ACCESS_TOKEN=token)
word = 'terminal' + ''.join(chr(97 + int(c, 16)) for c in uuid.uuid4().hex)


def run(*command, expected=0):
    result = subprocess.run([str(binary), *command], env=environment, capture_output=True, text=True, timeout=15)
    assert result.returncode == expected, (command, result.stdout, result.stderr)
    assert token not in result.stdout + result.stderr
    return result


identifier = None
try:
    assert 'words add' in run('--help').stdout
    added = run('words', 'add', word, 'Noun')
    identifier = re.search(r'#(\d+)', added.stdout).group(1)
    assert word in run('words', 'get', identifier).stdout
    assert '[Verb]' in run('words', 'update', identifier, word, 'Verb').stdout
    assert word in run('words', 'list', '--search', word, '--types', 'Verb').stdout
    run('words', 'delete', identifier, expected=2)
    assert word in run('words', 'get', identifier).stdout
    request_id = str(uuid.uuid4())
    first = run('sentences', 'add', identifier, identifier, '--request-id', request_id)
    second = run('sentences', 'add', identifier, identifier, '--request-id', request_id)
    assert first.stdout == second.stdout and request_id in first.stderr
    assert word in run('sentences', 'list').stdout
    assert 'one word' in run('words', 'add', 'two words', 'Noun', expected=1).stderr
    run('words', 'get', 'invalid', expected=2)
    print('Native CLI collection checks passed: help, CRUD, filters, delete confirmation, validation, sentence retries.')
finally:
    if identifier is not None:
        run('words', 'delete', identifier, '--yes')
