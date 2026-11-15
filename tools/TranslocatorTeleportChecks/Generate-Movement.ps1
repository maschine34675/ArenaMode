param([string]$Output,[string]$DoubleJumpSource,[string]$ReliabilitySource,[string]$BunnySource,[string]$SlideSource,[string]$VaultSource,[string]$AirSource,[string]$AreaPatchSource)
$ErrorActionPreference='Stop'
function Method([string]$Path,[string]$Signature) {
    $source=Get-Content -LiteralPath $Path -Raw
    $start=$source.IndexOf($Signature,[StringComparison]::Ordinal)
    if($start -lt 0 -or $source.IndexOf($Signature,$start+1,[StringComparison]::Ordinal) -ge 0){throw "Missing/ambiguous production integration $Signature"}
    $brace=$source.IndexOf('{',$start)
    $arrow=$source.IndexOf('=>',$start)
    if($arrow -ge 0 -and $arrow -lt $brace){$end=$source.IndexOf(';',$arrow);return $source.Substring($start,$end-$start+1)}
    $depth=0
    for($index=$brace;$index -lt $source.Length;$index++){
        if($source[$index] -eq '{'){$depth++}
        if($source[$index] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($start,$index-$start+1)}}
    }
    throw "Unterminated method $Signature"
}
$code="using System; using EFT;`nnamespace ArenaMode.Movement {`n"
foreach($entry in @(@('ArenaDoubleJump',$DoubleJumpSource),@('ArenaJumpReliability',$ReliabilitySource),@('ArenaSlideTransitions',$SlideSource),@('ArenaAirVault',$VaultSource),@('ArenaAirControl',$AirSource))){
    $code+='internal static partial class '+$entry[0]+" {`n"+(Method $entry[1] 'internal static void OnRelocated(Player player)')+"`n} `n"
}
$code+='internal static partial class ArenaBunnyhop {'+(Method $BunnySource 'internal static void OnRelocated(Player player, Vector3 delta)')+(Method $BunnySource 'internal static void ApplyCarry(MovementState state, ref Vector3 motion, float dt)')+"}`n"
$code+='internal static partial class ArenaAirControl {'+(Method $AirSource 'private static void Steer(MovementState state, ref Vector3 motion, float dt)')+"}`n"
$code+='internal static partial class ArenaAirVault {'+(Method $VaultSource 'internal static bool HasRelocationConflict(Player player)')+"}`n} `n"
$code+='namespace ArenaMode.Areas { internal static partial class ArenaAreaNavigationPatches {'+(Method $AreaPatchSource 'private static bool BeforePlayerTeleport(Player __instance, Vector3 __0)')+"} }`n"
$native=Join-Path $PSScriptRoot '../../../_decompiled/EFT/MovementContext.cs'
$ground=Method $native 'public virtual void UpdateGroundCollision(float deltaTime)'
$end=$ground.IndexOf('float val =',[StringComparison]::Ordinal)
if($end -lt 0 -or -not $ground.Substring(0,$end).Contains('_groundHit = groundHit;')){throw 'Native ground-decision prefix changed'}
# Execute the actual installed-version source decision. Native animator/slope/
# event tail remains a callback boundary, not a copied physics implementation.
$code+='namespace EFT { public partial class MovementContext {'+$ground.Substring(0,$end)+"ContactUpdates++; AfterContact?.Invoke(); }`n"+(Method $native 'public virtual void ApplyGravity(ref Vector3 motion, float deltaTime, bool stickToGround)')+"} }`n"
$code+='namespace EFT { public partial class IdlePlayerState {'+(Method (Join-Path $PSScriptRoot '../../../_decompiled/EFT/IdlePlayerState.cs') 'public override void Jump()')+"} }`n"
$code=$code.Replace('using System; using EFT;','using System; using EFT; using UnityEngine;')
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Output))) | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),$code)
