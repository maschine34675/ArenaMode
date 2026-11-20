param([string]$SptRoot = 'D:\SPT41', [string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'tools\Invoke-ArenaSourceChecks.ps1')
$arguments = if ($ReleaseDirectory) { @([IO.Path]::GetFullPath($ReleaseDirectory)) } else { @() }
$adapter = Join-Path $repo 'ArenaMode.Client\Compat\ArenaTarkovaColaCompat.cs'
$catalog = Join-Path $repo 'ArenaMode.Shared\TarkovaColaResearchCatalog.cs'
$faults = @(
    @{Name='new-writes-not-protected'; Path=$adapter; Environment='ARENA_COLA_ADAPTER_SOURCE';
      Find='private static bool PersistPrefix() => !Blocks;'; Replace='private static bool PersistPrefix() => true;';
      Failure='ordinary persistence passes through'},
    @{Name='machines-reinitialized-on-respawn'; Path=$adapter; Environment='ARENA_COLA_ADAPTER_SOURCE';
      Find='Patch(contract.MachinesReady, nameof(PlayerReadyPrefix))'; Replace='Patch(contract.MachinesReady, nameof(ArenaTickPrefix))';
      Failure='machine ready hook uses existing physical-rebind guard'},
    @{Name='new-perk-cleanup-removed'; Path=$adapter; Environment='ARENA_COLA_ADAPTER_SOURCE';
      Find='foreach (var reset in _contract.NewPerkCleanup) Invoke(reset);'; Replace='// removed';
      Failure='shared fuse/container cleanup runs before release'},
    @{Name='late-response-guard-removed'; Path=$adapter; Environment='ARENA_COLA_ADAPTER_SOURCE';
      Find='Patch(contract.RerollCallback, nameof(RerollCallbackPrefix), nameof(RerollCallbackPostfix), finalizer: nameof(RerollCallbackFinalizer))'; Replace='Patch(contract.RerollCallback, nameof(PersistPrefix))';
      Failure='late HTTP continuation bound'},
    @{Name='old-callback-accepted'; Path=$adapter; Environment='ARENA_COLA_ADAPTER_SOURCE';
      Find='|| !owner.Lease.CanApply(owner.World, owner.Body)) return false;'; Replace='|| !owner.Lease.CanApply(owner.World, owner.Body)) { }';
      Failure='old callback cannot clear a new request busy latch'},
    @{Name='wrong-container-accepted'; Path=$adapter; Environment='ARENA_COLA_ADAPTER_SOURCE';
      Find='if (!ReferenceEquals(__1, response.Container)) return false;'; Replace='// pairing removed';
      Failure='response cannot target a different container in the same life'},
    @{Name='current-catalog-fingerprint-stale'; Path=$catalog; Property='ColaCatalogSource';
      Find='a5aa369825bddc1cf03e7f2f88c8017d5446159a5209fd9a1d1365a21f70b802';
      Replace='ea2109653203d5d45b8ce2ffaecbe1aae2127d43544514ae36e81882e4f8db1d';
      Failure='released IL matches reviewed research catalog'}
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'ColaReleaseChecks.csproj') -Token 'PASS ColaReleaseChecks' `
    -Properties @("-p:SPTPath=$SptRoot", "-p:CustomAfterMicrosoftCommonTargets=$repo\tools\AirPitChecks\NoDeploy.targets", "-p:SPTUserMods=$repo\artifacts\cola-release-must-not-exist") `
    -Arguments $arguments -Faults $faults -FailurePrefix 'FAIL ColaReleaseChecks: ' -RequireSingleSite
