$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$patch=Join-Path $repo 'ArenaMode.Client/Bots/ArenaGunGameFreshGearPatch.cs'
$difficulty=Join-Path $repo 'ArenaMode.Client/Bots/ArenaDifficulty.cs'
$policy=Join-Path $repo 'ArenaMode.Shared/ArenaDifficultyPolicy.cs'
$clientConfig=Join-Path $repo 'ArenaMode.Client/Session/ArenaClientConfig.cs'
$plugin=Join-Path $repo 'ArenaMode.Client/Plugin.cs'
$driver=Join-Path $repo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
function Fault($name,$path,$property,$find,$replace,$failure) { @{Name=$name;Path=$path;Property=$property;Find=$find;Replace=$replace;Failure=$failure} }
$faults=@(
    # The patch's guards, one mutant each. Every Find occurs exactly once (-RequireSingleSite).
    (Fault 'scav-role-purged' $patch 'PatchSource' '|| (role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR)' '' 'a scav demand purges nothing'),
    (Fault 'purge-without-delete' $patch 'PatchSource' 'if (!withDelete)' 'if (false)' 'a demand that deletes nothing purges nothing'),
    (Fault 'purge-on-cancelled-demand' $patch 'PatchSource' 'cancellationToken.IsCancellationRequested ||' '' 'a cancelled demand purges nothing'),
    (Fault 'null-data-throws' $patch 'PatchSource' 'data == null || data.SpawnStopped' 'data.SpawnStopped' 'a demand without data is no failure'),
    (Fault 'purge-on-stopped-spawn' $patch 'PatchSource' '|| data.SpawnStopped' '' 'a stopped spawn purges nothing'),
    (Fault 'role-result-ignored' $patch 'PatchSource' 'data._profileData?.TryGetRole(out var role, out var difficulty) != true' 'data._profileData.TryGetRole(out var role, out var difficulty) & false' 'a demand without a role purges nothing'),
    (Fault 'exact-raid-not-required' $patch 'PatchSource' 'ArenaSession.IsExactActiveRaid(game, world) &&' '' 'a raid that is not active purges nothing'),
    (Fault 'exactness-not-required' $patch 'PatchSource' 'ArenaSession.IsExactActiveRaid(game, world)' 'ArenaSession.IsActive' 'a raid that is not the exact one purges nothing'),
    (Fault 'bear-role-not-purged' $patch 'PatchSource' '&& role != WildSpawnType.pmcBEAR' '' 'a BEAR demand drops its own stale profile'),
    (Fault 'interval-shared-by-role' $patch 'PatchSource' 'var key = (role, difficulty);' 'var key = (WildSpawnType.pmcUSEC, difficulty);' 'a BEAR demand drops its own stale profile'),
    (Fault 'pool-purged-while-deciding' $patch 'PatchSource' 'stale.Add(profile);' 'stale.Add(profile); __instance._simpleProfiles = new List<Profile>(__instance._simpleProfiles); __instance._simpleProfiles.Remove(profile);' 'an exception while reading the pool leaves it intact'),
    (Fault 'started-game-not-required' $patch 'PatchSource' '&& game?.Status == GameStatus.Started' '' 'a game that is not started purges nothing'),
    (Fault 'creator-game-not-checked' $patch 'PatchSource' 'ReferenceEquals(creator._game, game) &&' '' 'a creator of another game purges nothing'),
    (Fault 'creator-profile-client-not-checked' $patch 'PatchSource' '&& ReferenceEquals(creator._creatorProfile, provider)' '' 'a creator of another profile client purges nothing'),
    (Fault 'data-creator-not-checked' $patch 'PatchSource' '&& ReferenceEquals(data._botCreator, creator);' ';' 'a demand of another creator purges nothing'),
    (Fault 'purge-ignores-role' $patch 'PatchSource' '|| info.Settings.Role != role' '' 'profiles of other roles are untouched'),
    (Fault 'purge-ignores-difficulty' $patch 'PatchSource' '|| info.Settings.BotDifficulty != difficulty' '' 'profiles of another difficulty are untouched'),
    (Fault 'purge-everything-of-the-pair' $patch 'PatchSource' '|| !ArenaDifficultyPolicy.IsGunGameGearStale(info.Level, current))' '|| false)' 'a profile exactly ten levels below the current one is kept'),
    (Fault 'stale-arguments-swapped' $patch 'PatchSource' 'ArenaDifficultyPolicy.IsGunGameGearStale(info.Level, current)' 'ArenaDifficultyPolicy.IsGunGameGearStale(current, info.Level)' 'stale profiles of the demanded pair are dropped'),
    (Fault 'interval-not-held' $patch 'PatchSource' '&& now - last < PurgeIntervalSeconds' '&& false' 'a second purge of the same pair within 30 seconds is held back'),
    (Fault 'interval-boundary-held' $patch 'PatchSource' 'now - last < PurgeIntervalSeconds' 'now - last <= PurgeIntervalSeconds' 'the purge resumes after 30 seconds'),
    (Fault 'interval-shared-by-difficulty' $patch 'PatchSource' 'var key = (role, difficulty);' 'var key = (role, BotDifficulty.impossible);' 'the interval is kept per difficulty'),
    (Fault 'interval-never-recorded' $patch 'PatchSource' '_lastPurge[key] = now;' '' 'a second purge of the same pair within 30 seconds is held back'),
    (Fault 'empty-purge-starts-interval' $patch 'PatchSource' 'int lowest = int.MaxValue;' 'int lowest = int.MaxValue; _lastPurge[key] = now;' 'a demand that found nothing to drop starts no interval'),
    (Fault 'interval-survives-new-world' $patch 'PatchSource' 'if (!ReferenceEquals(world, _world))' 'if (false)' 'a new world starts with no interval'),
    (Fault 'reset-keeps-world' $patch 'PatchSource' '_world = null;' '' 'Reset starts the next raid with no interval'),
    (Fault 'reset-keeps-warning-flag' $patch 'PatchSource' '_loggedFailure = false;' '' 'a failure warns again in the next raid'),
    (Fault 'failure-warns-every-time' $patch 'PatchSource' 'if (!_loggedFailure)' 'if (true)' 'a failure warns once per raid'),
    (Fault 'exception-reaches-the-game' $patch 'PatchSource' 'Plugin.ModLogger.LogWarning($"Arena Gun Game fresh gear skipped; native generation retained: {ex.Message}");' 'throw;' 'an exception inside the patch never reaches the game'),
    (Fault 'purge-logged-as-warning' $patch 'PatchSource' 'Plugin.ModLogger.LogDebug(' 'Plugin.ModLogger.LogWarning(' 'a purge logs one line with role, difficulty, count, lowest and current level'),
    (Fault 'lowest-level-not-tracked' $patch 'PatchSource' 'lowest = Math.Min(lowest, info.Level);' '' 'a purge logs one line with role, difficulty, count, lowest and current level'),
    (Fault 'prefix-attribute-missing' $patch 'PatchSource' '[PatchPrefix]' '' 'Prefix is a Harmony prefix with the target''s parameter names'),
    (Fault 'wrong-target-signature' $patch 'PatchSource' 'typeof(CancellationToken), typeof(bool) });' 'typeof(CancellationToken) });' 'the patch targets BotProfileClient.CreateProfile(data, cancellationToken, withDelete)'),
    (Fault 'enable-failure-escapes' $patch 'PatchSource' 'served as before: {ex.Message}");' 'served as before: {ex.Message}"); throw;' 'a failing Enable never reaches Plugin.Load'),
    (Fault 'enable-does-nothing' $patch 'PatchSource' 'new ArenaGunGameFreshGearPatch().Enable();' '' 'EnablePatch enables the patch'),
    # The client reader of the gear level.
    (Fault 'gear-level-outside-gun-game' $difficulty 'DifficultySource' "            if (!IsGunGame)`n                return null;" "            if (false)`n                return null;" 'outside Gun Game there is no gear level'),
    (Fault 'gear-level-with-scale-off' $difficulty 'DifficultySource' 'if (!ArenaClientConfig.GetScaleBotTier(map))' 'if (false)' 'a map with scaleBotTier off has no gear level'),
    (Fault 'gear-level-ignores-start' $difficulty 'DifficultySource' 'ArenaClientConfig.GetBotTierStartLevel(map)' '1' 'the Gun Game gear level uses the map''s start and end level'),
    (Fault 'gear-level-ignores-end' $difficulty 'DifficultySource' 'ArenaClientConfig.GetBotTierEndLevel(map)' '66' 'the Gun Game gear level uses the map''s start and end level'),
    (Fault 'gear-level-ignores-step' $difficulty 'DifficultySource' 'GearLevelAt(Step,' 'GearLevelAt(ArenaDifficultyStep.Maximum,' 'the Gun Game gear level follows the frozen step'),
    (Fault 'gear-level-ignores-progress' $difficulty 'DifficultySource' 'GetBotTierEndLevel(map), Progress);' 'GetBotTierEndLevel(map), 1f);' 'the Gun Game gear level opens at the start level'),
    (Fault 'gear-level-on-exception' $difficulty 'DifficultySource' "            catch (Exception)`n            {`n                return null;" "            catch (Exception)`n            {`n                return 1;" 'an unreadable config gives no gear level'),
    # The policy.
    (Fault 'slack-halved' $policy 'PolicySource' 'public const int GunGameStaleGearLevels = 10;' 'public const int GunGameStaleGearLevels = 5;' 'the slack is ten levels'),
    (Fault 'stale-boundary-inclusive' $policy 'PolicySource' '=> profileLevel < currentGearLevel - GunGameStaleGearLevels;' '=> profileLevel <= currentGearLevel - GunGameStaleGearLevels;' 'ten below is fresh, eleven below is stale'),
    (Fault 'stale-direction-flipped' $policy 'PolicySource' '=> profileLevel < currentGearLevel - GunGameStaleGearLevels;' '=> profileLevel > currentGearLevel - GunGameStaleGearLevels;' 'ten below is fresh, eleven below is stale'),
    # Wiring that cannot be linked.
    (Fault 'plugin-does-not-enable' $plugin 'PluginText' 'Bots.ArenaGunGameFreshGearPatch.EnablePatch();' '' 'Plugin.Load enables the Gun Game fresh gear patch'),
    (Fault 'raid-reset-does-not-reset' $driver 'DriverText' 'Bots.ArenaGunGameFreshGearPatch.Reset();' '' 'the raid reset resets the Gun Game fresh gear patch'),
    (Fault 'start-key-misspelled' $clientConfig 'ClientConfigText' '"botTierStartLevel"' '"botTierStart"' 'client and server read the same botTierStartLevel key'),
    (Fault 'end-key-misspelled' $clientConfig 'ClientConfigText' '"botTierEndLevel"' '"botTierEnd"' 'client and server read the same botTierEndLevel key'),
    (Fault 'scale-key-misspelled' $clientConfig 'ClientConfigText' '"scaleBotTier"' '"scaleTier"' 'client and server read the same scaleBotTier key'),
    (Fault 'start-fallback-changed' $clientConfig 'ClientConfigText' 'GetBotTierStartLevel(string map, int fallback = 1)' 'GetBotTierStartLevel(string map, int fallback = 5)' 'the client''s start level fallback is the server''s default'),
    (Fault 'end-fallback-changed' $clientConfig 'ClientConfigText' 'GetBotTierEndLevel(string map, int fallback = 66)' 'GetBotTierEndLevel(string map, int fallback = 60)' 'the client''s end level fallback is the server''s default'),
    (Fault 'scale-fallback-changed' $clientConfig 'ClientConfigText' 'GetScaleBotTier(string map, bool fallback = true)' 'GetScaleBotTier(string map, bool fallback = false)' 'the client''s scaleBotTier fallback is the server''s default')
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'GunGameFreshGearChecks.csproj') 'ARENA_GUNGAME_FRESH_GEAR_BASELINE_PASSED' @() $faults -RequireSingleSite
Write-Output 'ARENA_GUNGAME_FRESH_GEAR_CHECKS_PASSED'
