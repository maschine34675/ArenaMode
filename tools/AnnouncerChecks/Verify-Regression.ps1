$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AnnouncerChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\AnnouncerService.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$catalogSourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\AnnouncerCueCatalog.cs'
$catalogSource = [System.IO.File]::ReadAllText($catalogSourcePath)
$catalogSourceHash = (Get-FileHash -LiteralPath $catalogSourcePath -Algorithm SHA256).Hash
$outputSourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\ArenaAnnouncerOutput.cs'
$outputSource = [System.IO.File]::ReadAllText($outputSourcePath)
$outputSourceHash = (Get-FileHash -LiteralPath $outputSourcePath -Algorithm SHA256).Hash
$sourceHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaAnnouncerMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase([string]$name, [string]$candidate, [string]$expectedFailure, [string]$property = 'AnnouncerServiceSource') {
    $copyPath = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copyPath, $candidate)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release "-p:${property}=$copyPath" "-p:OutputPath=$outputPath" "-p:IntermediateOutputPath=$intermediatePath" 2>&1
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

$resultQueueGatePattern = 'ClearPendingCallout\("round ended"\);\r?\n\s+if \(Time.realtimeSinceStartup < _calloutBusyUntil\)'
$resultQueueMatches = [regex]::Matches($source, $resultQueueGatePattern)
if ($resultQueueMatches.Count -ne 1) {
    throw 'Expected exactly one result-queue admission gate; refusing an ambiguous queue mutation.'
}
$resultQueueGate = $resultQueueMatches[0].Value
Invoke-NegativeCase 'result-queue-admission' ($source.Replace($resultQueueGate,
    $resultQueueGate.Replace('if (Time.realtimeSinceStartup < _calloutBusyUntil)', 'if (false)'))) `
    'A result cue arriving behind a kill callout must not replace it immediately.'

$calloutDeadlinePattern = 'Time\.realtimeSinceStartup \+ clipLengthSeconds\r?\n\s+\+ \(AnnouncerCueCatalog\.IsCountdown\(key\) \? 0f : ResultCueGapSeconds\);'
$deadlineMatch = [regex]::Match($source, $calloutDeadlinePattern)
if (-not $deadlineMatch.Success) { throw 'Expected the duration-based cue deadline.' }
$calloutDeadline = $deadlineMatch.Value
if ([regex]::Matches($source, [regex]::Escape($calloutDeadline)).Count -ne 1) {
    throw 'Expected exactly one clip-length result deadline; refusing an ambiguous duration mutation.'
}
Invoke-NegativeCase 'result-queue-duration' `
    ($source.Replace($calloutDeadline, 'Time.realtimeSinceStartup + ResultCueGapSeconds;')) `
    'The result queue must preserve a short post-callout gap before replacing the shared source.'

$resultQueueDrainPattern = 'if \(_match == null \|\| Time\.realtimeSinceStartup < _calloutBusyUntil\)'
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

Invoke-NegativeCase 'award-queue-admission' ($source.Replace('SubmitCallout(key);', 'StartCallout(key);')) `
    'Rapid awards cannot cut off the spoken First Blood phrase.'

Invoke-NegativeCase 'reserve-recursive-collision' ($catalogSource.Replace(
    'Directory.GetFiles(soundsDirectory, "*.ogg")',
    'Directory.GetFiles(soundsDirectory, "*.ogg", SearchOption.AllDirectories)')) `
    'Runtime loading admits exactly the 19 optional keys, with no recursive alternate or CTF collision.' 'AnnouncerCatalogSource'
Invoke-NegativeCase 'reserve-root-overwrite' ($catalogSource.Replace('!rootKeys.Contains(key) && File.Exists(path)', 'File.Exists(path)')) `
    'An existing root cue wins its basename once, without an optional reserve overwrite.' 'AnnouncerCatalogSource'
Invoke-NegativeCase 'multi-kill-queue-priority' ($source.Replace(
    '"monster_kill" or "multi_kill" or "triple_kill" or "double_kill" => 80,',
    '"monster_kill" or "multi_kill" or "triple_kill" or "double_kill" => 50,')) `
    'A queued multi-kill replaces a pending spree and drains before it.'
Invoke-NegativeCase 'countdown-backlog' ($source.Replace(
    'Skip(key, "countdown_not_currently_playable");', 'SubmitCallout(key);')) `
    'Busy countdown numbers are dropped instead of replaying after their spoken second.'
Invoke-NegativeCase 'status-deadline' ($source.Replace('StatusMaxWaitSeconds = 2f;', 'StatusMaxWaitSeconds = 20f;')) `
    'A status delayed beyond two seconds expires without consuming the result slot.'
Invoke-NegativeCase 'status-cancellation' ($source.Replace(
    'if (AnnouncerCueCatalog.IsStatus(_pendingCalloutKey)) ClearPendingCallout(reason);',
    'if (false) ClearPendingCallout(reason);')) `
    'Pending status is canceled by its lifecycle boundary: CancelStatus'
Invoke-NegativeCase 'director-before-drain' ($source.Replace('ArenaAnnouncementDirector.Tick();', '')) `
    'Director invalidation runs before the audio queue can release stale status.'
Invoke-NegativeCase 'clutch-fallback' ($source.Replace('string fallbackKey = clutchWin ? key : null;', 'string fallbackKey = (string)null;')) `
    'A missing optional Clutch preserves the established win recording.'
Invoke-NegativeCase 'clutch-frame-boundary' ($source.Replace('_clutchFrame == Time.frameCount', 'true')) `
    'An unpaired or losing outcome cannot inherit a Clutch audio hint: 1'
Invoke-NegativeCase 'status-owned-cancellation' ($source.Replace(
    'if (!Settings.AnnouncerUseUiVolume.Value) ArenaAnnouncerOutput.Reset();', '')) `
    'Readiness loss cancels playing owned countdown/status without touching shared GUI audio.'
Invoke-NegativeCase 'countdown-extra-gap' ($source.Replace(
    '(AnnouncerCueCatalog.IsCountdown(key) ? 0f : ResultCueGapSeconds)', 'ResultCueGapSeconds')) `
    'Pack-length countdown clips fit each actual second without an inherited combat-callout gap.'
Invoke-NegativeCase 'zero-volume-owned-cancellation' ($source.Replace(
    '&& Settings.AnnouncerVolume.Value > 0f && !Settings.AnnouncerUseUiVolume.Value',
    '&& !Settings.AnnouncerUseUiVolume.Value')) `
    'Canceled owned countdown cannot revive after mute, zero volume or listener pause: 0'

Invoke-NegativeCase 'independent-master-route' ($outputSource.Replace('FindMatchingGroups("Master")', 'FindMatchingGroups("UI")')) `
    'Independent output uses an owned source on the native host''s exact Master branch.' 'AnnouncerOutputSource'
Invoke-NegativeCase 'independent-listener-pause' ($outputSource.Replace('_source.ignoreListenerPause = false;', '_source.ignoreListenerPause = true;')) `
    'Independent output honors its volume, remains 2D and cannot bypass listener mute/pause.' 'AnnouncerOutputSource'
Invoke-NegativeCase 'independent-foreign-mixer' ($outputSource.Replace('!ReferenceEquals(candidate.audioMixer, host.MasterMixer) || master != null', 'master != null')) `
    'Missing, substring-only, foreign or ambiguous Master groups fail soft without fallback.' 'AnnouncerOutputSource'
Invoke-NegativeCase 'independent-ambiguous-master' ($outputSource.Replace(' || master != null', '')) `
    'Missing, substring-only, foreign or ambiguous Master groups fail soft without fallback.' 'AnnouncerOutputSource'
Invoke-NegativeCase 'independent-hot-volume' ($outputSource.Replace('_source.volume = Volume(volume); // F12 changes', '_source.volume = 0.42f; // F12 changes')) `
    'Announcer volume updates even during a spoken phrase.' 'AnnouncerOutputSource'

if ((Get-FileHash -LiteralPath $outputSourcePath -Algorithm SHA256).Hash -ne $outputSourceHash) {
    throw 'Production output source changed during negative verification.'
}
if ((Get-FileHash -LiteralPath $catalogSourcePath -Algorithm SHA256).Hash -ne $catalogSourceHash) {
    throw 'Production cue catalog changed during negative verification.'
}

$finalHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
if ($sourceHash -ne $finalHash) { throw 'Production source changed during negative verification.' }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'The unchanged production service failed its final positive check.' }
& python (Join-Path $PSScriptRoot 'verify_creator_metadata_faults.py')
if ($LASTEXITCODE -ne 0) { throw 'Creator metadata positive/negative checks failed.' }
& python (Join-Path $PSScriptRoot 'verify_creator_pack.py')
if ($LASTEXITCODE -ne 0) { throw 'Creator asset decoding, hashes or exact runtime mapping failed.' }
Write-Output "ARENA_ANNOUNCER_REGRESSION_VERIFIED (historical recovery/output and expansion collision, priority, countdown, freshness, lifecycle and Clutch mutants rejected; production sources unchanged; evidence: $scratch)"
