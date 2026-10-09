param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$nativePath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$pftPath = Join-Path $SptPath 'BepInEx/plugins/pitFireTeam/pitFireTeam.dll'
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($nativePath)
$pft = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pftPath)
function Method($assembly, [string]$type, [string]$name) {
    $methods = @($assembly.MainModule.GetType($type).Methods | Where-Object Name -eq $name)
    if ($methods.Count -ne 1) { throw "Missing/ambiguous method $type.$name" }
    return $methods[0]
}
function Calls($method) {
    return @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.DeclaringType.FullName + '::' + $_.Operand.Name })
}
try {
    if ($pft.Name.Version.ToString() -notin @('0.10.0.0', '0.10.3.0')) { throw 'Unsupported installed PFT identity' }
    $prefix = Method $pft 'pitTeam.Patches.HostileNonCombatActiveLayerFilterPatch' 'PatchPrefix'
    if (!$prefix.IsPrivate -or !$prefix.IsStatic -or $prefix.ReturnType.FullName -ne 'System.Void' -or
        ($prefix.Parameters.Name -join ',') -ne '__instance,prevResult,__state' -or
        $prefix.Parameters[0].ParameterType.FullName -ne 'System.Object' -or !$prefix.Parameters[2].ParameterType.IsByReference) {
        throw 'PFT nongeneric static prefix argument contract changed'
    }
    $calls = Calls $prefix
    $hostile = [Array]::IndexOf($calls, 'pitTeam.Patches.HostilePeacefulLayerInterrupt::HasHostileBossOrFollowerEnemy')
    $wake = [Array]::IndexOf($calls, 'pitTeam.Patches.HostilePeacefulLayerInterrupt::WakeHostileBot')
    $combat = [Array]::IndexOf($calls, 'pitTeam.Patches.HostilePeacefulLayerInterrupt::HasUsableCombatLayer')
    if ($hostile -lt 0 -or $wake -le $hostile -or $combat -le $wake) { throw 'PFT hostility/wake/combat order drifted' }
    $follower = Method $pft 'pitTeam.Modules.BossPlayers' 'IsFollower'
    if (!$follower.IsPublic -or !$follower.IsStatic -or $follower.ReturnType.FullName -ne 'System.Boolean' -or
        ($follower.Parameters.ParameterType.FullName -join ',') -ne 'EFT.BotOwner,AIBossPlayer') { throw 'Follower delegate contract drifted' }
    $classification = Method $pft 'pitTeam.Patches.HostilePeacefulLayerInterrupt' 'IsCombatLayer'
    $strings = @($classification.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)
    if (($strings -join ',') -ne 'Combat,Combat,.Combat,SAIN,Squad') { throw 'PFT combat classification changed' }
    $wakeMethod = Method $pft 'pitTeam.Patches.HostilePeacefulLayerInterrupt' 'WakeHostileBot'
    if ((Calls $wakeMethod) -notcontains 'BotStandBy::Activate') { throw 'PFT wake contract changed' }
    $ctor = Method $native 'InfectedLayersStrategy' '.ctor'
    $layerConstructors = @($ctor.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'newobj' -and $_.Operand.DeclaringType.Name -match 'Layer$'
    } | ForEach-Object { $_.Operand.DeclaringType.Name })
    if (($layerConstructors -join ',') -ne 'InfectedWaitLayer,InfectedFightRequestLayer,InfectedPeacefulRequestLayer,InfectedAttackLayer,InfectedTargetLayer,InfectedPatrolLayer') {
        throw 'Native infected constructor layer set changed'
    }
    $attackToggle = Calls (Method $native 'InfectedLayersStrategy' 'SetCanAttack')
    if (@($attackToggle | Where-Object { $_ -match '::ActivateLayer$' }).Count -ne 1 -or
        @($attackToggle | Where-Object { $_ -match '::DeactivateLayer$' }).Count -ne 1) { throw 'Native wait-layer toggle changed' }
    $insert = Calls (Method $native 'AICoreStrategy`1' 'method_4')
    if (@($insert | Where-Object { $_ -match '::Insert$' }).Count -ne 1 -or
        @($insert | Where-Object { $_ -match '::Add$' }).Count -ne 1 -or
        @($insert | Where-Object { $_ -match '::Contains$' }).Count -ne 0) { throw 'Native duplicate layer insertion contract changed' }
    foreach ($typeName in @('InfectedWaitLayer','InfectedAttackLayer','InfectedTargetLayer','InfectedPatrolLayer','DebugLogicLayer','FightRequestLayer','PeacefulRequestLayer')) {
        $name = Method $native $typeName 'Name'
        foreach ($text in @($name.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)) {
            if ($text -match '(?i)combat|squad') { throw "Unexpected combat name $typeName $text" }
        }
    }
    $requests = $native.MainModule.GetType('BotRequestType').Fields | Where-Object IsLiteral | ForEach-Object Name
    if (@($requests | Where-Object { $_ -match '(?i)combat|squad' }).Count -ne 0) { throw 'Request enum can classify native request as combat' }
    $fields = @(
        @('BaseBrain','_owner','EFT.BotOwner'),
        @('BaseLogicLayerSimple','_owner','EFT.BotOwner'),
        @('AICoreStrategy`1','_activeLayers','System.Collections.Generic.List`1<AICoreLayer`1<T>>')
    )
    foreach ($field in $fields) {
        $found = @($native.MainModule.GetType($field[0]).Fields | Where-Object { $_.Name -eq $field[1] -and $_.IsPublic -and $_.FieldType.FullName -eq $field[2] })
        if ($found.Count -ne 1) { throw "Native field drift $($field[0]).$($field[1])" }
    }
    $alive = Method $native 'EFT.GameWorld' 'GetAlivePlayerByProfileID'
    if (@((Calls $alive) | Where-Object { $_ -match '::TryGetValue$' }).Count -ne 1) { throw 'Live body lookup no longer a dictionary read' }
    Write-Output ('PFT assembly version=' + $pft.Name.Version + ' SHA256=' + (Get-FileHash -LiteralPath $pftPath).Hash)
    Write-Output ('Assembly-CSharp SHA256=' + (Get-FileHash -LiteralPath $nativePath).Hash)
    Write-Output 'ZOMBIE_PFT_NATIVE_CONTRACT_PASSED'
} finally { $native.Dispose(); $pft.Dispose() }
