param([switch] $BaselineOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sourcePath = Join-Path $repo 'ArenaMode.Client/Bots/BurstSpawnService.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$before = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-tdm-burst-integration-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$project = Join-Path $PSScriptRoot 'BurstIntegration.csproj'

function Slice([string] $text, [string] $start, [string] $end) {
    $first = $text.IndexOf($start, [StringComparison]::Ordinal)
    $last = $text.IndexOf($end, $first + $start.Length, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $last -le $first) { throw 'Production method extraction boundary missing' }
    return $text.Substring($first, $last - $first)
}
function Run-Extracted([string] $text, [string] $name) {
    $pair = Slice $text '    private static async void SpawnPair(' '    private static bool TryCaptureSpawnLease('
    $faction = Slice $text '    internal static int SpawnFactionBot(' '    private static bool HasRequestedFaction('
    $guard = Slice $text '    private static bool IsRequestStillCurrent(' '    private static Vector3? GetPlayerPosition('
    $extracted = @"
using EFT;
using EFT.Game.Spawning;
using Comfort.Common;
using UnityEngine;
namespace ArenaMode.Bots;
internal static partial class BurstSpawnService
{
$pair
$faction
$guard
}
"@
    $path = Join-Path $scratch ($name + '.cs')
    [IO.File]::WriteAllText($path, $extracted)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet run --project $project -c Release "-p:ExtractedBurstSource=$path" 2>&1 | Out-String -Width 10000
        return @{ Exit = $LASTEXITCODE; Output = $output }
    }
    finally { $ErrorActionPreference = $previous }
}

$positive = Run-Extracted $source 'production'
if ($positive.Exit -ne 0 -or !$positive.Output.Contains('ARENA_TDM_BURST_PREPARATION_CHECKS_PASSED')) {
    throw ('Production burst integration failed: ' + $positive.Output)
}
Write-Output $positive.Output.Trim()
if (!$BaselineOnly) {
    $faults = @(
        @{ Name = 'missing-transition'; Find = '|| !IsRequestStillCurrent(beginNativeDispatch, role)'; Replace = '|| false';
           Failure = 'one-shot transition runs after actual await and fresh area placement' },
        @{ Name = 'transition-before-preparation';
           Find = 'data = await BotCreationData.Create(profileParams, spawner._botCreator, count, spawner);';
           Replace = 'IsRequestStillCurrent(beginNativeDispatch, role); data = await BotCreationData.Create(profileParams, spawner._botCreator, count, spawner);';
           Failure = 'submission and real placement never run while profile preparation is unresolved' },
        @{ Name = 'missing-public-callback-guard';
           Find = '|| observer == null || requestStillCurrent == null || beginNativeDispatch == null';
           Replace = '|| observer == null || requestStillCurrent == null';
           Failure = 'public faction preflight refuses missing submission ownership' },
        @{ Name = 'missing-inner-callback-guard'; Find = '                    || beginNativeDispatch == null'; Replace = '';
           Failure = 'actual async dispatch also fails closed on a missing transition callback' },
        @{ Name = 'swallowed-callback-failure'; Find = 'return predicate();'; Replace = 'try { return predicate(); } catch { return true; }';
           Failure = 'throwing refused full or stale submission cannot cross native dispatch' },
        @{ Name = 'cancel-uncertain-native'; Find = 'if (explicitFaction && !handedToNative)'; Replace = 'if (explicitFaction)';
           Failure = 'successful native dispatch settles only its own request and never StopSpawn cancels it' },
        @{ Name = 'prefix-rejection-ignored'; Find = 'if (scope.WasRejected)'; Replace = 'if (false)';
           Failure = 'proven prefix rejection cancels only undispatched creation data' },
        @{ Name = 'late-native-admission'; Find = 'if (explicitFaction && (!IsRequestStillCurrent(requestStillCurrent, role)';
           Replace = 'if (explicitFaction && (false';
           Failure = 'expired native callback is individually retired without cancelling dispatched work' }
    )
    foreach ($fault in $faults) {
        if (($source.Split(@($fault.Find), [StringSplitOptions]::None).Count - 1) -ne 1) {
            throw ('Fault anchor is not unique: ' + $fault.Name)
        }
        $negative = Run-Extracted ($source.Replace($fault.Find, $fault.Replace)) $fault.Name
        if ($negative.Exit -eq 0 -or !$negative.Output.Contains($fault.Failure)) {
            throw ('Fault missed intended integration assertion: ' + $fault.Name + [Environment]::NewLine + $negative.Output)
        }
        Write-Output ('PASS burst fault: ' + $fault.Name)
    }
}
if ($before -ne (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash) { throw 'Production burst source changed during integration checks' }
Write-Output 'ARENA_TDM_BURST_PREPARATION_REGRESSION_PASSED'
