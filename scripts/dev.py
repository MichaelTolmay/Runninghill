#!/usr/bin/env python3
"""Prepare an isolated local database, debugger settings, and short-lived development tokens."""
import argparse
import base64
import hashlib
import hmac
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / '.run'
SERVICE = ROOT / 'src/Runninghill.Service'
WEB = ROOT / 'src/Clients/Runninghill.Web'


def write_private(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    # Write privately, then swap the whole file in one step. Two compound launch tasks
    # may refresh settings together; readers must never see a half-written JSON/token file.
    descriptor, temporary_name = tempfile.mkstemp(dir=path.parent, prefix='.settings-')
    temporary = Path(temporary_name)
    try:
        with os.fdopen(descriptor, 'w', encoding='utf-8') as output:
            output.write(text)
        temporary.chmod(0o600)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def settings():
    path = STATE / 'settings.json'
    return json.loads(path.read_text()) if path.exists() else {'docker_context': 'default', 'host_network': False}


def credentials():
    path = STATE / 'credentials.json'
    if not path.exists():
        write_private(path, json.dumps({'password': secrets.token_urlsafe(32), 'key': secrets.token_urlsafe(48)}))
    return json.loads(path.read_text())


def token():
    def encode(data):
        return base64.urlsafe_b64encode(data).rstrip(b'=')
    key = credentials()['key']
    header = encode(b'{"alg":"HS256","typ":"JWT"}')
    payload = encode(json.dumps({'iss': 'runninghill-development', 'aud': 'runninghill',
        'sub': 'local-debugger', 'scope': 'status.read words.read words.write sentences.read sentences.write logs.read', 'exp': int(time.time()) + 900}).encode())
    message = header + b'.' + payload
    return (message + b'.' + encode(hmac.new(key.encode(), message, hashlib.sha256).digest())).decode()


def prepare_files():
    secret = credentials()
    configuration = {
        'Database': {'Provider': 'Postgres'},
        'ConnectionStrings': {'Runninghill': 'Host=127.0.0.1;Port=55433;Database=runninghill;Username=runninghill;Password=' + secret['password'] + ';Timeout=5'},
        'Authentication': {'Audience': 'runninghill', 'Authority': '', 'DevelopmentSigningKey': secret['key']},
        'Kestrel': {'Endpoints': {
            'HttpJson': {'Url': 'http://localhost:5180', 'Protocols': 'Http1'},
            'Grpc': {'Url': 'http://localhost:5181', 'Protocols': 'Http2'}}}}
    write_private(SERVICE / 'appsettings.Development.local.json', json.dumps(configuration, indent=2) + '\n')
    write_private(STATE / 'cli.env', 'RUNNINGHILL_SERVICE_URL=http://localhost:5180/\nRUNNINGHILL_ACCESS_TOKEN=' + token() + '\n')


def compose(action, input_text=None):
    local = settings()
    command = ['docker', '--context', local['docker_context'], 'compose', '-f', str(ROOT / 'compose.debug.yaml')]
    if local['host_network']:
        if sys.platform != 'linux':
            raise ValueError('The host-network fallback is only intended for Linux. Run configure without --host-network.')
        command += ['-f', str(ROOT / 'deploy/compose.debug.host-network.yaml')]
    # Pass secrets through the environment, never as command-line arguments or printed configuration.
    environment = dict(os.environ, POSTGRES_PASSWORD=credentials()['password'])
    subprocess.run(command + action, cwd=ROOT, env=environment, check=True, input=input_text, text=True)


def configure(args):
    write_private(STATE / 'settings.json', json.dumps({'docker_context': args.docker_context, 'host_network': args.host_network}, indent=2))
    path = ROOT / 'Directory.Build.local.props'
    if path.exists():
        print('Keeping existing Directory.Build.local.props; edit it to change tool locations.')
        return
    project = ET.Element('Project')
    group = ET.SubElement(project, 'PropertyGroup')
    android = os.environ.get('ANDROID_HOME') or os.environ.get('ANDROID_SDK_ROOT')
    if not android:
        candidates = [Path.home() / 'Android/Sdk', Path.home() / 'Library/Android/sdk',
                      Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'Android/Sdk']
        android = next((str(item) for item in candidates if item.is_dir()), None)
    if android:
        ET.SubElement(group, 'AndroidSdkDirectory').text = android
        # Match an installed Android platform, including minor versions such as 36.1.
        platforms = [p.name.removeprefix('android-') for p in (Path(android) / 'platforms').glob('android-*')]
        platforms = [p for p in platforms if all(part.isdigit() for part in p.split('.'))]
        if platforms:
            version = max(platforms, key=lambda value: tuple(map(int, value.split('.'))))
            ET.SubElement(group, 'RunninghillAndroidTargetFramework').text = 'net10.0-android' + version
    java = os.environ.get('JAVA_HOME')
    if not java and Path('/usr/lib/jvm/java-17-openjdk').is_dir():
        java = '/usr/lib/jvm/java-17-openjdk'
    if java:
        ET.SubElement(group, 'JavaSdkDirectory').text = java
    if args.allow_missing_prune_data:
        ET.SubElement(group, 'AllowMissingPrunePackageData').text = 'true'
    ET.indent(project)
    path.write_text(ET.tostring(project, encoding='unicode') + '\n')
    print('Created ignored local tool settings. Review Directory.Build.local.props before building.')


def wait_for_service():
    # A CLI starts and exits quickly. Wait for the service before launching it in a compound.
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen('http://localhost:5180/health/ready', timeout=2) as response:
                if response.status == 200:
                    prepare_files()  # Refresh the CLI token just before launching the debugger.
                    return
        except (urllib.error.URLError, TimeoutError):
            pass
        time.sleep(0.25)
    raise ValueError('Service was not ready after 60 seconds. Start the Service debugger and inspect its output and database health.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['configure', 'prepare', 'database-down', 'token', 'wait', 'run'])
    parser.add_argument('--target', choices=['all', 'core', 'service', 'web', 'cli', 'maui'], default='core')
    parser.add_argument('--no-build', action='store_true')
    parser.add_argument('--docker-context', default='default')
    parser.add_argument('--host-network', action='store_true')
    parser.add_argument('--allow-missing-prune-data', action='store_true')
    args = parser.parse_args()
    if args.action == 'configure':
        configure(args)
    elif args.action == 'token':
        print(token())  # Only this explicit command prints a credential, for pasting into the UI.
    elif args.action == 'database-down':
        compose(['down'])  # Preserve the debug database volume and its data.
    elif args.action == 'wait':
        wait_for_service()
    elif args.action == 'prepare':
        prepare_files()
        compose(['up', '-d', '--build', '--wait', '--wait-timeout', '90'])
        if not args.no_build:
            subprocess.run([sys.executable, str(ROOT / 'scripts/build.py'), 'build', '-c', 'Debug', '--target', args.target], check=True)
        subprocess.run(['dotnet', 'run', '--project', str(SERVICE), '--no-launch-profile',
                        *(['--no-build'] if args.no_build else []), '--', '--migrate-database'],
                       cwd=ROOT, env=dict(os.environ, Database__Provider='Postgres',
                           ConnectionStrings__Runninghill=json.loads((SERVICE / 'appsettings.Development.local.json').read_text())['ConnectionStrings']['Runninghill']),
                       check=True)
        print('Debug environment ready. HTTP 5180; gRPC 5181; web 5182; database 55433.')
    else:
        if args.target not in ('service', 'web', 'cli'):
            parser.error('run supports service, web, or cli; use the MAUI debugger for device deployment')
        prepare_files()
        if not args.no_build:
            subprocess.run([sys.executable, str(ROOT / 'scripts/build.py'), '--target', args.target], check=True)
        environment = dict(os.environ)
        if args.target == 'cli':
            wait_for_service()
            environment.update(RUNNINGHILL_SERVICE_URL='http://localhost:5180/', RUNNINGHILL_ACCESS_TOKEN=token())
            command = ['dotnet', str(ROOT / 'src/Clients/Runninghill.Cli/bin/Debug/net10.0/Runninghill.Cli.dll')]
            directory = ROOT
        else:
            directory = SERVICE if args.target == 'service' else WEB
            command = ['dotnet', 'run', '--no-build', '--launch-profile', 'Debug']
        subprocess.run(command, cwd=directory, env=environment, check=True)


if __name__ == '__main__':
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode)
    except (OSError, ValueError) as error:
        # Configuration values may contain credentials, so report only the type for parsing/IO failures.
        if isinstance(error, ValueError) and not isinstance(error, json.JSONDecodeError):
            sys.exit(str(error))
        sys.exit(f'Development setup failed ({type(error).__name__}). Check .run settings, tool installation, and file permissions.')
    except KeyboardInterrupt:
        sys.exit(130)
