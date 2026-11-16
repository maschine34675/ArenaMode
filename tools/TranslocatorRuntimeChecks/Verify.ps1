[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
$runtime = Join-Path $repo 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorRuntime.cs'
$disc = Join-Path $repo 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorDisc.cs'
$geometry = Join-Path $repo 'ArenaMode.Client/Combat/Translocator/ArenaTranslocatorGeometry.cs'
$faults = @(
    @{Name='helper-overlap-mask';Path=$geometry;Property='GeometrySource';Find='Overlaps, CollisionMask, QueryTriggerInteraction.Ignore';Replace='Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore';Failure='helper volume does not block launch'},
    @{Name='helper-sweep-mask';Path=$geometry;Property='GeometrySource';Find='Hits, distance, CollisionMask, QueryTriggerInteraction.Ignore';Replace='Hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore';Failure='helper volume does not bounce flight'},
    @{Name='whole-map-movement-hulls';Path=$geometry;Property='GeometrySource';Find='ArenaAreaRuntime.CurrentArea != null';Replace='true';Failure='whole-map query excludes helper broadphase'},
    @{Name='foreign-movement-hull-solid';Path=$geometry;Property='GeometrySource';Find='!ArenaAreaRuntime.OwnsCollider(collider)';Replace='false';Failure='bounded arena ignores foreign movement hull'},
    @{Name='owned-enclosure-omitted';Path=$geometry;Property='GeometrySource';Find='? (int)LayersMaskController.LowPolyColliderLayerMask : 0';Replace='? 0 : 0';Failure='owned arena enclosure blocks launch'},
    @{Name='map-border-omitted';Path=$geometry;Property='GeometrySource';Find='| LayerMask.GetMask("LevelBorder")';Replace='';Failure='real solid overlap blocks launch'},
    @{Name='default-solid-omitted';Path=$geometry;Property='GeometrySource';Find='(int)LayersMaskController.DefaultLayerMask';Replace='0';Failure='real solid overlap blocks launch'},
    @{Name='loot-solid-omitted';Path=$geometry;Property='GeometrySource';Find='(int)LayersMaskController.LootLayerMask';Replace='0';Failure='real solid overlap blocks launch'},
    @{Name='own-spirit-not-excluded';Path=$geometry;Property='GeometrySource';Find='ReferenceEquals(collider.GetComponentInParent<PlayerSpiritBase>()?._player, owner)';Replace='false';Failure='exact own body and sibling spirit do not block launch'},
    @{Name='foreign-spirit-excluded';Path=$geometry;Property='GeometrySource';Find='ReferenceEquals(collider.GetComponentInParent<PlayerSpiritBase>()?._player, owner)';Replace='collider.GetComponentInParent<PlayerSpiritBase>() != null';Failure='foreign spirit body remains solid'},
    @{Name='null-owner-world-excluded';Path=$geometry;Property='GeometrySource';Find='owner != null && (ReferenceEquals';Replace='(ReferenceEquals';Failure='missing owner cannot ignore anonymous world'},
    @{Name='impact-separation-skips-world';Path=$disc;Property='DiscSource';Find='var separation = sweep(separated, trace.Position);';Replace='var separation = new Trace(TraceKind.Clear, trace.Position);';Failure='impact separation cannot enter neighbouring wall'},
    @{Name='promotion-retires-disc';Path=$runtime;Property='RuntimeSource';Find='GunGameMode mode => mode.HasActiveLadder';Replace='GunGameMode mode => mode.AssistScoringOpen';Failure='promotion preload retains disc but blocks actions'},
    @{Name='unbounded-hitch';Path=$disc;Property='DiscSource';Find='internal const int MaxSteps = 12;';Replace='internal const int MaxSteps = 1200;';Failure='hitch work is bounded'},
    @{Name='cross-area-flight';Path=$disc;Property='DiscSource';Find='!Finite(end) || !inside(Position, end)';Replace='!Finite(end)';Failure='area crossing retires before motion'},
    @{Name='substituted-clear-point';Path=$disc;Property='DiscSource';Find='Vector3.DistanceSquared(trace.Position, end) > .000001f';Replace='false';Failure='unknown or substituted trace is not movement proof'},
    @{Name='no-flight-expiry';Path=$disc;Property='DiscSource';Find='FlightSeconds > 30f';Replace='false';Failure='unbounded abyss flight expires'},
    @{Name='overlap-truncation';Path=$geometry;Property='GeometrySource';Find='if (count >= Overlaps.Length) return false;';Replace='if (count > Overlaps.Length) return false;';Failure='saturated overlap fails closed'},
    @{Name='sweep-truncation';Path=$geometry;Property='GeometrySource';Find='if (count >= Hits.Length)';Replace='if (count > Hits.Length)';Failure='saturated sweep fails closed'},
    @{Name='firearm-teleport';Path=$runtime;Property='RuntimeSource';Find='ReferenceEquals(hands.Item, item) && ArenaTranslocatorSupply.Owns(player, item)';Replace='ArenaTranslocatorSupply.Owns(player, item)';Failure='holster retains disc but cannot teleport from firearm'},
    @{Name='failed-teleport-consumes';Path=$runtime;Property='RuntimeSource';Find='if (!ArenaTranslocatorTeleport.TryTeleport(player, target, out var reason)) { Reject(reason); return; }';Replace='if (!ArenaTranslocatorTeleport.TryTeleport(player, target, out var reason)) { Reject(reason); }';Failure='failed teleport preserves disc'},
    @{Name='drop-retains-disc';Path=$runtime;Property='RuntimeSource';Find='|| !ArenaTranslocatorSupply.Owns(_owner, _device)';Replace='';Failure='dropped device cancels disc'},
    @{Name='death-retains-disc';Path=$runtime;Property='RuntimeSource';Find='|| _owner.HealthController?.IsAlive != true';Replace='';Failure='death clears even while paused'},
    @{Name='replacement-body-retains-disc';Path=$runtime;Property='RuntimeSource';Find='if (_disc != null && (_owner == null || !ReferenceEquals(_owner, player)';Replace='if (_disc != null && (_owner == null || false';Failure='replacement body cannot inherit disc'},
    @{Name='destroyed-unity-owner';Path=$runtime;Property='RuntimeSource';Find='if (_disc != null && (_owner == null || !ReferenceEquals(_owner, player)';Replace='if (_owner != null && (_owner == null || !ReferenceEquals(_owner, player)';Failure='destroyed Unity body clears disc'},
    @{Name='cross-round-disc';Path=$runtime;Property='RuntimeSource';Find='|| _round != Round(match)';Replace='';Failure='round transition clears old disc'},
    @{Name='normal-raid-enabled';Path=$runtime;Property='RuntimeSource';Find='return ArenaSession.IsActive && ArenaTranslocatorSettings.Enabled?.Value == true';Replace='return ArenaTranslocatorSettings.Enabled?.Value == true';Failure='ordinary raid cannot launch'},
    @{Name='stale-teleport-callback';Path=$runtime;Property='RuntimeSource';Find="if (!ReferenceEquals(_disc, disc) || !ReferenceEquals(_world, world)`n                || !ReferenceEquals(_match, match) || !ReferenceEquals(_owner, player)) return;";Replace='// Fault: callback cannot retire success.';Failure='callback teardown cannot publish stale success'}
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'TranslocatorRuntimeChecks.csproj') `
    -Token 'TRANSLOCATOR_RUNTIME_CHECKS_PASSED' -Faults $faults -RequireSingleSite
'TRANSLOCATOR_RUNTIME_VERIFIED'
