# Puts the HidHide installer that the release package bundles into deps\ (kept out of git).
# The file must match the pinned SHA-256, whichever way it gets there:
#   tools\fetch-deps.ps1 -From C:\path\to\HidHide_1.5.230_x64.exe   (a copy you already have)
#   tools\fetch-deps.ps1 -Download                                   (from the official GitHub release)
param([string]$From, [switch]$Download)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$name = 'HidHide_1.5.230_x64.exe'
$url = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/$name"
$sha256 = 'F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6'
$target = Join-Path $root "deps\$name"

function Test-Installer([string]$path) {
    (Test-Path $path) -and ((Get-FileHash $path -Algorithm SHA256).Hash -eq $sha256)
}

if (Test-Installer $target) { "$name is ready in deps\"; return }

New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
$temp = "$target.part"
if ($From) {
    Copy-Item $From $temp -Force
} elseif ($Download) {
    Invoke-WebRequest $url -OutFile $temp -UseBasicParsing
} else {
    throw "deps\$name is missing. Run tools\fetch-deps.ps1 -From <path to $name> or tools\fetch-deps.ps1 -Download"
}

if (-not (Test-Installer $temp)) {
    Remove-Item $temp -Force
    throw "$name does not match the expected SHA-256 ($sha256); not used"
}
Move-Item $temp $target -Force
"$name verified and placed in deps\"
