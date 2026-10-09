$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/zombie-wave-pending-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$projects = @{
    Runtime = Join-Path $PSScriptRoot 'ZombieWavePendingChecks.csproj'
    Service = Join-Path $PSScriptRoot 'ServiceChecks/ServiceChecks.csproj'
}
function Run-Case([string]$name, [string]$project, [string[]]$properties, [string]$expected, [bool]$pass) {
    $log = Join-Path $stage ($name + '.log')
    & dotnet run --project $projects[$project] -c $name --nologo -p:WarningLevel=0 @properties *> $log
    $code = $LASTEXITCODE
    $content = Get-Content -LiteralPath $log -Raw
    if ($pass) {
        if ($code -ne 0 -or !$content.Contains($expected)) { throw "Production failed: $log`n$content" }
        Write-Host $content.Trim()
    } elseif ($code -eq 0 -or $content -match 'error CS\d+' -or !$content.Contains('InvalidOperationException') -or !$content.Contains($expected)) {
        throw "Compiled fault must fail the intended behavioral assertion: $log`n$content"
    }
}
Run-Case 'RuntimeProduction' 'Runtime' @() 'ZOMBIE_WAVE_PENDING_CHECKS_PASSED' $true
Run-Case 'ServiceProduction' 'Service' @() 'ZOMBIE_PENDING_SERVICE_CHECKS_PASSED' $true
$faults = @(
    @{ Name='pending-clears-wave'; Project='Runtime'; File='Modes/ZombieWavePolicy.cs'; Property='WaveSource'; Old='remainingToSpawn == 0 && alive <= 0 && pending == 0'; New='remainingToSpawn == 0 && alive <= 0'; Failure='solo final pending must not refill or advance' },
    @{ Name='watchdog-forgets-pending'; Project='Runtime'; File='Modes/ZombieWavePolicy.cs'; Property='WaveSource'; Old='var forced = Evaluate(ref state, config, now, 0, pending, arrivedTotal);'; New='var forced = Evaluate(ref state, config, now, 0, 0, arrivedTotal);'; Failure='watchdog cannot refill or tear down team while final body is pending' },
    @{ Name='async-failure-loses-wave-slot'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='int failedDispatches = ZombieSpawnService.ConsumeFailedDispatches();'; New='int failedDispatches = 0;'; Failure='known asynchronous failure retries its owed wave slot before clearance' },
    @{ Name='sync-failure-loses-wave-slot'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='if (!dispatched)'; New='if (false)'; Failure='synchronous rejections do not silently clear a wave' },
    @{ Name='pending-not-counted-at-cap'; Project='Runtime'; File='Modes/ZombieWavePolicy.cs'; Property='WaveSource'; Old='remainingToSpawn > 0 && alive + pending < target'; New='remainingToSpawn > 0 && alive < target'; Failure='pending reservations fill alive cap without exceeding it' },
    @{ Name='native-timeout-ignored'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='if (ZombieSpawnService.TryGetPendingFailure(out string spawnFailure))'; New='if (ZombieSpawnService.TryGetPendingFailure(out string spawnFailure) && false)'; Failure='uncertain activation enters held no-contest for solo and PFT' },
    @{ Name='old-ageout-restored'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='internal static int Pending => PendingRequests.Count;'; New='internal static int Pending => Time.time > 20 ? 0 : PendingRequests.Count;'; Failure='25-second preparation retains pending ownership' },
    @{ Name='reservation-after-dispatch'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='PendingRequests.Add(entry);'; New=''; Old2='if (!dispatched) PendingRequests.Remove(entry);'; New2='PendingRequests.Add(entry); if (!dispatched) PendingRequests.Remove(entry);'; Failure='inline callbacks see pre-registered reservation' },
    @{ Name='failed-preparation-silently-clears'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='_failedDispatches++;'; New=''; Failure='known asynchronous predispatch failure restores exactly one slot' },
    @{ Name='uncertain-dispatch-freed'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='!IsPending(entry) || handedToNative || _pendingFailure != null'; New='!IsPending(entry) || _pendingFailure != null'; Failure='native clock starts at handoff, not initial request' },
    @{ Name='stale-rejection-poisons-new-raid'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='if (entry.Generation != _generation || _pendingFailure != null)'; New='if (_pendingFailure != null)'; Failure='old generation callbacks cannot settle or poison next raid' },
    @{ Name='native-clock-from-request'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='entry.NativeStartedAt = Time.time;'; New='entry.NativeStartedAt = entry.StartedAt;'; Failure='native clock starts at handoff, not initial request' }
)
foreach ($fault in $faults) {
    $text = [IO.File]::ReadAllText((Join-Path $repo ('ArenaMode.Client/' + $fault.File)))
    if (!$text.Contains($fault.Old)) { throw "Missing fault anchor: $($fault.Name)" }
    $text = $text.Replace($fault.Old, $fault.New)
    if ($fault.Old2) {
        if (!$text.Contains($fault.Old2)) { throw "Missing second fault anchor: $($fault.Name)" }
        $text = $text.Replace($fault.Old2, $fault.New2)
    }
    $copy = Join-Path $stage ($fault.Name + '.cs'); [IO.File]::WriteAllText($copy, $text)
    Run-Case $fault.Name $fault.Project @("-p:$($fault.Property)=$copy") $fault.Failure $false
    Write-Host "Rejected compiled fault: $($fault.Name)"
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), (@{ source=(& git -C $repo rev-parse HEAD); compiledFaults=$faults.Count; passed=$true } | ConvertTo-Json))
Write-Host "ZOMBIE_WAVE_PENDING_MUTATIONS_PASSED $($faults.Count) faults; evidence: $stage"
