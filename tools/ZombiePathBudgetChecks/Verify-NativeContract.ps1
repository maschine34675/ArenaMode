param([string]$SptPath='D:\SPT41')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$binary=Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($binary)
$harmony=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/core/0Harmony.dll'))
function Method([string]$type,[string]$name) {
    $native=$assembly.MainModule.GetType($type)
    if($null -eq $native){throw "Missing native type $type"}
    $found=@($native.Methods|Where-Object Name -EQ $name)
    if($found.Count -ne 1){throw "Missing/ambiguous native method $type.$name"}
    $found[0]
}
function Calls($method,[string]$name) {
    @($method.Body.Instructions|Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name })
}
function Require([bool]$condition,[string]$message){if(-not $condition){throw "Native contract drift: $message"}}
try {
    $approach=Method 'MoveToEnemyZigZag' 'NotMovingCheck'
    Require ($approach.IsPublic -and $approach.IsVirtual -and $approach.Parameters.Count -eq 0 -and $approach.ReturnType.FullName -eq 'System.Void') 'approach prefix signature'
    $clockReads=@($approach.Body.Instructions|Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq '_nextCheck' })
    $clockWrites=@($approach.Body.Instructions|Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq '_nextCheck' })
    $main=@(Calls $approach 'CalcPathToMoveZigZagAndGo');$fallback=@(Calls $approach 'NotMovingCheck')
    Require ($clockReads.Count -eq 1 -and $clockWrites.Count -eq 1 -and $main.Count -eq 1 -and $fallback.Count -eq 1) 'approach whole-job call graph'
    Require ($clockReads[0].Offset -lt $clockWrites[0].Offset -and $clockWrites[0].Offset -lt $main[0].Offset -and $main[0].Offset -lt $fallback[0].Offset) 'approach timer must be guarded before native write'
    foreach($type in @('GoToEnemyZigZag','RunToEnemyZigZag')) {
        $node=Method $type 'UpdateNodeByBrain'
        Require ((@(Calls $node 'NotMovingCheck')).Count -eq 1 -and (@(Calls $node 'PeriodChangeWeapon')).Count -eq 1 -and (@(Calls $node 'LookToMovingDirection')).Count -eq 1) "$type keeps independent combat/steering work"
    }
    $melee=Method 'BotMeleeWeaponData' 'RunToEnemyUpdate'
    Require ($melee.IsPublic -and $melee.Parameters.Count -eq 0 -and $melee.ReturnType.FullName -eq 'System.Boolean') 'melee reverse signature'
    $hit=@(Calls $melee 'TryHit');$reset=@(Calls $melee 'ResetHitTime');$path=@(Calls $melee 'get_HasPathAndNoComplete');$routes=@(Calls $melee 'CanRunToEnemyToHit')
    Require ($hit.Count -eq 1 -and $reset.Count -eq 1 -and $path.Count -eq 1 -and $routes.Count -eq 2) 'unique post-attack planning boundary'
    $all=@($melee.Body.Instructions);$index=[Array]::IndexOf($all,$path[0]);$anchor=$all[$index-3]
    Require ($anchor.OpCode.Name -eq 'ldarg.0' -and $all[$index-2].OpCode.Name -eq 'ldfld' -and $all[$index-2].Operand.Name -eq '_owner' -and $all[$index-1].Operand.Name -eq 'get_Mover') 'complete owner/mover expression'
    Require ($all[$index-4].Offset -eq $reset[0].Offset -and $hit[0].Offset -lt $reset[0].Offset -and $routes[0].Offset -gt $path[0].Offset) 'attacks precede guard, both route branches follow it'
    $incoming=@($all|Where-Object { $_.Operand -is [Mono.Cecil.Cil.Instruction] -and $_.Operand.Offset -eq $anchor.Offset })
    Require ($incoming.Count -ge 1) 'hit-not-due branch label must move onto guard'
    Require ($melee.Body.ExceptionHandlers.Count -eq 0) 'reverse-copy exception block shape'
    $pathClock=@($all|Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_runPathCheck' })
    Require ($pathClock.Count -eq 2 -and $pathClock[0].Offset -gt $anchor.Offset) 'guard precedes path timer reads/writes'
    $canRun=Method 'BotMeleeWeaponData' 'CanRunToEnemyToHit';$canHit=Method 'BotMeleeWeaponData' 'CanHitToPoint'
    Require ((@(Calls $canRun 'CanHitToPoint')).Count -eq 2 -and (@(Calls $canHit 'CalculatePath')).Count -eq 2 -and (@(Calls $canHit 'ModifyZigZagPercentOffset')).Count -eq 1) 'native routes include conditional zigzag searches'
    $zigzag=Method 'ZigZagWayBuilder' 'TryPath'
    Require ((@(Calls $zigzag 'CalculatePath')).Count -ge 1) 'nested searches remain outside a fixed path-call bound'
    $reverse=@($harmony.MainModule.GetType('HarmonyLib.Harmony').Methods|Where-Object {$_.Name -eq 'ReversePatch' -and $_.Parameters.Count -eq 4})
    Require ($reverse.Count -eq 1 -and $reverse[0].IsPublic -and $reverse[0].IsStatic -and $reverse[0].ReturnType.FullName -eq 'System.Reflection.MethodInfo') 'supported four-argument HarmonyX reverse copy'
    Require ((@($harmony.MainModule.GetType('HarmonyLib.HarmonyMethod').Fields|Where-Object Name -EQ 'reversePatchType')).Count -eq 1) 'explicit Original reverse-copy setting'
    'ZOMBIE_PATH_BUDGET_NATIVE_CONTRACT_PASSED'
    'Bound: two admitted native planning jobs; no fixed CalculatePath-call or CPU-time claim.'
    'Assembly-CSharp SHA256='+(Get-FileHash -LiteralPath $binary).Hash
} finally {$assembly.Dispose();$harmony.Dispose()}
