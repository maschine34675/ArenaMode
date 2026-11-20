$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$patch=Join-Path $repo 'ArenaMode.Client/Bots/ArenaTacticalFreshGearPatch.cs'
$plugin=Join-Path $repo 'ArenaMode.Client/Plugin.cs'
$driver=Join-Path $repo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
function Fault($name,$path,$property,$find,$replace,$failure) { @{Name=$name;Path=$path;Property=$property;Find=$find;Replace=$replace;Failure=$failure} }
function PatchFault($name,$find,$replace,$failure) { Fault $name $patch 'PatchSource' $find $replace $failure }
$faults=@(
    # The patch's guards, one mutant each. Every Find occurs exactly once (-RequireSingleSite).
    (PatchFault 'scav-role-purged' '|| (role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR)' '' 'a scav demand purges nothing'),
    (PatchFault 'purge-without-delete' 'if (!withDelete)' 'if (false)' 'a demand that deletes nothing purges nothing'),
    (PatchFault 'purge-on-cancelled-demand' 'cancellationToken.IsCancellationRequested ||' '' 'a cancelled demand purges nothing'),
    (PatchFault 'null-data-throws' 'data == null || data.SpawnStopped' 'data.SpawnStopped' 'a demand without data is no failure'),
    (PatchFault 'purge-on-stopped-spawn' '|| data.SpawnStopped' '' 'a stopped spawn purges nothing'),
    (PatchFault 'role-result-ignored' 'data._profileData?.TryGetRole(out var role, out var difficulty) != true' 'data._profileData.TryGetRole(out var role, out var difficulty) & false' 'a demand without a role purges nothing'),
    (PatchFault 'exact-raid-not-required' 'ArenaSession.IsExactActiveRaid(game, world) &&' '' 'a raid that is not active purges nothing'),
    (PatchFault 'exactness-not-required' 'ArenaSession.IsExactActiveRaid(game, world)' 'ArenaSession.IsActive' 'a raid that is not the exact one purges nothing'),
    (PatchFault 'started-game-not-required' '&& game?.Status == GameStatus.Started' '' 'a game that is not started purges nothing'),
    (PatchFault 'creator-game-not-checked' 'ReferenceEquals(creator._game, game) &&' '' 'a creator of another game purges nothing'),
    (PatchFault 'creator-profile-client-not-checked' '&& ReferenceEquals(creator._creatorProfile, provider)' '' 'a creator of another profile client purges nothing'),
    (PatchFault 'data-creator-not-checked' '&& ReferenceEquals(data._botCreator, creator);' ';' 'a demand of another creator purges nothing'),
    (PatchFault 'bear-role-not-purged' '&& role != WildSpawnType.pmcBEAR' '' 'a BEAR demand drops its own profiles outside the band, both sides'),
    # Which raids and modes.
    (PatchFault 'other-modes-purged' '|| !ArenaDifficultyPolicy.IsTacticalMode(ArenaSession.ActiveMode)' '' 'Gun Game purges nothing'),
    (PatchFault 'team-arena-purged' '|| PitFireTeamCompat.TeamArenaActive' '' 'a protected team raid purges nothing'),
    (PatchFault 'no-acknowledged-round-purged' 'ArenaTacticalOpening.CurrentGearLevel is not int centre' '(int?)(ArenaTacticalOpening.CurrentGearLevel ?? 25) is not int centre' 'a raid with no acknowledged round purges nothing'),
    # The scan.
    (PatchFault 'purge-ignores-role' '|| info.Settings.Role != role' '' 'profiles of other roles are untouched'),
    (PatchFault 'purge-ignores-difficulty' '|| info.Settings.BotDifficulty != difficulty' '' 'profiles of another difficulty are untouched'),
    (PatchFault 'lower-side-only' '|| !ArenaDifficultyPolicy.IsTacticalGearOutOfBand(info.Level, centre))' '|| info.Level >= Math.Max(1, centre - ArenaDifficultyPolicy.GearBandSpread))' 'profiles below and above the band of the acknowledged round are dropped'),
    (PatchFault 'upper-side-only' '|| !ArenaDifficultyPolicy.IsTacticalGearOutOfBand(info.Level, centre))' '|| info.Level <= centre + ArenaDifficultyPolicy.GearBandSpread)' 'profiles below and above the band of the acknowledged round are dropped'),
    (PatchFault 'purge-everything-of-the-pair' '|| !ArenaDifficultyPolicy.IsTacticalGearOutOfBand(info.Level, centre))' '|| false)' 'a profile at the lower edge, the centre and the upper edge of the band is kept'),
    (PatchFault 'pool-purged-while-deciding' 'outside.Add(profile);' 'outside.Add(profile); __instance._simpleProfiles = new List<Profile>(__instance._simpleProfiles); __instance._simpleProfiles.Remove(profile);' 'an exception while reading the pool leaves it intact'),
    # The budget: two per (role, difficulty, centre).
    (PatchFault 'budget-unlimited' 'if (spent.Count >= PurgesPerCentre)' 'if (false)' 'the third purge for the same centre is not made'),
    (PatchFault 'budget-off-by-one' 'if (spent.Count >= PurgesPerCentre)' 'if (spent.Count > PurgesPerCentre)' 'the third purge for the same centre is not made'),
    (PatchFault 'world-not-remembered' '_world = world;' '' 'the third purge for the same centre is not made'),
    (PatchFault 'skip-never-marked-reported' '_budget[key] = (spent.Centre, spent.Count, true);' '_budget[key] = (spent.Centre, spent.Count, false);' 'the skip is reported once per pair and centre, the warning once per raid'),
    (PatchFault 'budget-never-recorded' '_budget[key] = (spent.Centre, spent.Count + 1, false);' '' 'the third purge for the same centre is not made'),
    (PatchFault 'budget-three' 'internal const int PurgesPerCentre = 2;' 'internal const int PurgesPerCentre = 3;' 'two purges per centre'),
    (PatchFault 'budget-one' 'internal const int PurgesPerCentre = 2;' 'internal const int PurgesPerCentre = 1;' 'two purges per centre'),
    (PatchFault 'budget-keyed-without-the-centre' 'held.Centre == centre' 'true' 'round 3 acknowledged: the band is 61..71 at the full level, the round 2 profile is dropped'),
    (PatchFault 'budget-shared-by-role' 'var key = (role, difficulty);' 'var key = (WildSpawnType.pmcUSEC, difficulty);' 'a BEAR demand has its own budget'),
    (PatchFault 'budget-shared-by-difficulty' 'var key = (role, difficulty);' 'var key = (role, BotDifficulty.impossible);' 'a demand of another difficulty has its own budget'),
    (PatchFault 'empty-purge-spends-budget' "if (outside.Count == 0)`n                return;" "if (outside.Count == 0)`n            { }" 'a demand that found nothing to drop spends nothing of the budget'),
    (PatchFault 'budget-survives-a-new-world' 'if (!ReferenceEquals(world, _world))' 'if (false)' 'a second raid with the same centre starts with a fresh budget'),
    (PatchFault 'reset-keeps-the-budget' "_budget.Clear();`n        _world = null;" '' 'Reset starts the next raid with a fresh budget'),
    (PatchFault 'world-change-keeps-the-warning' "_budget.Clear();`n                _warnedMismatch = false;" '_budget.Clear();' 'the mismatch warning of the second raid is its own'),
    (PatchFault 'reset-keeps-the-failure-flag' '_loggedFailure = false;' '' 'a failure warns again in the next raid'),
    (PatchFault 'skip-reported-every-time' 'if (!spent.Reported)' 'if (true)' 'the skip is reported once per pair and centre, the warning once per raid'),
    (PatchFault 'skip-says-nothing' 'but {PurgesPerCentre} purges for this level are spent' 'but they are not purged' 'a skipped purge says so once'),
    (PatchFault 'mismatch-warns-every-time' 'if (!_warnedMismatch)' 'if (true)' 'the skip is reported once per pair and centre, the warning once per raid'),
    (PatchFault 'mismatch-never-warns' '_warnedMismatch = true;' '' 'the skip is reported once per pair and centre, the warning once per raid'),
    # Failure, logging, hooks.
    (PatchFault 'failure-warns-every-time' 'if (!_loggedFailure)' 'if (true)' 'a failure warns once per raid'),
    (PatchFault 'exception-reaches-the-game' 'Plugin.ModLogger.LogWarning($"Arena Tactical fresh gear skipped; native generation retained: {ex.Message}");' 'throw;' 'an exception inside the patch never reaches the game'),
    (PatchFault 'purge-logged-as-warning' "Plugin.ModLogger.LogDebug(`n                `$""Arena Tactical: dropped" "Plugin.ModLogger.LogWarning(`n                `$""Arena Tactical: dropped" 'a purge logs one line with role, difficulty, count, lowest, highest and the gear level'),
    (PatchFault 'lowest-level-not-tracked' 'lowest = Math.Min(lowest, info.Level);' '' 'a purge logs one line with role, difficulty, count, lowest, highest and the gear level'),
    (PatchFault 'highest-level-not-tracked' 'highest = Math.Max(highest, info.Level);' '' 'a purge logs one line with role, difficulty, count, lowest, highest and the gear level'),
    (PatchFault 'prefix-attribute-missing' '[PatchPrefix]' '' 'Prefix is a Harmony prefix with the target''s parameter names'),
    (PatchFault 'wrong-target-signature' 'typeof(CancellationToken), typeof(bool) });' 'typeof(CancellationToken) });' 'the patch targets BotProfileClient.CreateProfile(data, cancellationToken, withDelete)'),
    (PatchFault 'enable-failure-escapes' 'served as before: {ex.Message}");' 'served as before: {ex.Message}"); throw;' 'a failing Enable never reaches Plugin.Load'),
    (PatchFault 'enable-does-nothing' 'new ArenaTacticalFreshGearPatch().Enable();' '' 'EnablePatch enables the patch'),
    # Wiring that cannot be linked.
    (Fault 'plugin-does-not-enable' $plugin 'PluginText' 'Bots.ArenaTacticalFreshGearPatch.EnablePatch();' '' 'Plugin.Load enables the Tactical fresh gear patch right after the Gun Game one'),
    (Fault 'raid-reset-does-not-reset-the-patch' $driver 'DriverText' 'Bots.ArenaTacticalFreshGearPatch.Reset();' '' 'the raid reset resets the Tactical fresh gear patch beside the Gun Game one'),
    (Fault 'raid-reset-keeps-the-state' $driver 'DriverText' 'Bots.ArenaTacticalOpening.Reset();' '' 'the raid reset unpublishes the Tactical opening state')
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'TacticalFreshGearChecks.csproj') 'ARENA_TACTICAL_FRESH_GEAR_BASELINE_PASSED' @() $faults -RequireSingleSite
Write-Output 'ARENA_TACTICAL_FRESH_GEAR_CHECKS_PASSED'
