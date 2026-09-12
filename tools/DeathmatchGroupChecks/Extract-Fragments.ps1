param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$SourcePath = (Join-Path $PSScriptRoot '../../ArenaMode.Client/Bots/ArenaBotPatches.cs'),
    [string]$NativeRoot = (Join-Path $PSScriptRoot '../../../_decompiled')
)
$ErrorActionPreference = 'Stop'
$arenaSource = [IO.File]::ReadAllText($SourcePath)
function Take-GroupFragment([string]$Start, [string]$End) {
    $arenaFirst = $arenaSource.IndexOf($Start, [StringComparison]::Ordinal)
    if ($arenaFirst -lt 0) { throw "Missing production start: $Start" }
    $arenaLast = $arenaSource.IndexOf($End, $arenaFirst, [StringComparison]::Ordinal)
    if ($arenaLast -le $arenaFirst) { throw "Missing production end: $End" }
    return $arenaSource.Substring($arenaFirst, $arenaLast - $arenaFirst)
}
$arenaLifecycle = Take-GroupFragment 'private sealed class IndividualGroup' '    /// <summary>'
$arenaPatches = Take-GroupFragment 'private sealed class ArenaIsPlayerEnemyPatch' '    /// <summary>'
$arenaTemplate = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ArenaMode.Session;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
namespace ArenaMode.Bots;
// Exact production fragments extracted for each run. Native I/O is in Spies.cs.
internal static class ArenaBotPatches
{
    LIFECYCLE
    PATCHES
    private static class NoAccidentalBossPatch { internal static void ResetRaidState() { } }
}
'@
[IO.Directory]::CreateDirectory($OutputPath) | Out-Null
[IO.File]::WriteAllText((Join-Path $OutputPath 'ArenaBotPatches.cs'), $arenaTemplate.Replace('LIFECYCLE', $arenaLifecycle).Replace('PATCHES', $arenaPatches))
# Use the installed decompiled registry implementation directly. No substitute
# lookup/add/dispose algorithm is recreated in the test, and these native files
# remain in ignored run evidence, never tracked or shipped with ArenaMode.
foreach ($arenaNative in @('BotZoneGroups.cs', 'BotZoneGroupsDictionary.cs')) {
    $arenaNativeSource = [IO.File]::ReadAllText((Join-Path $NativeRoot $arenaNative))
    # The decompiler gives an unused compiler-generated cache field the same
    # name as its enclosing nested type, which C# source cannot declare. Rename
    # only that cache field; registry method bodies remain byte-for-byte intact.
    if ($arenaNative -eq 'BotZoneGroupsDictionary.cs') {
        $arenaNativeSource = $arenaNativeSource.Replace(
            'public static readonly CG_TryGetValue CG_TryGetValue = new CG_TryGetValue();',
            'public static readonly CG_TryGetValue Instance = new CG_TryGetValue();')
    }
    [IO.File]::WriteAllText((Join-Path $OutputPath $arenaNative), $arenaNativeSource)
}
