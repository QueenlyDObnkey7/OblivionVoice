param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $ServerRoot = '',

    [switch] $SkipDeploy,

    [switch] $NoExplorer
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot

$deployRoot = $ServerRoot

function Write-Step($text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Note($text) { Write-Host "    $text" -ForegroundColor DarkGray }

Write-Step "Unblocking scripts"
Get-ChildItem -Path $scriptDir -Recurse -Filter *.ps1 -ErrorAction SilentlyContinue | Unblock-File
Write-Ok "Done."

Write-Step "Loading ModFiles.ps1"
$modFiles = Join-Path $scriptDir 'ModFiles.ps1'
if (-not (Test-Path $modFiles)) {
    throw "ModFiles.ps1 not found in $scriptDir. It defines what goes into client/ and server/."
}
. $modFiles

if (-not $modName -or -not $clientProject -or -not $serverProject) {
    throw "ModFiles.ps1 loaded but did not define modName / clientProject / serverProject."
}
Write-Ok "Mod: $modName  (client: $clientProject, server: $serverProject)"

$contentDir = Join-Path $scriptDir 'Content'
$manifestPath = Join-Path $contentDir 'manifest.json'

if (-not (Test-Path $manifestPath)) {
    throw "Content/manifest.json is missing. Restore it from the repository."
}

Write-Step "Building ($Configuration)"

$solutionFiles = @(Get-ChildItem -Path $scriptDir -Filter *.sln)
if ($solutionFiles.Count -ne 1) {
    throw "Expected exactly one .sln in $scriptDir, found $($solutionFiles.Count)."
}

$buildLog = Join-Path $scriptDir 'build.log'
dotnet build $solutionFiles[0].FullName -c $Configuration -v minimal /t:Rebuild |
    Tee-Object -FilePath $buildLog

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE. See build.log."
}
Write-Ok "Build succeeded."

Write-Step "Packaging"

$outputRoot = Join-Path $scriptDir 'Output'
if (Test-Path $outputRoot) { Remove-Item $outputRoot -Recurse -Force }

$modRoot    = Join-Path (Join-Path $outputRoot 'mods') $modName
$clientRoot = Join-Path $modRoot 'client'
$serverRoot2 = Join-Path $modRoot 'server'
New-Item -ItemType Directory -Path $clientRoot -Force | Out-Null
New-Item -ItemType Directory -Path $serverRoot2 -Force | Out-Null

$tfm = 'net10.0'
$clientBuildDir = Join-Path $scriptDir "$clientProject\bin\$Configuration\$tfm"
$serverBuildDir = Join-Path $scriptDir "$serverProject\bin\$Configuration\$tfm"

$serverContentDir = Join-Path $scriptDir 'ServerContent'

$hostProvided = @(
    'ReadyM.*',
    'OblivionUI.Api*',
    'OblivionMp.Sdk*',
    'OblivionMpCSharpMod*',
    'Friflo.*',
    'LiteNetLib*',
    'Microsoft.Extensions.Logging.Abstractions*',
    'Microsoft.Extensions.DependencyInjection*',
    'Yooni.*',
    'DryIoc*'
)

function Copy-Artifacts {
    param(
        [AllowEmptyCollection()][string[]] $Files,
        [string] $BaseDir,
        [string] $DestRoot,
        [string] $Label
    )

    if (-not $Files -or $Files.Count -eq 0) { return }
    if (-not (Test-Path $BaseDir)) {
        Write-Warning "Source directory not found: $BaseDir"
        return
    }

    foreach ($file in $Files) {
        $source = Join-Path $BaseDir $file
        $dest   = Join-Path $DestRoot $file

        if (-not (Test-Path $source)) {
            Write-Warning "Missing: $source"
            continue
        }

        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }

        Copy-Item -Path $source -Destination $dest -Recurse -Force
        Write-Note "$Label <- $file"
    }
}

function Copy-RemainingAssemblies {
    param([string] $BaseDir, [string] $DestRoot, [string] $Label)

    if (-not (Test-Path $BaseDir)) { return }

    foreach ($dll in Get-ChildItem -Path $BaseDir -Filter *.dll -File) {
        $dest = Join-Path $DestRoot $dll.Name
        if (Test-Path $dest) { continue }

        $skip = $false
        foreach ($pattern in $hostProvided) {
            if ($dll.Name -like $pattern) { $skip = $true; break }
        }

        if ($skip) {
            Write-Note "$Label skip (host provides) $($dll.Name)"
            continue
        }

        Copy-Item -Path $dll.FullName -Destination $dest -Force
        Write-Host "    $Label <- $($dll.Name)  (swept)" -ForegroundColor Yellow
    }
}

$clientFiles = @($clientBuildFiles)
$serverFiles = @($serverBuildFiles)
if ($Configuration -eq 'Debug') {
    $clientFiles += $clientDebugBuildFiles
    $serverFiles += $serverDebugBuildFiles
}

Copy-Artifacts -Files $clientFiles        -BaseDir $clientBuildDir   -DestRoot $clientRoot  -Label 'client'
Copy-Artifacts -Files $clientContentFiles -BaseDir $contentDir       -DestRoot $clientRoot  -Label 'client'
Copy-Artifacts -Files $serverFiles        -BaseDir $serverBuildDir   -DestRoot $serverRoot2 -Label 'server'
Copy-Artifacts -Files $serverContentFiles -BaseDir $serverContentDir -DestRoot $serverRoot2 -Label 'server'
Copy-Artifacts -Files $manifestFiles      -BaseDir $contentDir       -DestRoot $modRoot     -Label 'root'

Copy-RemainingAssemblies -BaseDir $clientBuildDir -DestRoot $clientRoot -Label 'client'

# The native mouth driver loads /Game/OblivionVoice/Animations/A_VoiceJaw.
# Ship only the validated, owned mouth animation package in the client root.
$mouthAssetDir = Join-Path $contentDir 'MouthAssets'
$mouthAssetReceiptPath = Join-Path $mouthAssetDir 'receipt.json'
$mouthAssetAllowedNames = @('OblivionVoice_Mouth.pak', 'OblivionVoice_Mouth.ucas', 'OblivionVoice_Mouth.utoc')
if (-not (Test-Path -LiteralPath (Join-Path $mouthAssetDir 'OblivionVoice_Mouth.pak') -PathType Leaf)) {
    throw 'Required mouth animation asset missing: Content/MouthAssets/OblivionVoice_Mouth.pak.'
}
if (-not (Test-Path -LiteralPath $mouthAssetReceiptPath -PathType Leaf)) {
    throw 'Required mouth animation validation receipt missing: Content/MouthAssets/receipt.json.'
}
foreach ($mouthAssetItem in @((Get-Item -LiteralPath $mouthAssetDir -Force)) + @(Get-ChildItem -LiteralPath $mouthAssetDir -Recurse -Force)) {
    if ($mouthAssetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing linked mouth asset content: $($mouthAssetItem.FullName)"
    }
}
$mouthAssetNames = @(Get-ChildItem -LiteralPath $mouthAssetDir -File | Where-Object { $_.Name -in $mouthAssetAllowedNames } | Select-Object -ExpandProperty Name)
if (('OblivionVoice_Mouth.ucas' -in $mouthAssetNames) -ne ('OblivionVoice_Mouth.utoc' -in $mouthAssetNames)) {
    throw 'Mouth IoStore assets require both .ucas and .utoc.'
}
$mouthAssetReceipt = Get-Content -LiteralPath $mouthAssetReceiptPath -Raw | ConvertFrom-Json
$mouthAssetEntries = @($mouthAssetReceipt.hashes.PSObject.Properties)
if ($mouthAssetEntries.Count -ne $mouthAssetNames.Count) {
    throw 'Mouth asset receipt does not match the available asset files.'
}
foreach ($mouthAssetEntry in $mouthAssetEntries) {
    if ($mouthAssetEntry.Name -notin $mouthAssetNames -or $mouthAssetEntry.Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Unexpected mouth asset receipt entry: $($mouthAssetEntry.Name)"
    }
    $mouthAssetSource = Join-Path $mouthAssetDir $mouthAssetEntry.Name
    if ((Get-FileHash -LiteralPath $mouthAssetSource -Algorithm SHA256).Hash -ne $mouthAssetEntry.Value) {
        throw "Mouth asset validation failed: $($mouthAssetEntry.Name)"
    }
}
foreach ($mouthAssetEntry in $mouthAssetEntries) {
    $mouthAssetDestination = Join-Path $clientRoot $mouthAssetEntry.Name
    Copy-Item -LiteralPath (Join-Path $mouthAssetDir $mouthAssetEntry.Name) -Destination $mouthAssetDestination -Force
    if ((Get-FileHash -LiteralPath $mouthAssetDestination -Algorithm SHA256).Hash -ne $mouthAssetEntry.Value) {
        throw "Packaged mouth asset hash mismatch: $($mouthAssetEntry.Name)"
    }
    Write-Note "client <- $($mouthAssetEntry.Name)"
}

$requiredClient = @(
    "$clientProject.dll",
    'OblivionVoice.Common.dll',
    'Concentus.dll',
    'NAudio.Core.dll',
    'NAudio.Wasapi.dll',

    'System.Numerics.Tensors.dll'
)
$missing = $requiredClient | Where-Object { -not (Test-Path (Join-Path $clientRoot $_)) }
if ($missing) {
    throw "Client half is missing: $($missing -join ', '). If it is not in $clientBuildDir, add a PackageReference for it."
}

Write-Ok "Packaged to $modRoot"
Write-Note "client/ contains $((Get-ChildItem $clientRoot -Filter *.dll).Count) assemblies."

if ($SkipDeploy -or [string]::IsNullOrWhiteSpace($deployRoot)) {
    Write-Step "Package ready; deployment skipped"
}
else {
    Write-Step "Deploying to $deployRoot"

    if (-not (Test-Path $deployRoot)) {
        throw "Server root not found: $deployRoot. Pass -ServerRoot to override."
    }

    $serverModsDir = Join-Path $deployRoot 'mods'
    New-Item -ItemType Directory -Path $serverModsDir -Force | Out-Null
    $target = Join-Path $serverModsDir $modName

    $resolvedOutput = [IO.Path]::GetFullPath($outputRoot)
    $resolvedTarget = [IO.Path]::GetFullPath($target)

    if ($resolvedTarget.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Deploy target '$resolvedTarget' is inside the build output. Check -ServerRoot (currently '$deployRoot')."
    }

    if ($resolvedTarget.StartsWith([IO.Path]::GetFullPath($scriptDir), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Deploy target '$resolvedTarget' is inside the source tree. Check -ServerRoot (currently '$deployRoot')."
    }

    $running = Get-Process -Name 'server' -ErrorAction SilentlyContinue
    if ($running) {
        throw "The OBMP server appears to be running (PID $($running.Id -join ', ')). Stop it and re-run."
    }

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item -Path $modRoot -Destination $target -Recurse -Force

    Write-Ok "Deployed to $target"

    $legacyServerMods = Join-Path $deployRoot 'server_mods'
    if (Test-Path $legacyServerMods) {
        Write-Warning "server_mods\ still exists and is no longer read by the server."
        Write-Warning "Once this build is confirmed working, delete it: $legacyServerMods"
    }
}

Write-Step "Check for a stale manual client install"
Write-Host @"
    The server now serves client/ to connecting players, so the client half must NOT
    also be installed by hand. Two copies means two OblivionVoice.Common assemblies,
    which is exactly the situation that caused the RPC offset collisions.

    Check the game's mods folder and remove any OblivionVoice directory you copied
    there manually.
"@ -ForegroundColor Yellow

Write-Host "`nDone." -ForegroundColor Green

if (-not $NoExplorer -and -not $SkipDeploy -and -not [string]::IsNullOrWhiteSpace($deployRoot)) {
    Start-Process explorer.exe -ArgumentList "`"$outputRoot`""
}
