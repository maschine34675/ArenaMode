$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaTracerChecks.csproj'
$colorPath = Join-Path $repoRoot 'ArenaMode.Client\Effects\ArenaTracerColor.cs'
$teamsPath = Join-Path $repoRoot 'ArenaMode.Client\Effects\ArenaTracerTeams.cs'
$patchPath = Join-Path $repoRoot 'ArenaMode.Client\Effects\ArenaTracerPatches.cs'
$matchServicePath = Join-Path $repoRoot 'ArenaMode.Client\Modes\ArenaMatchService.cs'
$tacticalPath = Join-Path $repoRoot 'ArenaMode.Client\Modes\TacticalMode.cs'
$settingsPath = Join-Path $repoRoot 'ArenaMode.Client\Config\Settings.cs'
$settings = [System.IO.File]::ReadAllText($settingsPath)
$color = [System.IO.File]::ReadAllText($colorPath)
$teams = [System.IO.File]::ReadAllText($teamsPath)
$patch = [System.IO.File]::ReadAllText($patchPath)
$matchService = [System.IO.File]::ReadAllText($matchServicePath)
$tactical = [System.IO.File]::ReadAllText($tacticalPath)
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaTracerMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase(
    [string]$name,
    [string]$colorSource,
    [string]$teamsSource,
    [string]$patchSource,
    [string]$matchServiceSource,
    [string]$tacticalSource,
    [string]$expectedFailure,
    [string]$settingsSource = '') {
    $colorCopy = Join-Path $scratch ($name + '-color.cs')
    $teamsCopy = Join-Path $scratch ($name + '-teams.cs')
    $patchCopy = Join-Path $scratch ($name + '-patch.cs')
    $matchServiceCopy = Join-Path $scratch ($name + '-match-service.cs')
    $tacticalCopy = Join-Path $scratch ($name + '-tactical.cs')
    [System.IO.File]::WriteAllText($colorCopy, $colorSource)
    [System.IO.File]::WriteAllText($teamsCopy, $teamsSource)
    [System.IO.File]::WriteAllText($patchCopy, $patchSource)
    [System.IO.File]::WriteAllText($matchServiceCopy, $matchServiceSource)
    [System.IO.File]::WriteAllText($tacticalCopy, $tacticalSource)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $env:ARENA_TRACER_MATCH_SERVICE_SOURCE = $matchServiceCopy
    $env:ARENA_TRACER_TACTICAL_SOURCE = $tacticalCopy
    $env:ARENA_TRACER_REPO_ROOT = $repoRoot
    if ($settingsSource) {
        $settingsCopy = Join-Path $scratch ($name + '-settings.cs')
        [System.IO.File]::WriteAllText($settingsCopy, $settingsSource)
        $env:ARENA_TRACER_SETTINGS_SOURCE = $settingsCopy
    }
    try {
        $output = & dotnet run --project $project -c Release `
            "-p:ProductionColorSource=$colorCopy" `
            "-p:ProductionTeamsSource=$teamsCopy" `
            "-p:ProductionPatchSource=$patchCopy" `
            "-p:OutputPath=$outputPath" `
            "-p:IntermediateOutputPath=$intermediatePath" 2>&1
        $resultCode = $LASTEXITCODE
    }
    finally {
        Remove-Item Env:ARENA_TRACER_MATCH_SERVICE_SOURCE -ErrorAction SilentlyContinue
        Remove-Item Env:ARENA_TRACER_TACTICAL_SOURCE -ErrorAction SilentlyContinue
        Remove-Item Env:ARENA_TRACER_REPO_ROOT -ErrorAction SilentlyContinue
        Remove-Item Env:ARENA_TRACER_SETTINGS_SOURCE -ErrorAction SilentlyContinue
    }
    $output | ForEach-Object { Write-Output $_ }
    if ($resultCode -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($expectedFailure)) {
        throw "$name did not fail at the expected boundary (exit $resultCode). Evidence: $scratch"
    }
}

$arenaGate = 'if (!ArenaSession.IsActive || Settings.TracerEntry(ArenaSession.ActiveMode, ArenaTracerTeams.ProtectedTeamMatch)?.Value != true)'
if ([regex]::Matches($patch, [regex]::Escape($arenaGate)).Count -ne 1) {
    throw 'Expected exactly one active-Arena renderer gate; refusing an ambiguous mutation.'
}
Invoke-NegativeCase 'ordinary-raid-leak' $color $teams ($patch.Replace($arenaGate, 'if (false)')) $matchService $tactical `
    'ordinary raid runs complete native tracer method'

Invoke-NegativeCase 'lost-protected-team-selection' $color $teams ($patch.Replace('ArenaSession.ActiveMode, ArenaTracerTeams.ProtectedTeamMatch', 'ArenaSession.ActiveMode, false')) $matchService $tactical `
    'renderer selects protected team preference from frozen tracer match'

# Colored tracers are team-only (2026-10-08). The renderer prefix must stay native
# for a mode without a tracer entry, and Settings.TracerEntry must keep mapping
# only Team DM and protected Tactical 5v5 to TeamTracers.
$entryGate = 'Settings.TracerEntry(ArenaSession.ActiveMode, ArenaTracerTeams.ProtectedTeamMatch)?.Value != true'
if ([regex]::Matches($patch, [regex]::Escape($entryGate)).Count -ne 1) {
    throw 'Expected exactly one tracer entry test in the renderer gate; refusing an ambiguous mutation.'
}
Invoke-NegativeCase 'null-entry-renders' $color $teams ($patch.Replace($entryGate, 'Settings.TracerEntry(ArenaSession.ActiveMode, ArenaTracerTeams.ProtectedTeamMatch)?.Value == false')) $matchService $tactical `
    'renderer stays native in solo mode deathmatch regardless of TeamTracers'

$teamEntry = 'ArenaModeCatalog.TeamDeathmatchId => TeamTracers,'
if ([regex]::Matches($settings, [regex]::Escape($teamEntry)).Count -ne 1) {
    throw 'Expected exactly one Team DM tracer entry mapping; refusing an ambiguous mutation.'
}
Invoke-NegativeCase 'tracer-entry-solo-leak' $color $teams $patch $matchService $tactical `
    'TracerEntry maps only Team DM and protected Tactical/Zombie to TeamTracers' `
    ($settings.Replace($teamEntry, 'ArenaModeCatalog.DeathmatchId or ArenaModeCatalog.TeamDeathmatchId => TeamTracers,'))

$protectedPolicy = 'if (!verifiedPlayerIdentity'
if ([regex]::Matches($teams, [regex]::Escape($protectedPolicy)).Count -ne 1) {
    throw 'Expected exactly one protected-team unknown policy; refusing an ambiguous mutation.'
}
$verifiedGateMutant = $teams.Replace($protectedPolicy, 'if (false')
Invoke-NegativeCase 'unverified-known-id-colored' $color $verifiedGateMutant $patch $matchService $tactical `
    'known local ID without a matching shot bridge stays native'

$nonTracerInsertionPoint = 'string profileId = shot?.PlayerProfileID?.Trim();'
if ([regex]::Matches($patch, [regex]::Escape($nonTracerInsertionPoint)).Count -ne 1) {
    throw 'Expected exact profile resolution point; refusing an ambiguous mutation.'
}
$requiresNativeTracer = $nonTracerInsertionPoint + "`r`n                if (shot?.Ammo is Ammo nativeAmmo && !nativeAmmo.Tracer) return true;"
$mutatedPatch = $patch.Replace($nonTracerInsertionPoint, $requiresNativeTracer)
Invoke-NegativeCase 'nontracer-not-forced' $color $teams $mutatedPatch $matchService $tactical `
    'known local shot replaces native path'

function Remove-OnceAfter(
    [string]$source,
    [string]$anchor,
    [string]$needle) {
    $anchorIndex = $source.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($anchorIndex -lt 0) {
        throw "Lifecycle mutation anchor not found: $anchor"
    }
    $needleIndex = $source.IndexOf($needle, $anchorIndex, [System.StringComparison]::Ordinal)
    if ($needleIndex -lt 0) {
        throw "Lifecycle mutation target not found after: $anchor"
    }
    return $source.Remove($needleIndex, $needle.Length)
}

$resetCall = 'Effects.ArenaTracerTeams.Reset();'
$withoutStartFailureReset = Remove-OnceAfter $matchService 'catch (Exception ex)' $resetCall
Invoke-NegativeCase 'start-failure-state-leak' $color $teams $patch $withoutStartFailureReset $tactical `
    'StartMatch failure, raid end and new-raid recovery each own one tracer reset'

$withoutRaidEndReset = Remove-OnceAfter $matchService 'private static void OnRaidEnded()' $resetCall
Invoke-NegativeCase 'raid-end-state-leak' $color $teams $patch $withoutRaidEndReset $tactical `
    'StartMatch failure, raid end and new-raid recovery each own one tracer reset'

$withoutRecoveryReset = Remove-OnceAfter $matchService 'internal static void RecoverAtNewRaidStart()' $resetCall
Invoke-NegativeCase 'new-raid-state-leak' $color $teams $patch $withoutRecoveryReset $tactical `
    'StartMatch failure, raid end and new-raid recovery each own one tracer reset'

$factionFallback = 'return !_protectedTeamMatch && !_factionTeamMatch'
if (-not $teams.Contains($factionFallback)) { throw 'Faction unknown-shooter guard not found' }
Invoke-NegativeCase 'unowned-faction-colored' $color ($teams.Replace($factionFallback, 'return !_protectedTeamMatch')) $patch $matchService $tactical `
    'unowned faction body stays native'
$factionSwitch = 'if (other.Contains(id)) return false;'
if (-not $teams.Contains($factionSwitch)) { throw 'Faction history guard not found' }
Invoke-NegativeCase 'faction-history-switch' $color ($teams.Replace($factionSwitch, 'if (false) return false;')) $patch $matchService $tactical `
    'dead or live faction identities cannot change sides'

$squadRange = 'expectedCompanionCount is >= 1 and <= 4'
$squadCount = 'followers?.Count == expectedCompanionCount'
$squadStable = '(_expectedCompanionCount == 0 || _expectedCompanionCount == expectedCompanionCount)'
foreach ($anchor in @($squadRange, $squadCount, $squadStable)) {
    if ([regex]::Matches($teams, [regex]::Escape($anchor)).Count -ne 1) {
        throw "Expected exactly one variable-squad guard: $anchor"
    }
}
Invoke-NegativeCase 'four-only-squad' $color ($teams.Replace($squadRange, 'expectedCompanionCount == 4')) $patch $matchService $tactical `
    '1 allies bind for cooperative=False'
Invoke-NegativeCase 'partial-squad-admitted' $color ($teams.Replace($squadCount, 'true')) $patch $matchService $tactical `
    'variable squad missing follower rejected'
Invoke-NegativeCase 'squad-count-drift' $color ($teams.Replace($squadStable, 'true')) $patch $matchService $tactical `
    'variable squad count cannot drift after freeze'
Invoke-NegativeCase 'invalid-squad-count' $color ($teams.Replace($squadRange, 'true')) $patch $matchService $tactical `
    'cooperative rejects invalid frozen count 0'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production Arena tracer checks failed after mutation testing.'
}
Write-Output "ARENA_TRACER_REGRESSION_VERIFIED (15 mutants rejected: Arena gate, frozen team selection, null-entry renderer, solo TracerEntry leak, identity, non-tracer, three lifecycle resets, two faction policies and four variable-squad guards; evidence: $scratch)"
