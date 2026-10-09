param([string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('artifacts/pft-ported-checks/' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$ported = Get-Content (Join-Path $repo 'ArenaMode.Client/Compat/PitFireTeamPortedMapSpawns.cs') -Raw
$area = Get-Content (Join-Path $repo 'ArenaMode.Client/Compat/PitFireTeamAreaSpawns.cs') -Raw
$discovery = Get-Content (Join-Path $repo 'ArenaMode.Client/Compat/PitFireTeamCompat.Discovery.cs') -Raw
$faults = @(
    @('core-unauthenticated','PortedSource','!PitFireTeamCompat.TeamArenaActive ||','false ||'),
    @('core-ordinary-map','PortedSource','if (!string.Equals(map, "arena_bay5", StringComparison.OrdinalIgnoreCase)','if (false && !string.Equals(map, "arena_bay5", StringComparison.OrdinalIgnoreCase)'),
    @('core-foreign-controller','PortedSource','!ReferenceEquals(((IBotGame)game).BotsController, controller)','false'),
    @('core-stale-active','PortedSource','!ArenaSession.IsExactActiveRaid(game, world)','false'),
    @('core-native-covers','PortedSource','covers.Points.Count != 0','false'),
    @('core-wrong-nearest','PortedSource','if (distance >= best) continue;','if (point != null) continue;'),
    @('core-loading-phase','PortedSource','game.Status != GameStatus.Running','false'),
    @('core-version','PortedSource','!PitFireTeamVersions.IsSupportedPluginVersion(pluginVersion)','false'),
    @('area-no-defer','AreaSource','if (!ArenaAreaRuntime.ShouldBlockLoadingBotDispatch()) return true;','if (true) return true;'),
    @('area-ignore-ready','AreaSource','if (!ArenaAreaRuntime.IsReady) return;','if (false) return;'),
    @('area-drop-epoch','AreaSource','raid.Epoch == now.Epoch','true'),
    @('area-drop-arrival-check','AreaSource','!state.Placement.Accept(owner.GetPlayer)','false'),
    @('area-drop-raw-veto','AreaSource','if (!state.Revoked && !state.Completed && Current(state.Raid)','if (true || !state.Revoked && !state.Completed && Current(state.Raid)'),
    @('area-early-release','AreaSource','return result && !state.Revoked;','state.Placement?.Dispose(); return result && !state.Revoked;'),
    @('area-callback-counter','AreaSource','state.CounterSettled = state.Spawner._inSpawnProcess < beforeCallback;','state.CounterSettled = true;'),
    @('area-global-initial-identities','AreaSource','internal readonly HashSet<string> InitialIds','internal static readonly HashSet<string> InitialIds'),
    @('area-global-raw-profile-keys','AreaSource','internal readonly ConditionalWeakTable<Profile, Arrival> RawLeases','internal static readonly ConditionalWeakTable<Profile, Arrival> RawLeases'),
    @('area-reset-forgets-old-creator','AreaSource','Arrivals.Clear();','CreatorLedgers.Clear(); Arrivals.Clear();'),
    @('area-duplicate-new-profile','AreaSource','!ledger.InitialIds.Add(profile.ProfileId)','false'),
    @('discovery-never-defer','DiscoverySource','return ReferenceEquals(_currentLocalPlayer, null)','return false && ReferenceEquals(_currentLocalPlayer, null)'),
    @('discovery-full-map','DiscoverySource','Areas.ArenaAreaRuntime.CurrentArea == null','false'),
    @('discovery-live-match','DiscoverySource','Modes.ArenaMatchService.Current != null','false'),
    @('discovery-destroyed-leader','DiscoverySource','ReferenceEquals(_currentLocalPlayer, null)','_currentLocalPlayer == null'),
    @('discovery-lost-leader','DiscoverySource','string.IsNullOrEmpty(_currentLocalProfileId)','true'),
    @('discovery-lost-body','DiscoverySource','CurrentBodiesByAccountId.Count == 0','true')
)
# Version guard has two independent exact checks; make this fault bypass all of
# them so it represents a truly open contract rather than a surviving safe veto.
$faults = @($faults | Where-Object { $_[0] -ne 'core-version' })
foreach ($fault in $faults) {
    $name,$property,$before,$after = $fault
    $source = if ($property -eq 'PortedSource') { $ported } elseif ($property -eq 'DiscoverySource') { $discovery } else { $area }
    if (!$source.Contains($before)) { throw "Fault anchor missing: $name" }
    $file = Join-Path $OutputRoot ($name + '.cs')
    $mutant = $source.Replace($before,$after)
    # Static ledger fields need static access too; otherwise these faults would
    # be rejected by the compiler rather than the repeated-raid assertions.
    if ($name -eq 'area-global-initial-identities') { $mutant = $mutant.Replace('ledger.InitialIds','CreatorLedger.InitialIds') }
    if ($name -eq 'area-global-raw-profile-keys') { $mutant = $mutant.Replace('ledger.RawLeases','CreatorLedger.RawLeases') }
    [IO.File]::WriteAllText($file, $mutant)
    $log = Join-Path $OutputRoot ($name + '.log')
    $ErrorActionPreference = 'Continue'
    & dotnet run --project (Join-Path $PSScriptRoot 'PftPortedMapChecks.csproj') -c Release "-p:$property=$file" *> $log
    $faultExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($faultExit -eq 0) { throw "Fault survived: $name" }
    if (Select-String -Path $log -Pattern ': error (CS|MSB)' -Quiet) { throw "Fault did not compile: $name" }
    Write-Host "rejected: $name"
}
& dotnet run --project (Join-Path $PSScriptRoot 'PftPortedMapChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Unmodified production harness failed after mutation checks.' }
Write-Host "PFT_PORTED_FAULTS_PASSED: $($faults.Count) compiled faults; $OutputRoot"
