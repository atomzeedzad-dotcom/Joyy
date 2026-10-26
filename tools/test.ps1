# Builds the solution and runs all hardware-free tests.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (dotnet --list-sdks | Select-String '^[89]\.|^1\d\.')) {
    Write-Error 'No .NET 8+ SDK found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

dotnet test (Join-Path $root 'Joyy.sln') -c Release --nologo
exit $LASTEXITCODE
