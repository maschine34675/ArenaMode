param([string]$SptPath = 'D:\SPT41', [string]$PresetSourceProfile = '')
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaSourcePath = Join-Path $arenaRepo 'ArenaMode.Client\Pickups\ArenaWeaponPickup.cs'
$arenaSource = [IO.File]::ReadAllText($arenaSourcePath).Replace("`r`n", "`n")
$arenaWork = Join-Path $PSScriptRoot ('artifacts\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaDefinitionArgs = @($SptPath)
if ($PresetSourceProfile) { $arenaDefinitionArgs += $PresetSourceProfile }
$arenaDefinition = & dotnet run --project (Join-Path $PSScriptRoot 'Definition/M60DefinitionChecks.csproj') -c Release -- @arenaDefinitionArgs 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or !$arenaDefinition.Contains('ARENA M60 DEFINITION PASSED')) { throw $arenaDefinition }
[IO.File]::WriteAllText((Join-Path $arenaWork 'm60-definition.log'), $arenaDefinition)
Write-Output $arenaDefinition.Trim()
function Run-Adapter([string]$Name, [string]$Override = '') {
    $arenaArgs = @('run', '--project', (Join-Path $PSScriptRoot 'ArenaWeaponPickupChecks.csproj'), '-c', 'Release')
    if ($Override) { $arenaArgs += "-p:PickupSource=$Override" }
    $arenaText = (& dotnet @arenaArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
    $arenaExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name + '.log')), $arenaText)
    return @{ Code = $arenaExit; Output = $arenaText }
}
$arenaContracts = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1') -SptPath $SptPath 2>&1
if ($LASTEXITCODE -ne 0) { throw ($arenaContracts -join "`n") }
$arenaContracts | Set-Content -LiteralPath (Join-Path $arenaWork 'installed.log')
Write-Output $arenaContracts
$arenaNative = & dotnet build (Join-Path $PSScriptRoot 'Native\NativeContracts.csproj') -c Release --nologo -v:quiet "-p:SptPath=$SptPath" 2>&1
if ($LASTEXITCODE -ne 0) { throw ($arenaNative -join "`n") }
$arenaNative | Set-Content -LiteralPath (Join-Path $arenaWork 'native-compile.log')
Write-Output 'Exact production adapter compiled against installed EFT/Unity contracts.'
$arenaResult = Run-Adapter 'production'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('ARENA WEAPON ADAPTER PASSED')) { throw $arenaResult.Output }
Write-Output $arenaResult.Output
$arenaFaults = @(
    @('zero-destroy-bookkeeping', 'Any(count => count.NumberToDestroy > 0)', 'Any()', 'free compatible secondary starts'),
    @('active-slot', 'slot.ContainedItem != null || ReferenceEquals(slot, player.ActiveSlot)', 'slot.ContainedItem != null', 'empty-hands native transfer admitted'),
    @('late-scope', '&& IsAvailable && Eligible(player, isCurrent)', '&& IsAvailable', 'native boundary rejects delayed round'),
    @('ownership', 'result != null && result.Succeed && carried', 'result != null && result.Succeed', 'success callback without ownership is rejected'),
    @('premature-cleanup', 'if (!IsPending) ReleaseSourceVisual();', 'ReleaseSourceVisual();', 'dispose defers visual cleanup while native transfer may roll back')
)
try {
    foreach ($arenaFault in $arenaFaults) {
        if (($arenaSource.Split([string[]]@($arenaFault[1]), [StringSplitOptions]::None)).Count -ne 2) { throw "Expected one mutation site: $($arenaFault[0])" }
        $arenaMutant = Join-Path $arenaWork ($arenaFault[0] + '.cs')
        [IO.File]::WriteAllText($arenaMutant, $arenaSource.Replace($arenaFault[1], $arenaFault[2]))
        $arenaResult = Run-Adapter $arenaFault[0] $arenaMutant
        if ($arenaResult.Code -eq 0 -or !$arenaResult.Output.Contains('ASSERT: ' + $arenaFault[3])) { throw $arenaResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($arenaFault[0])"
    }
}
finally {
    $arenaRestore = & dotnet build (Join-Path $PSScriptRoot 'ArenaWeaponPickupChecks.csproj') -t:Rebuild -c Release --nologo -v:quiet 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($arenaRestore -join "`n") }
}
$arenaResult = Run-Adapter 'production-final'
if ($arenaResult.Code -ne 0 -or !$arenaResult.Output.Contains('ARENA WEAPON ADAPTER PASSED')) { throw $arenaResult.Output }
Write-Output $arenaResult.Output
(Get-FileHash -LiteralPath $arenaSourcePath -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $arenaWork 'production.sha256')
Write-Output 'ARENA WEAPON PICKUP CHECKS PASSED'
