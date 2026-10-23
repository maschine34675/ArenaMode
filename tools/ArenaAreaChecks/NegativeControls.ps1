$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaTemp = Join-Path $arenaRepo ('artifacts\area-checks\negative-' + [Guid]::NewGuid().ToString('N'))
$arenaSources = @('ArenaMode.Client\Areas\ArenaAreaRespawn.cs',
    'ArenaMode.Client\Respawn\ArenaSpawnSelector.cs', 'ArenaMode.Shared\ArenaAreaCatalog.cs')
function Get-AreaControlHash([string]$Path) {
    $arenaStream = [IO.File]::OpenRead($Path)
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash($arenaStream)).Replace('-', '') }
    finally { $arenaSha.Dispose(); $arenaStream.Dispose() }
}
$arenaBefore = @($arenaSources | ForEach-Object { Get-AreaControlHash (Join-Path $arenaRepo $_) })
$arenaClient = New-Item -ItemType Directory -Path (Join-Path $arenaTemp 'client') -Force
$arenaGeometry = New-Item -ItemType Directory -Path (Join-Path $arenaTemp 'geometry') -Force
$arenaProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>
</Project>
'@
# Only standalone copies are mutated. These projects have no deployment targets.
$arenaProject | Set-Content -LiteralPath (Join-Path $arenaClient 'Checks.csproj')
$arenaProject | Set-Content -LiteralPath (Join-Path $arenaGeometry 'Checks.csproj')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ClientContracts\Program.cs') -Destination $arenaClient
Copy-Item -LiteralPath (Join-Path $arenaRepo $arenaSources[0]) -Destination $arenaClient
Copy-Item -LiteralPath (Join-Path $arenaRepo $arenaSources[1]) -Destination $arenaClient
# Both client sources defer Equator placement to Bots.ArenaTeamBoxRuntime (381f784); the suite's external
# fixture stands in for it here as it does in the main ArenaAreaChecks project.
Copy-Item -LiteralPath (Join-Path $arenaRepo 'tools\EquatorSpawnExternalFixture.cs') -Destination $arenaClient
Copy-Item -LiteralPath (Join-Path $arenaRepo $arenaSources[2]) -Destination $arenaGeometry
Copy-Item -LiteralPath (Join-Path $arenaRepo 'ArenaMode.Shared\ArenaMapCatalog.cs') -Destination $arenaGeometry
@'
using ArenaMode.Shared;
var bounds = new ArenaAreaBounds(0, 0, 0, 10, 10, 10);
var start = new ArenaAreaPoint(1, 1, 1);
var end = new ArenaAreaPoint(8, 1, 8);
if (!bounds.ContainsPath(start, new[] { end }, true)) throw new Exception("Positive inside path failed.");
if (bounds.ContainsPath(start, new[] { new ArenaAreaPoint(12, 1, 1), end }, true))
    throw new Exception("An outside middle corner was accepted.");
Console.WriteLine("GEOMETRY_CONTROL_PASSED");
'@ | Set-Content -LiteralPath (Join-Path $arenaGeometry 'Program.cs')

function Invoke-Control([string]$Directory, [string]$Expected, [bool]$ShouldPass) {
    $arenaOldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arenaOutput = (& dotnet run --project (Join-Path $Directory 'Checks.csproj') -c Release 2>&1 | Out-String)
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaOldPreference }
    if (($ShouldPass -and $arenaExit -ne 0) -or (!$ShouldPass -and $arenaExit -eq 0) -or !$arenaOutput.Contains($Expected)) {
        throw "Control did not produce the required outcome (exit=$arenaExit): $arenaOutput"
    }
    Write-Output "Control verified: pass=$ShouldPass, expected=$Expected"
}

Invoke-Control $arenaClient 'ARENA_AREA_CLIENT_CONTRACTS_PASSED' $true
Invoke-Control $arenaGeometry 'GEOMETRY_CONTROL_PASSED' $true
$arenaSelector = Join-Path $arenaClient 'ArenaSpawnSelector.cs'
$arenaOriginalSelector = Get-Content -Raw -LiteralPath $arenaSelector
$arenaOriginalSelector.Replace('if (Areas.ArenaAreaRuntime.CurrentArea != null)', 'if (false)') |
    Set-Content -LiteralPath $arenaSelector
Invoke-Control $arenaClient 'Area used an unrestricted fallback.' $false
$arenaOriginalSelector | Set-Content -LiteralPath $arenaSelector

$arenaRespawn = Join-Path $arenaClient 'ArenaAreaRespawn.cs'
$arenaOriginalRespawn = Get-Content -Raw -LiteralPath $arenaRespawn
$arenaOriginalRespawn.Replace('|| ArenaAreaRuntime.CurrentArea?.Id != _areaId', '|| false') |
    Set-Content -LiteralPath $arenaRespawn
Invoke-Control $arenaClient 'Different area released a prepared replacement.' $false
$arenaOriginalRespawn | Set-Content -LiteralPath $arenaRespawn

$arenaRules = Join-Path $arenaGeometry 'ArenaAreaCatalog.cs'
$arenaOriginalRules = Get-Content -Raw -LiteralPath $arenaRules
$arenaOriginalRules.Replace('for (int i = 0; i < path.Count; i++)', 'for (int i = path.Count - 1; i < path.Count; i++)') |
    Set-Content -LiteralPath $arenaRules
Invoke-Control $arenaGeometry 'An outside middle corner was accepted.' $false
$arenaOriginalRules | Set-Content -LiteralPath $arenaRules
Invoke-Control $arenaClient 'ARENA_AREA_CLIENT_CONTRACTS_PASSED' $true
Invoke-Control $arenaGeometry 'GEOMETRY_CONTROL_PASSED' $true
$arenaAfter = @($arenaSources | ForEach-Object { Get-AreaControlHash (Join-Path $arenaRepo $_) })
if (Compare-Object $arenaBefore $arenaAfter) { throw 'Production sources changed during negative controls.' }
Write-Output 'ARENA_AREA_NEGATIVE_CONTROLS_PASSED (3 mutants, production hashes unchanged)'
