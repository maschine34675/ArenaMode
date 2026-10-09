param([switch] $BaselineOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceRoot = Join-Path $repo 'ArenaMode.Client/Bots'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-solo-checks-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$paths = @('ArenaSoloPopulation.cs','ArenaSoloPopulationPolicy.cs','ArenaSoloSpawnDispatch.cs','ArenaBotSpawnPatches.cs','BurstSpawnService.cs')
$before = @{}
foreach ($path in $paths) { $before[$path] = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $path)).Hash }
function Slice([string] $source, [string] $start, [string] $end) {
    $a = $source.IndexOf($start, [StringComparison]::Ordinal)
    $b = $source.IndexOf($end, $a + $start.Length, [StringComparison]::Ordinal)
    if ($a -lt 0 -or $b -le $a) { throw 'Missing production extraction boundary' }
    return $source.Substring($a, $b - $a)
}
function Run-Case([string] $name, [string] $file = '', [string] $find = '', [string] $replace = '') {
    $case = Join-Path $scratch $name
    [void][IO.Directory]::CreateDirectory($case)
    foreach ($path in $paths) {
        $content = [IO.File]::ReadAllText((Join-Path $sourceRoot $path))
        if ($path -eq $file) {
            if (($content.Split(@($find), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Fault anchor not unique: $name" }
            $content = $content.Replace($find, $replace)
        }
        [IO.File]::WriteAllText((Join-Path $case $path), $content)
    }
    $burst = [IO.File]::ReadAllText((Join-Path $case 'BurstSpawnService.cs'))
    $managed = Slice $burst '    internal static async void SpawnManagedBot(' '    public static int SpawnSquad('
    $guard = Slice $burst '    private static bool IsRequestStillCurrent(' '    private static Vector3? GetPlayerPosition('
    $retire = Slice $burst '    private static void RetireStaleSpawn(' '    internal static async void SpawnManagedBot('
    $extracted = "using EFT; using EFT.Game.Spawning; using Comfort.Common; using UnityEngine; using ArenaMode.Combat; namespace ArenaMode.Bots; internal static partial class BurstSpawnService {`n$managed`n$guard`n$retire`n}"
    $extractedPath = Join-Path $case 'ExtractedManaged.cs'
    [IO.File]::WriteAllText($extractedPath, $extracted)
    $output = & dotnet run --project (Join-Path $PSScriptRoot 'SoloPopulationChecks.csproj') -c Release "-p:SoloSourceRoot=$case" "-p:ExtractedBurstSource=$extractedPath" "-p:BaseIntermediateOutputPath=$case/obj/" "-p:OutputPath=$case/bin/" 2>&1 | Out-String -Width 10000
    return @{ Exit = $LASTEXITCODE; Output = $output }
}
$positive = Run-Case 'production'
if ($positive.Exit -ne 0 -or !$positive.Output.Contains('SOLO POPULATION RUNTIME PASSED')) { throw $positive.Output }
Write-Output $positive.Output.Trim()
if (!$BaselineOnly) {
    $faults = @(
        @{ Name='failed-start-lease'; File='ArenaSoloPopulation.cs'; Find='&& !Modes.ArenaMatchService.TeamStartAbortPending'; Replace='&& true'; Failure='failed start rejects delayed preparation before native dispatch' },
        @{ Name='human-slot'; File='ArenaSoloPopulationPolicy.cs'; Find='Capacity = participants - 1;'; Replace='Capacity = participants;'; Failure='total includes reserved human and exact PreActive bot capacity' },
        @{ Name='native-timeout'; File='ArenaSoloPopulationPolicy.cs'; Find='!t.Dispatched && now >= t.Expires'; Replace='now >= t.Expires'; Failure='native uncertainty never expires into overshoot' },
        @{ Name='release-uncertain'; File='ArenaSoloPopulationPolicy.cs'; Find='if (!handedToNative) _tickets.Remove(ticket);'; Replace='_tickets.Remove(ticket);'; Failure='native uncertainty never expires into overshoot' },
        @{ Name='preparation-no-retry'; File='ArenaSoloPopulationPolicy.cs'; Find='!t.Dispatched && now >= t.Expires'; Replace='false && now >= t.Expires'; Failure='only undispatched preparation may expire and retry' },
        @{ Name='register-double-count'; File='ArenaSoloPopulation.cs'; Find='TrySnapshot(bot, out var other'; Replace='TrySnapshot(null, out var other'; Failure='total includes reserved human and exact PreActive bot capacity' },
        @{ Name='boss-rotation'; File='ArenaSoloPopulationPolicy.cs'; Find='NextBossIsKilla = !NextBossIsKilla'; Replace='NextBossIsKilla = false'; Failure='dedicated boss alternates only after proven death' },
        @{ Name='boss-logic'; File='BurstSpawnService.cs'; Find='bot.Boss.SetBoss(0);'; Replace='{}'; Failure='dedicated boss initializes native boss logic with no extra followers' },
        @{ Name='retire-failure'; File='ArenaSoloPopulationPolicy.cs'; Find='if (retired) Settle(ticket);'; Replace='Settle(ticket);'; Failure='retired duplicate cannot settle original native ticket' },
        @{ Name='duplicate-settlement'; File='ArenaSoloPopulationPolicy.cs'; Find='if (!original || !_tickets.Contains(ticket)) return;'; Replace='if (!_tickets.Contains(ticket)) return;'; Failure='retired duplicate cannot settle original native ticket' },
        @{ Name='extra-profiles'; File='BurstSpawnService.cs'; Find='data.Count != 1 || data.Profiles[0]'; Replace='data.Count == 0 || data.Profiles[0]'; Failure='wrong-sized profile result never creates' },
        @{ Name='creator-bypass'; File='ArenaSoloSpawnDispatch.cs'; Find='if (lease == null || !lease.MatchesCreator(creator, data)'; Replace='if (lease == null) return true; if (!lease.MatchesCreator(creator, data)'; Failure='direct creator activation and profile creation cannot bypass owned dispatch' }
    )
    foreach ($fault in $faults) {
        $negative = Run-Case $fault.Name $fault.File $fault.Find $fault.Replace
        if ($negative.Exit -eq 0 -or !$negative.Output.Contains($fault.Failure)) { throw "Fault missed expected assertion $($fault.Name): $($negative.Output)" }
        Write-Output "PASS fault control: $($fault.Name)"
    }
}
foreach ($path in $paths) {
    if ($before[$path] -ne (Get-FileHash -LiteralPath (Join-Path $sourceRoot $path)).Hash) { throw "Production source changed during checks: $path" }
}
$native = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'NativeContracts.ps1') 2>&1 | Out-String -Width 10000
if ($LASTEXITCODE -ne 0 -or !$native.Contains('SOLO NATIVE CONTRACTS PASSED')) { throw $native }
Write-Output $native.Trim()
Write-Output 'SOLO POPULATION CHECKS PASSED'
