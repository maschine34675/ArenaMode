param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$text=[IO.File]::ReadAllText($Source)
function Slice([string]$First,[string]$Last){
 $a=$text.IndexOf($First,[StringComparison]::Ordinal)
 if($a -lt 0){throw "Missing extraction start: $First"}
 $b=$text.IndexOf($Last,$a,[StringComparison]::Ordinal)
 if($b -lt 0){throw "Missing extraction end: $Last"}
 $text.Substring($a,$b-$a)
}
$fields=Slice '    private static readonly object RerollSync' '    internal static void Initialize()'
$methods=Slice '    private sealed class RerollOwner' '    private static void TrackerPostfix()'
$offering=Slice '    private static bool PersistPrefix()' '    private static bool EnsureLoadedPrefix'
$code="using System;`nusing System.Runtime.CompilerServices;`nusing EFT;`nusing ArenaMode.Shared;`nusing Newtonsoft.Json.Linq;`nusing Redeploy.Core.Api;`nnamespace ArenaMode.Compat;`ninternal static partial class ArenaTarkovaColaCompat {`n$fields`n$methods`n$offering`n}"
$target=[IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
[IO.File]::WriteAllText($target,$code)
