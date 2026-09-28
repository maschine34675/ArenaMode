param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$agilityDll = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$agilityAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($agilityDll)
function Get-NativeMethod([string]$Type, [string]$Name, [string]$Parameters) {
    $typeDefinition = $agilityAssembly.MainModule.GetType($Type)
    if ($null -eq $typeDefinition) { throw "Missing native type $Type" }
    $methods = @($typeDefinition.Methods | Where-Object {
        $_.Name -eq $Name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -eq $Parameters
    })
    if ($methods.Count -ne 1 -or !$methods[0].HasBody -or $methods[0].IsStatic) { throw "Native signature drift $Type.$Name" }
    return $methods[0]
}
function Assert-Site($Method, [string]$Op, [string]$Type, [string]$Member, [int]$Count) {
    $sites = @($Method.Body.Instructions | Where-Object {
        $_.OpCode.Name -in $Op.Split(',') -and $_.Operand.DeclaringType.FullName -eq $Type -and $_.Operand.Name -eq $Member
    })
    if ($sites.Count -ne $Count) { throw "Native operand count changed $($Method.Name)/$Member" }
    foreach ($site in $sites) {
        $result = if ($site.Operand -is [Mono.Cecil.FieldReference]) { $site.Operand.FieldType } else { $site.Operand.ReturnType }
        if ($result.FullName -ne 'System.Single') { throw "Native scalar type changed $Member" }
    }
}
try {
    $move = Get-NativeMethod 'EFT.MovePlayerState' 'UpdateMovementDirection' 'System.Single'
    $direction = Get-NativeMethod 'EFT.MovePlayerState' 'ProcessDirection' 'System.Single,System.Single'
    $pre = Get-NativeMethod 'EFT.MovementContext' 'PreSprintAcceleration' 'System.Single'
    $sprint = Get-NativeMethod 'EFT.MovementContext' 'SprintAcceleration' 'System.Single'
    foreach ($method in @($move,$direction,$pre,$sprint)) {
        if ($method.ReturnType.FullName -ne 'System.Void' -or !$method.IsPublic -or $method.Body.ExceptionHandlers.Count -ne 0) {
            throw "Unreviewed native method shape $($method.Name)"
        }
    }
    Assert-Site $move 'call,callvirt' 'EFT.MovementContext' 'get_WalkInertia' 2
    Assert-Site $move 'call,callvirt' 'EFT.MovementContext' 'get_MoveSideInertia' 1
    Assert-Site $move 'call,callvirt' 'EFT.MovementContext' 'get_MoveDiagonalInertia' 1
    Assert-Site $move 'call,callvirt' 'EFT.MovementContext' 'get_Inertia' 1
    Assert-Site $move 'ldfld' 'EFT.GlobalConfiguration/InertiaSettings' 'SuddenChangesSmoothness' 1
    Assert-Site $direction 'ldfld' 'EFT.MovePlayerState' '_directionSpeedAcceleration' 1
    Assert-Site $pre 'ldfld' 'PhysicalBase' 'PreSprintAcceleration' 1
    Assert-Site $sprint 'ldfld' 'PhysicalBase' 'SprintAcceleration' 1
    Assert-Site $sprint 'ldfld' 'PhysicalBase' 'Inertia' 1
    # Baseline conversion is not guessed from the misleading extension names.
    $vectors = $agilityAssembly.MainModule.GetType('EFT.VectorTools')
    foreach ($name in @('Evaluate','InverseLerp')) {
        $method = @($vectors.Methods | Where-Object { $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'UnityEngine.Vector2,System.Single' })
        if ($method.Count -ne 1) { throw "Missing native Vector2 $name" }
        $target = if ($name -eq 'Evaluate') { 'Lerp' } else { 'InverseLerp' }
        Assert-Site $method[0] 'call' 'UnityEngine.Mathf' $target 1
    }
    $weights = Get-NativeMethod 'PhysicalBase' 'UpdateWeightLimits' ''
    foreach ($value in @(9000,10000)) {
        if (@($weights.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldc.r4' -and $_.Operand -eq $value }).Count -ne 4) {
            throw 'Native bot overweight sentinel contract changed'
        }
    }
    Write-Output "BOT_AGILITY_INSTALLED_CONTRACT_PASSED SHA256=$((Get-FileHash $agilityDll).Hash); 10 scalar operands; native baseline semantics"
} finally { $agilityAssembly.Dispose() }
