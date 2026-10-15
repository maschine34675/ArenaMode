$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/zombie-team-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$files = @('ZombieTeamRuntime.cs', 'ZombieTeamRuntime.Identity.cs', 'ZombieTeamRuntime.FriendlyFire.cs', 'ZombieTeamRuntime.Refill.cs', 'ZombieMode.cs', 'ZombieWavePolicy.cs', 'ZombieRefillPolicy.cs')
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
</Project>
'@
function New-Case([string]$name) {
    $dir = Join-Path $stage $name
    [IO.Directory]::CreateDirectory($dir) | Out-Null
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $repo "ArenaMode.Client/Modes/$file") -Destination $dir }
    Copy-Item -LiteralPath (Join-Path $repo 'ArenaMode.Client/Respawn/ArenaReplayWaitPolicy.cs') -Destination $dir
    Copy-Item -LiteralPath (Join-Path $repo 'ArenaMode.Client/Combat/ArenaFriendlyFireRoster.cs') -Destination $dir
    foreach ($file in @('Program.cs','Stubs.cs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $dir }
    [IO.File]::WriteAllText((Join-Path $dir 'Checks.csproj'), $project)
    return $dir
}
function Run-Case([string]$dir, [bool]$mustPass, [string]$expectedFailure = '') {
    $log = Join-Path $dir 'run.log'
    $ErrorActionPreference = 'Continue'
    & dotnet run --project (Join-Path $dir 'Checks.csproj') --nologo *> $log
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $content = Get-Content -LiteralPath $log -Raw
    if ($mustPass) {
        if ($code -ne 0 -or $content -notmatch 'ZOMBIE_TEAM_CHECKS_PASSED') { throw "Production failed: $log`n$content" }
        Write-Host ($content.Trim())
        $script:productionAssertions = [int]([regex]::Match($content, 'ZOMBIE_TEAM_CHECKS_PASSED (\d+)').Groups[1].Value)
    } else {
        if ($code -eq 0 -or $content -match 'error CS\d+' -or $content -notmatch 'InvalidOperationException') {
            throw "Mutant must compile and fail a runtime assertion: $log`n$content"
        }
        if ($expectedFailure -and !$content.Contains($expectedFailure)) { throw "Mutant failed for the wrong reason: $log`n$content" }
    }
}
Run-Case (New-Case 'production') $true
$mutants = @(
    @{ Name='empty-hostility-aborts-wave'; File='ZombieTeamRuntime.cs'; Old='enemies.Count > 0 && !PitFireTeamCompat.EnsureHostility'; New='!PitFireTeamCompat.EnsureHostility' },
    @{ Name='death-respawns-midwave'; File='ZombieTeamRuntime.cs'; Old='internal bool HoldRespawn => _phase == TeamPhase.Ended || _playerDown;'; New='internal bool HoldRespawn => false;' },
    @{ Name='wrong-frozen-companion-count'; File='ZombieTeamRuntime.cs'; Old='TeamCompanionCount = PitFireTeamCompat.FrozenCompanionCount;'; New='TeamCompanionCount = 3;' },
    @{ Name='solo-refill-enters-team'; File='ZombieMode.cs'; Old='playerEligible && _team == null'; New='playerEligible' },
    @{ Name='pending-final-infected-ignored'; File='ZombieWavePolicy.cs'; Old='remainingToSpawn == 0 && alive <= 0 && pending == 0'; New='remainingToSpawn == 0 && alive <= 0' },
    @{ Name='lifecycle-epoch-ignored'; File='ZombieTeamRuntime.Identity.cs'; Old='|| status.Epoch != _expectedPitLifecycleEpoch'; New='|| false' },
    @{ Name='local-body-registry-ignored'; File='ZombieTeamRuntime.Identity.cs'; Old='|| !ReferenceEquals(everExisted, _boundLocalPlayer)'; New='|| false' },
    @{ Name='camera-race'; File='ZombieTeamRuntime.cs'; Old='if (localDead && ArenaSession.ReplaySeconds > 0f && !KillcamReplayCompat.CameraReleased) return;'; New='if (false) return;' },
    @{ Name='wave-spawns-before-team-ready'; File='ZombieMode.cs'; Old='if (_team != null && !_team.Tick())'; New='if (false && _team != null && !_team.Tick())' },
    @{ Name='teardown-skips-final-survivor'; File='ZombieTeamRuntime.Identity.cs'; Old='if (_boundLocalPlayer?.HealthController?.IsAlive == false'; New='if (false && _boundLocalPlayer?.HealthController?.IsAlive == false' },
    @{ Name='reusable-respawn-lease'; File='ZombieTeamRuntime.Identity.cs'; Old='|| !_teamRespawnAuthorizationAvailable'; New='|| false'; Old2='|| _teamRespawnAuthorizationConsumed'; New2='|| false' },
    @{ Name='opening-countdown-bypassed'; File='ZombieMode.cs'; Old='_waveState = ZombieWavePolicy.InitialCountdown();'; New='_waveState = ZombieWavePolicy.Initial();'; Failure='first countdown starts only after team-ready' },
    @{ Name='kit-readiness-counts-as-countdown'; File='ZombieMode.cs'; Old='LiveKitRespawn.IsInitialSwapPending || ArenaRespawnDriver.IsRespawnInProgress'; New='false || ArenaRespawnDriver.IsRespawnInProgress'; Failure='solo opening uses full shared duration' },
    @{ Name='survivor-keeps-empty-gear'; File='ZombieTeamRuntime.cs'; Old='BeginLivingLeaderRefill();'; New='_teamRebindStep = TeamRebindStep.RebuildLeader;'; Failure='living refill fails closed: arm' },
    @{ Name='failed-live-commit-accepted'; File='ZombieTeamRuntime.Refill.cs'; Old='if (!prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result))'; New='if (!prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result) && false)'; Failure='living refill fails closed: commit' },
    @{ Name='live-commit-ignores-epoch'; File='ZombieTeamRuntime.Refill.cs'; Old='if (!LeaseIsCurrent() || !ReferenceEquals(previous, oldPlayer)'; New='if (false || !ReferenceEquals(previous, oldPlayer)'; Failure='invalid live refill lease cannot reach inventory commit: epoch' },
    @{ Name='unchanged-live-body-accepted'; File='ZombieTeamRuntime.Refill.cs'; Old='if (ReferenceEquals(replacement, oldPlayer))'; New='if (false)'; Failure='living refill fails closed: body' },
    @{ Name='stale-team-result-owns-next-match'; File='ZombieMode.cs'; Old='internal bool TeamResultOwnsCurrentRaid => _team?.IsComplete == true && IsCurrentRaid()'; New='internal bool TeamResultOwnsCurrentRaid => _team?.IsComplete == true && IsCurrentRaid()'; Old2='&& ReferenceEquals(ArenaMatchService.Current, _match);'; New2=';'; Failure='old team result cannot suppress another match' }
)
foreach ($m in $mutants) {
    $dir = New-Case $m.Name
    $path = Join-Path $dir $m.File
    $source = [IO.File]::ReadAllText($path)
    if (-not $source.Contains($m.Old)) { throw "Missing mutant anchor $($m.Name)" }
    $source = $source.Replace($m.Old,$m.New)
    if ($m.Old2) { if (-not $source.Contains($m.Old2)) { throw "Missing second anchor $($m.Name)" }; $source = $source.Replace($m.Old2,$m.New2) }
    [IO.File]::WriteAllText($path,$source)
    Run-Case $dir $false $m.Failure
    Write-Host "Rejected compiled mutant: $($m.Name)"
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{ source = (& git -C $repo rev-parse HEAD); productionAssertions=$script:productionAssertions; compiledMutants=$mutants.Count; passed=$true } | ConvertTo-Json))
Write-Host "ZOMBIE_TEAM_MUTATION_CHECKS_PASSED $($mutants.Count) mutants; evidence: $stage"
