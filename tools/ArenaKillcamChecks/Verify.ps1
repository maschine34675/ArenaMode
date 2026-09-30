param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/arena-killcam-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$sources = @{
 Wait = Join-Path $repo 'ArenaMode.Client/Respawn/ArenaReplayWaitPolicy.cs'
 Hold = Join-Path $repo 'ArenaMode.Client/Respawn/ArenaDeathReplayHold.cs'
 Compat = Join-Path $repo 'ArenaMode.Client/Spectator/KillcamReplayCompat.cs'
 Selection = Join-Path $repo 'ArenaMode.Client/Session/ArenaReplaySelection.cs'
 Policy = Join-Path $repo 'ArenaMode.Client/Spectator/SpectatorPolicy.cs'
 Driver = Join-Path $repo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
}
$textByKey = @{}
$hashes = [ordered]@{}
foreach ($key in $sources.Keys) {
 $textByKey[$key] = [IO.File]::ReadAllText($sources[$key])
 $hashes[$sources[$key]] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'source-hashes.json')

# These are injection-point contracts, not a second coroutine implementation.
# The actual hold and ownership reader run in the executable checks below.
$driver = $textByKey.Driver
$routineStart = $driver.IndexOf('private static IEnumerator RespawnRoutine(', [StringComparison]::Ordinal)
$routineEnd = $driver.IndexOf('private static IEnumerator RespawnProtectedTeamPlayer(', [StringComparison]::Ordinal)
if ($routineStart -lt 0 -or $routineEnd -le $routineStart) { throw 'Respawn routine boundaries changed.' }
$routine = $driver.Substring($routineStart, $routineEnd - $routineStart)
$frozen = $routine.IndexOf('if (PitFireTeamCompat.HasFrozenTeamRoster)', [StringComparison]::Ordinal)
$create = $routine.IndexOf('replayHold = new ArenaDeathReplayHold(game, deadPlayer, raidEpoch);', [StringComparison]::Ordinal)
$firstWait = $routine.IndexOf('yield return replayHold.Wait();', [StringComparison]::Ordinal)
$preload = $routine.IndexOf('yield return KitBundlePreloader.Preload(kit);', [StringComparison]::Ordinal)
$secondWait = $routine.IndexOf('yield return replayHold.Wait();', $firstWait + 1, [StringComparison]::Ordinal)
$request = $routine.IndexOf('var requestedSpawn = new ArenaSpawnSelector(', [StringComparison]::Ordinal)
if ($create -lt 0 -or $firstWait -le $create -or $frozen -le $firstWait -or $preload -le $frozen -or
 $secondWait -le $preload -or $request -le $secondWait) {
 throw 'Replay guard must precede protected authorization and ordinary replacement, then revalidate after preload.'
}
if ([regex]::Matches($routine, 'new ArenaDeathReplayHold\(').Count -ne 1 -or
 [regex]::Matches($routine, 'yield return replayHold\.Wait\(\);').Count -ne 2) {
 throw 'Both phases must share one per-death hold and deadline.'
}
if ($routine -notmatch 'ArenaDeathReplayHold replayHold = null;\s*if \(ArenaSession\.ReplaySeconds > 0f\)\s*\{' -or
 $routine -notmatch 'if \(replayHold != null\)\s*\{\s*yield return replayHold\.Wait\(\);') {
 throw 'Disabled replay must not add nested-coroutine delay to the original pipeline.'
}
if ([regex]::Matches($routine, 'yield return replayHold\.Wait\(\);\s*if \(!replayHold\.CanRespawn\)\s*\{\s*EndPipelineUse\(raidEpoch\);\s*yield break;').Count -ne 2) {
 throw 'Each replay wait must reject a cancelled/unsafe hold before replacement.'
}
if (!$routine.Contains('while (Modes.ArenaMatchService.Current?.Mode?.HoldRespawn == true') -or
 !$routine.Contains('while (IsTerminalTdmHold && IsSameActiveRaid(game, raidEpoch) && !StandDown)')) {
 throw 'Existing mode and terminal result hold contracts changed.'
}
if (($textByKey.Hold + $textByKey.Wait) -match 'StopPlayback\s*\(|StartPlayback\s*\(|\.SetValue\s*\(') {
 throw 'Respawn hold must not mutate optional camera state.'
}
Write-Output 'Replay source injection contracts passed.'

$tactical = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.cs'))
$start = $tactical.IndexOf('private void TickTeamRetirement()', [StringComparison]::Ordinal)
$end = $tactical.IndexOf('private TeamRetirementDecision ObserveTeamEnemyRetirement(', $start, [StringComparison]::Ordinal)
$retirement = $tactical.Substring($start, $end - $start)
$cameraGuard = $retirement.IndexOf('if (localDead && ArenaSession.ReplaySeconds > 0f && !KillcamReplayCompat.CameraReleased)', [StringComparison]::Ordinal)
if ($cameraGuard -lt 0 -or $cameraGuard -ge $retirement.IndexOf('_teamLoadoutSnapshot.TryArm(', [StringComparison]::Ordinal) -or
 $retirement.IndexOf('_teamRebindDeadlineAt = Time.time + TeamTransitionSeconds;', [StringComparison]::Ordinal) -lt $cameraGuard) {
 throw 'Protected replacement authorization and its deadline must start after camera release.'
}

$project = Join-Path $PSScriptRoot 'ArenaKillcamChecks.csproj'
function Invoke-Case([string]$name, [string]$kind = '', [string]$source = '') {
 $caseRoot = Join-Path $run $name
 $arguments = @('run', '--project', $project, '-c', 'Release',
  "-p:BaseIntermediateOutputPath=$caseRoot/obj/", "-p:BaseOutputPath=$caseRoot/bin/")
 if ($kind) { $arguments += "-p:Replay${kind}Source=$source" }
 $output = & dotnet @arguments 2>&1
 $code = $LASTEXITCODE
 $joined = $output -join "`n"
 [IO.File]::WriteAllText((Join-Path $run ($name + '.log')), $joined)
 return @{ Code = $code; Text = $joined }
}

$baseline = Invoke-Case 'baseline'
Write-Output $baseline.Text
if ($baseline.Code -ne 0 -or !$baseline.Text.Contains('ARENA KILLCAM CONTRACTS PASSED')) { throw 'Production replay baseline failed.' }

$mutations = @(
 @{ Name='timeout-releases-owner'; Kind='Wait'; Old='return now < _deadline.Value ? ArenaReplayWaitDecision.Wait : ArenaReplayWaitDecision.Unsafe;';
    New='return now < _deadline.Value ? ArenaReplayWaitDecision.Wait : ArenaReplayWaitDecision.Ready;';
    Failure='busy deadline is unsafe, never a respawn release' },
 @{ Name='ignore-pending-lead-in'; Kind='Compat'; Old='if (!pending && !playing && readyForStop)'; New='if (!playing && readyForStop)';
    Failure='production reflection owns lead-in' },
 @{ Name='accept-replacement-body'; Kind='Hold'; Old='&& ReferenceEquals(_game.LocalPlayer, _body)'; New='';
    Failure='stale lifetime cannot release: replacement local body' },
 @{ Name='ignore-mode-hold'; Kind='Hold'; Old='&& !_match.Mode.HoldRespawn'; New='';
    Failure='mode hold survives camera release' },
 @{ Name='sliding-deadline'; Kind='Wait'; Old='_deadline ??='; New='_deadline =';
    Failure='busy deadline is unsafe, never a respawn release' },
 @{ Name='probe-disabled-camera'; Kind='Hold'; Old='if (_enabled)'; New='if (true)';
    Failure='disabled snapshot remains immediate after setting changes' },
 @{ Name='reuse-previous-release'; Kind='Hold'; Old='CanRespawn = false;'; New='// negative control: previous release is left set';
    Failure='second wait rechecks replay after preload' },
 @{ Name='early-protected-rebind'; Kind='Compat'; Old='is OptionalCameraOwnerState.Absent or OptionalCameraOwnerState.Idle;';
    New='is not OptionalCameraOwnerState.Incompatible;'; Failure='protected rebind waits for lead-in' }
)
foreach ($mutation in $mutations) {
 $original = $textByKey[$mutation.Kind]
 if ([regex]::Matches($original, [regex]::Escape($mutation.Old)).Count -ne 1) {
  throw ('Negative-control anchor must occur exactly once: ' + $mutation.Name)
 }
 $path = Join-Path $run ($mutation.Name + '.cs')
 [IO.File]::WriteAllText($path, $original.Replace($mutation.Old, $mutation.New))
 $result = Invoke-Case $mutation.Name $mutation.Kind $path
 if ($result.Code -eq 0 -or $result.Text.Contains('error CS') -or !$result.Text.Contains('CHECK: ' + $mutation.Failure)) {
  throw ($mutation.Name + ' did not fail its intended executable assertion: ' + $result.Text)
 }
 Write-Output ('Negative control rejected: ' + $mutation.Name)
}

$restored = Invoke-Case 'restored'
if ($restored.Code -ne 0 -or !$restored.Text.Contains('ARENA KILLCAM CONTRACTS PASSED')) { throw ('Restored replay checks failed: ' + $restored.Text) }
foreach ($path in $hashes.Keys) {
 if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hashes[$path]) {
  throw ('Production changed during verification: ' + $path)
 }
}
Write-Output $restored.Text
Write-Output ('Evidence: ' + $run)
Write-Output 'ARENA_KILLCAM_CHECKS_PASSED'
