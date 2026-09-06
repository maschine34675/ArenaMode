$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'BotLifecycleSafetyChecks.csproj'
$paths = @{}
foreach ($pair in @(@('SafetySource','ArenaBotLifecycleSafety.cs'), @('ScopeSource','ArenaBotLifecycleScope.cs'), @('SainSource','ArenaSainActionSafety.cs'))) {
    $paths[$pair[0]] = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../../ArenaMode.Client/Bots/' + $pair[1])))
}
$hashes = @{}
foreach ($key in $paths.Keys) { $hashes[$key] = (Get-FileHash -LiteralPath $paths[$key]).Hash }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaBotLifecycle-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle baseline failed' }
$mutants = @(
    @{ Name='NoAddSentinel'; Property='SafetySource'; Anchor='EnsureRoom(__instance, 2)'; Replacement='EnsureRoom(__instance, 1)'; Failure='add reserves entry and null sentinel' },
    @{ Name='NoFullRemoveGuard'; Property='SafetySource'; Anchor='EnsureRoom(__instance, 1)'; Replacement='EnsureRoom(__instance, 0)'; Failure='full-array removals' },
    @{ Name='OrdinaryCapacityLeak'; Property='SafetySource'; Anchor='|| !ArenaBotLifecycleScope.Owns(owner)'; Replacement='|| false'; Failure='out-of-scope capacity unchanged: ordinary' },
    @{ Name='DroppedEnemyReferences'; Property='SafetySource'; Anchor='Array.Resize(ref controller.SortedInfos, capacity);'; Replacement='controller.SortedInfos = new EnemyInfo[capacity];'; Failure='prefix preserves native bookkeeping' },
    @{ Name='EndOwnershipTooSoon'; Property='ScopeSource'; Anchor='if (_owners.TryGetValue(owner, out var stamp))'; Replacement='if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid) return false; if (_owners.TryGetValue(owner, out var stamp))'; Failure='owned retired wrapper still gets sentinel after globals clear' },
    @{ Name='AllowMissingStop'; Property='SainSource'; Anchor='            return false;'; Replacement='            return true;'; Failure='missing-component guard preserves every viable or unknown cleanup: missing' },
    @{ Name='SkipLiveStop'; Property='SainSource'; Anchor='if (_cachedBot.GetValue(__instance) is UnityEngine.Object cached && cached != null) return true;'; Replacement='if (_cachedBot.GetValue(__instance) is UnityEngine.Object cached && cached != null) return false;'; Failure='missing-component guard preserves every viable or unknown cleanup: cached' },
    @{ Name='IgnoreDestroyedManager'; Property='SainSource'; Anchor='if (!ReferenceEquals(manager, null))'; Replacement='if (manager != null)'; Failure='missing-component guard preserves every viable or unknown cleanup: destroyed-manager-live-registry' }
)
foreach ($mutant in $mutants) {
    $source = [IO.File]::ReadAllText($paths[$mutant.Property])
    if ($mutant.Name -eq 'AllowMissingStop') {
        # Only the successful missing-component return, not TryBind's failure return.
        $anchor = "            return false;`n        }`n        catch (Exception exception)`n        {`n            Warn(exception);"
        $replacement = $anchor.Replace('return false;', 'return true;')
        $source = $source.Replace("`r`n", "`n")
    } else { $anchor = $mutant.Anchor; $replacement = $mutant.Replacement }
    if ([regex]::Matches($source, [regex]::Escape($anchor)).Count -ne 1) { throw "Ambiguous mutant anchor: $($mutant.Name)" }
    $path = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($path, $source.Replace($anchor, $replacement))
    $output = & dotnet run --project $project -c ('LifecycleMutant' + $mutant.Name) "-p:$($mutant.Property)=$path" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains('ARENA_BOT_LIFECYCLE_CHECKS_FAILED') -or !$output.Contains($mutant.Failure)) {
        throw "Mutant did not fail its intended behavior: $($mutant.Name)`n$output"
    }
    Write-Output "Rejected lifecycle mutant: $($mutant.Name)"
}
foreach ($key in $paths.Keys) { if ((Get-FileHash -LiteralPath $paths[$key]).Hash -ne $hashes[$key]) { throw 'Production source changed during checks' } }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Final lifecycle baseline failed' }
Write-Output "ARENA_BOT_LIFECYCLE_REGRESSION_PASSED ($($mutants.Count) mutants; evidence: $scratch)"
