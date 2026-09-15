param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
$powerContractCount = 0
function Assert-Power([bool]$value, [string]$label) {
    $script:powerContractCount++
    if (!$value) { throw "Powerup native/source contract failed: $label" }
}
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$powerAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
function Power-Method([string]$type, [string]$name, [string[]]$parameters) {
    $methods = @($powerAssembly.MainModule.GetType($type).Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($parameters -join '|')
    })
    Assert-Power ($methods.Count -eq 1) "unique $type.$name signature"
    return $methods[0]
}
function Power-Calls($method, [string]$name) {
    return @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name })
}
try {
    $shot = Power-Method 'EFT.Player' 'ApplyShot' @('EFT.Ballistics.DamageInfo','EBodyPart','EBodyPartColliderType','EArmorPlateCollider','EFT.Ballistics.ShotId')
    $armor = @(Power-Calls $shot 'ProceedDamageThroughArmor'); $healthRoute = @(Power-Calls $shot 'ApplyDamageInfo')
    Assert-Power ($armor.Count -eq 1 -and $healthRoute.Count -eq 1 -and $armor[0].Offset -lt $healthRoute[0].Offset) 'native shot runs armor before exactly one Player health route'
    $playerDamage = Power-Method 'EFT.Player' 'ApplyDamageInfo' @('EFT.Ballistics.DamageInfo','EBodyPart','EBodyPartColliderType','System.Single')
    $applyCall = @(Power-Calls $playerDamage 'ApplyDamage')
    Assert-Power ($applyCall.Count -eq 1 -and $applyCall[0].Operand.DeclaringType.FullName -eq 'EFT.HealthSystem.ActiveHealthController') 'Player health route calls the patched controller exactly once'
    $apply = Power-Method 'EFT.HealthSystem.ActiveHealthController' 'ApplyDamage' @('EBodyPart','System.Single','EFT.Ballistics.DamageInfo')
    Assert-Power (!$apply.IsVirtual -and !$apply.IsStatic -and $apply.ReturnType.FullName -eq 'System.Single') 'single nonvirtual health method returns actual HP loss'
    $coeff = @(Power-Calls $apply 'get_DamageCoeff'); $mult = @(Power-Calls $apply 'get_DamageMultiplier')
    $change = @(Power-Calls $apply 'ChangeHealth'); $enemy = @(Power-Calls $apply 'IsEnemyDamage')
    $ignore = @($apply.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'MUTUAL_IGNORE_FRIENDLY_FIRE' })
    Assert-Power ($coeff.Count -eq 1 -and $change.Count -eq 2 -and $coeff[0].Offset -lt $change[0].Offset) 'native invulnerability check precedes primary and overflow HP mutations'
    Assert-Power ($coeff[0].Next.OpCode.Name -eq 'ldc.r4' -and $coeff[0].Next.Operand -eq 0 -and
        $coeff[0].Next.Next.OpCode.Name -eq 'ble.un' -and $coeff[0].Next.Next.Operand.Next.OpCode.Name -eq 'ret') 'non-positive or NaN native coefficient branches to zero return'
    Assert-Power ($ignore.Count -eq 2 -and $ignore[-1].Offset -lt $change[0].Offset) 'both native mutual-friendly AI flags are checked before HP changes'
    Assert-Power ($mult.Count -eq 2 -and $enemy.Count -ge 2 -and $mult[-1].Offset -lt $change[0].Offset) 'native head/attack classification and multiplier precede health change'
    Assert-Power (@(Power-Calls $apply 'ApplyDamage').Count -eq 0) 'native overflow never recursively reapplies powerups'
    Assert-Power (@(Power-Calls $apply 'GetOverDamage').Count -eq 1) 'native overflow explicitly marks its provenance'
    $changeMethod = Power-Method 'EFT.HealthSystem.ActiveHealthController' 'ChangeHealth' @('EBodyPart','System.Single','EFT.Ballistics.DamageInfo')
    $set = @(Power-Calls $changeMethod 'set_Current'); $diff = @(Power-Calls $changeMethod 'get_LastDiff')
    $event = @($changeMethod.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'HealthChangedEvent' })
    Assert-Power ($set.Count -eq 1 -and $diff.Count -eq 1 -and $event.Count -eq 1 -and $set[0].Offset -lt $diff[0].Offset -and $diff[0].Offset -lt $event[0].Offset) 'assist callback observes completed actual HP mutation'
    $setter = Power-Method 'EFT.HealthSystem.HealthValue' 'set_Current' @('System.Single')
    Assert-Power (@(Power-Calls $setter 'get_DownMult').Count -eq 1 -and @(Power-Calls $setter 'Clamp').Count -eq 1) 'native per-part coefficient and clamp follow impact-point absorption'
    $explosion = Power-Method 'EFT.Player' 'ApplyExplosionDamageToArmor' @('System.Collections.Generic.Dictionary`2<EFT.ExplosionDamageInfo,System.Single>','EFT.Ballistics.DamageInfo')
    Assert-Power (@(Power-Calls $explosion 'ApplyExplosionDurabilityDamage').Count -eq 1 -and @(Power-Calls $explosion 'ApplyDamage').Count -eq 0) 'explosion armor durability remains a separate native operation'
    $dead = Power-Method 'EFT.Player' 'OnDead' @('EFT.EDamageType')
    Assert-Power (!$dead.IsStatic -and $dead.ReturnType.FullName -eq 'System.Void') 'physical life cleanup hooks native death entry'
    $lookup = Power-Method 'EFT.GameWorld' 'GetEverExistedPlayerByID' @('System.String')
    Assert-Power ($lookup.ReturnType.FullName -eq 'EFT.Player') 'dead owner admission can resolve exact physical body'
    # Evaluate the installed constant mask initializers without loading the game.
    # Unknown IL rejects the contract rather than falling back to remembered masks.
    $helper = Power-Method 'EFT.HealthSystem.DamageHelper' '.cctor' @()
    $weaponStore = @($helper.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stsfld' -and $_.Operand.Name -eq 'WeaponDamage' })
    Assert-Power ($weaponStore.Count -eq 1 -and $weaponStore[0].Previous.OpCode.Name -eq 'ldc.i4') 'native weapon mask starts as an explicit constant'
    $masks = @{ WeaponDamage = [int]$weaponStore[0].Previous.Operand }; $stack = [Collections.Generic.Stack[int]]::new()
    $instruction = $weaponStore[0].Next
    while ($instruction.OpCode.Name -ne 'ret') {
        switch ($instruction.OpCode.Name) {
            'ldsfld' { $stack.Push($masks[$instruction.Operand.Name]) }
            'ldc.i4' { $stack.Push([int]$instruction.Operand) }
            'ldc.i4.s' { $stack.Push([int]$instruction.Operand) }
            'ldc.i4.4' { $stack.Push(4) }
            'or' { $right = $stack.Pop(); $left = $stack.Pop(); $stack.Push($left -bor $right) }
            'stsfld' { $masks[$instruction.Operand.Name] = $stack.Pop() }
            default { throw "Unknown damage-mask initialization opcode: $($instruction.OpCode.Name)" }
        }
        $instruction = $instruction.Next
    }
    foreach ($type in @('Bullet','Explosion','GrenadeFragment')) {
        $value = [int]($powerAssembly.MainModule.GetType('EFT.EDamageType').Fields | Where-Object Name -eq $type).Constant
        Assert-Power (($masks.EnemiesDamage -band $value) -eq $value -and ($masks.EnvironmentalDamage -band $value) -eq 0) "$type is native enemy damage and not environmental"
    }
    $damageSource = [IO.File]::ReadAllText((Join-Path $SptPath 'Development/_decompiled/EFT.HealthSystem/ActiveHealthController.cs'))
    Assert-Power ($damageSource.Contains('base.DamageMultiplier > 1f || bodyPart != EBodyPart.Head || !damageInfo.DamageType.IsEnemyDamage()')) 'decompile spells out native enemy-head multiplier exemption'
    $bodyHead = @($apply.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_DamageMultiplier' })[0]
    $window = @($apply.Body.Instructions | Where-Object { $_.Offset -ge $bodyHead.Offset -and $_.Offset -lt $mult[-1].Offset })
    Assert-Power (@($window | Where-Object { $_.OpCode.Name -eq 'ldc.r4' -and $_.Operand -eq 1.0 }).Count -eq 1 -and
        @($window | Where-Object { $_.OpCode.Name -eq 'ldarg.1' }).Count -eq 1 -and
        @($window | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'IsEnemyDamage' }).Count -eq 1) 'installed native multiplier branch includes threshold1, head body index0 and enemy predicate'
    $head = $powerAssembly.MainModule.GetType('EBodyPart').Fields | Where-Object Name -eq 'Head'
    Assert-Power ($head.Constant -eq 0 -and $bodyHead.Next.Next.OpCode.Name -eq 'bgt.s' -and
        $bodyHead.Next.Next.Operand.Offset -eq $mult[-1].Previous.Previous.Offset -and
        $bodyHead.Next.Next.Next.Next.OpCode.Name -eq 'brtrue.s' -and
        $enemy[0].Next.OpCode.Name -eq 'brtrue.s' -and $enemy[0].Next.Operand.Offset -gt $mult[-1].Offset) 'installed branch exempts only enemy head reductions and sends all increases through multiplication'
    $grenade = Power-Method 'EFT.Grenade' 'GetDamageInfo' @('EFT.InventoryLogic.Item','System.String','UnityEngine.Vector3','System.Boolean')
    Assert-Power (@(Power-Calls $grenade 'Create').Count -eq 1) 'grenade detonation rebuilds a native shot using its owner profile argument'
    $shotTypes = @($powerAssembly.MainModule.GetType('EFT.Ballistics.Shot').Methods | Where-Object { $_.HasBody })
    Assert-Power (@($shotTypes | Where-Object { @(Power-Calls $_ 'GetEverExistedBridgeByProfileID').Count -eq 1 }).Count -ge 1) 'native shot construction can look up a replacement owner bridge'
    $info = Power-Method 'EFT.Ballistics.DamageInfo' '.ctor' @('EFT.EDamageType','EFT.Ballistics.Shot')
    Assert-Power (@($info.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'Weapon' -and $_.Operand.DeclaringType.FullName -eq 'EFT.Ballistics.Shot' }).Count -eq 1 -and
        @($info.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'Weapon' }).Count -eq 1) 'DamageInfo forwards original shot weapon for firearm versus grenade classification'
    $launcher = Power-Method 'EFT.InventoryLogic.Weapon' 'get_IsGrenadeLauncher' @()
    Assert-Power ($launcher.ReturnType.FullName -eq 'System.Boolean' -and $powerAssembly.MainModule.GetType('EFT.InventoryLogic.ThrowWeap').BaseType.FullName -eq 'EFT.InventoryLogic.Item') 'throwable weapon is not firearm Weapon; native launcher flag is available'
} finally { $powerAssembly.Dispose() }
$powerRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $powerRepo 'ArenaMode.Client/Pickups/ArenaPowerups.cs'))
$policy = [IO.File]::ReadAllText((Join-Path $powerRepo 'ArenaMode.Client/Pickups/ArenaPowerupState.cs'))
$combat = [IO.File]::ReadAllText((Join-Path $powerRepo 'ArenaMode.Client/Combat/CombatPatches.cs'))
$assist = [IO.File]::ReadAllText((Join-Path $powerRepo 'ArenaMode.Client/Combat/ArenaAssistCapture.cs'))
Assert-Power ($source.Contains('nameof(ActiveHealthController.ApplyDamage)') -and !$source.Contains('nameof(Player.ApplyShot)') -and !$source.Contains('nameof(Player.ApplyDamageInfo)')) 'production hooks one post-armor health seam only'
Assert-Power ($source.Contains('bool __runOriginal') -and $source.Contains('HarmonyPriority(Priority.Last)')) 'production respects earlier Harmony vetoes'
Assert-Power (!(($source + $policy) -match '\.DamageCoeff\s*=(?!=)|\.DamageMultiplier\s*=(?!=)|\.SetDamageCoeff\s*\(|\.Template\b')) 'production never writes shared health coefficients or templates'
Assert-Power ($source.Contains('info.DamageType == EDamageType.Bullet && info.Weapon is Weapon weapon && !weapon.IsGrenadeLauncher') -and $source.Contains('&& ArenaPickupRuntime.IsLiveActor(attacker)')) 'outgoing bonus requires firearm bullet and current living source; no physical grenade fragment bonus'
Assert-Power ($combat.Contains('BodyDamage = __result,') -and $assist.Contains('Damage = -(double)diff')) 'damage feed and assist policy still use actual native HP deltas'
Write-Output "ARENA_POWERUP_NATIVE_PASSED: $powerContractCount installed IL/source contracts; not Unity/Harmony runtime proof"
