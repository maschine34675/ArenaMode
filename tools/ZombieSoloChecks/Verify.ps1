$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/zombie-solo-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$project = Join-Path $PSScriptRoot 'ZombieSoloChecks.csproj'
function Run-Case([string]$name, [string[]]$properties, [bool]$pass) {
    $log = Join-Path $stage ($name + '.log')
    & dotnet run --project $project -c $name --nologo -p:WarningLevel=0 @properties *> $log
    $code = $LASTEXITCODE
    $content = Get-Content -LiteralPath $log -Raw
    if ($pass) {
        if ($code -ne 0 -or $content -notmatch 'ZOMBIE_SOLO_CHECKS_PASSED') { throw "Production failed: $log`n$content" }
        Write-Host ([regex]::Match($content, 'ZOMBIE_SOLO_CHECKS_PASSED.*').Value)
        $script:productionAssertions = [int]([regex]::Match($content, 'ZOMBIE_SOLO_CHECKS_PASSED (\d+)').Groups[1].Value)
    } elseif ($code -eq 0 -or $content -match 'error CS\d+' -or $content -notmatch 'InvalidOperationException') {
        throw "Mutant must compile and fail a runtime assertion: $log`n$content"
    }
}
Run-Case 'Release' @() $true
$mutants = @(
    @{Name='respawn-released';File='ZombieMode.cs';Property='ModeSource';Old='public bool HoldRespawn => _team == null || _team.HoldRespawn;';New='public bool HoldRespawn => _team?.HoldRespawn == true;'},
    @{Name='death-not-terminal';File='ZombieMode.cs';Property='ModeSource';Old='_soloDefeated = true;';New='_soloDefeated = false;'},
    @{Name='panel-during-replay-grace';File='ZombieMode.cs';Property='ModeSource';Old='_soloResultPanelReady = ArenaSession.ReplaySeconds <= 0f;';New='_soloResultPanelReady = true;'},
    @{Name='panel-over-playing-replay';File='ZombieMode.cs';Property='ModeSource';Old='_soloResultPanelReady = decision == ArenaReplayWaitDecision.Ready;';New='_soloResultPanelReady = true;'},
    @{Name='duplicate-final-killfeed';File='ZombieMode.cs';Property='ModeSource';Old='_soloDeathKillPublished = true;';New='_soloDeathKillPublished = false;'},
    @{Name='duplicate-casualty';File='ZombieMode.cs';Property='ModeSource';Old='if (_soloDeathCounted || !IsCurrentRaid() || HasResult) return false;';New='if (!IsCurrentRaid()) return false;'},
    @{Name='wave-after-defeat';File='ZombieMode.cs';Property='ModeSource';Old='if (HasResult)'+"`r`n"+'        {';New='if (false)'+"`r`n"+'        {'},
    @{Name='async-arrival-after-defeat';File='ZombieMode.cs';Property='ModeSource';Old='internal bool IsSpawnAdmissionOpen => IsCurrentRaid() && !IsComplete;';New='internal bool IsSpawnAdmissionOpen => IsCurrentRaid();'},
    @{Name='killcam-cut-short';File='ZombieMode.cs';Property='ModeSource';Old='if (decision == ArenaReplayWaitDecision.Wait) return;';New='if (false) return;'},
    @{Name='stale-terminal-exit';File='ZombieMode.cs';Property='ModeSource';Old='&& ReferenceEquals(ArenaMatchService.Current, _match);';New=';'},
    @{Name='wrong-world-owner-exit';File='TdmMatchExit.cs';Property='ExitSource';Old='|| !ReferenceEquals(world.MainPlayer, local)';New='|| false'},
    @{Name='missing-owner-releases-hold';File='TdmMatchExit.cs';Property='ExitSource';Old='reportProblem?.Invoke("The native player owner is not ready; safe exit pending.");';New='ArenaRespawnDriver.StandDown = true; reportProblem?.Invoke("The native player owner is not ready; safe exit pending.");'},
    @{Name='late-score-admitted';File='ArenaMatch.cs';Property='MatchSource';Old='if (zombie.IsComplete)';New='if (false)'}
)
foreach ($m in $mutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo ('ArenaMode.Client/Modes/' + $m.File)))
    $old = $m.Old.Replace("`r`n", "`n"); $next = $m.New.Replace("`r`n", "`n")
    $source = $source.Replace("`r`n", "`n")
    if (!$source.Contains($old)) { throw "Missing anchor: $($m.Name)" }
    $copy = Join-Path $stage ($m.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($old, $next))
    Run-Case $m.Name @("-p:$($m.Property)=$copy") $false
    Write-Host "Rejected compiled mutant: $($m.Name)"
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{ source = (& git -C $repo rev-parse HEAD); assertions=$script:productionAssertions; compiledMutants=$mutants.Count; passed=$true } | ConvertTo-Json))
Write-Host "ZOMBIE_SOLO_MUTATION_CHECKS_PASSED $($mutants.Count) mutants; evidence: $stage"
