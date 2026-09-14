param()
$ErrorActionPreference = 'Stop'
$combatRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$combatScratch = Join-Path $combatRoot ('artifacts/announcer-combat-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($combatScratch)
$combatSources = @{
    MatchSource = 'ArenaMode.Client/Modes/ArenaMatch.cs'
    ScoreboardSource = 'ArenaMode.Client/Modes/ArenaScoreboard.cs'
    AwardsSource = 'ArenaMode.Client/Combat/ArenaCombatAwards.cs'
}
$combatBoundPaths = @($combatSources.Values) + @('ArenaMode.Client/Combat/ArenaKill.cs',
    'ArenaMode.Client/Combat/ScoreRules.cs', 'ArenaMode.Client/Modes/IArenaMode.cs',
    'ArenaMode.Client/Modes/ArenaAnnouncerCue.cs', 'ArenaMode.Client/Modes/TacticalMode.cs')
$combatBoundPaths += Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object { 'tools/AnnouncerCombatChecks/' + $_.Name }
$combatBefore = @{}
foreach ($relative in $combatBoundPaths) { $combatBefore[$relative] = (Get-FileHash -LiteralPath (Join-Path $combatRoot $relative)).Hash }
$combatTexts = @{}
foreach ($property in $combatSources.Keys) { $combatTexts[$property] = [IO.File]::ReadAllText((Join-Path $combatRoot $combatSources[$property])).Replace("`r`n", "`n") }
function Invoke-CombatProbe([string]$name, [string]$property = '', [string]$source = '', [string]$part = 'all', [string]$failure = '') {
    $dir = Join-Path $combatScratch $name
    [void][IO.Directory]::CreateDirectory($dir)
    $arguments = @('build', (Join-Path $PSScriptRoot 'AnnouncerCombatChecks.csproj'), '-c', 'Release',
        "-p:BaseIntermediateOutputPath=$dir/obj/", "-p:OutputPath=$dir/out/", '--nologo')
    if ($property) {
        $file = Join-Path $dir 'Source.cs'; [IO.File]::WriteAllText($file, $source)
        $arguments += "-p:${property}=$file"
    }
    $build = & dotnet @arguments 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "$name did not compile: $build" }
    $run = & dotnet (Join-Path $dir 'out/AnnouncerCombatChecks.dll') $part 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $dir 'result.log'), $run)
    if ($failure) {
        if ($code -eq 0 -or !$run.Contains($failure)) { throw "$name failed to reject intended behavior: $run" }
        Write-Host "Rejected $name at: $failure"
    } elseif ($code -ne 0 -or !$run.Contains('ANNOUNCER_COMBAT_PRODUCTION_PASSED')) { throw "$name failed: $run" }
    else { Write-Host $run.Trim() }
}
# Full production match is exercised above native mode boundaries. Verify the
# live Tactical caller supplies its validated terminal snapshot before its win,
# since the match must never infer local survival from a previous frame.
$combatTactical = [IO.File]::ReadAllText((Join-Path $combatRoot 'ArenaMode.Client/Modes/TacticalMode.cs'))
$combatTeamLive = [regex]::Match($combatTactical, '(?ms)^    private void TickTeamLive\(\).*?^    \}').Value
if (!$combatTeamLive) { throw 'Expected exact production TickTeamLive method' }
$combatCounts = $combatTeamLive.IndexOf('int alliesAlive = followers.Count + (localAlive ? 1 : 0);')
$combatSnapshot = $combatTeamLive.IndexOf('_match.ReportTeamSurvivors(localAlive, alliesAlive, enemiesAlive);')
$combatWin = $combatTeamLive.IndexOf('if (enemiesAlive <= 0)')
if ($combatCounts -lt 0 -or $combatSnapshot -le $combatCounts -or $combatWin -le $combatSnapshot) {
    throw 'Tactical terminal outcome must observe verified current survivors before win'
}
Invoke-CombatProbe 'production'
$combatHistorical = (& git -C $combatRoot show '5593ea7:ArenaMode.Client/Modes/ArenaMatch.cs') -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Historical announcer baseline unavailable' }
Invoke-CombatProbe 'historical-spree-first' 'MatchSource' $combatHistorical 'multi' 'multi-kill must beat simultaneous spree'
$combatFaults = @(
    @{ Name='post-reset-shutdown'; Property='MatchSource'; Part='shutdown';
       Find="bool shutdown = Scoreboard.IsShutdown(kill);`n        Scoreboard.ReportKill(kill);";
       Replace="Scoreboard.ReportKill(kill);`n        bool shutdown = Scoreboard.IsShutdown(kill);";
       Failure='shutdown captured before victim reset' },
    @{ Name='wrong-body-shutdown'; Property='ScoreboardSource'; Part='shutdown';
       Find=' && victim.AwardBodyId == kill.VictimBodyId'; Replace=''; Failure='shutdown rejects wrong-victim-body' },
    @{ Name='retired-shutdown'; Property='ScoreboardSource'; Part='shutdown';
       Find='&& IsCurrentBody(victim, profile)'; Replace=''; Failure='shutdown rejects expired' },
    @{ Name='dead-life-shutdown'; Property='ScoreboardSource'; Part='shutdown';
       Find='victim.AwardLifeStreak = 0;'; Replace='victim.AwardLifeStreak += 0;'; Failure='shutdown rejects dead-life' },
    @{ Name='resurrection-clutch'; Property='AwardsSource'; Part='clutch';
       Find="_clutchInvalidated = true;`n        _clutchCandidate"; Replace="_clutchInvalidated = false;`n        _clutchCandidate"; Failure='clutch rejects resurrection' },
    @{ Name='stale-survival-clutch'; Property='AwardsSource'; Part='clutch';
       Find='&& _clutchVictoryObserved;'; Replace=';'; Failure='clutch rejects stale-alive' },
    @{ Name='next-round-clutch'; Property='AwardsSource'; Part='clutch';
       Find='_clutchCandidate = _clutchInvalidated = _clutchVictoryObserved = false;'; Replace='_clutchInvalidated = _clutchVictoryObserved = false;'; Failure='clutch rejects new-round' },
    @{ Name='suppressed-win-lifecycle'; Property='MatchSource'; Part='clutch';
       Find='RaiseSafe(() => OnAnnouncerCue?.Invoke(cue), nameof(OnAnnouncerCue));';
       Replace='if (cue != ArenaAnnouncerCue.RoundWon) RaiseSafe(() => OnAnnouncerCue?.Invoke(cue), nameof(OnAnnouncerCue));';
       Failure='clutch immediately precedes preserved win lifecycle' },
    @{ Name='early-first-blood-reset'; Property='MatchSource'; Part='multi';
       Find=' && beginAwardRound'; Replace=''; Failure='delayed cue preserves early First Blood and multi-kill chain' }
)
foreach ($fault in $combatFaults) {
    $source = $combatTexts[$fault.Property]
    if ($source.Split(@($fault.Find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.Name)" }
    Invoke-CombatProbe $fault.Name $fault.Property ($source.Replace($fault.Find, $fault.Replace)) $fault.Part $fault.Failure
}
foreach ($relative in $combatBoundPaths) {
    if ((Get-FileHash -LiteralPath (Join-Path $combatRoot $relative)).Hash -ne $combatBefore[$relative]) {
        throw "Bound production or harness source changed; rerun required: $relative"
    }
}
[ordered]@{ sourceHashes=$combatBefore; historical='5593ea7'; controls=$combatFaults.Count + 1;
    limitation='Offline full production match/scoreboard/awards; native mode callback boundaries stubbed; no GPU/audio proof' } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $combatScratch 'source-binding.json') -Encoding utf8
Write-Host "ANNOUNCER COMBAT CHECKS PASSED; 1 historical + $($combatFaults.Count) fault controls; evidence: $combatScratch"
