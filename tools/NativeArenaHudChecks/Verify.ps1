$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject = Join-Path $PSScriptRoot 'NativeArenaHudChecks.csproj'
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production extraction-list checks failed.' }
$arenaOriginal = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Hud\ArenaExtractionList.cs'))
$arenaAnchor = '!ReferenceEquals(__instance, panel)'
if (($arenaOriginal.Split(@($arenaAnchor), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'Fault anchor is ambiguous.' }
$arenaFaultDir = Join-Path $PSScriptRoot ('obj\fault-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaFaultDir) | Out-Null
$arenaFaultSource = Join-Path $arenaFaultDir 'ArenaExtractionList.cs'
[IO.File]::WriteAllText($arenaFaultSource, $arenaOriginal.Replace($arenaAnchor, 'true'))
$arenaFaultOutput = @(& dotnet run --project $arenaProject -c Release "-p:ExtractionSource=$arenaFaultSource" 2>&1)
$arenaFaultExit = $LASTEXITCODE
$arenaFaultOutput | Set-Content -LiteralPath (Join-Path $arenaFaultDir 'output.log')
if ($arenaFaultExit -eq 0 -or ($arenaFaultOutput -join "`n") -notmatch 'CHECK FAILED: Arena double-O exit list is suppressed') {
    throw 'Removed native-prefix suppression was not rejected by its behavioral assertion.'
}
foreach ($arenaControl in @(
    @('banner-left-visible', 'UpdateHeader(panel);', '// deliberately omitted header suppression', 'Confirmed startup hides the entire banner while retaining the clock'),
    @('clock-hidden-with-banner', '|| clock.transform.IsChildOf(header)', '', 'A banner containing the clock is never deactivated and list masking survives'),
    @('duplicate-canvas', 'var native = list.GetComponent<CanvasGroup>();', 'var native = list.gameObject.AddComponent<CanvasGroup>();', 'Confirmed startup hides existing native list'),
    @('missing-restore', 'if (mask.alpha == 0f) mask.alpha = _restoreAlpha;', '// deliberately omitted native alpha restoration', 'Foreign world restores native group synchronously without destruction')
)) {
    if (($arenaOriginal.Split(@($arenaControl[1]), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'Fault anchor is ambiguous.' }
    $arenaControlSource = Join-Path $arenaFaultDir ($arenaControl[0] + '.cs')
    [IO.File]::WriteAllText($arenaControlSource, $arenaOriginal.Replace($arenaControl[1], $arenaControl[2]))
    $arenaControlOutput = @(& dotnet run --project $arenaProject -c Release "-p:ExtractionSource=$arenaControlSource" 2>&1)
    $arenaControlExit = $LASTEXITCODE
    $arenaControlOutput | Set-Content -LiteralPath (Join-Path $arenaFaultDir ($arenaControl[0] + '.log'))
    if ($arenaControlExit -eq 0 -or ($arenaControlOutput -join "`n").Contains('error CS') -or
        !($arenaControlOutput -join "`n").Contains('CHECK FAILED: ' + $arenaControl[3])) {
        throw "Regression control was not rejected: $($arenaControl[0])"
    }
    Write-Output "REJECTED_SOURCE_FAULT: $($arenaControl[0])"
}
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production check after fault control failed.' }
Write-Output 'NATIVE_ARENA_HUD_CHECKS_PASSED'
