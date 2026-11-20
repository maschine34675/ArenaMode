$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$patch=Join-Path $repo 'ArenaMode.Client/Audio/ArenaAudioContainerReturnGuardPatch.cs'
$policy=Join-Path $repo 'ArenaMode.Client/Audio/AudioContainerReturnPolicy.cs'
$plugin=Join-Path $repo 'ArenaMode.Client/Plugin.cs'
$driver=Join-Path $repo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
function Fault($name,$path,$property,$find,$replace,$failure) { @{Name=$name;Path=$path;Property=$property;Find=$find;Replace=$replace;Failure=$failure} }
function PatchFault($name,$find,$replace,$failure) { Fault $name $patch 'PatchSource' $find $replace $failure }
function PolicyFault($name,$find,$replace,$failure) { Fault $name $policy 'PolicySource' $find $replace $failure }
$faults=@(
    # The guards, one mutant each. Every Find occurs exactly once (-RequireSingleSite).
    (PatchFault 'is-active-test-removed' 'if (!ArenaSession.IsActive)' 'if (false)' 'a session that is not active is decided before anything of the game is read'),
    (PatchFault 'exact-raid-test-removed' '|| !IsExactActiveRaid())' ')' 'an active session with another game than the started one is left alone'),
    (PatchFault 'exactness-reduced-to-active' 'ArenaSession.IsExactActiveRaid(Singleton<AbstractGame>.Instance, Singleton<GameWorld>.Instance)' 'ArenaSession.IsActive' 'an active session with another game than the started one is left alone'),
    (PatchFault 'pool-null-not-guarded' '_audioContainersPool?.gparam_0' '_audioContainersPool.gparam_0' 'a container data without a pool is left to the game'),
    (PatchFault 'null-container-skipped' 'if (__0 == null ||' 'if (__0 == null) return false; if (' 'a null container is left to the game'),
    # The decision.
    (PatchFault 'never-skips' "            return false;`n        }`n        catch" "            return true;`n        }`n        catch" 'a container returned twice is pooled once'),
    (PatchFault 'skips-every-return' 'if (!AudioContainerReturnPolicy.IsAlreadyPooled(slots, __0))' 'if (false)' 'a container returned once is pooled as in the game'),
    (PatchFault 'skip-decided-on-the-wrong-object' 'AudioContainerReturnPolicy.IsAlreadyPooled(slots, __0)' 'AudioContainerReturnPolicy.IsAlreadyPooled(slots, slots?[0])' 'a distinct container that compares equal is pooled'),
    (PatchFault 'failure-skips-the-game' "            return true;`n        }`n    }" "            return false;`n        }`n    }" 'an exception inside the prefix lets the game''s method run'),
    # The policy.
    (PolicyFault 'policy-default-equality' 'if (ReferenceEquals(slots[i], container))' 'if (Equals(slots[i], container))' 'a distinct container that compares equal is pooled'),
    (PolicyFault 'policy-misses-the-last-slot' 'i < slots.Length;' 'i < slots.Length - 1;' 'the policy finds a container in the last slot'),
    (PolicyFault 'policy-never-finds' '                return true;' '                return false;' 'the policy finds a container in any slot'),
    (PolicyFault 'policy-reads-only-the-first-slot' 'return true;' 'return true; else break;' 'the policy finds a container in any slot'),
    (PolicyFault 'policy-null-container-pooled' 'if (container == null || slots == null)' 'if (slots == null)' 'the policy never calls a null container or a null array pooled'),
    # Failure and its report.
    (PatchFault 'failure-swallowed-without-report' 'if (!_loggedFailure)' 'if (false)' 'an exception inside the prefix is reported'),
    (PatchFault 'failure-reported-every-time' 'if (!_loggedFailure)' 'if (true)' 'an exception inside the prefix is reported once per raid'),
    (PatchFault 'failure-never-marked-reported' '_loggedFailure = true;' '' 'an exception inside the prefix is reported once per raid'),
    (PatchFault 'reset-keeps-the-failure-flag' '=> _loggedFailure = false;' '{ }' 'Reset arms the report again'),
    (PatchFault 'failure-reaches-the-game' '_loggedFailure = true;' '_loggedFailure = true; throw;' 'an exception inside the patch never reaches the game'),
    (PatchFault 'skip-logged-as-warning' 'Plugin.ModLogger.LogDebug(' 'Plugin.ModLogger.LogWarning(' 'the skipped return is logged once at Debug with the container''s id'),
    (PatchFault 'skip-not-logged' "            Plugin.ModLogger.LogDebug(`n" "            if (false) Plugin.ModLogger.LogDebug(`n" 'the skipped return is logged once at Debug with the container''s id'),
    # Hooks and wiring.
    (PatchFault 'prefix-attribute-missing' '[PatchPrefix]' '' 'Prefix is a Harmony prefix'),
    (PatchFault 'container-parameter-renamed' @('AudioSourceContainer __0)','if (__0 == null ||','IsAlreadyPooled(slots, __0)','sound container {__0.ID}') @('AudioSourceContainer container)','if (container == null ||','IsAlreadyPooled(slots, container)','sound container {container.ID}') 'the prefix''s container parameter is named __0 and is by value'),
    (PatchFault 'wrong-target-method' 'nameof(AudioSourceContainerData.ReturnContainer)' '"GetContainer"' 'the patch targets AudioSourceContainerData.ReturnContainer(AudioSourceContainer)'),
    (PatchFault 'wrong-target-signature' 'new[] { typeof(AudioSourceContainer) });' 'new Type[0]);' 'the patch targets AudioSourceContainerData.ReturnContainer(AudioSourceContainer)'),
    (PatchFault 'enable-failure-escapes' 'pooled twice as in the game: {ex.Message}");' 'pooled twice as in the game: {ex.Message}"); throw;' 'EnablePatch never throws'),
    (PatchFault 'enable-does-nothing' 'new ArenaAudioContainerReturnGuardPatch().Enable();' '' 'EnablePatch enables the patch'),
    # Wiring that cannot be linked.
    (Fault 'plugin-does-not-enable' $plugin 'PluginText' 'Audio.ArenaAudioContainerReturnGuardPatch.EnablePatch();' '' 'Plugin.Load enables the audio container guard directly after the backup profile patch'),
    (Fault 'raid-reset-does-not-reset-the-guard' $driver 'DriverText' 'Audio.ArenaAudioContainerReturnGuardPatch.Reset();' '' 'the raid reset resets the audio container guard directly after the backup profile patch')
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'AudioContainerGuardChecks.csproj') 'ARENA_AUDIO_CONTAINER_GUARD_BASELINE_PASSED' @() $faults -RequireSingleSite
Write-Output 'ARENA_AUDIO_CONTAINER_GUARD_CHECKS_PASSED'
