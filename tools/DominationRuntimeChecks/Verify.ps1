$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$layout = Join-Path $root 'ArenaMode.Client/Modes/DominationMapLayout.cs'
$runtime = Join-Path $root 'ArenaMode.Client/Modes/DominationRuntime.cs'
$faults = @(
    @{ Name='factory-overlapping-points'; Path=$layout; Property='LayoutSource';
       Find='>= FactoryMinimumSeparation * FactoryMinimumSeparation'; Replace='>= 0';
       Failure='Factory objectives keep twelve metres horizontal separation' },
    @{ Name='factory-repeat-layout'; Path=$layout; Property='LayoutSource';
       Find='choices.RemoveAll(choice => SameSet(choice, previous));'; Replace='choices.RemoveAll(choice => false);';
       Failure='Factory avoids repeating the previous physical triple' },
    @{ Name='bowl-legacy-centroid'; Path=$layout; Property='LayoutSource';
       Find='new Point("A", -20.51f, 0f, -37.12f)'; Replace='new Point("A", -1.76f, -.05f, -33.92f)';
       Failure='individual source anchors and labels are preserved instead of aggregate centroids' },
    @{ Name='iceberg-pool-height'; Path=$layout; Property='LayoutSource';
       Find='new Point("A", 16.77f, 4.5f, -91.39f)'; Replace='new Point("A", 16.77f, 3.9f, -91.39f)';
       Failure='individual source anchors and labels are preserved instead of aggregate centroids' },
    @{ Name='chopshop-lifted-point'; Path=$layout; Property='LayoutSource';
       Find='private const float ChopShopControlPointY = -3.8f;'; Replace='private const float ChopShopControlPointY = -3.3f;';
       Failure='individual source anchors and labels are preserved instead of aggregate centroids' },
    @{ Name='one-point-renamed'; Path=$layout; Property='LayoutSource';
       Find='new Point("X", 1.28f, .25f, 4.22f)'; Replace='new Point("A", 1.28f, .25f, 4.22f)';
       Failure='individual source anchors and labels are preserved instead of aggregate centroids' },
    @{ Name='ports-nearby-relocation'; Path=$runtime; Property='RuntimeSource';
       Find='point.Position, anchor, _layout.Factory, out var floor'; Replace='point.Position, anchor, true, out var floor';
       Failure='ports forbid nearby relocation while Factory retains it' },
    @{ Name='count-mismatch-accepted'; Path=$runtime; Property='RuntimeSource';
       Find='if (rules.Points.Count != count)'; Replace='if (false)';
       Failure='runtime rejects policy and authored count mismatch before allocation' },
    @{ Name='return-path-omitted'; Path=$runtime; Property='RuntimeSource';
       Find='&& NavMesh.CalculatePath(finish.position, start.position, NavMesh.AllAreas, path)'; Replace='&& true';
       Failure='survey requires complete routes in both directions' },
    @{ Name='height-drift-accepted'; Path=$runtime; Property='RuntimeSource';
       Find='Math.Abs(start.position.y - from.y) > .5f || Math.Abs(finish.position.y - to.y) > .5f'; Replace='false';
       Failure='failed candidate or directed path rejects complete layout' },
    @{ Name='partial-path-accepted'; Path=$runtime; Property='RuntimeSource';
       Find='path.status == NavMeshPathStatus.PathComplete'; Replace='true';
       Failure='failed candidate or directed path rejects complete layout' },
    @{ Name='bear-booth-omitted'; Path=$runtime; Property='RuntimeSource';
       Find='foreach (bool usec in new[] { true, false })'; Replace='foreach (bool usec in new[] { true })';
       Failure='opened route proof covers both factions and every authored point in both directions' },
    @{ Name='failed-booth-proof-cached'; Path=$runtime; Property='RuntimeSource';
       Find='if (_layout.Factory || _teamPathsVerified) return;';
       Replace='if (_layout.Factory || _teamPathsVerified) return; _teamPathsVerified = true;';
       Failure='opened route proof covers both factions and every authored point in both directions' },
    @{ Name='disposed-pickup-authority'; Path=$runtime; Property='RuntimeSource';
       Find='=> _prepared && !_disposed'; Replace='=> _prepared';
       Failure='dispose removes all owned markers and pickup authority' },
    @{ Name='letter-inverted'; Path=$runtime; Property='RuntimeSource';
       Find='!ArenaMapCatalog.IsOptionalMap(_world.LocationId));'; Replace='ArenaMapCatalog.IsOptionalMap(_world.LocationId));';
       Failure='ported maps pass showLetter false and vanilla Factory passes true' },
    # Surveyed slots (The Lab, The Labyrinth): layout rows and the Tactical decoupling.
    @{ Name='lab-point-lifted'; Path=$layout; Property='LayoutSource';
       Find='new Point("A", new Vector3(-224.879f, .082f, -279.867f)'; Replace='new Point("A", new Vector3(-224.879f, .582f, -279.867f)';
       Failure='surveyed slots keep their measured primary and their ordered alternates' },
    @{ Name='labyrinth-network-seeds-dropped'; Path=$layout; Property='LayoutSource';
       Find='new[] { new Vector3(-23.83f, .1f, 20.08f), new Vector3(-10.91f, .1f, 6.08f), new Vector3(15.23f, .1f, 27.16f) },';
       Replace='Array.Empty<Vector3>(),';
       Failure='the network proof pool is every slot candidate plus the extra seeds, which are never points' },
    @{ Name='tactical-resolver-gains-lab'; Path=$layout; Property='LayoutSource';
       Find='"arena_autoservice" => ChopShop,'; Replace='"arena_autoservice" => ChopShop, "laboratory" => Laboratory,';
       Failure='Tactical''s layout resolver still refuses The Lab, The Labyrinth, Customs and unknown maps' },
    @{ Name='tactical-count-gains-lab'; Path=$layout; Property='LayoutSource';
       Find="|| ArenaMapCatalog.IsIceberg(map) || ArenaMapCatalog.IsAutoService(map))) return 0;`n        return Resolve(map).Points.Count;";
       Replace="|| ArenaMapCatalog.IsIceberg(map) || ArenaMapCatalog.IsAutoService(map)`n            || string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase))) return 0;`n        return ResolveDomination(map, null).Points.Count;";
       Failure='Domination counts the surveyed slots while Tactical''s count stays 0' },
    @{ Name='domination-count-ignores-slots'; Path=$layout; Property='LayoutSource';
       Find='return ResolveDomination(map, areaId).Points.Count;'; Replace='return 0;';
       Failure='Domination counts the surveyed slots while Tactical''s count stays 0' },
    # Surveyed slots: the runtime's slot rules.
    @{ Name='surveyed-separation-ignored'; Path=$runtime; Property='RuntimeSource';
       Find='if (dx * dx + dz * dz < separation * separation) near = earlier;'; Replace='if (false) near = earlier;';
       Failure='a surveyed slot never takes a candidate closer than 15 m to an earlier slot' },
    @{ Name='surveyed-slot-without-candidate-continues'; Path=$runtime; Property='RuntimeSource';
       Find='if (used < 0)'; Replace='if (false)';
       Failure='a slot without a passing candidate aborts the match and names the slot and the last refusal' },
    @{ Name='surveyed-primary-only'; Path=$runtime; Property='RuntimeSource';
       Find='foreach (var candidate in slot.Candidates)'; Replace='foreach (var candidate in slot.Candidates.Take(1))';
       Failure='a refused primary falls to the first alternate and leaves the other slots alone' },
    @{ Name='surveyed-no-relocation'; Path=$runtime; Property='RuntimeSource';
       Find='TryPlacePoint(candidate, anchor, true, out var floor, out var reason, out _)'; Replace='TryPlacePoint(candidate, anchor, false, out var floor, out var reason, out _)';
       Failure='surveyed placement uses the pool anchor and keeps the bounded 2 m relocation' },
    @{ Name='surveyed-anchor-from-primaries'; Path=$runtime; Property='RuntimeSource';
       Find='TryPoolAnchor(_layout.NavigationSeeds,'; Replace='TryPoolAnchor(_layout.Points.Select(p => p.Position).ToArray(),';
       Failure='a surveyed layout proves the navigation network of its own seed pool, never Factory''s' },
    @{ Name='surveyed-whole-map-team-proof'; Path=$runtime; Property='RuntimeSource';
       Find='if (Areas.ArenaAreaRuntime.CurrentArea == null) return;'; Replace='';
       Failure='a surveyed whole-map layout skips the team proofs like Factory' },
    @{ Name='area-proof-skips-bear'; Path=$runtime; Property='RuntimeSource';
       Find='for (int side = 0; side < 2; side++)'; Replace='for (int side = 0; side < 1; side++)';
       Failure='the area team-start proof covers both factions and every point in both directions' },
    @{ Name='area-proof-needs-every-start'; Path=$runtime; Property='RuntimeSource';
       Find='if (!starts.Any(start => ConnectedBothWays(start, point)))'; Replace='if (!starts.All(start => ConnectedBothWays(start, point)))';
       Failure='one connected start per faction is enough, a second start may be cut off' },
    @{ Name='area-proof-skipped'; Path=$runtime; Property='RuntimeSource';
       Find='if (!starts.Any(start => ConnectedBothWays(start, point)))'; Replace='if (false)';
       Failure='the area team-start proof covers both factions and every point in both directions' },
    # The three Customs areas.
    @{ Name='customs-center-point-lifted'; Path=$layout; Property='LayoutSource';
       Find='new Point("A", new Vector3(355f, 1.1f, -80f)'; Replace='new Point("A", new Vector3(355f, 1.6f, -80f)';
       Failure='the Customs rows keep their measured primaries and ordered alternates: customs-factory-center-polygon-v2 A' },
    @{ Name='customs-side-alternate-dropped'; Path=$layout; Property='LayoutSource';
       Find='new Vector3(583.1f, 1.2f, -100f), '; Replace='';
       Failure='the Customs rows keep their measured primaries and ordered alternates: customs-factory-side-polygon-v2 B' },
    @{ Name='customs-yard-slot-moved'; Path=$layout; Property='LayoutSource';
       Find='new Point("C", new Vector3(-165f, .8f, -75f)'; Replace='new Point("C", new Vector3(-175f, .8f, -75f)';
       Failure='the Customs rows keep their measured primaries and ordered alternates: customs-yard-polygon-v1 C' },
    @{ Name='customs-area-count-zero'; Path=$layout; Property='LayoutSource';
       Find='return ResolveArea(map, areaId)?.Points.Count ?? 0;'; Replace='return 0;';
       Failure='Domination counts three points in every Customs area' },
    @{ Name='customs-area-any-map'; Path=$layout; Property='LayoutSource';
       Find='if (!string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase)) return null;'; Replace='';
       Failure='an area id counts only on its own map and only spelled exactly' },
    @{ Name='customs-area-id-drift'; Path=$layout; Property='LayoutSource';
       Find='internal const string CustomsYardAreaId = "customs-yard-polygon-v1";'; Replace='internal const string CustomsYardAreaId = "customs-yard-polygon-v2";';
       Failure='the repeated Customs area ids equal the shared catalogue''s ids' },
    @{ Name='layout-borrowed-by-another-area'; Path=$runtime; Property='RuntimeSource';
       Find='!string.Equals(_areaId, ActiveAreaId, StringComparison.Ordinal)'; Replace='false';
       Failure='bounded area cannot borrow a full map objective layout' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'DominationRuntimeChecks.csproj') -Token 'DOMINATION_RUNTIME_CHECKS_PASSED' -Properties @() -Faults $faults
