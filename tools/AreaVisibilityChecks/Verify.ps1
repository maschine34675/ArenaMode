$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaWork = Join-Path $arenaRepo ('artifacts\area-visibility\' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaWork) | Out-Null
$arenaProject = Join-Path $PSScriptRoot 'AreaVisibilityChecks.csproj'
$arenaPolicy = Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaGridVisibility.cs'
$arenaBuilder = Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaEnclosure.cs'
function Invoke-Case([string[]]$properties = @()) {
    $arenaOutput = @(& dotnet run --project $arenaProject -c Release @properties 2>&1)
    return @{ Code=$LASTEXITCODE; Text=($arenaOutput -join "`n") }
}
$arenaBaseline = Invoke-Case
Write-Output $arenaBaseline.Text
if ($arenaBaseline.Code -ne 0) { throw 'Area visibility baseline failed' }
$arenaMutations = @(
    @{Name='always-visible'; Path=$arenaPolicy; Property='VisibilitySource'; Old='if (distance >= HiddenDistance) return 0;'; New='if (distance >= HiddenDistance) return 255;'; Failure='distant wall is transparent'},
    @{Name='endpoint-distance'; Path=$arenaPolicy; Property='VisibilitySource'; Old='t = Math.Max(0, Math.Min(1, t));'; New='t = 0;'; Failure='near middle of long wall remains visible'},
    @{Name='skip-mesh-alpha'; Path=$arenaBuilder; Property='BuilderSource'; Old='if (changed) _grid.colors32 = _gridColors;'; New='// missing native mesh color upload'; Failure='interior update hides actual mesh colors'},
    @{Name='hide-physical-root'; Path=$arenaBuilder; Property='BuilderSource'; Old='if (changed) _grid.colors32 = _gridColors;'; New='if (changed) _grid.colors32 = _gridColors; _root.SetActive(false);'; Failure='transparent grid retains all enclosure invariants'}
)
foreach ($arenaMutation in $arenaMutations) {
    $arenaSource = [IO.File]::ReadAllText($arenaMutation.Path)
    if (-not $arenaSource.Contains($arenaMutation.Old) -or $arenaSource.IndexOf($arenaMutation.Old) -ne $arenaSource.LastIndexOf($arenaMutation.Old)) { throw ('Mutation anchor mismatch: ' + $arenaMutation.Name) }
    $arenaPath = Join-Path $arenaWork ($arenaMutation.Name + '.cs')
    [IO.File]::WriteAllText($arenaPath, $arenaSource.Replace($arenaMutation.Old, $arenaMutation.New))
    $arenaResult = Invoke-Case @("-p:$($arenaMutation.Property)=$arenaPath")
    $arenaResult.Text | Set-Content -LiteralPath (Join-Path $arenaWork ($arenaMutation.Name + '.log'))
    if ($arenaResult.Code -eq 0 -or -not $arenaResult.Text.Contains('ASSERT: ' + $arenaMutation.Failure)) { throw ('Mutation not rejected as intended: ' + $arenaMutation.Name + "`n" + $arenaResult.Text) }
    Write-Output ('Rejected area visibility defect: ' + $arenaMutation.Name)
}
$arenaRestored = Invoke-Case
if ($arenaRestored.Code -ne 0) { throw $arenaRestored.Text }
& pwsh -NoProfile -File (Join-Path $arenaRepo 'tools\PolygonAreaChecks\Enclosure\Verify.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Existing physical enclosure regressions failed' }
Write-Output 'ARENA_AREA_VISIBILITY_CHECKS_PASSED'
