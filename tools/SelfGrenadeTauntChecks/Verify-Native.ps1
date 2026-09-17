param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Support.ps1')
$tauntContractCount = 0
function Assert-Taunt([bool]$value, [string]$label) {
    $script:tauntContractCount++
    if (!$value) { throw "Native/source taunt contract failed: $label" }
}
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$tauntAssemblyPath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$tauntAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($tauntAssemblyPath)
function Native-TauntMethod([string]$type, [string]$name, [string[]]$parameters) {
    $methods = @($tauntAssembly.MainModule.GetType($type).Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($parameters -join '|')
    })
    Assert-Taunt ($methods.Count -eq 1) "unique $type.$name native signature"
    return $methods[0]
}
function Calls-Taunt($method, [string]$type, [string]$name) {
    return @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.FullName -eq $type -and $_.Operand.Name -eq $name }).Count -gt 0
}
try {
    $handsName = 'EFT.Player/BaseGrenadeHandsController'
    $create = Native-TauntMethod $handsName 'CreateAndRegisterGrenade' @('UnityEngine.Vector3','UnityEngine.Quaternion','UnityEngine.Vector3','System.Single')
    Assert-Taunt ($create.ReturnType.FullName -eq 'EFT.Grenade') 'hand create returns real grenade'
    Assert-Taunt (Calls-Taunt $create 'GrenadeFactory' 'Create') 'hand create reaches grenade factory'
    $throw = Native-TauntMethod $handsName 'ThrowGrenade' @('System.Single','UnityEngine.Vector3','UnityEngine.Quaternion','UnityEngine.Vector3','System.Boolean')
    Assert-Taunt (Calls-Taunt $throw $handsName 'CreateAndRegisterGrenade') 'actual throw uses observed hand creation method'
    foreach ($type in @('EFT.Player/GrenadeHandsController','EFT.Player/QuickGrenadeThrowHandsController')) {
        Assert-Taunt ($tauntAssembly.MainModule.GetType($type).BaseType.FullName -eq $handsName) "$type shares physical owner path"
    }
    $owner = @($tauntAssembly.MainModule.GetType('EFT.Player/ItemHandsController').Fields | Where-Object { $_.Name -eq '_player' -and $_.FieldType.FullName -eq 'EFT.Player' })
    Assert-Taunt ($owner.Count -eq 1) 'exact physical controller owner available'
    $tripwire = Native-TauntMethod 'EFT.SynchronizableObjects.TripwireSynchronizableObject' 'ActivateGrenade' @()
    Assert-Taunt (Calls-Taunt $tripwire 'GrenadeFactory' 'Create') 'tripwire directly uses factory'
    Assert-Taunt (!(Calls-Taunt $tripwire $handsName 'CreateAndRegisterGrenade')) 'tripwire bypasses registered hand throw origin'
    $blast = Native-TauntMethod 'EFT.Grenade' 'GetDamageInfo' @('EFT.InventoryLogic.Item','System.String','UnityEngine.Vector3','System.Boolean')
    Assert-Taunt (Calls-Taunt $blast 'EFT.Ballistics.Shot' 'Create') 'blast creates damage carrier with original item'
    Assert-Taunt (Calls-Taunt $blast 'EFT.Ballistics.DamageInfo' '.ctor') 'blast constructs actual DamageInfo'
    $fragmentValue = ($tauntAssembly.MainModule.GetType('EFT.EDamageType').Fields | Where-Object { $_.Name -eq 'GrenadeFragment' }).Constant
    Assert-Taunt (@($blast.Body.Instructions | Where-Object { $_.OpCode.Name -in @('ldc.i4','ldc.i4.s') -and $_.Operand -eq $fragmentValue }).Count -eq 1) 'blast uses actual GrenadeFragment enum value'
    $fragment = Native-TauntMethod 'EFT.InventoryLogic.ThrowWeap' 'CreateFragment' @()
    Assert-Taunt (Calls-Taunt $fragment 'EFT.InventoryLogic.ThrowWeap' 'get_FragmentType') 'fragment item uses grenade FragmentType'
    $ctor = Native-TauntMethod 'EFT.Ballistics.DamageInfo' '.ctor' @('EFT.EDamageType','EFT.Ballistics.Shot')
    foreach ($field in @('Weapon','Player','SourceId')) {
        Assert-Taunt (@($ctor.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq $field }).Count -eq 1) "native damage preserves $field"
    }
    $impact = Native-TauntMethod 'EFT.ClientGameWorld' 'ShotDelegate' @('EFT.Ballistics.Shot')
    $bulletValue = ($tauntAssembly.MainModule.GetType('EFT.EDamageType').Fields | Where-Object { $_.Name -eq 'Bullet' }).Constant
    Assert-Taunt (@($impact.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldc.i4' -and $_.Operand -eq $bulletValue }).Count -ge 1) 'physical fragment impact uses actual Bullet type'
    $apply = Native-TauntMethod 'EFT.Player' 'ApplyDamageInfo' @('EFT.Ballistics.DamageInfo','EBodyPart','EBodyPartColliderType','System.Single')
    Assert-Taunt (Calls-Taunt $apply 'EFT.HealthSystem.ActiveHealthController' 'ApplyDamage') 'scoped player damage reaches health synchronously'
    $dead = Native-TauntMethod 'EFT.Player' 'OnDead' @('EFT.EDamageType')
    Assert-Taunt (Calls-Taunt $dead 'EFT.Player' 'OnBeenKilledByAggressor') 'native death enters existing kill capture even for self attribution'
} finally { $tauntAssembly.Dispose() }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$combat = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/CombatPatches.cs'))
$taunt = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/ArenaSelfGrenadeTaunt.cs'))
$hud = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Hud/ArenaHud.cs'))
$audio = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Audio/AnnouncerService.cs'))
$match = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Modes/ArenaMatch.cs'))
Assert-Taunt ((Get-TauntMethod $combat 'public static void Enable()').Contains('ArenaSelfGrenadeTaunt.Enable();')) 'combat enables taunt observation'
Assert-Taunt ((Get-TauntMethod $combat 'public static void ResetRaidState()').Contains('ArenaSelfGrenadeTaunt.Reset();')) 'raid clears origins and report state'
$kill = Get-TauntMethod $combat 'private sealed class KillCapturePatch'
$observe = $kill.IndexOf('ArenaSelfGrenadeTaunt.ObserveDeath(__instance, aggressor, damageInfo, lethalDamageType);')
$selfReturn = [regex]::Match($kill, 'if \(string\.IsNullOrEmpty\(victimId\) \|\| aggressor\.ProfileId == victimId\)\s*return;')
Assert-Taunt ($observe -ge 0 -and $selfReturn.Success -and $observe -lt $selfReturn.Index) 'observe before unchanged score-excluding self-kill return'
Assert-Taunt ((Get-TauntMethod $hud 'public static void Attach(').Contains('match.OnTextCallout += OnCallout;')) 'HUD attaches text-only callout'
Assert-Taunt ((Get-TauntMethod $hud 'public static void Detach()').Contains('_match.OnTextCallout -= OnCallout;')) 'HUD detaches text-only callout'
Assert-Taunt (!$audio.Contains('OnTextCallout')) 'announcer does not subscribe to text event'
Assert-Taunt ((Get-TauntMethod $match 'internal void BeginAwardRound(').Contains('ArenaShotAwardHistory.Reset();')) 'existing award round epoch invalidates thrown origins'
$observeDeath = Get-TauntMethod $taunt 'internal static void ObserveDeath('
Assert-Taunt ($observeDeath.Contains('ReportTextCallout(Label)') -and $observeDeath.Contains('ReportAnnouncerCue(ArenaAnnouncerCue.Embarrassing)') -and !($observeDeath -match '\.ReportKill\(|\.ReportLocalDeath\(')) 'taunt dispatch adds only separate Embarrassing speech without scoring or death events'
Write-Output "SELF_GRENADE_NATIVE_CONTRACT_PASSED: $tauntContractCount native metadata and source wiring assertions; not native runtime proof"
