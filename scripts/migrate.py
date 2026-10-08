#!/usr/bin/env python3
"""Apply EF Core migrations without starting the API or deleting existing data."""
import argparse
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--docker-context', default='default')
parser.add_argument('--host-network', action='store_true', help='Use the Linux host-network compose override')
parser.add_argument('--local', action='store_true', help='Use dotnet and Database__Provider / ConnectionStrings__Runninghill from the environment')
args = parser.parse_args()
if args.host_network and (sys.platform != 'linux' or args.local):
    parser.error('--host-network is only supported with Docker on Linux')
if args.local:
    command = ['dotnet', 'run', '--project', 'src/Runninghill.Service', '--no-launch-profile', '--', '--migrate-database']
else:
    command = ['docker', '--context', args.docker_context, 'compose', '-f', 'compose.yaml']
    if args.host_network:
        command += ['-f', 'deploy/compose.host-network.yaml']
    command += ['run', '--rm', '--no-deps', 'service', '--migrate-database']
try:
    subprocess.run(command, cwd=ROOT, check=True)
except (OSError, subprocess.CalledProcessError):
    raise SystemExit('Migration did not finish. Check provider configuration, connectivity and the migration output. Do not delete the database; fix the cause and rerun this command.') from None
print('EF database migrations completed. The service can now be started.')
