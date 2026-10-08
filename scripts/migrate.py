#!/usr/bin/env python3
"""Apply the additive word schema to an existing development Docker database; preserve its data."""
import argparse
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--docker-context', default='default')
parser.add_argument('--host-network', action='store_true', help='Use the Linux host-network override and port 55432')
args = parser.parse_args()
if args.host_network and sys.platform != 'linux':
    parser.error('--host-network is only supported on Linux')
command = ['docker', '--context', args.docker_context, 'compose', '-f', 'compose.yaml']
if args.host_network:
    command += ['-f', 'deploy/compose.host-network.yaml']
command += ['exec', '-T', 'database', 'psql', '-p', '55432' if args.host_network else '5432',
            '-U', 'runninghill', '-d', 'runninghill', '-v', 'ON_ERROR_STOP=1']
try:
    subprocess.run(command, cwd=ROOT, input=(ROOT / 'deploy/database/002-words.sql').read_text(), text=True, check=True)
except (OSError, subprocess.CalledProcessError):
    raise SystemExit('Migration did not finish. Check Docker connectivity and the database output above; existing data was not erased.') from None
print('Word collection schema is ready. You can now start the updated service.')
