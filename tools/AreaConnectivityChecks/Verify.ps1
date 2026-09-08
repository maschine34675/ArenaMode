param([switch]$IntegrationOnly)
$ErrorActionPreference = 'Stop'
$connectivityRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$connectivityProject = Join-Path $PSScriptRoot 'AreaConnectivityChecks.csproj'
$connectivityWork = Join-Path $connectivityRepo ('artifacts\area-connectivity-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $connectivityWork -Force | Out-Null
$connectivityGraph = [IO.File]::ReadAllText((Join-Path $connectivityRepo 'ArenaMode.Client\Areas\ArenaAreaConnectivityGraph.cs'))
$connectivityAdapter = [IO.File]::ReadAllText((Join-Path $connectivityRepo 'ArenaMode.Client\Areas\ArenaAreaConnectivityRuntime.cs'))

function Invoke-Checks([string[]]$Extra = @()) {
    $connectivityPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $connectivityBuild = @(& dotnet build $connectivityProject -t:Rebuild -c Release --nologo @Extra 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($connectivityBuild -join [Environment]::NewLine) }
        $connectivityOutput = @(& dotnet run --project $connectivityProject -c Release --no-build 2>&1)
        $connectivityExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $connectivityPreference }
    return @{ Exit = $connectivityExit; Text = (($connectivityOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine) }
}
function Assert-Production {
    $connectivityResult = Invoke-Checks
    Write-Output $connectivityResult.Text
    if ($connectivityResult.Exit -ne 0 -or $connectivityResult.Text -notmatch 'AREA_CONNECTIVITY_CHECKS_PASSED') {
        throw 'Production-linked connectivity checks failed'
    }
    [IO.File]::WriteAllText((Join-Path $connectivityWork 'production.log'), $connectivityResult.Text)
}
function Test-Mutant([string]$Name, [string]$SourceKind, [string]$Before, [string]$After, [string]$Assertion) {
    $connectivitySource = if ($SourceKind -eq 'graph') { $connectivityGraph } else { $connectivityAdapter }
    if (-not $connectivitySource.Contains($Before)) { throw "Missing mutation anchor: $Name" }
    if (($connectivitySource.Length - $connectivitySource.Replace($Before, '').Length) / $Before.Length -ne 1) {
        throw "Mutation anchor is not unique: $Name"
    }
    $connectivityPath = Join-Path $connectivityWork ($Name + '.cs')
    [IO.File]::WriteAllText($connectivityPath, $connectivitySource.Replace($Before, $After))
    $connectivityProperty = if ($SourceKind -eq 'graph') { 'ConnectivitySource' } else { 'AdapterSource' }
    $connectivityResult = Invoke-Checks @("-p:${connectivityProperty}=$connectivityPath")
    [IO.File]::WriteAllText((Join-Path $connectivityWork ($Name + '.log')), $connectivityResult.Text)
    if ($connectivityResult.Exit -eq 0 -or -not $connectivityResult.Text.Contains('ASSERT: ' + $Assertion)) {
        Write-Output $connectivityResult.Text
        throw "Mutant must fail the named behavioral assertion: $Name"
    }
    Write-Output "Connectivity negative control rejected: $Name -> $Assertion"
}
function Assert-Integration {
    $connectivityRuntime = [IO.File]::ReadAllText((Join-Path $connectivityRepo 'ArenaMode.Client\Areas\ArenaAreaRuntime.cs'))
    $connectivityTeam = [IO.File]::ReadAllText((Join-Path $connectivityRepo 'ArenaMode.Client\Areas\ArenaAreaTeamRuntime.cs'))
    $connectivityChecks = [ordered]@{
        'spawn placement uses fresh helper' = $connectivityRuntime.Contains('&& TrySample(point.Position, out var sample) && CompleteConnectedPath(sample)')
        'body preflight uses fresh helper' = $connectivityRuntime.Contains('|| !TrySampleBody(position, out var sample) || !CompleteConnectedPath(sample)')
        'teleport uses fresh helper' = $connectivityRuntime.Contains('&& Contains(target, ArenaAreaCatalog.NavigationInset) && CompleteConnectedPath(sample)')
        'arrival uses fresh helper' = $connectivityTeam.Contains('&& TrySample(player.Position, out var sample) && CompleteConnectedPath(sample)')
        'actual movement checks supplied corners' = $connectivityRuntime.Contains('InsideCorners(bot.Position, corners)')
        'sample radius stays 0.75' = $connectivityRuntime.Contains('NavMesh.SamplePosition(point, out var hit, 0.75f, NavMesh.AllAreas)')
        'floor tolerance stays 0.5' = $connectivityRuntime.Contains('Mathf.Abs(hit.position.y - point.y) > 0.5f')
        'preparation timeout stays 12 seconds' = $connectivityRuntime.Contains('_deadline = Time.realtimeSinceStartup + 12f')
        'only new team starts use graph' = $connectivityRuntime.Contains('if (_area.TeamStarts != null) return TryTeamConnectivity(liveAnchor, out problem)')
        'timeout emits team roster diagnostics' = $connectivityAdapter.Contains('if (timeout && _area?.TeamStarts != null) LogTeamStartDiagnostics()')
    }
    foreach ($connectivityCheck in $connectivityChecks.GetEnumerator()) {
        if (-not $connectivityCheck.Value) { throw ('Integration assertion: ' + $connectivityCheck.Key) }
    }
    Write-Output "AREA_CONNECTIVITY_INTEGRATION_PASSED: $($connectivityChecks.Count) source integration contracts"
}
if ($IntegrationOnly) { Assert-Integration; exit 0 }
try {
    Assert-Production
    Assert-Integration
    Test-Mutant 'reverse-discovery-removed' graph '_forward && reverse' '_forward' 'one way edge is rejected'
    Test-Mutant 'forward-discovery-removed' graph '_forward && reverse' 'reverse' 'reverse only edge is rejected'
    Test-Mutant 'query-budget-overrun' graph 'while (!Complete && used < budget)' 'while (!Complete && used <= budget)' 'forward alone never publishes a vertex'
    Test-Mutant 'fresh-reverse-chain-removed' graph '|| !travel(node, parent)' '' 'stale reverse chain edge is rejected'
    Test-Mutant 'fresh-chain-vertices-removed' graph '|| !current(node) || !current(parent)' '' 'moved parent vertex is rejected'
    Test-Mutant 'final-parent-proof-removed' graph 'verified[parent] && current(parent)' 'current(parent)' 'final snapshot prunes descendants of changed forward edge'
    Test-Mutant 'final-reverse-proof-removed' graph '&& travel(node, parent)' '' 'final snapshot independently validates reverse edge'
    Test-Mutant 'raid-token-proof-removed' adapter '_connectivityToken != _token ||' '' 'old graph cannot authorize new raid token'
    Test-Mutant 'marker-identity-proof-removed' adapter 'vertex.Point.Id != vertex.Id' 'false' 'changed native parent identity rejects placement'
    Test-Mutant 'marker-reference-proof-removed' adapter '!Candidates.Any(p => ReferenceEquals(p, vertex.Point))' 'false' 'replacement marker object cannot inherit old proof'
    Test-Mutant 'outside-corners-accepted' adapter 'if (!InsideCorners(origin, ScratchPath.corners))' 'if (false)' 'actual outside corner path never proves connectivity'
    Test-Mutant 'nearby-is-identical' adapter '(origin - target).sqrMagnitude == 0f' '(origin - target).sqrMagnitude < 1f' 'nearby position still needs actual directed path'
    Test-Mutant 'old-discovery-published' adapter 'if (verified[i]) Connected.Add' 'if (_connectivity.Reached(i) && ConnectivityVertexCurrent(i)) Connected.Add' 'preparation never publishes descendants of stale edge'
}
finally { Assert-Production }
Write-Output "Connectivity check evidence: $connectivityWork"
Write-Output 'ARENA_AREA_CONNECTIVITY_CHECKS_PASSED'
