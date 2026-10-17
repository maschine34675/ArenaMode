param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('artifacts/failed-start-abort-checks/' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$source = Get-Content (Join-Path $repo 'ArenaMode.Client/Modes/ArenaMatchService.Abort.cs') -Raw
$intro = Get-Content (Join-Path $repo 'ArenaMode.Client/Session/ArenaFailedStartIntro.cs') -Raw
$faults = @(
    @('synchronous-stop','// Never call Stop inside OnGameStarted/area Begin.','localGame.Stop(localGame.ProfileId, ExitStatus.MissingInAction, "Arena", 0f); // regression'),
    @('same-frame','Time.frameCount <= _teamStartAbortFrame || ','false || '),
    @('no-op-committed','if (IsNativeTeardown(game)) CommitFailedStartAbort();','if (true) CommitFailedStartAbort();'),
    @('wrong-native-status','game.Status == GameStatus.Stopping || game.Status == GameStatus.Stopped','game.Status != GameStatus.Started'),
    @('wrong-stop-profile','game.Stop(_teamStartAbortProfileId, exitStatus, "Arena", 0f);','game.Stop(world.MainPlayer.ProfileId, exitStatus, "Arena", 0f);'),
    @('ordinary-raid','(!ArenaSession.IsActive && !ArenaSession.LastRaidWasArena)','false'),
    @('foreign-game','!ReferenceEquals(Singleton<AbstractGame>.Instance, game)','false'),
    @('foreign-world','!ReferenceEquals(Singleton<GameWorld>.Instance, world)','false'),
    @('drop-committed-latch','=> _teamStartAbortPending || _teamStartAbortCommitted;','=> _teamStartAbortPending;'),
    @('unpaced-retry','|| Time.unscaledTime < _nextTeamStartAbortAttemptAt','|| false'),
    @('detached-request-leak','Bots.BurstSpawnService.Reset();',''),
    @('repeat-schedule','if (TeamStartAbortPending) return;','if (false) return;'),
    @('profile-drift','!string.Equals(game.ProfileId, _teamStartAbortProfileId, StringComparison.Ordinal)','false'),
    @('respawn-overlap','RespawnEngineState.IsPipelineRunning || ','false || '),
    @('pause-overlap','Time.timeScale <= 0f','false'),
    @('owner-overlap','!ReferenceEquals(owner.Player, game.LocalPlayer)','false'),
    @('fade-precommit','&& IsNativeTeardown(game);','&& true;'),
    @('fade-foreign-started','&& ArenaSession.IsExactStartedRaid(game, world)','&& true')
)
foreach ($fault in $faults) {
    $name,$before,$after = $fault
    if (!$source.Contains($before)) { throw "Fault anchor missing: $name" }
    $file = Join-Path $OutputRoot ($name + '.cs')
    [IO.File]::WriteAllText($file, $source.Replace($before,$after))
    $log = Join-Path $OutputRoot ($name + '.log')
    $ErrorActionPreference = 'Continue'
    & dotnet run --project (Join-Path $PSScriptRoot 'FailedStartAbortChecks.csproj') -c Release "-p:AbortSource=$file" *> $log
    $faultExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($faultExit -eq 0) { throw "Fault survived: $name" }
    if (Select-String -Path $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault did not compile: $name" }
    if (!(Select-String -Path $log -Pattern 'Unhandled exception. System.Exception:' -SimpleMatch -Quiet)) { throw "Fault lacked assertion evidence: $name" }
    Write-Host "rejected: $name"
}
$introFaults = @(
    @('intro-unprotected','&& ArenaMatchService.OwnsFailedStartTeardown(game, world, profileId)','&& false'),
    @('intro-all-fades','!ReferenceEquals(_intro, preloader)','false'),
    @('intro-stale-epoch','epoch == ArenaSession.ArmEpoch','true'),
    @('intro-foreign-preloader','ReferenceEquals(MonoBehaviourSingleton<PreloaderUI>.Instance, preloader)','true'),
    @('intro-no-scope-restore','_intro = previous;','_intro = _intro;')
)
foreach ($fault in $introFaults) {
    $name,$before,$after = $fault
    if (!$intro.Contains($before)) { throw "Fault anchor missing: $name" }
    $file = Join-Path $OutputRoot ($name + '.cs')
    [IO.File]::WriteAllText($file, $intro.Replace($before,$after))
    $log = Join-Path $OutputRoot ($name + '.log')
    $ErrorActionPreference = 'Continue'
    & dotnet run --project (Join-Path $PSScriptRoot 'FailedStartAbortChecks.csproj') -c Release "-p:IntroSource=$file" *> $log
    $faultExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($faultExit -eq 0) { throw "Fault survived: $name" }
    if (Select-String -Path $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault did not compile: $name" }
    if (!(Select-String -Path $log -Pattern 'Unhandled exception. System.Exception:' -SimpleMatch -Quiet)) { throw "Fault lacked assertion evidence: $name" }
    Write-Host "rejected: $name"
}
& dotnet run --project (Join-Path $PSScriptRoot 'FailedStartAbortChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Unmodified production harness failed after mutation checks.' }
Write-Host "FAILED_START_ABORT_FAULTS_PASSED: $($faults.Count + $introFaults.Count) compiled faults; $OutputRoot"
