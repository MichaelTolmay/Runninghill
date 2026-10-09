"""Check CLI launcher setup without publishing binaries, trusting certificates or modifying data."""
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipUnless(platform.system() == 'Linux' and shutil.which('bash'), 'Bash launcher tests use a Linux stub CLI')
class BashCliSetupTests(unittest.TestCase):
    """Exercise the real launcher against tiny token, publisher and CLI stand-ins."""

    def setUp(self):
        """Create an isolated checkout whose path contains spaces."""
        self.temporary = tempfile.TemporaryDirectory(prefix='runninghill cli setup ')
        self.root = Path(self.temporary.name)
        scripts = self.root / 'scripts'
        scripts.mkdir()
        shutil.copy(ROOT / 'scripts/setup-cli.sh', scripts / 'setup-cli.sh')
        (self.root / '.env').write_text('test settings')
        certificate = self.root / '.run/tls/localhost.crt'
        certificate.parent.mkdir(parents=True)
        certificate.write_text('test certificate')
        (scripts / 'dev-token.py').write_text("print('test-only-token')\n")
        arch = 'arm64' if platform.machine() in ('arm64', 'aarch64') else 'x64'
        self.binary = self.root / f'artifacts/Release/cli/linux-{arch}/Runninghill.Cli'
        self.binary.parent.mkdir(parents=True)
        self.stub = '''#!/usr/bin/env python3
import json, os, sys
assert os.environ['RUNNINGHILL_ACCESS_TOKEN'] == 'test-only-token'
assert os.environ['RUNNINGHILL_SERVICE_URL'] == 'https://localhost:5443/'
assert os.path.isfile(os.environ['SSL_CERT_FILE'])
print(json.dumps(sys.argv[1:]))
sys.exit(7 if sys.argv[1:] == ['fail'] else 0)
'''
        self.binary.write_text(self.stub)
        self.binary.chmod(0o755)

    def tearDown(self):
        """Remove only this test's disposable checkout."""
        self.temporary.cleanup()

    def run_script(self, *arguments):
        """Run from another folder, capturing output to check that credentials stay private."""
        result = subprocess.run(['bash', str(self.root / 'scripts/setup-cli.sh'), *arguments], cwd='/tmp', capture_output=True, text=True)
        self.assertNotIn('test-only-token', result.stdout + result.stderr)
        return result

    def test_defaults_arguments_and_exit_codes(self):
        """Status is the default, quoted arguments survive, and CLI failures reach the caller."""
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), ['status'])
        result = self.run_script('words', 'list', '--search', 'two words')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), ['words', 'list', '--search', 'two words'])
        self.assertEqual(self.run_script('fail').returncode, 7)

    def test_missing_settings_fail_before_a_request(self):
        """An absent environment file provides a specific recovery command."""
        (self.root / '.env').unlink()
        result = self.run_script()
        self.assertEqual(result.returncode, 2)
        self.assertIn('dev-setup.py', result.stderr)
        self.assertEqual(result.stdout, '')

    def test_first_use_publishes_missing_binary(self):
        """A fresh checkout invokes the publisher with the host RID before starting the CLI."""
        self.binary.unlink()
        tools = self.root / 'fake tools'
        tools.mkdir()
        dotnet = tools / 'dotnet'
        dotnet.write_text('#!/usr/bin/env sh\nexit 0\n')
        dotnet.chmod(0o755)
        publisher = self.root / 'scripts/build.py'
        publisher.write_text('from pathlib import Path\nimport sys\n'
                             'assert sys.argv[1:4] == ["publish", "--target", "cli"]\n'
                             f'assert sys.argv[4:] == ["--rid", {self.binary.parent.name!r}]\n'
                             f'p = Path({str(self.binary)!r})\np.write_text({self.stub!r})\np.chmod(0o755)\n')
        environment = dict(os.environ, PATH=str(tools) + os.pathsep + os.environ['PATH'])
        result = subprocess.run(['bash', str(self.root / 'scripts/setup-cli.sh')], cwd='/tmp', env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('["status"]', result.stdout)
        self.assertNotIn('test-only-token', result.stdout + result.stderr)


class PowerShellCliSetupTests(unittest.TestCase):
    """Parse the PowerShell launcher on machines with PowerShell, including Windows CI."""

    @unittest.skipUnless(shutil.which('pwsh') or shutil.which('powershell'), 'PowerShell is not installed')
    def test_script_parses(self):
        """Use PowerShell's own parser without executing trust-store changes."""
        executable = shutil.which('pwsh') or shutil.which('powershell')
        command = "$tokens=$null; $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile($env:RUNNINGHILL_TEST_PS_SCRIPT, [ref]$tokens, [ref]$errors) | Out-Null; if ($errors.Count) { $errors | Out-String | Write-Error; exit 1 }"
        environment = dict(os.environ, RUNNINGHILL_TEST_PS_SCRIPT=str(ROOT / 'scripts/setup-cli.ps1'))
        subprocess.run([executable, '-NoProfile', '-NonInteractive', '-Command', command], env=environment, check=True, capture_output=True)


if __name__ == '__main__':
    unittest.main()
