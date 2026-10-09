param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$binary = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($binary)
function Method([string]$type, [string]$name, [string]$arguments, [string]$result) {
    $nativeType = $assembly.MainModule.GetType($type)
    if ($null -eq $nativeType) { throw "Missing installed type $type" }
    $methods = @($nativeType.Methods | Where-Object {
        $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join ',') -eq $arguments
    })
    if ($methods.Count -ne 1 -or $methods[0].ReturnType.FullName -ne $result) { throw "Native contract drift $type.$name" }
    return $methods[0]
}
function Calls($method, [string]$type, [string]$name) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $type -and $_.Operand.Name -eq $name
    })
}
function Field([string]$type, [string]$name, [string]$fieldType) {
    $nativeType = $assembly.MainModule.GetType($type)
    $fields = @($nativeType.Fields | Where-Object { $_.Name -eq $name -and $_.FieldType.FullName -eq $fieldType -and $_.IsPublic })
    if ($fields.Count -ne 1) { throw "Native field drift $type.$name" }
}
try {
    $hearing = Method 'BotHearingSensor' 'OnSoundPlayed' 'EFT.IPlayer,UnityEngine.Vector3,System.Single,AISoundType' 'System.Void'
    if (!$hearing.IsPublic -or ($hearing.Parameters.Name -join ',') -ne 'player,position,power,type') { throw 'Hearing Harmony argument contract changed' }
    if ((Calls $hearing 'AITaskManager' 'RegisterDelayedTask').Count -ne 1 -or
        (Calls $hearing 'BotHearingSensor' 'OnSoundPlayedInner').Count -ne 2 -or
        (Calls $hearing 'EFT.BotMemory' 'get_GoalEnemy').Count -ne 1) { throw 'Native scheduling/inline hearing paths changed' }
    $inner = Method 'BotHearingSensor' 'OnSoundPlayedInner' 'EFT.IPlayer,UnityEngine.Vector3,System.Single,AISoundType' 'System.Void'
    if ((Calls $inner 'BotsGroup' 'IsEnemy').Count -ne 1) { throw 'Native enemy hearing predicate changed' }
    $social = Method 'BotFriendChecker' 'ManualUpdate' '' 'System.Void'
    if ((Calls $social 'BotsConnections' 'GetClosest').Count -ne 1 -or
        (Calls $social 'UnityEngine.Physics' 'Linecast').Count -ne 1) { throw 'Native social search scope changed' }
    $alive = Method 'EFT.GameWorld' 'GetAlivePlayerByProfileID' 'System.String' 'EFT.Player'
    if (@($alive.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'TryGetValue' }).Count -ne 1) {
        throw 'Live-body lookup no longer a dictionary read'
    }
    Field 'BotHearingSensor' '_botOwner' 'EFT.BotOwner'
    Field 'BotData' '_owner' 'EFT.BotOwner'
    Field 'BotFriendChecker' '_nextTimeCheck' 'System.Single'
    Field 'BotGlobasHearingSettings' 'HEAR_DELAY_WHEN_PEACE' 'System.Single'
    Field 'BotGlobasHearingSettings' 'HEAR_DELAY_WHEN_HAVE_SMT' 'System.Single'
    [void](Method 'BotGoalTarget' 'HavePlaceTarget' '' 'System.Boolean')
    [void](Method 'BotsGroup' 'Contains' 'EFT.BotOwner' 'System.Boolean')
    [void](Method 'BotsGroup' 'IsAlly' 'EFT.IPlayer' 'System.Boolean')
    [void](Method 'BotsGroup' 'IsSuitable' 'EFT.WildSpawnType' 'System.Boolean')
    [void](Method 'EFT.BotOwner' 'get_FriendChecker' '' 'BotFriendChecker')
    [void](Method 'EFT.BotOwner' 'get_HearingSensor' '' 'BotHearingSensor')
    Write-Output 'ZOMBIE_PERFORMANCE_NATIVE_CONTRACT_PASSED'
    Write-Output ('Assembly-CSharp SHA256=' + (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash)
} finally { $assembly.Dispose() }
