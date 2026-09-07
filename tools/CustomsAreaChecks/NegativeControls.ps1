param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaTemp = Join-Path $arenaRepo ('artifacts\customs-checks\negative-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaTemp -Force | Out-Null
$arenaProject = Join-Path $arenaRepo 'tools\NativeMenuChecks\NativeMenuChecks.csproj'

function Invoke-CustomsControl([string[]]$Properties, [bool]$Pass, [string]$Message) {
    $arenaOldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arenaOutput = (& dotnet run --project $arenaProject -c Release "-p:SPTPath=$SptRoot" @Properties 2>&1 | ForEach-Object { $_.ToString() } | Out-String)
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaOldPreference }
    if (($Pass -and $arenaExit -ne 0) -or (!$Pass -and $arenaExit -eq 0) -or !$arenaOutput.Contains($Message)) {
        throw "Customs control unexpected result exit=${arenaExit}: $arenaOutput"
    }
    Write-Output "Customs control verified: pass=$Pass; $Message"
}

# Only copies become mutant inputs to the standalone, non-deploying harness.
Invoke-CustomsControl @() $true 'ARENA_MENU_CHECKS_PASSED'
$arenaCatalog = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Shared\ArenaAreaCatalog.cs'))
$arenaDeny = 'if (string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase))'
if (!$arenaCatalog.Contains($arenaDeny)) { throw 'Customs full-map mutation site drifted.' }
$arenaMutant = Join-Path $arenaTemp 'ArenaAreaCatalog.cs'
[IO.File]::WriteAllText($arenaMutant, $arenaCatalog.Replace($arenaDeny, 'if (false)'))
Invoke-CustomsControl @("-p:ProductionAreaCatalogSource=$arenaMutant") $false 'F12 area downgrade also clears and hides Customs'

$arenaView = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Session\ArenaConditionalMapView.cs'))
$arenaClear = 'if (!available && selected) _clearSelection();'
if (!$arenaView.Contains($arenaClear)) { throw 'Customs deselection mutation site drifted.' }
$arenaViewMutant = Join-Path $arenaTemp 'ArenaConditionalMapView.cs'
[IO.File]::WriteAllText($arenaViewMutant, $arenaView.Replace($arenaClear, '// omitted stale selection clearing'))
Invoke-CustomsControl @("-p:ProductionConditionalMapSource=$arenaViewMutant") $false 'Changing mode hides Customs and clears its stale native selection'
Invoke-CustomsControl @() $true 'ARENA_MENU_CHECKS_PASSED'
Write-Output 'ARENA_CUSTOMS_NEGATIVE_CONTROLS_PASSED (2 isolated mutants)'
