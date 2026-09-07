param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
function Get-PolygonHash([string]$Path) {
    # Windows PowerShell launched by a coordinator can have different module
    # auto-loading behavior. Hash actual bytes without relying on Get-FileHash.
    $arenaStream = [IO.File]::OpenRead($Path)
    $arenaSha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($arenaSha.ComputeHash($arenaStream)).Replace('-', '') }
    finally { $arenaSha.Dispose(); $arenaStream.Dispose() }
}
$arenaSource = Join-Path $arenaRepo 'ArenaMode.Shared\ArenaAreaCatalog.cs'
$arenaBefore = Get-PolygonHash $arenaSource
$arenaOriginal = Get-Content -LiteralPath $arenaSource -Raw
$arenaTemp = Join-Path $arenaRepo ('artifacts\polygon-checks\negative-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaTemp -Force | Out-Null
$arenaMutant = Join-Path $arenaTemp 'ArenaAreaCatalog.cs'
$arenaProject = Join-Path $PSScriptRoot 'Capture\Capture.csproj'

function Test-PolygonControl([string]$Expected, [bool]$ShouldPass) {
    $arenaPrior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arenaOutput = (& dotnet run --project $arenaProject -c Release "-p:SPTPath=$SptRoot" "-p:ProductionAreaCatalogSource=$arenaMutant" 2>&1 |
            ForEach-Object { $_.ToString() }) -join "`n"
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPrior }
    if (($ShouldPass -and $arenaExit -ne 0) -or (!$ShouldPass -and $arenaExit -eq 0) -or !$arenaOutput.Contains($Expected)) {
        throw "Polygon control failed its expected assertion (exit=$arenaExit): $arenaOutput"
    }
    Write-Output "Polygon fault control verified: pass=$ShouldPass; assertion=$Expected"
}
function Write-PolygonMutant([string]$Needle, [string]$Replacement) {
    if (!$arenaOriginal.Contains($Needle)) { throw "Mutation anchor missing: $Needle" }
    $arenaOriginal.Replace($Needle, $Replacement) | Set-Content -LiteralPath $arenaMutant -Encoding UTF8
}

# Standalone linked production model, no server/project deployment targets.
$arenaOriginal | Set-Content -LiteralPath $arenaMutant -Encoding UTF8
Test-PolygonControl 'ARENA_POLYGON_CAPTURE_PASSED' $true
Write-PolygonMutant '|| !ContainsXZ(x, z)' '|| false'
Test-PolygonControl 'The L-shaped void must not become allowed by bounding extents' $false
Write-PolygonMutant '|| (!_rectangle && !ContainsSegment(origin, point, horizontalInset))' '|| false'
Test-PolygonControl 'An all-inside-endpoints path must not cut across the concave void' $false
$arenaOriginal | Set-Content -LiteralPath $arenaMutant -Encoding UTF8
Test-PolygonControl 'ARENA_POLYGON_CAPTURE_PASSED' $true
if ((Get-PolygonHash $arenaSource) -ne $arenaBefore) {
    throw 'Production polygon source changed during negative controls.'
}

$arenaCaptureSource = Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaCapture.cs'
$arenaCaptureBefore = Get-PolygonHash $arenaCaptureSource
$arenaCaptureOriginal = Get-Content -LiteralPath $arenaCaptureSource -Raw
$arenaCaptureMutant = Join-Path $arenaTemp 'ArenaAreaCapture.cs'
$arenaScopeProject = Join-Path $PSScriptRoot 'Scope\Scope.csproj'
function Test-ScopeControl([string]$Expected, [bool]$ShouldPass) {
    $arenaPrior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arenaOutput = (& dotnet run --project $arenaScopeProject -c Release "-p:SPTPath=$SptRoot" "-p:ProductionCaptureSource=$arenaCaptureMutant" 2>&1 |
            ForEach-Object { $_.ToString() }) -join "`n"
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPrior }
    if (($ShouldPass -and $arenaExit -ne 0) -or (!$ShouldPass -and $arenaExit -eq 0) -or !$arenaOutput.Contains($Expected)) {
        throw "Scope control failed its expected assertion (exit=$arenaExit): $arenaOutput"
    }
    Write-Output "Scope fault control verified: pass=$ShouldPass; assertion=$Expected"
}
$arenaCaptureOriginal | Set-Content -LiteralPath $arenaCaptureMutant -Encoding UTF8
Test-ScopeControl 'ARENA_POLYGON_SCOPE_PASSED' $true
$arenaCaptureAnchor = 'ReferenceEquals(local.LocalPlayer, _world.MainPlayer)'
if (!$arenaCaptureOriginal.Contains($arenaCaptureAnchor)) { throw 'Capture body mutation anchor missing.' }
$arenaCaptureOriginal.Replace($arenaCaptureAnchor, 'true') | Set-Content -LiteralPath $arenaCaptureMutant -Encoding UTF8
Test-ScopeControl 'main and local body mismatch: interaction gated before polling input' $false
$arenaWorldAnchor = 'ReferenceEquals(local.GameWorld, world)'
if (!$arenaCaptureOriginal.Contains($arenaWorldAnchor)) { throw 'Capture world mutation anchor missing.' }
$arenaCaptureOriginal.Replace($arenaWorldAnchor, 'true') | Set-Content -LiteralPath $arenaCaptureMutant -Encoding UTF8
Test-ScopeControl 'game world mismatch: intended draft lifetime' $false
$arenaCaptureOriginal | Set-Content -LiteralPath $arenaCaptureMutant -Encoding UTF8
Test-ScopeControl 'ARENA_POLYGON_SCOPE_PASSED' $true
if ((Get-PolygonHash $arenaCaptureSource) -ne $arenaCaptureBefore) {
    throw 'Production capture source changed during negative controls.'
}
Write-Output 'ARENA_POLYGON_NEGATIVE_CONTROLS_PASSED (4 mutants fail named assertions, production hashes unchanged)'
