param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
$ErrorActionPreference = 'Stop'
$shotContractCount = 0
function Assert-Contract([bool]$value, [string]$label) {
    $script:shotContractCount++
    if (!$value) { throw "Native/source integration contract failed: $label" }
}
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$shotAssemblyPath = Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$shotAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($shotAssemblyPath)
function Native-Type([string]$name) {
    $type = $shotAssembly.MainModule.GetType($name)
    if (!$type) { throw "Native type missing: $name" }
    return $type
}
function Native-Method([string]$type, [string]$name, [string[]]$parameters) {
    $methods = @((Native-Type $type).Methods | Where-Object {
        $_.Name -eq $name -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($parameters -join '|')
    })
    Assert-Contract ($methods.Count -eq 1) "unique $type.$name signature"
    return $methods[0]
}
function Has-Call($method, [string]$type, [string]$name) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq $type -and $_.Operand.Name -eq $name
    }).Count -gt 0
}
try {
    $fireParameters = @('EFT.InventoryLogic.IWeapon','EFT.InventoryLogic.Ammo','UnityEngine.Vector3','UnityEngine.Vector3','UnityEngine.Vector3','System.Int32','System.Single')
    $fire = Native-Method 'EFT.Player/FirearmController' 'InitiateShot' $fireParameters
    $clientFire = Native-Method 'EFT.ClientFirearmController' 'InitiateShot' $fireParameters
    Assert-Contract (Has-Call $clientFire 'EFT.Player/FirearmController' 'InitiateShot') 'client fire override calls instrumented base'
    Assert-Contract (Has-Call $fire 'EFT.Ballistics.IBallisticsCalculator' 'Shoot') 'single projectile emitted inside fire scope'
    Assert-Contract (Has-Call $fire 'EFT.Ballistics.IBallisticsCalculator' 'ShotMultiProjectileShot') 'pellets emitted inside fire scope'
    $hands = Native-Type 'EFT.Player/ItemHandsController'
    Assert-Contract (@($hands.Fields | Where-Object { $_.Name -eq '_player' -and $_.FieldType.FullName -eq 'EFT.Player' }).Count -eq 1) 'exact controller owner field'
    $aim = Native-Method 'EFT.Player/FirearmController' 'get_IsAiming' @()
    Assert-Contract ($aim.ReturnType.FullName -eq 'System.Boolean') 'native aim state is boolean'
    $bolt = Native-Method 'EFT.InventoryLogic.Weapon' 'get_BoltAction' @()
    Assert-Contract (@($bolt.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'BoltAction' }).Count -eq 1) 'bolt-action identity reads template flag'
    $ground = Native-Method 'EFT.MovementContext' 'get_IsGrounded' @()
    $flight = Native-Method 'EFT.MovementContext' 'CheckFlying' @('System.Single')
    $groundRefresh = Native-Method 'EFT.MovementContext' 'UpdateGroundCollision' @('System.Single')
    Assert-Contract (Has-Call $groundRefresh 'EFT.MovementContext' 'CheckFlying') 'flight sample follows common native ground refresh'
    foreach ($name in @('PlayerAnimatorGetIsVaulting','get_IsInMountedState','get_IsStationaryWeaponInHands')) {
        $state = Native-Method 'EFT.MovementContext' $name @()
        Assert-Contract ($state.ReturnType.FullName -eq 'System.Boolean') "$name provides actual supported-state exclusion"
    }
    Assert-Contract ($ground.ReturnType.FullName -eq 'System.Boolean') 'native movement ground state is boolean'
    $createMethods = @((Native-Type 'EFT.Ballistics.Shot').Methods | Where-Object { $_.IsPublic -and $_.IsStatic -and $_.Name -eq 'Create' -and $_.ReturnType.FullName -eq 'EFT.Ballistics.Shot' })
    Assert-Contract ($createMethods.Count -eq 1) 'creation hook unique native selector'
    $create = $createMethods[0]
    Assert-Contract ($create.Parameters.Count -eq 25 -and $create.Parameters[23].ParameterType.FullName -eq 'EFT.Ballistics.Shot') 'native creation exposes exact parent parameter'
    Assert-Contract (Has-Call $create 'EFT.Ballistics.Shot' 'GetShotFromPool') 'creation uses pool before snapshot'
    $continuation = Native-Method 'EFT.Ballistics.Shot' 'CreateDeviatedFragment' @()
    Assert-Contract (Has-Call $continuation 'EFT.Ballistics.Shot' 'Create') 'penetration creates a new pooled Shot'
    $projectileCount = Native-Method 'EFT.InventoryLogic.Ammo' 'get_ProjectileCount' @()
    Assert-Contract ($projectileCount.ReturnType.FullName -eq 'System.Int32') 'native ammunition exposes pellet count'
    foreach ($name in @('CreateBulletFragments','CreateRicochetedFragment')) {
        $parameters = if ($name -eq 'CreateRicochetedFragment') { @('System.Boolean') } else { @() }
        $fragment = Native-Method 'EFT.Ballistics.Shot' $name $parameters
        Assert-Contract (Has-Call $fragment 'EFT.Ballistics.Shot' 'Create') "$name creates independent child objects"
    }
    foreach ($field in @(@('Player','EFT.IObserverToPlayerBridge'),@('Weapon','EFT.InventoryLogic.Item'),@('Ammo','EFT.InventoryLogic.Item'),@('Parent','EFT.Ballistics.Shot'),@('FireIndex','System.Int32'))) {
        Assert-Contract (@((Native-Type 'EFT.Ballistics.Shot').Fields | Where-Object { $_.Name -eq $field[0] -and $_.FieldType.FullName -eq $field[1] }).Count -eq 1) "projectile field $($field[0])"
    }
    Assert-Contract ((Native-Type 'ClientLocalGameWorld').BaseType.FullName -eq 'EFT.ClientGameWorld') 'offline world inherits actual hooked override'
    $impact = Native-Method 'EFT.ClientGameWorld' 'ShotDelegate' @('EFT.Ballistics.Shot')
    Assert-Contract (!(Has-Call $impact 'EFT.GameWorld' 'ShotDelegate')) 'local hit bypasses base world callback'
    Assert-Contract (Has-Call $impact 'EFT.Ballistics.DamageInfo' '.ctor') 'impact constructs damage from projectile'
    Assert-Contract (Has-Call $impact 'EFT.Ballistics.BallisticCollider' 'ApplyHit') 'impact synchronously applies hit'
    $damageCtor = Native-Method 'EFT.Ballistics.DamageInfo' '.ctor' @('EFT.EDamageType','EFT.Ballistics.Shot')
    foreach ($field in @('Player','Weapon','FireIndex','SourceId')) {
        Assert-Contract (@($damageCtor.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq $field }).Count -eq 1) "damage constructor preserves $field correlation"
    }
    $shotParams = @('EFT.Ballistics.DamageInfo','EBodyPart','EBodyPartColliderType','EArmorPlateCollider','EFT.Ballistics.ShotId')
    $applyShot = Native-Method 'EFT.Player' 'ApplyShot' $shotParams
    Assert-Contract (Has-Call $applyShot 'EFT.Player' 'ApplyDamageInfo') 'shot enters player damage synchronously'
    $applyDamage = Native-Method 'EFT.Player' 'ApplyDamageInfo' @('EFT.Ballistics.DamageInfo','EBodyPart','EBodyPartColliderType','System.Single')
    Assert-Contract (Has-Call $applyDamage 'EFT.HealthSystem.ActiveHealthController' 'ApplyDamage') 'player damage enters lethal health path'
    $onDead = Native-Method 'EFT.Player' 'OnDead' @('EFT.EDamageType')
    Assert-Contract (Has-Call $onDead 'EFT.Player' 'OnBeenKilledByAggressor') 'native death forwards retained lethal damage'
} finally { $shotAssembly.Dispose() }

# These are source-wiring checks, explicitly distinct from executable adapter tests above.
$shotRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$combat = [IO.File]::ReadAllText((Join-Path $shotRepo 'ArenaMode.Client/Combat/CombatPatches.cs'))
$match = [IO.File]::ReadAllText((Join-Path $shotRepo 'ArenaMode.Client/Modes/ArenaMatch.cs'))
function Method-Text([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production declaration: $signature" }
    $brace = $source.IndexOf('{', $start)
    $depth = 1; $end = $brace + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production declaration: $signature" }
    return $source.Substring($start, $end - $start)
}
Assert-Contract ((Method-Text $combat 'public static void Enable()').Contains('ArenaShotAwardHooks.Enable();')) 'combat enables shot observation hooks'
$reset = Method-Text $combat 'public static void ResetRaidState()'
Assert-Contract ($reset.Contains('ArenaShotAwardHooks.Reset();') -and $reset.Contains('ArenaHitHistory.Reset();')) 'raid resets projectile and contact scopes'
$beginHit = Method-Text $combat 'private static IDisposable BeginHitContext('
Assert-Contract ($beginHit.Contains('ArenaShotAwardHooks.CurrentFacts(info, shotId)')) 'accepted contact requests exact current projectile facts'
Assert-Contract ($beginHit -match 'ArenaHitHistory\.BeginShot\([^;]+facts\)' -and $beginHit -match 'ArenaHitHistory\.BeginDamage\([^;]+facts\)') 'both accepted native contact scopes receive facts'
$shotPatch = Method-Text $combat 'private sealed class TeamStagingShotPatch'
Assert-Contract ($shotPatch -match 'Prefix\([^)]*ShotId shotId' -and $shotPatch.Contains('shot: true, shotId: shotId')) 'native shot id reaches accepted shot scope'
Assert-Contract ($shotPatch -match '\[PatchFinalizer\]' -and $shotPatch.Contains('__state?.Dispose();')) 'native shot context clears on exception'
$damagePatch = Method-Text $combat 'private sealed class TeamStagingPlayerDamagePatch'
Assert-Contract ($damagePatch.Contains('shot: false') -and $damagePatch -match '\[PatchFinalizer\]' -and $damagePatch.Contains('__state?.Dispose();')) 'damage context enters and clears on exception'
$killPatch = Method-Text $combat 'private sealed class KillCapturePatch'
Assert-Contract ($killPatch -match 'ArenaHitHistory\.ShotAwards\(__instance, aggressor,\s*lethalDamageType == EDamageType\.Bullet\)') 'death restricts captured awards to lethal bullet'
Assert-Contract ($killPatch.Contains('AirborneHipFire = shotAwards.AirborneHipFire') -and $killPatch.Contains('BoltActionHipFire = shotAwards.BoltActionHipFire')) 'kill event receives frozen scalar awards'
Assert-Contract ($killPatch -match 'kill\.CollateralProof = kill\.KilledByLocal && !kill\.LocalIsVictim\s*&& kill\.AwardEligible && kill\.ScoreEligible\s*\? ArenaShotAwardHooks\.CaptureCollateral\(shotAwards\) : default') 'native capture creates only local direct enemy proof, never settles before mode admission'
Assert-Contract ($killPatch.IndexOf('ArenaShotAwardHooks.CaptureCollateral') -lt $killPatch.IndexOf('match.ReportKill(kill)')) 'synchronous proof capture precedes leaving the native impact stack'
$matchKill = Method-Text $match 'public void ReportKill('
Assert-Contract ($matchKill.IndexOf('teams.ClassifyKill(kill)') -lt $matchKill.IndexOf('_awards.ReportKill(kill') -and $matchKill.IndexOf('deathmatch.TryBeginScoredEvent()') -lt $matchKill.IndexOf('_awards.ReportKill(kill')) 'production award settlement follows mode admission and team classification'
Assert-Contract ((Method-Text $match 'internal void BeginAwardRound(').Contains('ArenaShotAwardHistory.Reset();')) 'award round resets in-flight projectile facts'

# Decompiled details complement the installed-method/field contracts above. Native
# Tick creates continuations before delivering the old bullet's lethal callback.
$decompiled = Join-Path $SptPath 'Development/_decompiled'
$nativeShot = [IO.File]::ReadAllText((Join-Path $decompiled 'EFT.Ballistics/Shot.cs'))
$nativeCalculator = [IO.File]::ReadAllText((Join-Path $decompiled 'EFT.Ballistics/BallisticsCalculator.cs'))
$nativeContinuation = Method-Text $nativeShot 'public void CreateDeviatedFragment()'
Assert-Contract ($nativeContinuation.Contains('Create(Ammo, FragmentIndex,') -and $nativeContinuation.Contains('Weapon, FireIndex, this)')) 'penetration preserves FragmentIndex and exact parent'
foreach ($signature in @('public void CreateBulletFragments()', 'public void CreateRicochetedFragment(')) {
    Assert-Contract ((Method-Text $nativeShot $signature).Contains('Create(Ammo, FragmentIndex + 1,')) 'actual fragments and ricochets increment FragmentIndex'
}
$nativeUpdate = Method-Text $nativeCalculator 'public void UpdateShots('
Assert-Contract ($nativeUpdate.IndexOf('shot.Tick(') -lt $nativeUpdate.IndexOf('_shotDelegate(shot)')) 'child creation occurs before parent impact callback'
Assert-Contract ((Method-Text $nativeShot 'public void HandleCollision(').Contains('CreateFragments();')) 'native collision prepares continuation during Tick'
Write-Output "SHOT_AWARD_NATIVE_SOURCE_CONTRACT_PASSED: $shotContractCount metadata and wiring assertions; not native runtime proof"
Write-Output "Assembly-CSharp SHA256: $((Get-FileHash -LiteralPath $shotAssemblyPath).Hash)"
