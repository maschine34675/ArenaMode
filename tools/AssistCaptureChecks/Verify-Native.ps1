param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
$assistContractCount = 0
function Assert-Assist([bool]$value, [string]$label) {
    $script:assistContractCount++
    if (!$value) { throw "Assist native/source contract failed: $label" }
}
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$assistAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
function Assist-Method([string]$type, [string]$name, [string[]]$parameters) {
    $methods = @($assistAssembly.MainModule.GetType($type).Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($parameters -join '|')
    })
    Assert-Assist ($methods.Count -eq 1) "unique $type.$name signature"
    return $methods[0]
}
function Assist-Call($method, [string]$type, [string]$name) {
    return @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.FullName -eq $type -and $_.Operand.Name -eq $name })
}
function Assist-Methods($type) {
    foreach ($method in $type.Methods) { if ($method.HasBody) { $method } }
    foreach ($nested in $type.NestedTypes) { Assist-Methods $nested }
}
try {
    $health = Assist-Method 'EFT.Player' 'OnHealthChanged' @('EBodyPart','System.Single','EFT.Ballistics.DamageInfo')
    Assert-Assist (!$health.IsStatic -and !$health.IsVirtual -and $health.ReturnType.FullName -eq 'System.Void') 'observed event handler is an exact nonvirtual instance method'
    $changes = Assist-Method 'EFT.HealthSystem.ActiveHealthController' 'ChangeHealth' @('EBodyPart','System.Single','EFT.Ballistics.DamageInfo')
    $setter = @(Assist-Call $changes 'EFT.HealthSystem.HealthValue' 'set_Current')
    $diff = @(Assist-Call $changes 'EFT.HealthSystem.HealthValue' 'get_LastDiff')
    $event = @($changes.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'HealthChangedEvent' })
    Assert-Assist ($setter.Count -eq 1 -and $diff.Count -eq 1 -and $event.Count -eq 1) 'actual HP mutation computes one LastDiff before one health event'
    Assert-Assist ($setter[0].Offset -lt $diff[0].Offset -and $diff[0].Offset -lt $event[0].Offset) 'health event reports completed native mutation'
    $clamp = Assist-Method 'EFT.HealthSystem.HealthValue' 'set_Current' @('System.Single')
    Assert-Assist (@(Assist-Call $clamp 'UnityEngine.Mathf' 'Clamp').Count -eq 1) 'actual health setter clamps damage and overkill'
    Assert-Assist (@(Assist-Call $clamp 'EFT.HealthSystem.HealthValue' 'set_LastDiff').Count -eq 1) 'actual health setter publishes resulting delta'
    $apply = Assist-Method 'EFT.HealthSystem.ActiveHealthController' 'ApplyDamage' @('EBodyPart','System.Single','EFT.Ballistics.DamageInfo')
    $changeCalls = @(Assist-Call $apply 'EFT.HealthSystem.ActiveHealthController' 'ChangeHealth')
    $destroy = @(Assist-Call $apply 'EFT.HealthSystem.ActiveHealthController' 'DestroyBodyPart')
    Assert-Assist ($changeCalls.Count -eq 2 -and $destroy.Count -eq 2) 'native primary and spread paths each change HP before body destruction'
    Assert-Assist ($changeCalls[0].Offset -lt $destroy[0].Offset -and $changeCalls[1].Offset -lt $destroy[1].Offset) 'lethal HP contribution exists before either death path'
    Assert-Assist (@(Assist-Call $changes 'EFT.HealthSystem.ActiveHealthController' 'ApplyDamage').Count -eq 0) 'health event is not another enclosing damage application'
    $spread = Assist-Method 'EFT.Ballistics.DamageInfo' 'GetOverDamage' @('EBodyPart')
    Assert-Assist (@($spread.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'OverDamageFrom' }).Count -eq 1) 'native spread copies damage provenance and labels source part'
    Assert-Assist (@($spread.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'Player' }).Count -eq 0) 'native spread does not overwrite attacker'
    $kill = Assist-Method 'EFT.HealthSystem.ActiveHealthController' 'Kill' @('EFT.EDamageType')
    Assert-Assist (@($kill.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'DiedEvent' }).Count -eq 1) 'native Kill publishes synchronous death event'
    $dead = Assist-Method 'EFT.Player' 'OnDead' @('EFT.EDamageType')
    $killCallback = @(Assist-Call $dead 'EFT.Player' 'OnBeenKilledByAggressor')
    Assert-Assist ($killCallback.Count -eq 1) 'existing kill scoring occurs inside observed native OnDead'
    $playerDead = @($dead.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'OnPlayerDead' })
    Assert-Assist ($playerDead.Count -eq 1 -and $killCallback[0].Offset -lt $playerDead[0].Offset) 'kill capture precedes later player death and group cleanup callbacks'
    $aiKill = Assist-Method 'AIData' 'KillEnemy' @('EFT.Player')
    Assert-Assist (@(Assist-Call $aiKill 'PlayerLoyaltyData' 'KillEnemy').Count -eq 1) 'native aggressor notification only reaches loyalty bookkeeping'
    Assert-Assist (@($aiKill.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'BotsGroup' }).Count -eq 0) 'aggressor notification does not remove native group hostility before capture'
    $methods = @(Assist-Methods ($assistAssembly.MainModule.GetType('EFT.Player')))
    $subscriptions = @($methods | Where-Object {
        @(Assist-Call $_ 'EFT.Player' 'OnHealthChanged').Count -gt 0 -and
        @($_.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'add_HealthChangedEvent' }).Count -gt 0
    })
    Assert-Assist ($subscriptions.Count -eq 1) 'native Player init subscribes exact observed callback once'
    $removals = @($methods | Where-Object {
        @(Assist-Call $_ 'EFT.Player' 'OnHealthChanged').Count -gt 0 -and
        @($_.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'remove_HealthChangedEvent' }).Count -gt 0
    })
    Assert-Assist ($removals.Count -eq 1) 'native Player disposal removes its own callback'
    foreach ($name in @('GetEverExistedPlayerByID','GetAlivePlayerByProfileID')) {
        $lookup = Assist-Method 'EFT.GameWorld' $name @('System.String')
        Assert-Assist ($lookup.ReturnType.FullName -eq 'EFT.Player') "$name returns physical Player"
    }
    $helper = Assist-Method 'EFT.HealthSystem.DamageHelper' '.cctor' @()
    Assert-Assist (@($helper.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'Player' }).Count -eq 0) 'environmental and bleed DamageHelper values contain no attacker'
} finally { $assistAssembly.Dispose() }
$assistRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = [IO.File]::ReadAllText((Join-Path $assistRoot 'ArenaMode.Client/Combat/ArenaAssistCapture.cs'))
$combat = [IO.File]::ReadAllText((Join-Path $assistRoot 'ArenaMode.Client/Combat/CombatPatches.cs'))
Assert-Assist ($combat.Contains('ArenaAssistCapture.Enable();')) 'combat bootstrap enables assist observation'
Assert-Assist ($source.Contains('nameof(Player.OnHealthChanged)') -and $source.Contains('nameof(Player.OnDead)')) 'source hooks measured event and cleanup'
Assert-Assist (!$source.Contains('nameof(Player.ApplyShot)') -and !$source.Contains('nameof(Player.ApplyDamageInfo)') -and !$source.Contains('nameof(ActiveHealthController.ApplyDamage)')) 'no enclosing damage hook double-counts HP'
Assert-Assist (!$source.Contains('.LastAggressor') -and !$source.Contains('info.Damage;') -and !$source.Contains('info.DidArmorDamage')) 'no stale attacker or raw damage fallback'
Assert-Assist ($source.Contains('match.RecordAssistDamage(damage);') -and $source.Contains('match.ForgetAssistVictim(ArenaHitHistory.BodyId(victim))')) 'actual match receives damage and post-death cleanup'
Assert-Assist ($source.Contains('TryHostileSource(mode, world, local, victim, info.Player?.iPlayer,') -and
    $source.Contains('TryHostileSource(match.Mode, world, local, victim, aggressor, damageType, out _, out _)')) 'damage attribution and final kill share the same hostile-source policy'
$prepare = $combat.IndexOf('ArenaAssistCapture.PrepareKill(match, __instance, aggressor, lethalDamageType);', [StringComparison]::Ordinal)
$report = $combat.IndexOf('match.ReportKill(kill);', [StringComparison]::Ordinal)
Assert-Assist ($prepare -ge 0 -and $prepare -lt $report) 'terminal assist rejection precedes unchanged killer score admission'
Assert-Assist (!$source.Contains('.Kill(') -and !$source.Contains('.ApplyDamage(') -and !$source.Contains('.ChangeHealth(')) 'observational adapter does not mutate native health'
Write-Output "ASSIST_CAPTURE_NATIVE_CONTRACT_PASSED: $assistContractCount native IL and source contracts; not Unity/Harmony runtime proof"
