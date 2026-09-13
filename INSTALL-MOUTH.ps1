param(
    [Parameter(Mandatory = $true)][string]$ServerRoot,
    [string]$CacheRoot = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'ReadyM.Launcher/Oblivion/Mods/OblivionMp/dlls/Mods'),
    [string]$ReceiptPath = (Join-Path $PSScriptRoot 'Output/mouth-package-last.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$ServerRoot = [IO.Path]::GetFullPath($ServerRoot)
$CacheRoot = [IO.Path]::GetFullPath($CacheRoot)
$serverMods = Join-Path $ServerRoot 'mods'
$serverMod = [IO.Path]::GetFullPath((Join-Path $serverMods 'OblivionVoice'))
$cacheMod = [IO.Path]::GetFullPath((Join-Path $CacheRoot 'OblivionVoice'))
$serverExe = Join-Path $ServerRoot 'server.exe'
$generatedRelative = 'server/OblivionVoice.server.trace.log'
$generatedFile = [IO.Path]::GetFullPath((Join-Path $serverMod $generatedRelative))
$requiredChangedFiles = @('client/OblivionVoice.Client.dll', 'manifest.json')
$allowedAssetNames = @('OblivionVoice_Mouth.pak', 'OblivionVoice_Mouth.ucas', 'OblivionVoice_Mouth.utoc')
$dataRoot = Join-Path $ServerRoot 'data'
# These exact SQLite files belong to the running server. Hash all data while the
# server is stopped, then exclude only its open database and sidecars after restart.
$runtimeDataNames = @('web.db', 'web.db-wal', 'web.db-shm')
$afterRestartExcludedRuntimeData = @($runtimeDataNames | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $dataRoot $_)) })

function Assert-NoLinks([string]$Directory) {
    if (!(Test-Path -LiteralPath $Directory -PathType Container)) { throw "Directory missing: $Directory" }
    foreach ($item in @((Get-Item -LiteralPath $Directory -Force)) + @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked installation content: $($item.FullName)" }
    }
}
function Hash-Tree([string]$Directory, [string[]]$ExcludedFiles = @(), [string]$ExcludedDirectory = '') {
    $hashes = @{}
    if (Test-Path -LiteralPath $Directory) {
        foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force) {
            if ($file.FullName -in $ExcludedFiles) { continue }
            if ($ExcludedDirectory -and $file.FullName.StartsWith($ExcludedDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
            $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $hashes
}
function Convert-HashMap($Value) {
    $map = @{}
    foreach ($entry in $Value.PSObject.Properties) {
        if ([IO.Path]::IsPathRooted($entry.Name) -or $entry.Name -match '(^|[\\/])\.\.([\\/]|$)' -or $entry.Name.Contains(':')) { throw "Unsafe receipt file name: $($entry.Name)" }
        if ($entry.Value -notmatch '^[0-9a-fA-F]{64}$') { throw "Invalid receipt hash: $($entry.Name)" }
        $map[$entry.Name] = [string]$entry.Value
    }
    return $map
}
function Compare-Hashes($Before, $After, [string]$Label) {
    if ($Before.Count -ne $After.Count) { throw "$Label file count changed." }
    foreach ($name in $Before.Keys) {
        if (!$After.ContainsKey($name) -or $After[$name] -ne $Before[$name]) { throw "$Label changed: $name" }
    }
}
function Get-OtherModHashes {
    $result = @{}
    $serverHashes = Hash-Tree $serverMods @($generatedFile) $serverMod
    foreach ($name in $serverHashes.Keys) { $result["server/$name"] = $serverHashes[$name] }
    $cacheHashes = Hash-Tree $CacheRoot @() $cacheMod
    foreach ($name in $cacheHashes.Keys) { $result["cache/$name"] = $cacheHashes[$name] }
    return $result
}
function Game-Running { return @(Get-Process -Name 'OblivionRemastered-Win64-Shipping' -ErrorAction SilentlyContinue).Count -gt 0 }
function Write-ServerPidRecords([int]$ProcessId) {
    $workspace = Split-Path $root -Parent
    foreach ($relative in @('OblivionDevTools/Output/server.pid', 'OblivionWoodcutting/Output/server.pid', 'ArenaMod/Output/server-entry.pid', 'OblivionVoice/Output/server-book.pid')) {
        $pidPath = Join-Path $workspace $relative
        if (Test-Path -LiteralPath (Split-Path $pidPath -Parent)) { Set-Content -LiteralPath $pidPath -Value $ProcessId }
    }
}
function Stop-MatchingServer($Process) {
    $Process.Refresh()
    if (!$Process.HasExited) {
        if ($Process.Path -ne $serverExe) { throw "Refusing to stop a process outside $serverExe." }
        $Process | Stop-Process
        $Process.WaitForExit()
    }
}
function Wait-VoiceReady($Process, [string]$Log, [string]$ExpectedVersion) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 250
        $Process.Refresh()
        if ($Process.HasExited) { throw "Server exited with code $($Process.ExitCode). Inspect $Log" }
        if (Test-Path -LiteralPath $Log) {
            $versionReady = Select-String -LiteralPath $Log -Pattern ('OblivionVoice v' + [regex]::Escape($ExpectedVersion) + '(?:,|\s|$)') -Quiet
            $serverReady = Select-String -LiteralPath $Log -SimpleMatch 'Running server on port' -Quiet
            if ($versionReady -and $serverReady) { return }
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Voice $ExpectedVersion package advertisement and server startup were not confirmed. Inspect $Log"
}

$receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
if ($receipt.schemaVersion -ne 1 -or $receipt.release -ne 'mouth-animation' -or $receipt.version -ne '0.6.4') { throw 'Expected the verified mouth-animation 0.6.4 receipt.' }
$assetHashes = Convert-HashMap $receipt.assetHashes
foreach ($name in $assetHashes.Keys) { if ($name -notin $allowedAssetNames) { throw "Unexpected mouth asset: $name" } }
if (!$assetHashes.ContainsKey('OblivionVoice_Mouth.pak')) { throw 'Mouth asset set is missing its required .pak file.' }
if ($assetHashes.ContainsKey('OblivionVoice_Mouth.ucas') -ne $assetHashes.ContainsKey('OblivionVoice_Mouth.utoc')) { throw 'Mouth IoStore assets require both .ucas and .utoc.' }
$changedFiles = @($requiredChangedFiles) + @($assetHashes.Keys | ForEach-Object { "client/$_" })
if ($receipt.excludedGeneratedFile -ne $generatedRelative -or @(Compare-Object $changedFiles @($receipt.changedFiles)).Count -ne 0) { throw 'The receipt describes an unexpected installation scope.' }
$source = [IO.Path]::GetFullPath($receipt.package)
$output = [IO.Path]::GetFullPath((Join-Path $root 'Output'))
if (!$source.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $source -notmatch '[\\/]mouth-package-[^\\/]+[\\/]mods[\\/]OblivionVoice$') { throw 'Package must be a mouth-package under this project Output directory.' }
if ([IO.Path]::GetFullPath($receipt.installedBaseline) -ne $serverMod) { throw 'Receipt baseline does not match the requested server Voice directory.' }
if (!(Test-Path -LiteralPath $serverExe -PathType Leaf)) { throw "Server executable missing: $serverExe" }
Assert-NoLinks $source
Assert-NoLinks $serverMod
if ((Get-Item -LiteralPath $serverMods -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Server mods directory is a link.' }
if (Test-Path -LiteralPath $cacheMod) { Assert-NoLinks $cacheMod }
$hashes = Convert-HashMap $receipt.hashes
$baseline = Convert-HashMap $receipt.baselineHashes
$expectedNames = @(@($baseline.Keys) + $changedFiles | Sort-Object -Unique)
if (@(Compare-Object $expectedNames @($hashes.Keys)).Count -ne 0) { throw 'Package contains an unexpected file set.' }
foreach ($name in $baseline.Keys) {
    if (!$hashes.ContainsKey($name) -or ($name -notin $changedFiles -and $hashes[$name] -ne $baseline[$name])) { throw "Unintended package change: $name" }
}
foreach ($name in $assetHashes.Keys) { if ($hashes["client/$name"] -ne $assetHashes[$name]) { throw "Mouth asset receipt mismatch: $name" } }
Compare-Hashes $hashes (Hash-Tree $source) 'Verified package'
Compare-Hashes $baseline (Hash-Tree $serverMod @($generatedFile)) 'Installed Voice baseline; rebuild the package if the baseline has changed'
$manifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.uniqueId -ne 'OblivionVoice' -or $manifest.version -ne '0.6.4') { throw 'Unexpected package manifest.' }
foreach ($dependency in $manifest.dependencies) {
    $found = @(Get-ChildItem -LiteralPath $serverMods -Directory | ForEach-Object {
        $path = Join-Path $_.FullName 'manifest.json'
        if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    } | Where-Object { $_.uniqueId -eq $dependency.uniqueId -and [version]$_.version -ge [version]$dependency.minimumVersion })
    if ($found.Count -eq 0) { throw "Required installed mod missing: $($dependency.uniqueId) $($dependency.minimumVersion)" }
}
$running = @(Get-Process -Name server -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $serverExe })
if ($running.Count -gt 1) { throw 'Multiple matching OBMP servers are running; installation stopped before modifying files.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backup = Join-Path $root "Backups/mouth-install-$stamp"
if (Test-Path -LiteralPath $backup) { throw "Backup directory already exists: $backup" }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$started = $null
$serverStopped = $false
$serverChanges = [Collections.Generic.List[string]]::new()
$cacheChanges = [Collections.Generic.List[string]]::new()
$cacheRemovals = [Collections.Generic.List[string]]::new()
$cacheInstalled = $false
$cacheReason = 'Game is running; cached Voice files were left intact.'
$cacheBefore = $null
try {
    foreach ($process in $running) { Stop-MatchingServer $process }
    $serverStopped = $true
    Compare-Hashes $baseline (Hash-Tree $serverMod @($generatedFile)) 'Installed Voice baseline'
    Copy-Item -LiteralPath $serverMod -Destination (Join-Path $backup 'server-mod') -Recurse
    $otherBefore = Get-OtherModHashes
    $progressBefore = Hash-Tree $dataRoot
    $progressAfterRestartBefore = @{}
    foreach ($name in $progressBefore.Keys) {
        if ($name -notin $runtimeDataNames) { $progressAfterRestartBefore[$name] = $progressBefore[$name] }
    }
    $configPath = Join-Path $ServerRoot 'config.json'
    $configHash = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash
    $cacheBefore = Hash-Tree $cacheMod
    @{ OtherMods = $otherBefore; Progress = $progressBefore; ServerConfig = $configHash; Voice = $baseline; VoiceCache = $cacheBefore; ExcludedGeneratedModFile = $generatedFile; afterRestartExcludedRuntimeData = $afterRestartExcludedRuntimeData } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'preservation-hashes.json') -Encoding utf8

    # Write only the client DLL, manifest, and explicitly receipted mouth assets.
    foreach ($name in $changedFiles) {
        $serverChanges.Add($name)
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $serverMod $name) -Force
    }
    if (!(Game-Running)) {
        if (Test-Path -LiteralPath $cacheMod -PathType Container) {
            Copy-Item -LiteralPath $cacheMod -Destination (Join-Path $backup 'cache-mod') -Recurse
            $cacheTargets = @('manifest.json')
            $hasFlatClient = Test-Path -LiteralPath (Join-Path $cacheMod 'OblivionVoice.Client.dll') -PathType Leaf
            foreach ($name in @('OblivionVoice.Client.dll', 'client/OblivionVoice.Client.dll')) {
                if (Test-Path -LiteralPath (Join-Path $cacheMod $name) -PathType Leaf) {
                    $cacheTargets += $name
                }
            }
            if ($cacheTargets.Count -eq 1) { throw 'Existing Voice cache has no recognized client DLL layout.' }
            # Update every existing DLL layout, but mount the mouth container once.
            $primaryAssetPrefix = if ($hasFlatClient) { '' } else { 'client/' }
            $duplicateAssetPrefix = if ($hasFlatClient) { 'client/' } else { '' }
            foreach ($assetName in $assetHashes.Keys) { $cacheTargets += "$primaryAssetPrefix$assetName" }
            $duplicateAssets = @()
            foreach ($assetName in $allowedAssetNames) {
                $duplicateName = "$duplicateAssetPrefix$assetName"
                $duplicatePath = Join-Path $cacheMod $duplicateName
                if (Test-Path -LiteralPath $duplicatePath -PathType Leaf) {
                    if (!$assetHashes.ContainsKey($assetName) -or (Get-FileHash -LiteralPath $duplicatePath -Algorithm SHA256).Hash -ne $assetHashes[$assetName]) {
                        throw "Existing duplicate mouth asset does not match the verified receipt: $duplicateName"
                    }
                    $duplicateAssets += $duplicateName
                }
            }
            # Check again immediately before any cache write; never stop the game.
            if (!(Game-Running)) {
                foreach ($name in $cacheTargets) {
                    $cacheChanges.Add($name)
                    $packageName = if ($name -eq 'manifest.json') { $name } else { 'client/' + [IO.Path]::GetFileName($name) }
                    Copy-Item -LiteralPath (Join-Path $source $packageName) -Destination (Join-Path $cacheMod $name) -Force
                }
                foreach ($name in $duplicateAssets) {
                    $duplicatePath = [IO.Path]::GetFullPath((Join-Path $cacheMod $name))
                    if (!$duplicatePath.StartsWith($cacheMod + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing duplicate asset cleanup outside the Voice cache.' }
                    $cacheChanges.Add($name)
                    $cacheRemovals.Add($name)
                    Remove-Item -LiteralPath $duplicatePath -Force
                }
                $cacheInstalled = $true
                $cacheReason = 'Updated existing Voice DLL layouts and installed mouth assets once in the primary cache layout.'
            }
        } else { $cacheReason = 'No existing Voice cache; the launcher will download the served client.' }
    }
    Compare-Hashes $hashes (Hash-Tree $serverMod @($generatedFile)) 'Installed Voice package'
    $cacheExpected = @{}
    foreach ($name in $cacheBefore.Keys) { $cacheExpected[$name] = $cacheBefore[$name] }
    foreach ($name in $cacheChanges) { $cacheExpected[$name] = if ($name -eq 'manifest.json') { $hashes['manifest.json'] } else { $hashes['client/' + [IO.Path]::GetFileName($name)] } }
    foreach ($name in $cacheRemovals) { $cacheExpected.Remove($name) }
    Compare-Hashes $cacheExpected (Hash-Tree $cacheMod) 'Voice cache preservation'
    Compare-Hashes $otherBefore (Get-OtherModHashes) 'Other installed mods'
    Compare-Hashes $progressBefore (Hash-Tree $dataRoot) 'Server progress before restart'
    if ((Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash -ne $configHash) { throw 'Server config changed.' }

    $out = Join-Path $root "Output/server-mouth-$stamp.log"
    $err = Join-Path $root "Output/server-mouth-$stamp-error.log"
    $started = Start-Process -FilePath $serverExe -WorkingDirectory $ServerRoot -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    Write-ServerPidRecords $started.Id
    Wait-VoiceReady $started $out '0.6.4'
    Compare-Hashes $hashes (Hash-Tree $serverMod @($generatedFile)) 'Voice package after restart'
    Compare-Hashes $otherBefore (Get-OtherModHashes) 'Other installed mods after restart'
    Compare-Hashes $progressAfterRestartBefore (Hash-Tree $dataRoot $afterRestartExcludedRuntimeData) 'Server progress after restart'
    if ((Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash -ne $configHash) { throw 'Server config changed after restart.' }
    $result = [pscustomobject]@{
        version = '0.6.4'; installed = $true; serverPid = $started.Id; serverLog = $out; serverErrorLog = $err
        cacheInstalled = $cacheInstalled; cacheStatus = $cacheReason; relaunchRequired = $true; backup = $backup
        changedServerFiles = $changedFiles; changedCacheFiles = @($cacheChanges.ToArray())
        removedDuplicateCacheAssets = @($cacheRemovals.ToArray())
        otherModFilesPreserved = $otherBefore.Count; progressFilesPreserved = $progressBefore.Count
        afterRestartProgressFilesPreserved = $progressAfterRestartBefore.Count; afterRestartExcludedRuntimeData = $afterRestartExcludedRuntimeData
        voiceConfigPreserved = $true; serverConfigPreserved = $true; excludedGeneratedModFile = $generatedFile
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'Output/mouth-install-last.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 5
} catch {
    $failure = $_
    if ($null -ne $started) { Stop-MatchingServer $started }
    # Restore only files this installer touched, leaving the existing trace log intact.
    foreach ($name in $serverChanges) {
        $saved = Join-Path $backup "server-mod/$name"
        $destination = [IO.Path]::GetFullPath((Join-Path $serverMod $name))
        if (!$destination.StartsWith($serverMod + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing rollback outside the server Voice directory.' }
        if (Test-Path -LiteralPath $saved -PathType Leaf) { Copy-Item -LiteralPath $saved -Destination $destination -Force }
        elseif ($name -in @($allowedAssetNames | ForEach-Object { "client/$_" })) {
            if (Test-Path -LiteralPath $destination -PathType Leaf) { Remove-Item -LiteralPath $destination -Force }
        }
        else { throw "Rollback backup is missing: $name" }
    }
    foreach ($name in $cacheChanges) {
        $saved = Join-Path $backup "cache-mod/$name"
        $destination = [IO.Path]::GetFullPath((Join-Path $cacheMod $name))
        if (!$destination.StartsWith($cacheMod + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing rollback outside the Voice cache.' }
        if (Test-Path -LiteralPath $saved -PathType Leaf) { Copy-Item -LiteralPath $saved -Destination $destination -Force }
        elseif (Test-Path -LiteralPath $destination -PathType Leaf) { Remove-Item -LiteralPath $destination -Force }
    }
    if ($serverChanges.Count -gt 0) { Compare-Hashes $baseline (Hash-Tree $serverMod @($generatedFile)) 'Rolled-back Voice package' }
    if ($cacheChanges.Count -gt 0) { Compare-Hashes $cacheBefore (Hash-Tree $cacheMod) 'Rolled-back Voice cache' }
    if ($serverStopped -and $running.Count -gt 0) {
        $rollbackOut = Join-Path $root "Output/server-mouth-rollback-$stamp.log"
        $rollbackErr = Join-Path $root "Output/server-mouth-rollback-$stamp-error.log"
        $rollback = Start-Process -FilePath $serverExe -WorkingDirectory $ServerRoot -WindowStyle Hidden -RedirectStandardOutput $rollbackOut -RedirectStandardError $rollbackErr -PassThru
        Write-ServerPidRecords $rollback.Id
        try { Wait-VoiceReady $rollback $rollbackOut $receipt.baselineVersion }
        catch { throw "Mouth installation failed and rollback startup could not be verified. Original failure: $($failure.Exception.Message). Rollback log: $rollbackOut" }
        Write-Warning "Mouth installation rolled back; previous server restarted as PID $($rollback.Id). Backup: $backup"
    }
    throw $failure
}
