#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamGiveUpChecks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$modePath = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMode.cs'
$giveUpPath = Join-Path $repo 'ArenaMode.Client/Combat/ArenaGiveUp.cs'
$scoreboardPath = Join-Path $repo 'ArenaMode.Client/Modes/ArenaScoreboard.cs'
$flowPath = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMatchFlow.cs'
$policyPath = Join-Path $repo 'ArenaMode.Client/Modes/TeamDeathmatchMatchPolicy.cs'
$matchPath = Join-Path $repo 'ArenaMode.Client/Modes/ArenaMatch.cs'
$originalHashes = @{}
foreach ($path in @($modePath, $giveUpPath, $scoreboardPath, $flowPath, $policyPath, $matchPath)) {
    $originalHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$scratch = Join-Path $PSScriptRoot ('obj/giveup-regression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
function Invoke-Baseline {
    $output = & dotnet run --project $project -c Release 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'ARENA_TEAM_GIVE_UP_CHECKS_PASSED') {
        throw "Baseline failed: $($output -join [Environment]::NewLine)"
    }
    $output | Write-Output
}
# ReportKill's team branch now contains the assist-cleanup block. Balance its
# nested braces so this scope ends at the team branch, never at the adjacent
# solo Deathmatch branch that carries the same local-death notification.
$tdmKillScope = '(?s)if\s*\(Mode is TeamDeathmatchMode teams\)\s*\{(?<body>(?>[^{}]+|\{(?<depth>)|\}(?<-depth>))*)(?(depth)(?!))\}'
$mutants = @(
    @{ Name = 'missing-forfeit'; Source = $modePath; Property = 'ModeSource';
       From = '_match.Scoreboard.ReportOpponentForfeitKill();'; To = '/* missing original give-up team score */';
       Failure = 'give-up grants exactly one opposing PMC kill and base points' },
    @{ Name = 'unconfirmed-death'; Source = $modePath; Property = 'ModeSource';
       From = 'if (!deathConfirmed || !IsCurrentLocalGiveUpLife(player)'; To = 'if (!IsCurrentLocalGiveUpLife(player)';
       AlsoFrom = '|| player.ActiveHealthController?.IsAlive != false'; AlsoTo = '';
       Failure = 'suppressed/throwing native kill cannot score' },
    @{ Name = 'stale-context'; Source = $modePath; Property = 'ModeSource';
       From = 'if (!deathConfirmed || !IsCurrentLocalGiveUpLife(player)'; To = 'if (!deathConfirmed';
       Failure = 'stale match/world/mode/body/profile/health cannot receive give-up point' },
    @{ Name = 'stale-aggressor-gate'; Source = $giveUpPath; Property = 'GiveUpSource';
       From = 'using (ArenaAdministrativeDeathGate.Enter(player.ProfileId))'; To = '';
       Failure = 'give-up killed current life' },
    @{ Name = 'wrong-team'; Source = $scoreboardPath; Property = 'ScoreboardSource';
       From = 'if (side == ArenaScoreboardSide.Opponent)'; To = 'if (side == ArenaScoreboardSide.Friendly)';
       Failure = 'give-up grants exactly one opposing PMC kill and base points' },
    @{ Name = 'historical-visual-owner'; Source = $modePath; Property = 'ModeSource';
       From = '|| !ReferenceEquals(_world.GetAlivePlayerByProfileID(player.ProfileId), player)'; To = '';
       Failure = 'historical profile/new body alias cannot borrow visual affiliation' },
    @{ Name = 'staging-kill-event'; Source = $matchPath; Property = 'MatchSource';
       Within = $tdmKillScope;
       From = 'if (!teams.TryBeginScoredEvent())'; To = 'if (false)';
       Failure = 'assembling callbacks cannot change scores, deaths, awards or kill events' },
    @{ Name = 'staging-death-score'; Source = $matchPath; Property = 'MatchSource';
       From = 'Mode is TeamDeathmatchMode teams && !teams.TryRecordLocalDeath()';
       To = 'Mode is TeamDeathmatchMode teams && false';
       Failure = 'stopped/failed/unbound team scoring never blocks the existing give-up escape action' },
    @{ Name = 'duplicate-local-death'; Source = $flowPath; Property = 'FlowSource';
       From = '_localDeathCounted || !TryBeginScoredEvent()'; To = '!TryBeginScoredEvent()';
       Failure = 'final local kill and driver death count exactly once in either native event order' },
    @{ Name = 'kill-first-local-death'; Source = $matchPath; Property = 'MatchSource';
       Within = $tdmKillScope;
       From = 'if (kill.LocalIsVictim) ReportLocalDeath();'; To = '';
       Failure = 'final local kill and driver death count exactly once in either native event order' },
    @{ Name = 'terminal-respawn-board'; Source = $matchPath; Property = 'MatchSource';
       From = 'if (ArenaMatchResult.HasResult(Mode)) return;'; To = '';
       Failure = 'post-result kill, death and replacement callbacks cannot mutate the final board or events' },
    @{ Name = 'event-before-deadline-check'; Source = $flowPath; Property = 'FlowSource';
       From = 'CheckMatchLimits();'; To = '';
       Failure = 'an event at the exact time limit cannot score into an expired match' },
    @{ Name = 'terminal-giveup-exit'; Source = $giveUpPath; Property = 'GiveUpSource';
       From = 'if (ArenaMatchResult.HasResult(ArenaMatchService.Current?.Mode))'; To = 'if (false)';
       Failure = 'result exit works while the dead-player respawn hold remains set' },
    @{ Name = 'staging-death-countdown'; Source = $matchPath; Property = 'MatchSource';
       From = 'RaiseSafe(teams.OnLocalDeath, "mode.OnLocalDeath");'; To = '';
       Failure = 'unscored staging death immediately invalidates countdown before any roster tick' },
    @{ Name = 'staging-handover-countdown'; Source = $modePath; Property = 'ModeSource';
       From = 'if (_rules?.Phase == TdmMatchPhase.Countdown) TickMatchStart(false);'; To = '';
       Failure = 'staging body handover immediately invalidates countdown without a score' },
    @{ Name = 'giveup-discovers-deadline'; Source = $giveUpPath; Property = 'GiveUpSource';
       From = 'if (teams?.HasResult == true)'; To = 'if (false)';
       Failure = 'give-up that first discovers an expired match requests exit without a post-result native death' }
)
foreach ($mutant in $mutants) {
    $source = Get-Content -LiteralPath $mutant.Source -Raw
    $segment = $source
    $body = $null
    if ($mutant.ContainsKey('Within')) {
        # The same death notification also exists in unified solo Deathmatch.
        # Bound this fault to the named TDM branch, never to the first occurrence.
        # An unbalanced or ambiguous branch fails closed for manual review;
        # the exact fault anchor must still occur once inside the whole branch.
        $scopes = [regex]::Matches($source, $mutant.Within)
        if ($scopes.Count -ne 1 -or !$scopes[0].Groups['body'].Success) {
            throw "Mutation scope must occur exactly once: $($mutant.Name)"
        }
        $body = $scopes[0].Groups['body']
        $segment = $body.Value
    }
    if (($segment.Split($mutant.From)).Count -ne 2) { throw "Mutation target must occur exactly once: $($mutant.Name)" }
    $changed = $segment.Replace($mutant.From, $mutant.To)
    $source = if ($null -eq $body) { $changed } else {
        $source.Substring(0, $body.Index) + $changed + $source.Substring($body.Index + $body.Length)
    }
    if ($mutant.ContainsKey('AlsoFrom')) {
        if (($source.Split($mutant.AlsoFrom)).Count -ne 2) { throw "Second mutation target missing: $($mutant.Name)" }
        $source = $source.Replace($mutant.AlsoFrom, $mutant.AlsoTo)
    }
    $copy = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source)
    $mutant['Copy'] = $copy
}
# Resolve every target before compiling any fault, so contract drift near the
# end of the list does not waste a full partial run or hide other stale anchors.
Write-Output ('PREPARED ' + $mutants.Count + ' uniquely bound production mutations.')
Invoke-Baseline
foreach ($mutant in $mutants) {
    $argument = '-p:' + $mutant.Property + '=' + $mutant.Copy
    $output = & dotnet run --project $project -c ('GiveUpMutant-' + $mutant.Name) $argument 2>&1
    $exitCode = $LASTEXITCODE
    $combined = $output -join "`n"
    if ($exitCode -eq 0 -or $combined -match 'error CS\d+' -or $combined -notmatch [regex]::Escape($mutant.Failure)) {
        throw "Mutant did not fail its behavioral assertion: $($mutant.Name), exit=$exitCode`n$combined"
    }
    Write-Output ('REJECTED ' + $mutant.Name + ': ' + $mutant.Failure)
}
foreach ($path in $originalHashes.Keys) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $originalHashes[$path]) {
        throw "Production source changed during regression run: $path"
    }
}
Invoke-Baseline
Write-Output 'ARENA_TEAM_GIVE_UP_REGRESSION_PASSED'
