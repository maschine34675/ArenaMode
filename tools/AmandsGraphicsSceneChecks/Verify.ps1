$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'AmandsGraphicsSceneChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat/ArenaAmandsGraphicsScene.cs'

# The published-release check (the shipped 1.8.0 DLL still holds exactly one reviewed find/get/store sequence) needs the
# third-party DLL, which is never copied into tracked files. It is the archived, git-ignored release of the 2026-11-08 native
# run, accepted only with its pinned hash. Without it the check is skipped, and the script says so: never silently.
$publishedDll = Join-Path $repo 'artifacts/amands-native-20261108/graphics-release/BepInEx/plugins/AmandsGraphics/AmandsGraphics.dll'
$publishedHash = '0BAD292DA16386DE8A737C5C627016030A343256A3670823ABEE805439CB75BD'
$arguments = @()
if ((Test-Path -LiteralPath $publishedDll) -and (Get-FileHash -LiteralPath $publishedDll -Algorithm SHA256).Hash -eq $publishedHash) {
    $arguments = @($publishedDll)
    Write-Output 'Published-DLL check: running against the archived Amand''s Graphics 1.8.0 (SHA-256 matches).'
} else {
    Write-Output 'Published-DLL check SKIPPED: the archived Amand''s Graphics 1.8.0 DLL is absent or its hash differs; only the offline stand-in checks run.'
}

function Fault([string]$name, [string]$find, [string]$replace, [string]$failure) {
    @{ Name = $name; Path = $source; Property = 'AmandsSceneSource'; Find = $find; Replace = $replace; Failure = $failure }
}
$loadingArmLocation = 'string.Equals(settings.location, world.LocationId, StringComparison.OrdinalIgnoreCase)'
$faults = @(
    # Amand's own lookup wins whenever it yields a LevelSettings.
    (Fault 'r1-skip-component-check' 'if (original != null && original.GetComponent<LevelSettings>() != null)' 'if (original != null)' 'named object without a LevelSettings is replaced by the live one'),
    (Fault 'r1-drop-early-return' 'if (original != null && original.GetComponent<LevelSettings>() != null)' 'if (false)' 'Amand''s own object that carries a LevelSettings wins over the live singleton'),
    (Fault 'native-log-leak' 'if (ArenaSession.IsActive || ArenaSession.ArmedForNextRaid) NoteBranch("native", original);' 'NoteBranch("native", original);' 'ordinary raid logs nothing'),
    # The gates in front of the substitution.
    (Fault 'no-arena-gate' 'if (!ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid) return original;' '' 'ordinary raid with a valid-looking arm but no Arena flag preserves the missing lookup'),
    (Fault 'fika' 'FikaCompatCheck.IsFikaLoaded || ' '' 'Fika excluded'),
    (Fault 'camera' '!ReferenceEquals(cameraObject, CameraManager.Instance?.Camera?.gameObject)' 'false' 'foreign camera while armed'),
    (Fault 'camera-null' 'cameraObject == null || ' '' 'absent camera object'),
    (Fault 'world-identity' '!ReferenceEquals(game.GameWorld, world)' 'false' 'different physical world'),
    (Fault 'map-any' '!ArenaMapCatalog.IsSupportedMap(world.LocationId)' 'false' 'unsupported map ''shoreline'' stays untouched in the confirmed raid'),
    (Fault 'map-labyrinth-only' '!ArenaMapCatalog.IsSupportedMap(world.LocationId)' '!string.Equals(world.LocationId, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase)' 'armed first activation resolves on'),
    (Fault 'exact-raid' '!ArenaSession.IsExactActiveRaid(game, world)' 'false' 'stale active world'),
    (Fault 'loading-arm' ': !MatchesLoadingArm(game, world)' ': false' 'armed map differing from the world''s map'),
    (Fault 'armed-map-ignored' '!string.Equals(ArenaSession.ArmedMap, world.LocationId, StringComparison.OrdinalIgnoreCase)' 'false' 'armed map differing from the world''s map'),
    (Fault 'settings-location-ignored' $loadingArmLocation 'true' 'native settings location differing from the world''s map'),
    (Fault 'start-or-end-identity' '(Protocol.IsNativeStartIdentity(settings.serverId, token) || Protocol.HasNativeEndIdentity(settings.serverId, token))' 'true' 'stale native start token'),
    # The live LevelSettings must be usable.
    (Fault 'null-level' 'level == null || ' '' 'missing LevelSettings names the reason'),
    (Fault 'enabled' '!level.isActiveAndEnabled' 'false' 'disabled settings'),
    (Fault 'loaded' '|| !level.gameObject.scene.isLoaded' '' 'unloading scene'),
    (Fault 'component-identity' '|| !ReferenceEquals(level.gameObject.GetComponent<LevelSettings>(), level)' '' 'component mismatch'),
    # This code is never the thrower. The anchor spans lines (compared with LF), the other catch is in GetRejectionReason.
    (Fault 'no-catch' "catch (Exception ex)`n        {`n            // Never the thrower" "catch (Exception ex) when (false)`n        {`n            // Never the thrower" 'an exception inside the Arena lookup ends as Amand''s own result')
)
Test-ArenaSourceFaults $project 'Amand''s Graphics scene checks passed' @() $faults -FailurePrefix '' -RequireSingleSite -Arguments $arguments
