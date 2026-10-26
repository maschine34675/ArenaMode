$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$layout = Join-Path $root 'ArenaMode.Client/Modes/DominationMapLayout.cs'
$runtime = Join-Path $root 'ArenaMode.Client/Modes/DominationRuntime.cs'
$faults = @(
    @{ Name='bowl-legacy-centroid'; Path=$layout; Property='LayoutSource';
       Find='new Point("A", -20.51f, 0f, -37.12f)'; Replace='new Point("A", -1.76f, -.05f, -33.92f)';
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
       Failure='dispose removes all owned markers and pickup authority' }
)
Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'DominationRuntimeChecks.csproj') -Token 'DOMINATION_RUNTIME_CHECKS_PASSED' -Properties @() -Faults $faults
