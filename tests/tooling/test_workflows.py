"""Check build routing and secret-file handling without running Docker or downloading SDKs."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


build = load('build')
dev = load('dev')


class BuildTests(unittest.TestCase):
    def commands(self, *arguments):
        with patch('sys.argv', ['build.py', *arguments]), patch.object(build, 'run') as run:
            build.main()
        return [call.args[0] for call in run.call_args_list]

    def test_all_build_limits_mobile_runtime_to_maui(self):
        command, = self.commands('build', '--framework', 'net10.0-android36.1', '--rid', 'android-arm64')
        self.assertIn('Runninghill.slnx', command)
        self.assertIn('-p:RunninghillMauiRuntimeIdentifier=android-arm64', command)
        self.assertNotIn('-r', command)

    def test_core_publish_defaults_to_release(self):
        commands = self.commands('publish', '--target', 'core')
        self.assertEqual(len(commands), 3)
        for command in commands:
            self.assertIn('Release', command)
        self.assertNotIn('-r', commands[-1])  # Browser output has no native host RID.

    def test_all_publish_does_not_give_mobile_rid_to_service(self):
        with patch.object(build.platform, 'system', return_value='Linux'), patch.object(build.platform, 'machine', return_value='x86_64'):
            commands = self.commands('publish', '--framework', 'net10.0-android36.1', '--rid', 'android-arm64')
        self.assertIn('linux-x64', commands[0])
        self.assertIn('linux-x64', commands[1])
        self.assertIn('android-arm64', commands[-1])

    def test_individual_project_uses_its_own_dependency_graph(self):
        command, = self.commands('build', '--target', 'database', '-c', 'Release')
        self.assertIn(build.PROJECTS['database'], command)


class DebugSettingsTests(unittest.TestCase):
    def test_compose_uses_configured_context_and_private_environment(self):
        with patch.object(dev, 'settings', return_value={'docker_context': 'local-test', 'host_network': False}), \
             patch.object(dev, 'credentials', return_value={'password': 'private'}), \
             patch.object(dev.subprocess, 'run') as run:
            dev.compose(['up', '-d', 'database'])
        self.assertEqual(run.call_args.args[0][0:3], ['docker', '--context', 'local-test'])
        self.assertEqual(run.call_args.kwargs['env']['POSTGRES_PASSWORD'], 'private')
        self.assertNotIn('private', ' '.join(run.call_args.args[0]))

    def test_failed_settings_refresh_preserves_previous_file(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'settings.json'
            path.write_text('original')
            with patch.object(dev.os, 'replace', side_effect=OSError('simulated failure')):
                with self.assertRaises(OSError):
                    dev.write_private(path, 'replacement')
            self.assertEqual(path.read_text(), 'original')
            self.assertEqual(list(path.parent.iterdir()), [path])

    def test_credentials_are_stable_and_private(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with patch.object(dev, 'STATE', root / '.run'), patch.object(dev, 'SERVICE', root / 'service'):
                dev.prepare_files()
                first = dev.credentials()
                dev.prepare_files()
                self.assertEqual(first, dev.credentials())
                payload = json.loads((root / 'service/appsettings.Development.local.json').read_text())
                self.assertIn('Port=55433;', payload['ConnectionStrings']['Runninghill'])
                self.assertIn('RUNNINGHILL_ACCESS_TOKEN=', (root / '.run/cli.env').read_text())
                if os.name != 'nt':
                    self.assertEqual((root / '.run/credentials.json').stat().st_mode & 0o777, 0o600)


if __name__ == '__main__':
    unittest.main()
