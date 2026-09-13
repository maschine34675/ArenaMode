param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$eastRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$eastProject = Join-Path $PSScriptRoot 'CustomsEastAreaChecks.csproj'
$eastFixture = Join-Path $PSScriptRoot 'Fixtures\area-draft-20260913-090513-276a2a3d6c424846bd202992faf489b4.json'
$eastDatabase = Join-Path $SptRoot 'SPT_Runtime\SPT_Data\database\locations\bigmap\base.json'
$eastRun = Join-Path $PSScriptRoot ('artifacts\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $eastRun -Force | Out-Null

function Build-EastHarness([string]$Name, [string]$CatalogSource, [string]$StateSource) {
    $eastDestination = Join-Path $eastRun $Name
    $eastArguments = @('build', $eastProject, '-c', 'Release', '--nologo', '-v:q',
        "-p:OutputPath=$eastDestination\out\", "-p:BaseIntermediateOutputPath=$eastDestination\obj\")
    if ($CatalogSource) { $eastArguments += "-p:AreaCatalogSource=$CatalogSource" }
    if ($StateSource) { $eastArguments += "-p:AreaSelectionStateSource=$StateSource" }
    & dotnet @eastArguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Harness build failed: $Name" }
    return Join-Path $eastDestination 'out\CustomsEastAreaChecks.dll'
}
function Write-EastMutation([string]$Source, [string]$Old, [string]$Replacement, [string]$Name) {
    if ([regex]::Matches($Source,[regex]::Escape($Old)).Count -ne 1) { throw "Mutation anchor must occur once: $Name" }
    $eastFile = Join-Path $eastRun ($Name + '.cs')
    [IO.File]::WriteAllText($eastFile,$Source.Replace($Old,$Replacement))
    return $eastFile
}

Start-Transcript -Path (Join-Path $eastRun 'verification.log') | Out-Null
try {
    # This standalone executable links plain production source, never a mod
    # project or SPT SDK target. Its output and intermediates stay in this leaf.
    $eastBaseline = Build-EastHarness 'baseline' '' ''
    & dotnet $eastBaseline $eastFixture $eastDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Baseline production checks failed.' }

    $eastCatalog = Get-Content -LiteralPath (Join-Path $eastRepo 'ArenaMode.Shared\ArenaAreaCatalog.cs') -Raw
    $eastState = Get-Content -LiteralPath (Join-Path $eastRepo 'ArenaMode.Client\Session\ArenaAreaSelectionState.cs') -Raw
    $eastFaults = @(
        @{ Name='vertex-drift'; Target='catalog'; Old='new ArenaAreaVertex(494.5332f, -104.145111f)'; New='new ArenaAreaVertex(495.5332f, -104.145111f)'; Marker='EXACT-GEOMETRY' },
        @{ Name='height-clamp'; Target='catalog'; Old='}, 0f, 12f, limitsHeight: false), 0.5f, 3f, 2);'; New='}, 0f, 12f, limitsHeight: true), 0.5f, 3f, 2);'; Marker='UNLIMITED-HEIGHT' },
        @{ Name='mode-bypass'; Target='catalog'; Old='if (pitFireTeam || !string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))'; New='if (pitFireTeam)'; Marker='REJECT-MODES' },
        @{ Name='menu-missing'; Target='state'; Old='        new(ArenaAreaCatalog.CustomsFactoryEastPolygonId, "Factory East (contour)", ArenaAreaCatalog.CustomsFactoryEastPolygon.MaxTeamSize),'; New=''; Marker='MENU-AREA' }
    )
    foreach ($eastFault in $eastFaults) {
        $eastSource = if ($eastFault.Target -eq 'catalog') { $eastCatalog } else { $eastState }
        $eastMutation = Write-EastMutation $eastSource $eastFault.Old $eastFault.New $eastFault.Name
        $eastCatalogOverride = if ($eastFault.Target -eq 'catalog') { $eastMutation } else { '' }
        $eastStateOverride = if ($eastFault.Target -eq 'state') { $eastMutation } else { '' }
        $eastDll = Build-EastHarness $eastFault.Name $eastCatalogOverride $eastStateOverride
        $eastOutput = & dotnet $eastDll $eastFixture $eastDatabase 2>&1
        $eastExit = $LASTEXITCODE
        $eastOutput | Set-Content -LiteralPath (Join-Path $eastRun ($eastFault.Name + '.log'))
        if ($eastExit -eq 0 -or ($eastOutput -join "`n") -notmatch ('CUSTOMS EAST REJECTED: ' + [regex]::Escape($eastFault.Marker) + ':')) {
            throw "Fault did not fail for the expected production assertion: $($eastFault.Name)"
        }
        Write-Output "Negative control rejected as intended: $($eastFault.Name) [$($eastFault.Marker)]"
    }
    & dotnet $eastBaseline $eastFixture $eastDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Final clean baseline verification failed.' }
    Write-Output "Evidence: $eastRun"
    Write-Output 'CUSTOMS EAST CHECKS PASSED'
}
finally { Stop-Transcript | Out-Null }
