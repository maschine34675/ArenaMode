param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$bleedingRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bleedingNative = Join-Path $bleedingRoot '../_decompiled'
$bleedingWork = Join-Path $PSScriptRoot '.work'
[void][IO.Directory]::CreateDirectory($bleedingWork)
$bleedingProject = Join-Path $PSScriptRoot 'BleedingCleanupChecks.csproj'
$bleedingSourcePath = Join-Path $bleedingRoot 'ArenaMode.Client/Respawn/ArenaRetiredBodyEffects.cs'
$bleedingSource = [IO.File]::ReadAllText($bleedingSourcePath)
$bleedingSourceHash = (Get-FileHash -LiteralPath $bleedingSourcePath).Hash
$bleedingPlayer = [IO.File]::ReadAllText((Join-Path $bleedingNative 'EFT/Player.cs'))
$bleedingCommutator = [IO.File]::ReadAllText((Join-Path $bleedingNative 'Systems.Effects/EffectsCommutator.cs'))
$bleedingSession = [IO.File]::ReadAllText((Join-Path $bleedingRoot 'ArenaMode.Client/Session/ArenaSession.cs'))
$bleedingExactScope = [regex]::Match($bleedingSession,
    'internal static bool IsExactActiveRaid\(AbstractGame game, GameWorld world\)\s*=>[\s\S]*?;').Value
if (!$bleedingExactScope) { throw 'Production exact-raid scope extraction changed.' }

function Extract-Method([string]$Source, [string]$Signature) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $Source.IndexOf($Signature, $start + 1, [StringComparison]::Ordinal) -ge 0) {
        throw "Native method anchor ambiguous/missing: $Signature"
    }
    $brace = $Source.IndexOf('{', $start)
    $depth = 0
    for ($i = $brace; $i -lt $Source.Length; $i++) {
        if ($Source[$i] -eq '{') { $depth++ }
        if ($Source[$i] -eq '}') { $depth--; if ($depth -eq 0) { return $Source.Substring($start, $i - $start + 1) } }
    }
    throw "Unclosed native method: $Signature"
}

# Compile the actual local native algorithms and visual event wiring. Only the
# surrounding engine types are stand-ins; no native method body is rewritten.
$bleedingPlayerMethods = @('public virtual void OnHealthEffectVisualAdded(IHealthEffect effect)',
    'public virtual void OnHealthEffectVisualRemoved(IHealthEffect effect)',
    'public virtual void OnPlayerVisualDied(EDamageType damageType)',
    'public void SubscribeVisualEvents()', 'public void UnsubscribeVisualEvents()') |
    ForEach-Object { Extract-Method $bleedingPlayer $_ }
$bleedingNativeMethods = @('public void StartBleedingForPlayer(IPlayer player)',
    'public void StopBleedingForPlayer(IPlayer player)', 'public void UpdatePlayersBleedings()') |
    ForEach-Object { Extract-Method $bleedingCommutator $_ }
$bleedingField = [regex]::Match($bleedingCommutator,
    'private readonly List<KeyValuePair<IPlayer, float>> _playerBleedingDecalTimes = [^;]+;').Value
if (!$bleedingField) { throw 'Native bleeding list changed.' }
$bleedingGenerated = @"
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using Systems.Effects;
using UnityEngine;
namespace ArenaMode.Session { internal static partial class ArenaSession {
$bleedingExactScope
} }
namespace EFT { public partial class Player {
$($bleedingPlayerMethods -join "`n")
} }
namespace Systems.Effects { public sealed class EffectsCommutator {
$bleedingField
public Vector2 _minMaxBleedingSpawnDelta = new Vector2(1f, 3f);
$($bleedingNativeMethods -join "`n")
} }
"@
[IO.File]::WriteAllText((Join-Path $bleedingWork 'NativeBleeding.cs'), $bleedingGenerated)

# Check the deployed native assembly too, so a stale decompile cannot silently
# validate a different game contract. This reads IL only; it never loads Unity.
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$bleedingAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
    (Join-Path $SptPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
function Method-Text($method) { ($method.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n" }
function Get-Method($type, [string]$name) {
    $found = @($type.Methods | Where-Object Name -eq $name)
    if ($found.Count -ne 1) { throw "Native method changed: $($type.FullName).$name" }
    return $found[0]
}
try {
    $nativeCommutator = $bleedingAssembly.MainModule.GetType('Systems.Effects.EffectsCommutator')
    $nativePlayer = $bleedingAssembly.MainModule.GetType('EFT.Player')
    $nativeWorld = $bleedingAssembly.MainModule.GetType('EFT.GameWorld')
    $nativeStop = Get-Method $nativeCommutator 'StopBleedingForPlayer'
    $nativeStart = Get-Method $nativeCommutator 'StartBleedingForPlayer'
    foreach ($method in @($nativeStart, $nativeStop)) {
        if (!$method.IsPublic -or $method.IsStatic -or $method.Parameters.Count -ne 1 -or
            $method.Parameters[0].ParameterType.FullName -ne 'EFT.IPlayer' -or
            $method.ReturnType.FullName -ne 'System.Void') { throw 'Native bleeding API changed.' }
        $text = Method-Text $method
        if (!$text.Contains('::get_Key()') -or $text.Contains('get_ProfileId') -or
            $text.Contains('op_Equality') -or $text.Contains('::Equals(')) {
            throw 'Native reference-based bleeding membership changed.'
        }
    }
    if (!(Method-Text $nativeStop).Contains('::RemoveAt(System.Int32)')) { throw 'Native exact bleeding removal changed.' }
    $nativeUpdate = Get-Method $nativeCommutator 'UpdatePlayersBleedings'
    if (!(Method-Text $nativeUpdate).Contains('EFT.IPlayer::get_Position()') -or
        $nativeUpdate.Body.ExceptionHandlers.Count -ne 0) { throw 'Native bleeding update fault path changed.' }
    $nativeList = @($nativeCommutator.Fields | Where-Object Name -eq '_playerBleedingDecalTimes')
    if ($nativeList.Count -ne 1 -or !$nativeList[0].IsPrivate -or !$nativeList[0].IsInitOnly -or
        !$nativeList[0].FieldType.FullName.Contains('EFT.IPlayer,System.Single')) { throw 'Native bleeding list contract changed.' }
    $nativeUnsubscribe = Method-Text (Get-Method $nativePlayer 'UnsubscribeVisualEvents')
    foreach ($pair in @(@('EffectStartedEvent','OnHealthEffectVisualAdded'),
        @('EffectResidualEvent','OnHealthEffectVisualRemoved'), @('DiedEvent','OnPlayerVisualDied'))) {
        if (!$nativeUnsubscribe.Contains('::remove_' + $pair[0] + '(') -or
            !$nativeUnsubscribe.Contains('::' + $pair[1] + '(')) { throw 'Native visual event unsubscribe changed.' }
    }
    if (@([regex]::Matches($nativeUnsubscribe, '::remove_')).Count -ne 3) { throw 'Native visual unsubscribe gained other handlers.' }
    foreach ($pair in @(@('OnHealthEffectVisualAdded','StartBleedingForPlayer'),
        @('OnHealthEffectVisualRemoved','StopBleedingForPlayer'), @('OnPlayerVisualDied','StopBleedingForPlayer'))) {
        if (!(Method-Text (Get-Method $nativePlayer $pair[0])).Contains('::' + $pair[1] + '(')) {
            throw 'Native health-to-blood registration changed.'
        }
    }
    foreach ($method in @((Get-Method $nativePlayer 'Dispose'), (Get-Method $nativePlayer 'OnDestroy'),
        (Get-Method $nativeWorld 'UnregisterPlayer'))) {
        if ((Method-Text $method).Contains('StopBleedingForPlayer')) { throw 'Native teardown now cleans bleeding; review necessity.' }
    }
    Write-Output 'ARENA_BLEEDING_NATIVE_CONTRACTS_PASSED'
}
finally { $bleedingAssembly.Dispose() }

# Both existing terminal transactions use the helper; their epoch/stand-down
# and failure-recovery ordering stays intact. No global bleeding hook is added.
$bleedingLive = [IO.File]::ReadAllText((Join-Path $bleedingRoot 'ArenaMode.Client/Respawn/LiveKitRespawn.cs'))
if ([regex]::Matches($bleedingLive, [regex]::Escape('ArenaRetiredBodyEffects.TryDestroy(game, oldBody);')).Count -ne 2 -or
    $bleedingLive.Contains('Destroy(oldBody.gameObject)')) { throw 'Live body retirement caller coverage changed.' }
$bleedingSwap = Extract-Method $bleedingLive 'private static IEnumerator SwapRoutine('
$bleedingRecovery = Extract-Method $bleedingLive 'private static void RecoverReplacementBody('
if ($bleedingSwap.IndexOf('if (!sameActiveRaid)') -lt 0 -or
    $bleedingSwap.IndexOf('if (!sameActiveRaid)') -gt $bleedingSwap.IndexOf('RecoverOldBody(game, oldBody)') -or
    $bleedingSwap.IndexOf('RecoverOldBody(game, oldBody)') -gt $bleedingSwap.IndexOf('ArenaRetiredBodyEffects.TryDestroy') -or
    !$bleedingSwap.Contains('ArenaRespawnDriver.IsSameActiveRaid(game, raidEpoch)') -or
    !$bleedingSwap.Contains('&& !ArenaRespawnDriver.StandDown') -or
    !$bleedingRecovery.Contains('ArenaRetiredBodyEffects.TryDestroy(game, oldBody);') -or
    !$bleedingRecovery.Contains('EndRaidAsLastResort(game);')) { throw 'Live respawn epoch/recovery boundary changed.' }
if ($bleedingSource.Contains('PatchPrefix') -or $bleedingSource.Contains('PatchFinalizer') -or
    $bleedingSource.Contains('.StartBleedingForPlayer(')) { throw 'Unexpected global suppression/registration in retirement helper.' }

& dotnet run --project $bleedingProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Bleeding cleanup production-linked runtime checks failed.' }
$bleedingMutants = @(
    @{ Name='WrongIdentity'; Anchor='.StopBleedingForPlayer(retiredBody);'; Replacement='.StopBleedingForPlayer(game.LocalPlayer);'; Failure='native old registration is removed before Destroy is requested' },
    @{ Name='MissingVisualDetach'; Anchor='health.EffectStartedEvent -= retiredBody.OnHealthEffectVisualAdded;'; Replacement='// omitted visual detach'; Failure='only exact old reference removed before destruction; replacement and others preserved' },
    @{ Name='StaleGame'; Anchor='|| !ArenaSession.IsExactActiveRaid(game, game.GameWorld)'; Replacement='|| false'; Failure='out-of-scope body retained: other-game-same-world' },
    @{ Name='DeadBody'; Anchor='|| retiredBody.HealthController == null || !retiredBody.HealthController.IsAlive'; Replacement='|| retiredBody.HealthController == null'; Failure='out-of-scope body retained: dead' },
    @{ Name='PartialRollback'; Anchor='if (removedDetached) health.EffectResidualEvent += retiredBody.OnHealthEffectVisualRemoved;'; Replacement='health.EffectResidualEvent += retiredBody.OnHealthEffectVisualRemoved;'; Failure='failure rolls back without duplicate delegates: started' }
)
foreach ($mutant in $bleedingMutants) {
    if ([regex]::Matches($bleedingSource, [regex]::Escape($mutant.Anchor)).Count -ne 1) { throw "Ambiguous mutation anchor: $($mutant.Name)" }
    $path = Join-Path $bleedingWork ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($path, $bleedingSource.Replace($mutant.Anchor, $mutant.Replacement))
    $output = & dotnet run --project $bleedingProject -c ('BleedingMutant' + $mutant.Name) "-p:CleanupSource=$path" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or !$output.Contains('ARENA_BLEEDING_RUNTIME_FAILED') -or !$output.Contains($mutant.Failure)) {
        throw "Bleeding mutant did not fail at its intended assertion: $($mutant.Name)`n$output"
    }
    Write-Output "Rejected bleeding cleanup mutant: $($mutant.Name)"
}
if ((Get-FileHash -LiteralPath $bleedingSourcePath).Hash -ne $bleedingSourceHash) { throw 'Production source changed during checks.' }
& dotnet run --project $bleedingProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Bleeding cleanup final baseline failed.' }
Write-Output 'ARENA_BLEEDING_CLEANUP_PASSED'
