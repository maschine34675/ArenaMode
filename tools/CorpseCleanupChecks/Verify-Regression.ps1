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
# The damage-source guard is linked as production source too, so its own rules
# can be mutated rather than only its call site. Target = 'DamageSources'.
$arenaSourcesSource = Join-Path $arenaRepo 'ArenaMode.Client\Bots\CorpseDamageSources.cs'
$arenaSourcesNormalized = [IO.File]::ReadAllText($arenaSourcesSource).Replace("`r`n", "`n")
$arenaSourcesHash = Get-ArenaSha256 $arenaSourcesSource
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
$arenaTripwireFinished = @'
        => state == ETripwireState.Exploded
            || state == ETripwireState.Inert
            || state == ETripwireState.None;
'@
$arenaSourcesFailClosed = @'
            WarnOnce(ex.GetBaseException().Message);
            return Unreadable;
'@
$arenaFlareBranch = @'
                case FlareCartridge flare:
                    if (Same(FlareShooter(flare)?.ProfileId, profileId))
                        return "a flare it fired";
                    break;

'@
$arenaMutants = @(
    @{ Name = 'missing-begin'; From = 'result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Begin);'; To = '// mutation: missing Begin'; Failure = 'successful cleanup has no missing Begin activity' },
    @{ Name = 'missing-completion'; From = 'result.Value.RaiseEvents(controller, EFT.InventoryLogic.CommandStatus.Succeed);'; To = '// mutation: missing completion'; Failure = 'successful cleanup leaves no active activities' },
    @{ Name = 'existing-activity'; From = 'controller.SelectEvents(item).Any()'; To = 'false'; Failure = 'pre-existing same-item activity is preserved' },
    @{ Name = 'missing-finally'; From = $arenaPair.Replace("`r`n", "`n"); To = $arenaUnprotectedPair.Replace("`r`n", "`n"); Failure = 'tested failure boundary leaves no unpaired activity: begin' },
    @{ Name = 'pooled-instead-of-destroyed'; From = 'pool.IsStub = true;'; To = '// mutation: let the body go back into the shared player pool'; Failure = 'a released body is destroyed, never pushed back into the shared player pool' },
    # "corpse and player remain registered" is the earliest assertion that
    # states this property; the release block states it again by name. Since
    # the damage-source quiet period, whose first reading is taken AT the trim,
    # a body cannot be released in the sweep that hid it on that ground alone
    # either - so this control defeats both, like the own/teammate pair below.
    @{ Name = 'released-in-the-hiding-sweep'; From = 'state.Sweep >= _sweep'; To = 'false';
       From2 = 'return Time.time - state.SourcesQuietSince >= DamageSourceQuietSeconds;'; To2 = 'return true;';
       Failure = 'corpse and player remain registered' },
    @{ Name = 'retries-a-failed-release'; From = 'if (!state.Completed || state.ReleaseFailed || state.Protected)'; To = 'if (!state.Completed || state.Protected)'; Failure = 'a failed release is never retried, even once the fault is gone: retire' },
    # The two layers that protect a PitFireTeam body: the trim-time snapshot
    # (state.Protected, in the fast path) and the live re-read
    # (IsTeamProfileId). Both have to go before one is actually released,
    # like the own/teammate pair this replaced - own now has its own pair of
    # mutants below, since it moved to a different mechanism (IsNewestOwnCorpse)
    # entirely.
    @{ Name = 'releases-a-teammate-body';
       From = 'if (!state.Completed || state.ReleaseFailed || state.Protected)';
       To = 'if (!state.Completed || state.ReleaseFailed)';
       From2 = 'if (Compat.PitFireTeamCompat.IsTeamProfileId(corpse.PlayerProfileID))';
       To2 = 'if (false)';
       Failure = 'this body is hidden but never taken out of the scene: team' },
    # Own corpses release too (2026-09-18): only the NEWEST is exempt, decided
    # by a live re-read (IsNewestOwnCorpse) rather than the trim-time
    # TrimState.Own snapshot. Removing the newest-check here releases even
    # the sole/newest own corpse - the positive control the "own" case in the
    # same "never released" loop above already relies on.
    @{ Name = 'releases-the-newest-own-body';
       From = 'if (isOwnCorpse && IsNewestOwnCorpse(corpse, world, mainPlayer.ProfileId))';
       To = 'if (false)';
       Failure = 'this body is hidden but never taken out of the scene: own' },
    # DamageSourcesQuiet's own-corpse refinement: an own corpse the world's
    # profile-id registry no longer names bypasses the ordinary hold, because
    # nothing can resolve a source TO it any more (CORPSE-CLEANUP-REVIEW.md,
    # "Own corpses release too", "the registry argument"). Two ways to break
    # it, both defeating the "still named" half of the two-corpse registry
    # test: dropping the condition bypasses EVERY own corpse regardless of
    # what the registry says, and inverting it bypasses exactly the corpses
    # that should stay held while holding exactly the ones that should go.
    @{ Name = 'ignores-the-registry-for-own-corpses';
       From = 'state.LeftRegistry = !CorpseDamageSources.StillNamedInRegistry(player, corpse.PlayerProfileID, world);';
       To = 'state.LeftRegistry = true;';
       Failure = 'an own corpse the registry still names stays held behind a live damage source, exactly like before' },
    @{ Name = 'inverts-the-registry-condition';
       From = 'state.LeftRegistry = !CorpseDamageSources.StillNamedInRegistry(player, corpse.PlayerProfileID, world);';
       To = 'state.LeftRegistry = CorpseDamageSources.StillNamedInRegistry(player, corpse.PlayerProfileID, world);';
       Failure = 'an own corpse the registry still names stays held behind a live damage source, exactly like before' },
    @{ Name = 'unbounded-release-burst'; From = 'if (due.Count >= MaxReleasesPerSweep)'; To = 'if (false)'; Failure = 'one sweep never releases more than its budget' },
    @{ Name = 'releases-a-pending-aggressor'; From = 'if (CorpseAggressors.IsPendingAggressor(player, world))'; To = 'if (false)'; Failure = 'a body that stopped being releasable stays: aggressor' },
    # The damage-source guard: the call site, the measured tripwire rule (a
    # tripwire leaves TripwireManager the moment it is TRIGGERED, so reading only
    # armed ones is exactly the gap the 2026-09-17 raids fell into), the quiet
    # period behind BotMemory.GetHit's timer, and the fail-closed catch.
    @{ Name = 'releases-a-named-damage-source'; From = 'if (!DamageSourcesQuiet(corpse, state, world, player, isOwnCorpse))'; To = 'if (false)'; Failure = 'a body a tripwire still names stays hidden: Wait' },
    @{ Name = 'forgets-a-triggered-tripwire'; Target = 'DamageSources';
       From = $arenaTripwireFinished.Replace("`r`n", "`n"); To = "        => state != ETripwireState.Wait;";
       Failure = 'a body a tripwire still names stays hidden: Active' },
    @{ Name = 'releases-inside-the-hit-delay'; From = 'return Time.time - state.SourcesQuietSince >= DamageSourceQuietSeconds;'; To = 'return true;'; Failure = 'a body is not released on the first reading that finds it quiet: Wait' },
    @{ Name = 'trusts-an-unreadable-world'; Target = 'DamageSources';
       From = $arenaSourcesFailClosed.Replace("`r`n", "`n"); To = "            WarnOnce(ex.GetBaseException().Message);`n            return null;";
       Failure = 'a body stays hidden while the world cannot say what still names it: no-processor' },
    # Without its own branch a flare falls to the fail-closed default and holds
    # EVERY body while it flies - safe, but no longer the shooter's alone.
    @{ Name = 'holds-every-flare'; Target = 'DamageSources';
       From = $arenaFlareBranch.Replace("`r`n", "`n"); To = '';
       Failure = 'a source that names nobody holds nothing, on the ordinary schedule: other-flare' },
    # A smoke stays registered for the rest of the raid after it goes off, so
    # counting it would hold its thrower for good, for an explosion of no reach.
    @{ Name = 'holds-behind-a-smoke'; Target = 'DamageSources';
       From = 'if (Same(grenade.ProfileId, profileId) && CanReachAnybody(grenade.WeaponSource))';
       To = 'if (Same(grenade.ProfileId, profileId))';
       Failure = 'a source that names nobody holds nothing, on the ordinary schedule: smoke' },
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
    $arenaOnSources = $arenaMutant.ContainsKey('Target') -and $arenaMutant.Target -eq 'DamageSources'
    $arenaText = if ($arenaOnSources) { $arenaSourcesNormalized } else { $arenaNormalized }
    $arenaProperty = if ($arenaOnSources) { 'DamageSourcesSource' } else { 'CleanupSource' }
    if (-not $arenaText.Contains($arenaMutant.From)) { throw "Missing mutation anchor: $($arenaMutant.Name)" }
    $arenaMutated = $arenaText.Replace($arenaMutant.From, $arenaMutant.To)
    if ($arenaMutant.ContainsKey('From2')) {
        if (-not $arenaMutated.Contains($arenaMutant.From2)) { throw "Missing second mutation anchor: $($arenaMutant.Name)" }
        $arenaMutated = $arenaMutated.Replace($arenaMutant.From2, $arenaMutant.To2)
    }
    $arenaPath = Join-Path $arenaMutantDir ($arenaMutant.Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaMutated)
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project $arenaProject -c Release "-p:${arenaProperty}=$arenaPath" 2>&1 | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    if ($arenaExit -eq 0 -or -not $arenaOutput.Contains($arenaMutant.Failure)) {
        throw "Mutant was not rejected by its intended assertion: $($arenaMutant.Name)`n$arenaOutput"
    }
    Write-Output "Rejected mutant: $($arenaMutant.Name)"
}
if ((Get-ArenaSha256 $arenaSource) -ne $arenaHash) { throw 'Production source changed' }
if ((Get-ArenaSha256 $arenaSourcesSource) -ne $arenaSourcesHash) { throw 'Production damage-source guard changed' }
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production checks failed' }
Write-Output 'ARENA_CORPSE_CLEANUP_REGRESSION_PASSED'
