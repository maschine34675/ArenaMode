$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSource = Join-Path $arenaRepo 'ArenaMode.Client\Bots\CorpseCleanupService.cs'
$arenaProject = Join-Path $PSScriptRoot 'CorpseCleanupChecks.csproj'
$arenaOriginal = [IO.File]::ReadAllText($arenaSource)
function Get-ArenaSha256([string] $Path) {
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-', '') }
    finally { $arenaSha.Dispose() }
}
$arenaHash = Get-ArenaSha256 $arenaSource
$arenaMutantDir = Join-Path ([IO.Path]::GetTempPath()) ('arena-cleanup-mutants-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaMutantDir)
$arenaPair = @'
                        try
                        {
                            result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);
                        }
                        finally
                        {
                            result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);
                        }
'@
$arenaUnprotectedPair = @'
                        result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);
                        result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);
'@
$arenaNormalized = $arenaOriginal.Replace("`r`n", "`n")
# The dead-body record is cleared AFTER the AI's knowledge of the person, because
# clearing that knowledge is itself what re-creates the record. Moving it earlier
# takes two edits, and so does defeating a pair of guards that protect the same
# body twice on purpose - hence the optional second pair.
$arenaRecordGuard = @'
            if (!RemoveDeadBodyRecord(corpse))
                throw new InvalidOperationException("the AI's dead-body record could not be cleared");
'@
$arenaOwnerReread = @'
        if (corpse.PlayerProfileID == mainPlayer.ProfileId
            || Compat.PitFireTeamCompat.IsTeamProfileId(corpse.PlayerProfileID))
'@
$arenaMutants = @(
    @{ Name = 'missing-begin'; From = 'result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);'; To = '// mutation: missing Begin'; Failure = 'successful cleanup has no missing Begin activity' },
    @{ Name = 'missing-completion'; From = 'result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);'; To = '// mutation: missing completion'; Failure = 'successful cleanup leaves no active activities' },
    @{ Name = 'existing-activity'; From = 'controller.SelectEvents(item).Any()'; To = 'false'; Failure = 'pre-existing same-item activity is preserved' },
    @{ Name = 'missing-finally'; From = $arenaPair.Replace("`r`n", "`n"); To = $arenaUnprotectedPair.Replace("`r`n", "`n"); Failure = 'tested failure boundary leaves no unpaired activity: begin' },
    @{ Name = 'pooled-instead-of-destroyed'; From = 'pool.IsStub = true;'; To = '// mutation: let the body go back into the shared player pool'; Failure = 'a released body is destroyed, never pushed back into the shared player pool' },
    # "corpse and player remain registered" is the earliest assertion that
    # states this property; the release block states it again by name.
    @{ Name = 'released-in-the-hiding-sweep'; From = 'state.Sweep >= _sweep'; To = 'false'; Failure = 'corpse and player remain registered' },
    @{ Name = 'retries-a-failed-release'; From = 'if (!state.Completed || state.ReleaseFailed || state.Own || state.Protected)'; To = 'if (!state.Completed || state.Own || state.Protected)'; Failure = 'a failed release is never retried, even once the fault is gone: retire' },
    @{ Name = 'releases-own-and-teammate-bodies';
       From = 'if (!state.Completed || state.ReleaseFailed || state.Own || state.Protected)';
       To = 'if (!state.Completed || state.ReleaseFailed)';
       From2 = $arenaOwnerReread.Replace("`r`n", "`n");
       To2 = '        if (false)';
       Failure = 'this body is hidden but never taken out of the scene: own' },
    @{ Name = 'unbounded-release-burst'; From = 'if (due.Count >= MaxReleasesPerSweep)'; To = 'if (false)'; Failure = 'one sweep never releases more than its budget' },
    @{ Name = 'releases-a-pending-aggressor'; From = 'if (CorpseAggressors.IsPendingAggressor(player, world))'; To = 'if (false)'; Failure = 'a body that stopped being releasable stays: aggressor' },
    @{ Name = 'leaves-the-trigger-coroutine-running'; From = 'CorpseTriggerZones.Release(player);'; To = '// mutation: leave the endless coroutine reading a destroyed body'; Failure = 'the trigger-zone coroutine that reads this body is retired before the body goes' },
    @{ Name = 'clears-the-record-before-the-ai-knowledge';
       From = $arenaRecordGuard.Replace("`r`n", "`n"); To = '';
       From2 = '            Singleton<IBotGame>.Instance?.BotsController?.DestroyInfo(player);';
       To2 = $arenaRecordGuard.Replace("`r`n", "`n") + '            Singleton<IBotGame>.Instance?.BotsController?.DestroyInfo(player);';
       Failure = 'the dead-body record DestroyInfo re-adds for a dead person is cleared afterwards' },
    @{ Name = 'records-a-release-that-never-happened';
       From = '            Diagnostics.CorpseReleaseLedger.Record(profileId, _sweep);'; To = '';
       From2 = '        var profileId = corpse.PlayerProfileID;';
       To2 = "        var profileId = corpse.PlayerProfileID;`n        Diagnostics.CorpseReleaseLedger.Record(profileId, _sweep);";
       Failure = 'a failed release records nothing: the damage diagnostic must never name a body that is still there: retire' }
)
foreach ($arenaMutant in $arenaMutants) {
    if (-not $arenaNormalized.Contains($arenaMutant.From)) { throw "Missing mutation anchor: $($arenaMutant.Name)" }
    $arenaMutated = $arenaNormalized.Replace($arenaMutant.From, $arenaMutant.To)
    if ($arenaMutant.ContainsKey('From2')) {
        if (-not $arenaMutated.Contains($arenaMutant.From2)) { throw "Missing second mutation anchor: $($arenaMutant.Name)" }
        $arenaMutated = $arenaMutated.Replace($arenaMutant.From2, $arenaMutant.To2)
    }
    $arenaPath = Join-Path $arenaMutantDir ($arenaMutant.Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaMutated)
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project $arenaProject -c Release "-p:CleanupSource=$arenaPath" 2>&1 | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    if ($arenaExit -eq 0 -or -not $arenaOutput.Contains($arenaMutant.Failure)) {
        throw "Mutant was not rejected by its intended assertion: $($arenaMutant.Name)`n$arenaOutput"
    }
    Write-Output "Rejected mutant: $($arenaMutant.Name)"
}
if ((Get-ArenaSha256 $arenaSource) -ne $arenaHash) { throw 'Production source changed' }
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production checks failed' }
Write-Output 'ARENA_CORPSE_CLEANUP_REGRESSION_PASSED'
