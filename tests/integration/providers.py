#!/usr/bin/env python3
"""Test EF migrations and collection behavior on disposable PostgreSQL, MySQL and SQL Server databases."""
import argparse
import base64
import hashlib
import hmac
import json
import tempfile
import urllib.request
import os
from pathlib import Path
import secrets
import socket
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--docker-context', default='default')
parser.add_argument('--service', type=Path, help='Published service executable or DLL to test instead of the Debug DLL')
parser.add_argument('--host-network', action='store_true', help='Linux fallback for hosts without bridge networking')
args = parser.parse_args()
if args.host_network and os.name != 'posix':
    parser.error('--host-network requires a Linux Docker host')
docker = ['docker', '--context', args.docker_context]
environment = dict(os.environ)
names = []

def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]

def start(provider, image, internal_port, variables, command, ready_text):
    name = 'runninghill-provider-test-' + provider.lower() + '-' + uuid.uuid4().hex[:8]
    host_port = port()
    container_port = host_port if args.host_network else internal_port
    variables = {k: v.replace('{port}', str(container_port)) for k, v in variables.items()}
    env = dict(environment, **variables)
    network = ['--network=host'] if args.host_network else ['-p', f'127.0.0.1:{host_port}:{container_port}']
    cmd = docker + ['run', '-d', '--name', name, *network]
    for key in variables: cmd += ['-e', key]  # Values stay out of process arguments and logs.
    cmd += [image, *[part.replace('{port}', str(container_port)) for part in command]]
    names.append(name)
    subprocess.run(cmd, env=env, check=True, stdout=subprocess.DEVNULL)
    print(f'Waiting for disposable {provider}...', flush=True)
    deadline = time.monotonic() + 150
    while time.monotonic() < deadline:
        logs = subprocess.run(docker + ['logs', name], capture_output=True, text=True, check=True)
        if ready_text in logs.stdout + logs.stderr:
            # MySQL's initialization server also reports ready but has no TCP listener.
            try:
                with socket.create_connection(('127.0.0.1', host_port), timeout=1): pass
                return host_port
            except OSError: pass
        time.sleep(1)
    raise RuntimeError(f'{provider} did not become ready. Inspect container {name} logs.')

def check_http(provider, connection):
    service = args.service or ROOT / 'src/Runninghill.Service/bin/Debug/net10.0/Runninghill.Service.dll'
    service_command = ['dotnet', str(service)] if service.suffix == '.dll' else [str(service)]
    http_port, grpc_port = port(), port()
    key = secrets.token_urlsafe(48)
    env = dict(environment, Database__Provider=provider, ConnectionStrings__Runninghill=connection,
               ASPNETCORE_ENVIRONMENT='Development', Authentication__Audience='runninghill',
               Authentication__Authority='', Authentication__DevelopmentSigningKey=key,
               Kestrel__Endpoints__HttpJson__Url=f'http://127.0.0.1:{http_port}',
               Kestrel__Endpoints__HttpJson__Protocols='Http1',
               Kestrel__Endpoints__Grpc__Url=f'http://127.0.0.1:{grpc_port}',
               Kestrel__Endpoints__Grpc__Protocols='Http2')
    env.pop('RUNNINGHILL_LOCAL_DEBUG', None)
    def encode(value):
        return base64.urlsafe_b64encode(value).rstrip(b'=')
    header = encode(b'{"alg":"HS256","typ":"JWT"}')
    payload = encode(json.dumps({'iss': 'runninghill-development', 'aud': 'runninghill', 'sub': 'provider-test',
        'scope': 'status.read words.read words.write sentences.read sentences.write', 'exp': int(time.time()) + 900}).encode())
    message = header + b'.' + payload
    env['RUNNINGHILL_ACCESS_TOKEN'] = (message + b'.' + encode(hmac.new(key.encode(), message, hashlib.sha256).digest())).decode()
    # Capture only our temporary server logs; never print the environment or token.
    with tempfile.TemporaryFile(mode='w+') as logs:
        migrated = subprocess.run([*service_command, '--migrate-database'], cwd=ROOT, env=env, stdout=logs, stderr=logs)
        if migrated.returncode:
            logs.seek(0); print(logs.read())
            raise RuntimeError(f'{provider} service migration failed')
        server = subprocess.Popen(service_command, cwd=ROOT, env=env, stdout=logs, stderr=logs)
        try:
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                try:
                    with urllib.request.urlopen(f'http://127.0.0.1:{http_port}/health/ready', timeout=1) as response:
                        if response.status == 200: break
                except OSError: time.sleep(0.2)
            else:
                logs.seek(0); print(logs.read())
                raise RuntimeError(f'{provider} API did not become ready')
            subprocess.run(['python3', 'tests/integration/collection.py', '--url', f'http://127.0.0.1:{http_port}'],
                           cwd=ROOT, env=env, check=True)
            print(f'{provider}: service migration command and authenticated HTTP contract passed.', flush=True)
        finally:
            server.terminate()
            try: server.wait(timeout=15)
            except subprocess.TimeoutExpired: server.kill(); server.wait()

try:
    password = 'Test9!' + secrets.token_hex(24)
    pg = start('Postgres', 'postgres:17-alpine', 5432, {'POSTGRES_PASSWORD': password},
               ['postgres', '-p', '{port}', '-c', 'listen_addresses=' + ('127.0.0.1' if args.host_network else '*')], 'database system is ready to accept connections')
    environment['RUNNINGHILL_TEST_POSTGRES'] = f'Host=127.0.0.1;Port={pg};Database=runninghill_test_collection;Username=postgres;Password={password};Timeout=5'
    mysql = start('MySQL', 'mysql:8.4', 3306, {'MYSQL_ROOT_PASSWORD': password, 'MYSQL_ROOT_HOST': '%'},
                  ['--port={port}', '--bind-address=' + ('127.0.0.1' if args.host_network else '0.0.0.0')], 'ready for connections')
    environment['RUNNINGHILL_TEST_MYSQL'] = f'Server=127.0.0.1;Port={mysql};Database=runninghill_test_collection;User=root;Password={password};SslMode=Required'
    sql = start('MSSQL', 'mcr.microsoft.com/mssql/server:2022-latest', 1433,
                {'ACCEPT_EULA': 'Y', 'MSSQL_PID': 'Developer', 'MSSQL_SA_PASSWORD': password,
                 'MSSQL_TCP_PORT': '{port}', 'MSSQL_IP_ADDRESS': '127.0.0.1' if args.host_network else '0.0.0.0',
                 'MSSQL_MEMORY_LIMIT_MB': '2048'}, [], 'SQL Server is now ready for client connections')
    environment['RUNNINGHILL_TEST_MSSQL'] = f'Server=127.0.0.1,{sql};Database=runninghill_test_collection;User ID=sa;Password={password};TrustServerCertificate=True;Connect Timeout=5'
    subprocess.run(['dotnet', 'test', 'tests/Runninghill.Tests', '-c', 'Debug', '-m:1',
                    '-p:AllowMissingPrunePackageData=true', '--filter', 'FullyQualifiedName~DatabaseProviderTests'],
                   cwd=ROOT, env=environment, check=True)
    for provider in ('Postgres', 'MSSQL', 'MySQL'):
        check_http(provider, environment['RUNNINGHILL_TEST_' + provider.upper()])
    with tempfile.TemporaryDirectory(prefix='runninghill-ef-http-') as directory:
        check_http('SQLite', 'Data Source=' + str(Path(directory) / 'collection.db'))
finally:
    # These names were generated above; never delete the user's normal containers or volumes.
    for name in reversed(names):
        subprocess.run(docker + ['rm', '-f', '-v', name], stdout=subprocess.DEVNULL, check=False)
