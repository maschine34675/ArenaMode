$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$arenaProject = Join-Path $PSScriptRoot 'KillfeedTextChecks.csproj'
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production killfeed formatting failed' }

# Reintroduce the reported missing local-attacker name in linked production code.
$arenaSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../../ArenaMode.Client/Hud/KillfeedText.cs'))
$arenaAnchor = '{Literal(kill.KillerName)}'
if (($arenaSource.Split(@($arenaAnchor), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'Fault anchor is ambiguous' }
$arenaEvidence = Join-Path $PSScriptRoot ('obj/fault-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaEvidence) | Out-Null
$arenaFault = Join-Path $arenaEvidence 'KillfeedText.cs'
[IO.File]::WriteAllText($arenaFault, $arenaSource.Replace($arenaAnchor, '{(kill.KilledByLocal ? string.Empty : Literal(kill.KillerName))}'))
$arenaOutput = @(& dotnet run --project $arenaProject -c Release "-p:KillfeedTextSource=$arenaFault" 2>&1)
$arenaExit = $LASTEXITCODE
$arenaOutput | Set-Content -LiteralPath (Join-Path $arenaEvidence 'output.log')
if ($arenaExit -eq 0 -or ($arenaOutput -join "`n").Contains('error CS') -or
    !($arenaOutput -join "`n").Contains('CHECK FAILED: local-kill retains the entire attacker name in headline')) {
    throw 'The missing attacker regression was not rejected at its intended assertion'
}
Write-Output 'REJECTED_SOURCE_FAULT: missing-local-attacker'
foreach ($arenaControl in @(
    @('local-only-distance', 'kill.DistanceKnown &&', 'kill.KilledByLocal && kill.DistanceKnown &&', 'local-death includes measured distance'),
    @('unknown-distance-as-measured', 'kill.DistanceKnown && ', '', 'local-kill labels unconfirmed distance as unknown')
)) {
    if (($arenaSource.Split(@($arenaControl[1]), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'Fault anchor is ambiguous' }
    $arenaControlSource = Join-Path $arenaEvidence ($arenaControl[0] + '.cs')
    [IO.File]::WriteAllText($arenaControlSource, $arenaSource.Replace($arenaControl[1], $arenaControl[2]))
    $arenaControlOutput = @(& dotnet run --project $arenaProject -c Release "-p:KillfeedTextSource=$arenaControlSource" 2>&1)
    $arenaControlExit = $LASTEXITCODE
    $arenaControlOutput | Set-Content -LiteralPath (Join-Path $arenaEvidence ($arenaControl[0] + '.log'))
    if ($arenaControlExit -eq 0 -or ($arenaControlOutput -join "`n").Contains('error CS') -or
        !($arenaControlOutput -join "`n").Contains('CHECK FAILED: ' + $arenaControl[3])) {
        throw ('Regression control was not rejected: ' + $arenaControl[0])
    }
    Write-Output ('REJECTED_SOURCE_FAULT: ' + $arenaControl[0])
}
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Production formatting failed after fault control' }
