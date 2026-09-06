$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TeamArenaGoalHandoffChecks.csproj'
$paths = @{
    HandoffSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ArenaMode.Client/Bots/TeamArenaGoalHandoff.cs'))
    ContextSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ArenaMode.Client/Compat/PitFireTeamGoalContext.cs'))
    RoundSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ArenaMode.Client/Modes/TacticalMode.Pursuit.cs'))
}
$hashes = @{}
foreach ($key in $paths.Keys) { $hashes[$key] = (Get-FileHash -LiteralPath $paths[$key]).Hash }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaGoalHandoff-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Goal handoff baseline failed' }
$mutants = @(
    @{ Name='NoPftScope'; Property='HandoffSource'; Anchor='if (follower && !PitFireTeamGoalContext.TryEnter(out scope, out problem))'; Replacement='if (false)'; Failure='scoped handoff admits the live memory-only target' },
    @{ Name='NoDispose'; Property='HandoffSource'; Anchor='finally { scope?.Dispose(); }'; Replacement='finally { }'; Failure='scope is confined to one native setter' },
    @{ Name='IgnoreVeto'; Property='HandoffSource'; Anchor='if (!ReferenceEquals(source.Memory.GoalEnemy, enemy))'; Replacement='if (false)'; Failure='PFT mission veto is retained' },
    @{ Name='LivingHuman'; Property='RoundSource'; Anchor='|| _boundLocalPlayer.HealthController?.IsAlive != false'; Replacement=''; Failure='isolation: living human' },
    @{ Name='OldRound'; Property='RoundSource'; Anchor='|| epoch != _teamRoundEpoch'; Replacement=''; Failure='isolation: old round' },
    @{ Name='MissingPrevious'; Property='HandoffSource'; Anchor='if (previous != null)'; Replacement='if (false)'; Failure='missing previous subscription is rejected before' },
    @{ Name='ReplacedLease'; Property='HandoffSource'; Anchor='if (!TeamArenaPursuitLeaseBridge.TryGet(source.ProfileId, lease.Epoch, lease.Generation, out var after)'; Replacement='if (!TeamArenaPursuitLeaseBridge.TryGet(source.ProfileId, out var after)'; Failure='lease replacement during native callback' },
    @{ Name='RemovedRelation'; Property='HandoffSource'; Anchor=('if (!HasExactRelation(source, target, enemy))' + "`n" + '            { problem = "pursuit-relation-ended-during-setter"; return false; }'); Replacement=''; Failure='removed relation during native callback' }
)
foreach ($mutant in $mutants) {
    $source = [IO.File]::ReadAllText($paths[$mutant.Property]).Replace("`r`n", "`n")
    if ([regex]::Matches($source, [regex]::Escape($mutant.Anchor)).Count -ne 1) { throw "Ambiguous mutant anchor: $($mutant.Name)" }
    $path = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($mutant.Anchor, $mutant.Replacement))
    $output = & dotnet run --project $project -c ('GoalMutant' + $mutant.Name) "-p:$($mutant.Property)=$path" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains('ARENA_PFT_GOAL_CHECKS_FAILED:') -or !$output.Contains($mutant.Failure)) { throw "Mutant did not fail its intended behavior: $($mutant.Name)`n$output" }
    Write-Output "Rejected goal handoff mutant: $($mutant.Name)"
}
foreach ($key in $paths.Keys) { if ((Get-FileHash -LiteralPath $paths[$key]).Hash -ne $hashes[$key]) { throw 'Production source changed during checks' } }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Final goal handoff baseline failed' }
Write-Output "ARENA_PFT_GOAL_REGRESSION_PASSED ($($mutants.Count) mutants; evidence: $scratch)"
