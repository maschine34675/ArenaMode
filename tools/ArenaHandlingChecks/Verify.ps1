param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaProject = Join-Path $PSScriptRoot 'ArenaHandlingChecks.csproj'
$arenaWork = Join-Path $arenaRepo ('artifacts\arena-handling-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaSourceFile = Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaHandlingPatches.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourceFile).Replace("`r`n", "`n")
function Run-Check([string]$Name, [string]$Override = '') {
    $arenaArgs = @('run', '--project', $arenaProject, '-c', 'Release')
    if ($Override) { $arenaArgs += "-p:HandlingSource=$Override" }
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
$arenaContracts = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1') -SptPath $SptPath 2>&1
if ($LASTEXITCODE -ne 0) { throw ($arenaContracts -join "`n") }
$arenaContracts | Set-Content -LiteralPath (Join-Path $arenaWork 'installed-contracts.log')
Write-Output $arenaContracts
$arenaResult = Run-Check 'production'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('ARENA_HANDLING_CHECKS_PASSED')) { throw $arenaResult.Output }
Write-Output $arenaResult.Output
$arenaFaults = @(
    @('landing-duration', 'duration *= factor;', 'duration *= 1f;', 'hard landing halves only duration'),
    @('landing-scope', '&& ArenaMovementScope.TryGetPlayer(context, out _)', '&& true', 'foreign movement context excluded'),
    @('landing-cause', '_enabled && cause == Player.ESpeedLimit.Fall && FinitePositive(duration)', '_enabled && FinitePositive(duration)', 'unrelated speed-limit causes unchanged'),
    @('operation-scope', '!ArenaMovementScope.IsLocal(operation.Player)', 'false', 'foreign player draw is excluded'),
    @('wrong-holster-factor', 'factor = Settings.WeaponHolsterSpeedMultiplier?.Value ?? 1f;', 'factor = Settings.WeaponDrawSpeedMultiplier?.Value ?? 1f;', 'holster uses separate factor'),
    @('accumulating-factor', 'float changed = lease.Original * factor;', 'float changed = current * factor;', 'frame updates never compound draw multiplier'),
    @('missing-restoration', 'AnimationControllerParametersTable.SetSpeedDraw(lease.Animator, lease.Original);', 'AnimationControllerParametersTable.SetSpeedDraw(lease.Animator, lease.Written);', 'draw completion restores before native follow-up callbacks'),
    @('external-writer-clobber', '&& AnimationControllerParametersTable.GetFloatSpeedDraw(lease.Animator) == lease.Written)', ')', 'foreign parameter writer is preserved'),
    @('early-native-write-ownership', 'draw = pending.Written;', 'lease.Original = pending.Original; lease.Written = pending.Written; draw = pending.Written;', 'native parameter failure before draw restores prior baseline and preserves exception'),
    @('early-hot-write-ownership', "AnimationControllerParametersTable.SetSpeedDraw(lease.Animator, value);`n            lease.Written = value;", "lease.Written = value;`n            AnimationControllerParametersTable.SetSpeedDraw(lease.Animator, value);", 'failed hot factor write restores original owned parameter'),
    @('draw-completion-release', '[PatchPrefix] private static void Prefix(Player.FirearmController.SpawnOperation __instance) => Release(__instance?.FirearmsAnimator);', '[PatchPrefix] private static void Prefix(Player.FirearmController.SpawnOperation __instance) { }', 'draw completion restores before native follow-up callbacks'),
    @('lost-raid-cleanup', 'RaidLifecycle.OnRaidEnded += ResetPerRaidState;', '// omitted raid cleanup', 'raid end restores owned draw value')
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
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('ARENA_HANDLING_CHECKS_PASSED')) { throw $arenaResult.Output }
(Get-FileHash -LiteralPath $arenaSourceFile -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $arenaWork 'production.sha256')
Write-Output $arenaResult.Output
Write-Output "Evidence: $arenaWork"
