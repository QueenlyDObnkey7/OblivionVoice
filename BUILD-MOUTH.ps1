param([Parameter(Mandatory = $true)][string]$ServerRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$installed = [IO.Path]::GetFullPath((Join-Path $ServerRoot 'mods/OblivionVoice'))
$version = '0.6.3'
$generatedRelative = 'server/OblivionVoice.server.trace.log'
$changedFiles = @('client/OblivionVoice.Client.dll', 'manifest.json')
$allowedAssetNames = @('OblivionVoice_Mouth.pak', 'OblivionVoice_Mouth.ucas', 'OblivionVoice_Mouth.utoc')

function Assert-NoLinks([string]$Directory) {
    if (!(Test-Path -LiteralPath $Directory -PathType Container)) { throw "Directory missing: $Directory" }
    $linked = @((Get-Item -LiteralPath $Directory -Force)) + @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)
    foreach ($item in $linked) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked package content: $($item.FullName)" }
    }
}
function Hash-Tree([string]$Directory) {
    $hashes = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
        if ($relative -eq $generatedRelative) { continue }
        $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}
function Assert-Hashes($Before, $After, [string]$Label) {
    if ($Before.Count -ne $After.Count) { throw "$Label file count changed. Rebuild from the current installed Voice mod." }
    foreach ($name in $Before.Keys) {
        if (!$After.Contains($name) -or $Before[$name] -ne $After[$name]) { throw "$Label differs: $name" }
    }
}
function Run-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}

Assert-NoLinks $installed
$manifestPath = Join-Path $root 'Content/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.uniqueId -ne 'OblivionVoice' -or $manifest.version -ne $version) { throw "Content/manifest.json must identify OblivionVoice $version." }
$installedManifest = Get-Content -LiteralPath (Join-Path $installed 'manifest.json') -Raw | ConvertFrom-Json
if ($installedManifest.uniqueId -ne 'OblivionVoice') { throw 'The installed baseline is not OblivionVoice.' }
$baseline = Hash-Tree $installed
$assetRoot = Join-Path $root 'Content/MouthAssets'
$assetHashes = [ordered]@{}
# The native mouth driver loads /Game/OblivionVoice/Animations/A_VoiceJaw.
# Packaging without its cooked animation would produce a client that cannot animate.
if (!(Test-Path -LiteralPath (Join-Path $assetRoot 'OblivionVoice_Mouth.pak') -PathType Leaf)) {
    throw 'Required mouth animation asset missing: Content/MouthAssets/OblivionVoice_Mouth.pak.'
}
if (!(Test-Path -LiteralPath (Join-Path $assetRoot 'receipt.json') -PathType Leaf)) {
    throw 'Required mouth animation validation receipt missing: Content/MouthAssets/receipt.json.'
}
if (Test-Path -LiteralPath $assetRoot -PathType Container) {
    Assert-NoLinks $assetRoot
    $assetNames = @(Get-ChildItem -LiteralPath $assetRoot -File | Where-Object { $_.Name -in $allowedAssetNames } | Select-Object -ExpandProperty Name)
    if ($assetNames.Count -gt 0) {
        if ('OblivionVoice_Mouth.pak' -notin $assetNames) { throw 'Mouth asset set must contain OblivionVoice_Mouth.pak.' }
        if (('OblivionVoice_Mouth.ucas' -in $assetNames) -ne ('OblivionVoice_Mouth.utoc' -in $assetNames)) { throw 'Mouth IoStore assets require both .ucas and .utoc.' }
        $assetReceipt = Get-Content -LiteralPath (Join-Path $assetRoot 'receipt.json') -Raw | ConvertFrom-Json
        if (@($assetReceipt.hashes.PSObject.Properties).Count -ne $assetNames.Count) { throw 'Mouth asset receipt does not match the available asset files.' }
        foreach ($entry in $assetReceipt.hashes.PSObject.Properties) {
            if ($entry.Name -notin $assetNames -or $entry.Value -notmatch '^[0-9a-fA-F]{64}$') { throw "Unexpected mouth asset receipt entry: $($entry.Name)" }
            $hash = (Get-FileHash -LiteralPath (Join-Path $assetRoot $entry.Name) -Algorithm SHA256).Hash
            if ($hash -ne $entry.Value) { throw "Mouth asset validation failed: $($entry.Name)" }
            $assetHashes[$entry.Name] = $hash
            $changedFiles += "client/$($entry.Name)"
        }
    }
}

# This build intentionally never invokes BUILD.ps1, which recreates Output.
Run-DotNet @('build', (Join-Path $root 'OblivionVoice.Client/OblivionVoice.Client.csproj'), '-c', 'Release', '--nologo')
$tests = @(Get-ChildItem -LiteralPath (Join-Path $root 'Tests') -Filter '*.csproj' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | Select-Object -ExpandProperty FullName)
foreach ($requiredFolder in @('MouthAudio', 'MouthAnimation')) {
    if (@($tests | Where-Object { $_.StartsWith((Join-Path $root "Tests/$requiredFolder") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
        throw "Required mouth animation checks missing: Tests/$requiredFolder."
    }
}
if ($tests.Count -lt 6) { throw 'Expected the existing voice, bootstrap, preferences, key bindings, mouth audio, and mouth animation checks.' }
foreach ($test in $tests) { Run-DotNet @('run', '--project', $test, '-c', 'Release', '--nologo') }

$clientBuild = Join-Path $root 'OblivionVoice.Client/bin/Release/net10.0'
$clientDll = Join-Path $clientBuild 'OblivionVoice.Client.dll'
if (!(Test-Path -LiteralPath $clientDll -PathType Leaf)) { throw 'Built Voice client DLL is missing.' }
$requiredDependencies = @('OblivionVoice.Common.dll', 'Concentus.dll', 'NAudio.Core.dll', 'NAudio.Wasapi.dll', 'System.Numerics.Tensors.dll')
$hostProvided = @('ReadyM.*', 'OblivionUI.Api*', 'OblivionMp.Sdk*', 'OblivionMpCSharpMod*', 'Friflo.*', 'LiteNetLib*', 'Microsoft.Extensions.Logging.Abstractions*', 'Microsoft.Extensions.DependencyInjection*', 'Yooni.*', 'DryIoc*')
$requiredDependencies += @(Get-ChildItem -LiteralPath (Join-Path $installed 'client') -Filter '*.dll' -File | Where-Object { $_.Name -ne 'OblivionVoice.Client.dll' } | Select-Object -ExpandProperty Name)
foreach ($dll in Get-ChildItem -LiteralPath $clientBuild -Filter '*.dll' -File) {
    if ($dll.Name -eq 'OblivionVoice.Client.dll') { continue }
    $isHostProvided = $false
    foreach ($pattern in $hostProvided) { if ($dll.Name -like $pattern) { $isHostProvided = $true; break } }
    if (!$isHostProvided) { $requiredDependencies += $dll.Name }
}
$dependencyHashes = [ordered]@{}
foreach ($name in $requiredDependencies | Sort-Object -Unique) {
    $builtPath = Join-Path $clientBuild $name
    $installedPath = Join-Path $installed "client/$name"
    if (!(Test-Path -LiteralPath $builtPath -PathType Leaf) -or !(Test-Path -LiteralPath $installedPath -PathType Leaf)) {
        throw "Dependency is missing in the build or installed baseline: $name. This client-only release cannot change dependencies."
    }
    $builtHash = (Get-FileHash -LiteralPath $builtPath -Algorithm SHA256).Hash
    if ($builtHash -ne (Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash) {
        throw "Dependency changed: $name. This client-only release preserves installed dependencies; investigate before packaging."
    }
    $dependencyHashes[$name] = $builtHash
}
if ((Get-FileHash -LiteralPath (Join-Path $installed 'server/OblivionVoice.Common.dll') -Algorithm SHA256).Hash -ne $dependencyHashes['OblivionVoice.Common.dll']) {
    throw 'Built Common differs from the installed server RPC contract. No package was created.'
}
Assert-Hashes $baseline (Hash-Tree $installed) 'Installed Voice baseline'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$bundle = Join-Path $root "Output/mouth-package-$stamp"
if (Test-Path -LiteralPath $bundle) { throw "Package directory already exists: $bundle" }
$package = Join-Path $bundle 'mods/OblivionVoice'
New-Item -ItemType Directory -Path $package -Force | Out-Null
# Copy only the reviewed installed files. The exact generated trace log is omitted.
foreach ($name in $baseline.Keys) {
    $destination = Join-Path $package $name
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $installed $name) -Destination $destination
}
Assert-Hashes $baseline (Hash-Tree $package) 'Packaged baseline'
Copy-Item -LiteralPath $clientDll -Destination (Join-Path $package 'client/OblivionVoice.Client.dll') -Force
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $package 'manifest.json') -Force
foreach ($name in $assetHashes.Keys) {
    Copy-Item -LiteralPath (Join-Path $assetRoot $name) -Destination (Join-Path $package "client/$name") -Force
}
$hashes = Hash-Tree $package
$expectedNames = @(@($baseline.Keys) + $changedFiles | Sort-Object -Unique)
if (@(Compare-Object $expectedNames @($hashes.Keys)).Count -ne 0) { throw 'Unexpected package file set.' }
foreach ($name in $baseline.Keys) {
    if ($name -notin $changedFiles -and $hashes[$name] -ne $baseline[$name]) { throw "Unintended package change: $name" }
}
foreach ($name in $assetHashes.Keys) { if ($hashes["client/$name"] -ne $assetHashes[$name]) { throw "Packaged mouth asset hash mismatch: $name" } }
$receipt = [pscustomobject]@{
    schemaVersion = 1
    release = 'mouth-animation'
    version = $version
    baselineVersion = $installedManifest.version
    installedBaseline = $installed
    package = $package
    createdAt = [DateTimeOffset]::Now.ToString('o')
    configuration = 'Release'
    tests = $tests
    changedFiles = $changedFiles
    assetHashes = $assetHashes
    excludedGeneratedFile = $generatedRelative
    dependencyHashes = $dependencyHashes
    baselineHashes = $baseline
    hashes = $hashes
}
$receiptJson = $receipt | ConvertTo-Json -Depth 8
$receiptJson | Set-Content -LiteralPath (Join-Path $bundle 'receipt.json') -Encoding utf8
$receiptJson | Set-Content -LiteralPath (Join-Path $root 'Output/mouth-package-last.json') -Encoding utf8
Write-Output "Verified OblivionVoice $version package: $package"
Write-Output "Passed $($tests.Count) test projects; dependencies and server content match the installed baseline."
