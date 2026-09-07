param(
    [Parameter(Mandatory=$true)]
    [string]$OfficialTemplatePath
)

$ErrorActionPreference = "Stop"
$source = Join-Path $OfficialTemplatePath "Dependencies"
$dest = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Dependencies"

if (-not (Test-Path $source)) {
    throw "No Dependencies folder found at '$source'. Point OfficialTemplatePath at the root of readycodeio/oblivionmp-mod-template."
}

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item $source $dest -Recurse -Force
Write-Host "Copied official OblivionMP Dependencies into $dest" -ForegroundColor Green
