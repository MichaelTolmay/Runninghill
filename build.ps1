# The same build entry point as build.sh, for PowerShell on Windows/macOS/Linux.
& python (Join-Path $PSScriptRoot 'scripts/build.py') @args
exit $LASTEXITCODE
