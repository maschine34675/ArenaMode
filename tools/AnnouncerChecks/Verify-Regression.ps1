$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AnnouncerChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\AnnouncerService.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$sourceHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaAnnouncerMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase([string]$name, [string]$candidate, [string]$expectedFailure) {
    $copyPath = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copyPath, $candidate)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release "-p:AnnouncerServiceSource=$copyPath" "-p:OutputPath=$outputPath" "-p:IntermediateOutputPath=$intermediatePath" 2>&1
    $resultCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    if ($resultCode -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($expectedFailure)) {
        throw "$name did not fail at the expected recovery boundary (exit $resultCode). Evidence: $scratch"
    }
}

$loadingResetPattern = '_loading = false;\r?\n\s+_nextRecoveryAt = Time\.realtimeSinceStartup \+ RecoveryIntervalSeconds;'
$loadingResetMatches = [regex]::Matches($source, $loadingResetPattern)
if ($loadingResetMatches.Count -ne 1) {
    throw 'Expected exactly one loading-state reset; refusing an ambiguous source-copy mutation.'
}
$loadingReset = $loadingResetMatches[0].Value
$loadingResetMutant = $loadingReset.Replace('_loading = false;', '_loading = true;')
Invoke-NegativeCase 'load-recovery' ($source.Replace($loadingReset, $loadingResetMutant)) `
    'A failed first load must retry only its failed clip; healthy cached clips must survive.'

$forbiddenContracts = @(
    'System.Reflection',
    'BindingFlags',
    'AudioSource',
    'BetterAudio',
    '_outputCheck',
    'LeaseUiPriority',
    'RestoreUiPriority',
    'InterfaceVolume'
)
foreach ($forbidden in $forbiddenContracts) {
    if ($source.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "Native UI-only source contract still contains forbidden machinery: $forbidden"
    }
}

$nativeCall = 'Singleton<GUISounds>.Instance.PlaySound('
if ([regex]::Matches($source, [regex]::Escape($nativeCall)).Count -ne 1) {
    throw 'Expected exactly one direct native GUISounds.PlaySound call.'
}

$requiredLifetimeContracts = @(
    'float[] Samples;',
    'CachedClip cached = TryCapturePcm(decoded, key, out string pcmProblem);',
    'if (!TryGetRuntimeClip(cached, out AudioClip clip, out string clipProblem))',
    'clip.samples == cached.Frames',
    'clip.channels == cached.Channels'
)
foreach ($required in $requiredLifetimeContracts) {
    if (-not $source.Contains($required, [StringComparison]::Ordinal)) {
        throw "Managed PCM lifetime contract missing: $required"
    }
}
if ($source.Contains('retainDecoded', [StringComparison]::Ordinal)) {
    throw 'Decoder-owned clips must not remain as the durable cache.'
}

$runtimeValidityGuard = 'if (IsRuntimeClipValid(cached, clip))'
if ([regex]::Matches($source, [regex]::Escape($runtimeValidityGuard)).Count -ne 1) {
    throw 'Expected exactly one runtime-clip validity guard; refusing an ambiguous reset mutation.'
}
Invoke-NegativeCase 'audio-reset-rebuild' `
    ($source.Replace($runtimeValidityGuard, 'if (!ReferenceEquals(clip, null))')) `
    'The next cue synchronously reconstructs a signal-bearing runtime clip from managed PCM.'

$managedSetData = 'if (!clip.SetData(cached.Samples, 0))'
if ([regex]::Matches($source, [regex]::Escape($managedSetData)).Count -ne 1) {
    throw 'Expected exactly one managed PCM SetData call; refusing an ambiguous PCM mutation.'
}
Invoke-NegativeCase 'managed-pcm-rebuild' `
    ($source.Replace($managedSetData, 'if (!clip.SetData(new float[cached.Samples.Length], 0))')) `
    'The first match must play a subsequently loaded clip without reattaching.'

$loadEpochContract = '=> loadEpoch == _loadEpoch'
if ([regex]::Matches($source, [regex]::Escape($loadEpochContract)).Count -ne 1) {
    throw 'Expected exactly one load-epoch comparison; refusing an ambiguous shutdown-race mutation.'
}
Invoke-NegativeCase 'shutdown-load-epoch' `
    ($source.Replace($loadEpochContract, '=> true || loadEpoch == _loadEpoch')) `
    'A stale completion neither republishes managed PCM nor leaves the loader stuck after Shutdown.'

$singleFlag = 'single: true,'
if ([regex]::Matches($source, [regex]::Escape($singleFlag)).Count -ne 1) {
    throw 'Expected exactly one native single:true flag; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'native-single-contract' ($source.Replace($singleFlag, 'single: false,')) `
    'Native UI primary must request GUISounds single:true replacement semantics.'

$commonUiFlag = 'commonUiSound: false,'
if ([regex]::Matches($source, [regex]::Escape($commonUiFlag)).Count -ne 1) {
    throw 'Expected exactly one native commonUiSound:false flag; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'native-mixer-contract' ($source.Replace($commonUiFlag, 'commonUiSound: true,')) `
    'Native UI primary must request commonUiSound:false and preserve explicit volume.'

$resultQueueGate = 'if (Time.realtimeSinceStartup < _calloutBusyUntil)'
if ([regex]::Matches($source, [regex]::Escape($resultQueueGate)).Count -ne 1) {
    throw 'Expected exactly one result-queue admission gate; refusing an ambiguous queue mutation.'
}
Invoke-NegativeCase 'result-queue-admission' ($source.Replace($resultQueueGate, 'if (false)')) `
    'A result cue arriving behind a kill callout must not replace it immediately.'

$calloutDeadline = 'Time.realtimeSinceStartup + clipLengthSeconds + ResultCueGapSeconds'
if ([regex]::Matches($source, [regex]::Escape($calloutDeadline)).Count -ne 1) {
    throw 'Expected exactly one clip-length result deadline; refusing an ambiguous duration mutation.'
}
Invoke-NegativeCase 'result-queue-duration' `
    ($source.Replace($calloutDeadline, 'Time.realtimeSinceStartup + ResultCueGapSeconds')) `
    'The result queue must preserve a short post-callout gap before replacing the shared source.'

$resultQueueDrainPattern = 'if \(_pendingResultKey == null \|\| _match == null\r?\n\s+\|\| Time\.realtimeSinceStartup < _calloutBusyUntil\)'
$resultQueueDrainMatches = [regex]::Matches($source, $resultQueueDrainPattern)
if ($resultQueueDrainMatches.Count -ne 1) {
    throw 'Expected exactly one result-queue drain gate; refusing an ambiguous queue mutation.'
}
$resultQueueDrain = $resultQueueDrainMatches[0].Value
Invoke-NegativeCase 'result-queue-drain' ($source.Replace($resultQueueDrain, 'if (true)')) `
    'The queued result plays exactly once after the real callout duration and guard gap.'

$detachCancellation = 'CancelPendingResult("match detached");'
if ([regex]::Matches($source, [regex]::Escape($detachCancellation)).Count -ne 1) {
    throw 'Expected exactly one detach cancellation; refusing an ambiguous lifecycle mutation.'
}
Invoke-NegativeCase 'result-queue-detach' ($source.Replace($detachCancellation, '')) `
    'Detach cancels an old match''s queued result before a replacement match can drain it.'

$roundStartCancellation = 'CancelPendingResult("new round started");'
if ([regex]::Matches($source, [regex]::Escape($roundStartCancellation)).Count -ne 1) {
    throw 'Expected exactly one round-start cancellation; refusing an ambiguous lifecycle mutation.'
}
Invoke-NegativeCase 'result-queue-round-start' ($source.Replace($roundStartCancellation, '')) `
    'A result canceled by round start cannot leak into the live round.'

$finalHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
if ($sourceHash -ne $finalHash) { throw 'Production source changed during negative verification.' }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'The unchanged production service failed its final positive check.' }
Write-Output "ARENA_ANNOUNCER_REGRESSION_VERIFIED (load, reset-rebuild, managed-PCM, shutdown-epoch, native-route and result-queue timing/lifecycle mutants rejected; production source unchanged; evidence: $scratch)"
