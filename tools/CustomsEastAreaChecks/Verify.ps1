param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$eastRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$eastProject = Join-Path $PSScriptRoot 'CustomsEastAreaChecks.csproj'
$eastFixture = Join-Path $PSScriptRoot '..\CustomsAreaRefreshChecks\Fixtures\area-draft-20260928-172225-67f3ee0cd11d4aaaac29c63eee0e7769.json'
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

    $eastCatalog = (Get-Content -LiteralPath (Join-Path $eastRepo 'ArenaMode.Shared\ArenaAreaCatalog.cs') -Raw).Replace("`r`n","`n")
    $eastState = Get-Content -LiteralPath (Join-Path $eastRepo 'ArenaMode.Client\Session\ArenaAreaSelectionState.cs') -Raw
    $eastFaults = @(
        @{ Name='vertex-drift'; Target='catalog'; Old='new ArenaAreaVertex(656.139343f, -45.62339f)'; New='new ArenaAreaVertex(657.139343f, -45.62339f)'; Marker='EXACT-GEOMETRY' },
        @{ Name='height-clamp'; Target='catalog'; Old="new ArenaAreaVertex(671.3696f, -48.25674f),`n        }, 0f, 12f, limitsHeight: false)"; New="new ArenaAreaVertex(671.3696f, -48.25674f),`n        }, 0f, 12f, limitsHeight: true)"; Marker='UNLIMITED-HEIGHT' },
        @{ Name='mode-bypass'; Target='catalog'; Old='if (pitFireTeam || !string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))'; New='if (pitFireTeam)'; Marker='REJECT-MODES' },
        @{ Name='menu-missing'; Target='state'; Old='        new(ArenaAreaCatalog.CustomsFactoryEastPolygonId, "Factory Side"),'; New=''; Marker='MENU-AREA' },
        @{ Name='missing-native-start'; Target='catalog'; Old='"4fa5e11e-daa0-4594-88ef-d43e87d19f94"'; New='"00000000-0000-0000-0000-000000000001"'; Marker='START-BOT' },
        @{ Name='wrong-native-category'; Target='catalog'; Old='"4fa5e11e-daa0-4594-88ef-d43e87d19f94"'; New='"0327c5d1-cc7f-432f-92dc-f95fb4719f1b"'; Marker='START-BOT' },
        @{ Name='opposing-too-close'; Target='catalog'; Old='"4fa5e11e-daa0-4594-88ef-d43e87d19f94"'; New='"ae0c5e1d-71cc-44a2-86ae-4c45e224a1ff"'; Marker='START-OPPOSING' },
        @{ Name='retired-area-admitted'; Target='catalog'; Old='var candidate = id == CustomsFactoryCenterTeamsId ? CustomsFactoryCenterTeams'; New='var candidate = id == FactorySouthId ? FactorySouth : id == CustomsFactoryCenterTeamsId ? CustomsFactoryCenterTeams'; Marker='RETIRED-AREA' },
        @{ Name='team-picker-center-only'; Target='state'; Old='HasTeamSizeChoice = SelectedArea?.TeamStarts != null;'; New='HasTeamSizeChoice = SelectedId == ArenaAreaCatalog.CustomsFactoryCenterTeamsId;'; Marker='MENU-TEAM-SIZE' }
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
    # Mutate only a scratch native corpus to demonstrate that real coordinate
    # spacing is checked, not just known IDs or the catalog's array lengths.
    $eastNativeFault = Get-Content -LiteralPath $eastDatabase -Raw | ConvertFrom-Json
    $eastNativeAnchor = $eastNativeFault.SpawnPointParams | Where-Object Id -EQ '01a445b0-4545-48da-9ce6-6d0190869360'
    $eastNativeMoved = $eastNativeFault.SpawnPointParams | Where-Object Id -EQ '15fb12c7-625d-4665-9fb6-2774905743ca'
    $eastNativeMoved.Position.x = $eastNativeAnchor.Position.x + 0.1
    $eastNativeMoved.Position.z = $eastNativeAnchor.Position.z
    $eastFaultDatabase = Join-Path $eastRun 'close-native-starts.json'
    $eastNativeFault | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $eastFaultDatabase
    $eastFaultOutput = & dotnet $eastBaseline $eastFixture $eastFaultDatabase 2>&1
    $eastFaultExit = $LASTEXITCODE
    $eastFaultOutput | Set-Content -LiteralPath (Join-Path $eastRun 'close-native-starts.log')
    if ($eastFaultExit -eq 0 -or ($eastFaultOutput -join "`n") -notmatch 'CUSTOMS EAST REJECTED: START-SPACING:') {
        throw 'Close native start corpus did not fail at physical spacing.'
    }
    Write-Output 'Negative control rejected as intended: close-native-starts [START-SPACING]'
    & dotnet $eastBaseline $eastFixture $eastDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Final clean baseline verification failed.' }
    Write-Output "Evidence: $eastRun"
    Write-Output 'CUSTOMS EAST CHECKS PASSED'
}
finally { Stop-Transcript | Out-Null }
