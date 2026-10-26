# Builds the release package: one self-contained Joyy.exe (no .NET install needed) in artifacts\publish.
# Double-click it to be offered an install: Start menu "Joyy" entry + start with Windows.
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\publish'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'src\Joyy.App\Joyy.App.csproj') -c $Configuration -r win-x64 `
    -p:JoyyPublished=true -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

Get-ChildItem $out | ForEach-Object { '{0,-24} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
