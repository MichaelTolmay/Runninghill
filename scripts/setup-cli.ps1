# Usage: .\scripts\setup-cli.ps1 [status | words list | ...]
# Environment settings are restored after the command; the token is never printed or saved.
$runninghillArguments = @($args)
$runninghillRoot = Split-Path -Parent $PSScriptRoot
$runninghillCertificate = Join-Path $runninghillRoot '.run/tls/localhost.crt'
$runninghillExitCode = 2
$runninghillOldErrorPreference = $ErrorActionPreference
$ErrorActionPreference = 'Stop'
# Preserve the CLI's own exit code even when the caller enables native-command exceptions.
$PSNativeCommandUseErrorActionPreference = $false
$runninghillEnvironmentNames = @('RUNNINGHILL_SERVICE_URL', 'RUNNINGHILL_ACCESS_TOKEN', 'SSL_CERT_FILE')
$runninghillPreviousEnvironment = @{}
foreach ($name in $runninghillEnvironmentNames) {
    $runninghillPreviousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    # Prefer python on Windows; python3 is the usual command on Linux/macOS.
    $runninghillPython = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $runninghillPython) {
        $runninghillPython = Get-Command python3 -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if (-not $runninghillPython) { throw 'Python 3 is required. Install it and run this script again.' }
    if (-not (Test-Path -LiteralPath (Join-Path $runninghillRoot '.env') -PathType Leaf)) {
        throw 'Missing .env. Run scripts/dev-setup.py from this checkout to configure the local Docker stack.'
    }
    if (-not (Test-Path -LiteralPath $runninghillCertificate -PathType Leaf)) {
        throw 'Missing HTTPS certificate. Run scripts/dev-certificate.py and start the HTTPS containers first.'
    }
    # Avoid embedded quotes in Python code: Windows PowerShell 5 passes native arguments differently.
    $runninghillPlatform = & $runninghillPython.Source -c 'import platform; print(platform.system(), platform.machine())'
    if ($LASTEXITCODE -ne 0) { throw 'Could not detect the platform. Use Python 3 on an x64 or arm64 computer.' }
    $runninghillPlatformParts = "$runninghillPlatform".Trim() -split '\s+'
    $runninghillOs = switch ($runninghillPlatformParts[0]) {
        'Windows' { 'win' }; 'Linux' { 'linux' }; 'Darwin' { 'osx' }
        default { throw 'This setup supports Windows, Linux and macOS.' }
    }
    $runninghillArch = switch ($runninghillPlatformParts[1]) {
        'AMD64' { 'x64' }; 'x86_64' { 'x64' }; 'aarch64' { 'arm64' }; 'arm64' { 'arm64' }
        default { throw 'This setup supports x64 and arm64 computers.' }
    }
    $runninghillRid = "$runninghillOs-$runninghillArch"
    $runninghillFileName = if ($runninghillRid.StartsWith('win-')) { 'Runninghill.Cli.exe' } else { 'Runninghill.Cli' }
    $runninghillBinary = Join-Path $runninghillRoot "artifacts/Release/cli/$runninghillRid/$runninghillFileName"
    if (-not (Test-Path -LiteralPath $runninghillBinary -PathType Leaf)) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw 'The published CLI is missing. Install the .NET 10 SDK and native AOT build tools, then retry.'
        }
        Write-Host "Publishing the CLI for $runninghillRid..."
        & $runninghillPython.Source (Join-Path $runninghillRoot 'scripts/build.py') publish --target cli --rid $runninghillRid
        if ($LASTEXITCODE -ne 0) { throw 'CLI publishing failed. Check the build output and native AOT tools for your operating system.' }
    }
    if ($runninghillRid.StartsWith('win-')) {
        # Windows .NET uses the Windows certificate store. This trusts the public
        # certificate for this user only; no administrator/system-store change is needed.
        $runninghillPublicCertificate = Get-PfxCertificate -FilePath $runninghillCertificate
        if ($runninghillPublicCertificate.Subject -ne 'CN=localhost') { throw 'Expected the localhost development certificate.' }
        $runninghillThumbprint = $runninghillPublicCertificate.Thumbprint
        if (-not (Test-Path "Cert:\CurrentUser\Root\$runninghillThumbprint")) {
            Write-Host 'Trusting the local HTTPS certificate in your current-user certificate store.'
            Import-Certificate -FilePath $runninghillCertificate -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
        }
    } elseif ($runninghillRid.StartsWith('osx-')) {
        Write-Host 'Trusting the local HTTPS certificate in your login keychain (macOS may ask for permission).'
        & security add-trusted-cert -r trustRoot -k (Join-Path $env:HOME 'Library/Keychains/login.keychain-db') $runninghillCertificate
        if ($LASTEXITCODE -ne 0) { throw 'Could not trust the localhost certificate in your login keychain.' }
    }
    # Generate after publishing, so the token is still fresh when the CLI starts.
    $runninghillToken = & $runninghillPython.Source (Join-Path $runninghillRoot 'scripts/dev-token.py')
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$runninghillToken")) {
        throw 'Could not generate an access token. Check the development settings in .env.'
    }
    $env:RUNNINGHILL_SERVICE_URL = 'https://localhost:5443/'
    $env:RUNNINGHILL_ACCESS_TOKEN = "$runninghillToken".Trim()
    $env:SSL_CERT_FILE = $runninghillCertificate
    if ($runninghillArguments.Count -eq 0) { $runninghillArguments = @('status') }
    Write-Host 'Using https://localhost:5443/ with a fresh development token.'
    & $runninghillBinary @runninghillArguments
    $runninghillExitCode = $LASTEXITCODE
} catch {
    # Exceptions here describe setup steps, never request headers or token contents.
    Write-Host ("CLI setup failed: " + $_.Exception.Message) -ForegroundColor Red
} finally {
    foreach ($name in $runninghillEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $runninghillPreviousEnvironment[$name], 'Process')
    }
    $runninghillToken = $null
    $ErrorActionPreference = $runninghillOldErrorPreference
}
exit $runninghillExitCode
