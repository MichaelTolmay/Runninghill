#!/usr/bin/env python3
"""Build or publish the solution without depending on a particular shell or IDE."""
import argparse
import os
from pathlib import Path
import platform
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = {
    'application': 'src/Runninghill.Application/Runninghill.Application.csproj',
    'contracts': 'src/Runninghill.Contracts/Runninghill.Contracts.csproj',
    'database': 'src/Runninghill.Database/Runninghill.Database.csproj',
    'service': 'src/Runninghill.Service/Runninghill.Service.csproj',
    'cli': 'src/Clients/Runninghill.Cli/Runninghill.Cli.csproj',
    'web': 'src/Clients/Runninghill.Web/Runninghill.Web.csproj',
    'maui': 'src/Clients/Runninghill.Maui/Runninghill.Maui.csproj',
    'tests': 'tests/Runninghill.Tests/Runninghill.Tests.csproj',
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['build', 'publish', 'test'], nargs='?', default='build')
    parser.add_argument('--configuration', '-c', choices=['Debug', 'Release'], help='Defaults to Release for publish; Debug for build/test')
    parser.add_argument('--target', choices=['all', 'core', *PROJECTS], default='all')
    parser.add_argument('--framework', help='MAUI target, for example net10.0-android36.1')
    parser.add_argument('--rid', help='Publish runtime, for example linux-x64 or android-arm64')
    parser.add_argument('--jobs', type=int, default=1, help='MSBuild worker count; dependencies still build before their callers')
    parser.add_argument('--property', action='append', default=[], help='Extra MSBuild Name=Value (repeatable)')
    args = parser.parse_args()
    args.configuration = args.configuration or ('Release' if args.action == 'publish' else 'Debug')
    if args.jobs < 1:
        parser.error('--jobs must be at least 1')
    if args.framework and args.target not in ('maui', 'all'):
        parser.error('--framework applies to maui or all')
    common = ['-c', args.configuration, f'-m:{args.jobs}'] + ['-p:' + value for value in args.property]
    maui = []
    if args.framework:
        # Limit restore as well as compilation, so an Android build does not require Apple workloads.
        maui = ['-p:MauiTargetFrameworks=' + args.framework, '-f', args.framework]
    if args.action == 'publish':
        targets = ['service', 'cli', 'web'] if args.target in ('all', 'core') else [args.target]
        if args.target == 'all':
            targets.append('maui')
        if 'maui' in targets and (not args.framework or not args.rid):
            parser.error('MAUI publishing requires --framework and --rid for one selected device platform')
        if any(target not in ('service', 'cli', 'web', 'maui') for target in targets):
            parser.error('Publish an executable client/service. Libraries are included with their host; use test for tests.')
        host_os = {'Linux': 'linux', 'Windows': 'win', 'Darwin': 'osx'}[platform.system()]
        host_arch = 'arm64' if platform.machine().lower() in ('arm64', 'aarch64') else 'x64'
        for target in targets:
            # A mobile RID must not accidentally be passed to the service or CLI in an all publish.
            rid = args.rid if args.target != 'all' or target == 'maui' else None
            rid = rid or f'{host_os}-{host_arch}'
            command = ['dotnet', 'publish', PROJECTS[target], *common]
            if target != 'web':
                command += ['-r', rid]
            if target == 'maui':
                command += maui
            output = ROOT / 'artifacts' / args.configuration / target
            if target != 'web':
                output /= rid
            run([*command, '-o', str(output)])
    elif args.action == 'test':
        run(['dotnet', 'test', PROJECTS['tests'], *common])
    else:
        target = {'all': 'Runninghill.slnx', 'core': 'Runninghill.Server.slnf'}.get(args.target, PROJECTS.get(args.target))
        command = ['dotnet', 'build', target, *common]
        if args.target == 'maui':
            command += maui
            if args.rid:
                command += ['-r', args.rid]
        elif args.target == 'all':
            if args.framework:
                command += ['-p:MauiTargetFrameworks=' + args.framework]
            if args.rid:
                command += ['-p:RunninghillMauiRuntimeIdentifier=' + args.rid]
        run(command)


def run(command):
    print('Running:', ' '.join(command), flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


if __name__ == '__main__':
    try:
        main()
    except FileNotFoundError:
        sys.exit('Could not find dotnet. Install the .NET 10 SDK and add it to PATH.')
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode)
    except KeyboardInterrupt:
        sys.exit(130)
