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
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production check after fault control failed.' }
Write-Output 'NATIVE_ARENA_HUD_CHECKS_PASSED'
