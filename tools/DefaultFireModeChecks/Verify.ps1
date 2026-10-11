$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject = Join-Path $PSScriptRoot 'DefaultFireModeChecks.csproj'
$arenaWork = Join-Path $arenaRepo ('artifacts\default-fire-mode-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaSourceFile = Join-Path $arenaRepo 'ArenaMode.Client\Combat\ArenaDefaultFireMode.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourceFile).Replace("`r`n", "`n")
function Run-Check([string]$Name, [string]$Override = '') {
    $arenaArgs = @('run', '--project', $arenaProject, '-c', 'Release')
    if ($Override) { $arenaArgs += "-p:FireModeSource=$Override" }
    $arenaPrevious = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaText = (& dotnet @arenaArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPrevious }
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name + '.log')), $arenaText)
    return @{ Code = $arenaExit; Output = $arenaText }
}
$arenaResult = Run-Check 'production'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('ARENA_DEFAULT_FIRE_MODE_CHECKS_PASSED')) { throw $arenaResult.Output }
Write-Output $arenaResult.Output
$arenaFaults = @(
    @('exact-raid', '!ArenaSession.IsExactActiveRaid(game, world)', 'false', 'stale world excluded'),
    @('ai', 'player.IsAI ||', 'false ||', 'AI excluded'),
    @('body', '!ReferenceEquals(game.LocalPlayer, player)', 'false', 'old player excluded'),
    @('spectator', 'ArenaSpectatorService.IsActive ||', 'false ||', 'spectator excluded'),
    @('respawn', '|| ArenaRespawnDriver.IsRespawnInProgress', '|| false', 'respawn in progress excluded'),
    @('supported-modes', 'Array.IndexOf(mode.AvailableEFireModes, Weapon.EFireMode.fullauto) < 0', 'false', 'unsupported fire modes are not invented'),
    @('manual-selection', '&& ReferenceEquals(_observed, hands)) return;', '&& false) return;', 'manual semi while held is preserved'),
    @('draw-hook', 'Apply(hands, "draw");', '{}', 'redraw defaults to fullauto again'),
    @('animator', 'hands.FirearmsAnimator.SetFireMode(Weapon.EFireMode.fullauto, true);', '{}', 'native animator synchronized without selector animation'),
    @('native-callback', 'Current(out var current) && ReferenceEquals(current, hands)', 'true', 'native callback cannot animate retired hands'),
    @('raid-cleanup', 'RaidLifecycle.OnRaidEnded += Reset;', '// omitted cleanup', 'raid end releases observed controller')
)
try {
    foreach ($arenaFault in $arenaFaults) {
        if (($arenaSource.Split([string[]]@($arenaFault[1]), [StringSplitOptions]::None)).Count -ne 2) { throw "Expected one mutation site: $($arenaFault[0])" }
        $arenaMutant = Join-Path $arenaWork ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaFault[1], $arenaFault[2]))
        $arenaResult = Run-Check $arenaFault[0] $arenaMutant
        if ($arenaResult.Code -eq 0 -or !$arenaResult.Output.Contains('ASSERT: ' + $arenaFault[3])) { throw $arenaResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
}
finally {
    $arenaRestore = & dotnet build $arenaProject -t:Rebuild -c Release --nologo -v:quiet 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($arenaRestore -join "`n") }
    $arenaRestore | Set-Content -LiteralPath (Join-Path $arenaWork 'restore.log')
}
$arenaResult = Run-Check 'production-final'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('ARENA_DEFAULT_FIRE_MODE_CHECKS_PASSED')) { throw $arenaResult.Output }
(Get-FileHash -LiteralPath $arenaSourceFile -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $arenaWork 'production.sha256')
Write-Output $arenaResult.Output
Write-Output "Evidence: $arenaWork"
