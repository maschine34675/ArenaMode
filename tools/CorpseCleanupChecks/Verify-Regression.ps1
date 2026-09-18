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
    # Falls through to the ordinary (bottom) path instead of the LeftRegistry
    # fast path for a body that HAS left the registry - and since nothing
    # names it there either, the ordinary path's SourcesQuietSince (already
    # primed at trim time, when nothing named it yet) is already past
    # DamageSourceQuietSeconds by the first sweep, releasing it a whole sweep
    # earlier than the correct LeftRegistry path's own freshly-started
    # ShotQuietSince timer would (see the "Own corpses release too" tests
    # added for the DamageSourceQuietSeconds bridge, 2026-09-18 review pass).
    @{ Name = 'inverts-the-registry-condition';
       From = 'state.LeftRegistry = !CorpseDamageSources.StillNamedInRegistry(player, corpse.PlayerProfileID, world);';
       To = 'state.LeftRegistry = CorpseDamageSources.StillNamedInRegistry(player, corpse.PlayerProfileID, world);';
       Failure = 'the first reading that finds the older own corpse has left the registry, with nothing in flight, starts its quiet period rather than releasing it on its own' },
    # The shot-in-flight refinement (2026-09-18 review pass, major finding):
    # once the registry has moved on, a directly-fired shot is the one source
    # that does NOT resolve lazily (Shot.Player is frozen at the moment of
    # firing), so a reference check against this exact body is what still
    # holds it. Dropping the check no longer shows up as an early RELEASE by
    # itself - the DamageSourceQuietSeconds bridge added for the
    # BotMemory.GetHit deferred-read hazard (2026-09-18 review pass, minor
    # finding) holds every LeftRegistry own corpse for at least one sweep
    # regardless, shot-in-flight or not. What the dropped check still changes
    # is whether the shot-in-flight hold is ever entered (and logged) at all.
    @{ Name = 'ignores-an-own-bodys-own-shot-in-flight';
       From = 'if (CorpseDamageSources.ShotStillInFlightFromBody(player, world))';
       To = 'if (false)';
       Failure = 'the shot-in-flight hold is said, distinct from the ordinary damage-source hold line' },
    # The DamageSourceQuietSeconds bridge for the LeftRegistry branch itself
    # (2026-09-18 review pass, minor finding): a shot landing (leaving the
    # flight list) is not the end of the BotMemory.GetHit deferred-read
    # hazard - up to ~0.4s can still be pending. Reverting to an immediate
    # release the moment nothing is in flight anymore skips that bridge.
    @{ Name = 'skips-the-left-registry-quiet-period';
       From = 'return Time.time - state.ShotQuietSince >= DamageSourceQuietSeconds;';
       To = 'return true;';
       Failure = 'the first reading that finds the older own corpse has left the registry, with nothing in flight, starts its quiet period rather than releasing it on its own' },
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
    # A UNIQUE build output per mutant, not the project's shared bin/. A prior
    # run reported this script failing nondeterministically with an MSB3026
    # "being used by another process" warning at a DIFFERENT mutant each
    # time (2026-09-18 review pass, minor finding); this was not reproduced
    # directly, but giving every mutant its own output directory removes any
    # possibility of one mutant's build touching a file a previous run's
    # process has not yet released, whatever the exact original cause was.
    # (The actually-reproduced part of that finding turned out to be a
    # different bug entirely - see the Contains() fix below.)
    $arenaOutDir = Join-Path $arenaMutantDir ($arenaMutant.Name + '.out')
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaOutput = & dotnet run --project $arenaProject -c Release "-p:${arenaProperty}=$arenaPath" "-p:BaseOutputPath=$arenaOutDir\" 2>&1 | Out-String
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    # PowerShell renders a native process's merged stderr (2>&1) through its
    # own ErrorRecord formatting, which WORD-WRAPS at the host's console
    # width - and an unhandled .NET exception's own dump is exactly what a
    # rejected mutant throws. A long Failure phrase can straddle that wrap
    # and come back with a newline in the middle of it, and because the wrap
    # position also depends on the length of whatever precedes it on screen
    # (this mutant's own GUID-named temp path, different every run), WHICH
    # Failure text gets broken looks random from one run to the next - this
    # is what a prior run diagnosed as a build-output file-lock race; it is
    # not one (reproduced deterministically without any lock, mutant
    # 'pooled-instead-of-destroyed', 2026-09-18 review pass). Collapsing
    # whitespace runs on both sides before comparing survives any wrap.
    $arenaOutputFlat = $arenaOutput -replace '\s+', ' '
    if ($arenaExit -eq 0 -or -not $arenaOutputFlat.Contains(($arenaMutant.Failure -replace '\s+', ' '))) {
        throw "Mutant was not rejected by its intended assertion: $($arenaMutant.Name)`n$arenaOutput"
    }
    Write-Output "Rejected mutant: $($arenaMutant.Name)"
}
if ((Get-ArenaSha256 $arenaSource) -ne $arenaHash) { throw 'Production source changed' }
if ((Get-ArenaSha256 $arenaSourcesSource) -ne $arenaSourcesHash) { throw 'Production damage-source guard changed' }
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production checks failed' }
Write-Output 'ARENA_CORPSE_CLEANUP_REGRESSION_PASSED'
