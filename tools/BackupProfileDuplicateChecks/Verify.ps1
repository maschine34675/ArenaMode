$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$patch=Join-Path $repo 'ArenaMode.Client/Bots/ArenaBackupProfileDuplicatePatch.cs'
$policy=Join-Path $repo 'ArenaMode.Client/Bots/BackupProfileDuplicatePolicy.cs'
$plugin=Join-Path $repo 'ArenaMode.Client/Plugin.cs'
$driver=Join-Path $repo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
function Fault($name,$path,$property,$find,$replace,$failure) { @{Name=$name;Path=$path;Property=$property;Find=$find;Replace=$replace;Failure=$failure} }
function PatchFault($name,$find,$replace,$failure) { Fault $name $patch 'PatchSource' $find $replace $failure }
function PolicyFault($name,$find,$replace,$failure) { Fault $name $policy 'PolicySource' $find $replace $failure }
$faults=@(
    # The guards, one mutant each. Every Find occurs exactly once (-RequireSingleSite).
    (PatchFault 'is-active-test-removed' 'if (!ArenaSession.IsActive)' 'if (false)' 'a session that is not active is decided before anything of the game is read'),
    (PatchFault 'exact-raid-test-removed' '|| !IsExactActiveRaidOf(__instance)' '' 'an active session with another game than the started one is left alone'),
    (PatchFault 'exactness-reduced-to-active' 'ArenaSession.IsExactActiveRaid(game, world)' 'ArenaSession.IsActive' 'an active session with another game than the started one is left alone'),
    (PatchFault 'creator-game-not-checked' 'ReferenceEquals(creator._game, game) &&' '' 'a creator of another game is left alone'),
    (PatchFault 'provider-identity-test-removed' '&& ReferenceEquals(creator._creatorProfile, provider)' '' 'another profile client than the game''s own is left alone'),
    # The replacement.
    (PatchFault 'argument-not-replaced' '__0 = fresh;' '' 'a reload the pool already holds leaves the callee with an empty list'),
    (PatchFault 'argument-replaced-without-duplicates' 'if (duplicates == 0)' 'if (false)' 'a reload the pool does not hold is passed on unchanged'),
    (PatchFault 'argument-emptied-on-failure' "catch (Exception ex)`n        {`n            if (!_loggedFailure)" "catch (Exception ex)`n        {`n            __0 = new List<Profile>();`n            if (!_loggedFailure)" 'an exception inside the prefix leaves the argument untouched'),
    (PatchFault 'pool-changed-by-the-prefix' 'if (duplicates == 0)' 'pool.Clear(); if (duplicates == 0)' 'the prefix does not change the pool'),
    # The policy.
    (PolicyFault 'policy-default-equality' 'new HashSet<T>(ReferenceComparer<T>.Instance)' 'new HashSet<T>()' 'two distinct profiles that compare equal are both kept'),
    (PolicyFault 'duplicates-counted-but-passed-on' 'duplicates++;' '{ duplicates++; fresh.Add(entry); }' 'a reload the pool already holds leaves the callee with an empty list'),
    (PolicyFault 'duplicates-never-counted' 'duplicates++;' '{ }' 'a reload the pool already holds leaves the callee with an empty list'),
    (PolicyFault 'pool-not-read' 'held.Add(pool[i]);' '_ = held.Count;' 'a reload the pool already holds leaves the callee with an empty list'),
    (PolicyFault 'order-not-kept' 'fresh.Add(entry);' 'fresh.Insert(0, entry);' 'a mixed reload passes on exactly the profiles not yet pooled, in order'),
    (PolicyFault 'reload-duplicates-kept' 'if (held.Add(entry))' 'if (!held.Contains(entry))' 'a profile twice in one reload is passed on once'),
    # Failure and its report.
    (PatchFault 'failure-swallowed-without-report' 'if (!_loggedFailure)' 'if (false)' 'an exception inside the prefix is reported'),
    (PatchFault 'failure-reported-every-time' 'if (!_loggedFailure)' 'if (true)' 'an exception inside the prefix is reported once per raid'),
    (PatchFault 'failure-never-marked-reported' '_loggedFailure = true;' '' 'an exception inside the prefix is reported once per raid'),
    (PatchFault 'reset-keeps-the-failure-flag' '=> _loggedFailure = false;' '{ }' 'Reset arms the report again'),
    (PatchFault 'failure-reaches-the-game' '_loggedFailure = true;' '_loggedFailure = true; throw;' 'an exception inside the patch never reaches the game'),
    (PatchFault 'duplicates-logged-as-warning' 'Plugin.ModLogger.LogDebug(' 'Plugin.ModLogger.LogWarning(' 'the duplicates are logged once at Debug with their count'),
    # Hooks and wiring.
    (PatchFault 'prefix-attribute-missing' '[PatchPrefix]' '' 'Prefix is a Harmony prefix'),
    (PatchFault 'list-parameter-by-value' 'ref List<Profile> __0' 'List<Profile> __0' 'the prefix''s list parameter is named __0 and is by-ref'),
    (PatchFault 'list-parameter-renamed' @('ref List<Profile> __0','var reloaded = __0;','__0 = fresh;') @('ref List<Profile> list','var reloaded = list;','list = fresh;') 'the prefix''s list parameter is named __0 and is by-ref'),
    (PatchFault 'wrong-target-method' 'nameof(BotProfileClient.LoadedByBackup)' '"LoadProfiles"' 'the patch targets BotProfileClient.LoadedByBackup(List<Profile>)'),
    (PatchFault 'wrong-target-signature' 'new[] { typeof(List<Profile>) });' 'new Type[0]);' 'the patch targets BotProfileClient.LoadedByBackup(List<Profile>)'),
    (PatchFault 'enable-failure-escapes' 'pooled twice as in the game: {ex.Message}");' 'pooled twice as in the game: {ex.Message}"); throw;' 'EnablePatch never throws'),
    (PatchFault 'enable-does-nothing' 'new ArenaBackupProfileDuplicatePatch().Enable();' '' 'EnablePatch enables the patch'),
    # Wiring that cannot be linked.
    (Fault 'plugin-does-not-enable' $plugin 'PluginText' 'Bots.ArenaBackupProfileDuplicatePatch.EnablePatch();' '' 'Plugin.Load enables the backup profile patch directly after the Tactical fresh gear patch'),
    (Fault 'raid-reset-does-not-reset-the-patch' $driver 'DriverText' 'Bots.ArenaBackupProfileDuplicatePatch.Reset();' '' 'the raid reset resets the backup profile patch directly after the Tactical opening')
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'BackupProfileDuplicateChecks.csproj') 'ARENA_BACKUP_PROFILE_DUPLICATE_BASELINE_PASSED' @() $faults -RequireSingleSite
Write-Output 'ARENA_BACKUP_PROFILE_DUPLICATE_CHECKS_PASSED'
