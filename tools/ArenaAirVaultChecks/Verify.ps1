param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$arenaNative = [IO.Path]::GetFullPath((Join-Path $arenaRepo '..\_decompiled'))
$arenaWork = Join-Path $arenaRepo ('artifacts\air-vault-checks\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaWork) | Out-Null
$arenaProject = Join-Path $PSScriptRoot 'ArenaAirVaultChecks.csproj'
$arenaSource = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Movement\ArenaAirVault.cs'))

function Read-Method([string]$File, [string]$Signature) {
    $arenaText = [IO.File]::ReadAllText((Join-Path $arenaNative $File))
    $arenaStart = $arenaText.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($arenaStart -lt 0) { throw "Missing native method: $Signature" }
    $arenaBrace = $arenaText.IndexOf('{', $arenaStart)
    $arenaDepth = 0
    for ($arenaIndex = $arenaBrace; $arenaIndex -lt $arenaText.Length; $arenaIndex++) {
        if ($arenaText[$arenaIndex] -eq '{') { $arenaDepth++ }
        if ($arenaText[$arenaIndex] -eq '}') { $arenaDepth--; if ($arenaDepth -eq 0) { return $arenaText.Substring($arenaStart,$arenaIndex-$arenaStart+1) } }
    }
    throw "Unclosed native method: $Signature"
}
$arenaMethods = @(
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public bool CanVaulting()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public EVaultingStrategy GetVaultingStrategy()').Replace('public EVaultingStrategy GetVaultingStrategy()', 'public EVaultingStrategy NativeGetVaultingStrategy()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public EVaultingStrategy GetAutoVaultingStrategy()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public bool TryVaulting()'),
    (Read-Method 'EFT.Vaulting\VaultingComponent.cs' 'public bool DoVaultingByStrategy(EVaultingStrategy strategy)').Replace('public bool DoVaultingByStrategy(', 'public bool NativeDoVaultingByStrategy('))
$arenaNativeFile = Join-Path $arenaWork 'Native.cs'
[IO.File]::WriteAllText($arenaNativeFile, 'namespace EFT.Vaulting { public partial class VaultingComponent {' + ($arenaMethods -join [Environment]::NewLine) + '} }' + [Environment]::NewLine + 'namespace EFT { public partial class MovementContext {' + (Read-Method 'EFT\MovementContext.cs' 'public bool TryVaulting()') + '} }')

function Invoke-Checks([string]$Source = '') {
    $arenaExtra = @("-p:NativeSource=$arenaNativeFile")
    if ($Source) { $arenaExtra += "-p:AirVaultSource=$Source" }
    $arenaLog = @(& dotnet build $arenaProject -t:Rebuild -c Release --nologo @arenaExtra 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($arenaLog -join [Environment]::NewLine) }
    $arenaOutput = @(& dotnet run --project $arenaProject -c Release --no-build 2>&1)
    return @{ Exit=$LASTEXITCODE; Text=($arenaOutput -join [Environment]::NewLine) }
}
function Assert-Production {
    $arenaResult = Invoke-Checks
    [IO.File]::WriteAllText((Join-Path $arenaWork 'production.log'),$arenaResult.Text)
    if ($arenaResult.Exit -ne 0 -or !$arenaResult.Text.Contains('ARENA_AIR_VAULT_CONTRACTS_PASSED')) { throw $arenaResult.Text }
    Write-Output $arenaResult.Text
}
function Test-Fault([string]$Name,[string]$Before,[string]$After,[string]$Assertion) {
    if (!$arenaSource.Contains($Before) -or ($arenaSource.Length-$arenaSource.Replace($Before,'').Length)/$Before.Length -ne 1) { throw "Missing/ambiguous fault anchor: $Name" }
    $arenaFile=Join-Path $arenaWork ($Name+'.cs')
    [IO.File]::WriteAllText($arenaFile,$arenaSource.Replace($Before,$After))
    $arenaResult=Invoke-Checks $arenaFile
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name+'.log')),$arenaResult.Text)
    if ($arenaResult.Exit -eq 0 -or !$arenaResult.Text.Contains('ASSERT: '+$Assertion)) { throw "Fault $Name did not fail its intended assertion: $($arenaResult.Text)" }
    Write-Output "Air vault negative control rejected: $Name"
}
try {
    Assert-Production
    Test-Fault 'scope-removed' '|| !ArenaMovementScope.IsLocal(request.Player, requireInput: true)' '' 'ordinary raid never receives airborne strategy'
    Test-Fault 'held-input-removed' 'return request.Player.IsVaultingPressed' 'return true' 'normal jump without held vault is untouched'
    Test-Fault 'geometry-removed' '&& model.VaultingStatesModel.ClimbState.CanMove() && !context.IsSprintEnabled' '&& !context.IsSprintEnabled' 'no native geometry means no mantle or jump consumption'
    Test-Fault 'stamina-removed' 'component._canClimbByStaminaGetter?.Invoke() == true' 'true' 'native climb stamina remains required'
    Test-Fault 'final-hands-check-removed' '&& request.Player.VaultingGameplayRestrictions?.CanVaulting() == true' '' 'final native geometry rechecks hands restrictions before commit'
    Test-Fault 'lease-cleanup-removed' "catch (Exception ex) { Warn(ex); }`n            finally { if (ReferenceEquals(_request, request)) _request = null; }" "catch (Exception ex) { Warn(ex); }`n            finally { }" 'strategy exception does not survive synchronous request'
    Test-Fault 'pending-geometry-removed' '&& request.CommittedGeometry.Matches(request.Component)' '' 'same strategy with changed geometry cannot use stale animator parameters'
    Test-Fault 'world-binding-removed' '|| !ReferenceEquals(Singleton<GameWorld>.Instance, request.World)' '' 'world drift during geometry refresh rejects commit'
    Test-Fault 'game-binding-removed' '|| !ReferenceEquals(Singleton<AbstractGame>.Instance, request.Game)' '' 'game drift during geometry refresh rejects commit'
    Test-Fault 'finite-geometry-removed' 'if (request.CommittedGeometry.IsFinite) return true;' 'return true;' 'nonfinite native parameters cannot enter animator'
    Test-Fault 'animator-binding-removed' '&& ReferenceEquals(request.Context.PlayerAnimator, request.Animator)' '' 'replacement animator flag is not owned by pending request'
    Test-Fault 'animator-handle-binding-removed' '&& ReferenceEquals(request.Animator.Animator, request.AnimatorHandle)' '' 'replacement underlying animator flag is not owned by pending request'
    Test-Fault 'component-binding-removed' '&& ReferenceEquals(request.Player.VaultingComponent, request.Component)' '' 'replacement vault component cannot inherit old pending request'
}
finally { Assert-Production }
Write-Output "Air vault evidence: $arenaWork"
Write-Output 'ARENA_AIR_VAULT_CHECKS_PASSED'
