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
    @{ Name='failed-dispatch-no-backoff'; Project='Runtime'; File='Modes/ZombieWavePolicy.cs'; Property='WaveSource'; Old='if (state.NextSpawnAt < retryAt) state.NextSpawnAt = retryAt;'; New=';'; Failure='a failed dispatch is not retried on the pacing cadence' },
    @{ Name='pending-not-counted-at-cap'; Project='Runtime'; File='Modes/ZombieWavePolicy.cs'; Property='WaveSource'; Old='remainingToSpawn > 0 && alive + pending < target'; New='remainingToSpawn > 0 && alive < target'; Failure='pending reservations fill alive cap without exceeding it' },
    @{ Name='native-timeout-ignored'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='if (ZombieSpawnService.TryGetPendingFailure(out string spawnFailure))'; New='if (ZombieSpawnService.TryGetPendingFailure(out string spawnFailure) && false)'; Failure='uncertain activation enters held no-contest for solo and PFT' },
    @{ Name='old-ageout-restored'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='internal static int Pending => PendingRequests.Count;'; New='internal static int Pending => Time.time > 20 ? 0 : PendingRequests.Count;'; Failure='25-second preparation retains pending ownership' },
    @{ Name='reservation-after-dispatch'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='PendingRequests.Add(entry);'; New=''; Old2='if (!dispatched) PendingRequests.Remove(entry);'; New2='PendingRequests.Add(entry); if (!dispatched) PendingRequests.Remove(entry);'; Failure='inline callbacks see pre-registered reservation' },
    @{ Name='failed-preparation-silently-clears'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='_failedDispatches++;'; New=''; Failure='known asynchronous predispatch failure restores exactly one slot' },
    @{ Name='uncertain-dispatch-freed'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='!IsPending(entry) || handedToNative || _pendingFailure != null'; New='!IsPending(entry) || _pendingFailure != null'; Failure='native clock starts at handoff, not initial request' },
    @{ Name='stale-rejection-poisons-new-raid'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='if (entry.Generation != _generation || _pendingFailure != null)'; New='if (_pendingFailure != null)'; Failure='old generation callbacks cannot settle or poison next raid' },
    @{ Name='native-clock-from-request'; Project='Service'; File='Bots/ZombieSpawnService.cs'; Property='ServiceSource'; Old='entry.NativeStartedAt = Time.time;'; New='entry.NativeStartedAt = entry.StartedAt;'; Failure='native clock starts at handoff, not initial request' },
    @{ Name='theme-warning-repeats'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old=' && _waveNumber > 0 && _lastWarnedWave != themedWave'; New=' && _waveNumber > 0'; Failure='special waves are announced once before and once at their start' },
    @{ Name='theme-warning-before-barrier'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='if (_team != null && _waveNumber > 0 && _phase == ZombieWavePolicy.Phase.Intermission && !_teamIntermissionRestored)'; New='if (theme != ZombieWaveTheme.None && _phase == ZombieWavePolicy.Phase.Intermission && _waveNumber > 0 && _lastWarnedWave != themedWave) { _lastWarnedWave = themedWave; _match?.ReportTextCallout($"NEXT: {ThemeLabel(theme)}"); } if (_team != null && _waveNumber > 0 && _phase == ZombieWavePolicy.Phase.Intermission && !_teamIntermissionRestored)'; Failure='no warning is raised while the squad is held in the restore barrier' },
    @{ Name='theme-ignores-step'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='ArenaDifficultyPolicy.ZombieWaveThemeFor(_step, _shooterPercent, themedWave)'; New='ArenaDifficultyPolicy.ZombieWaveThemeFor(ArenaMode.Shared.ArenaDifficultyStep.Maximum, _shooterPercent, themedWave)'; Failure='Reduced announces no special wave' },
    @{ Name='theme-ignores-user-shooter'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='ArenaDifficultyPolicy.ZombieWaveThemeFor(_step, _shooterPercent, themedWave)'; New='ArenaDifficultyPolicy.ZombieWaveThemeFor(_step, 37, themedWave)'; Failure='a shooter share of 0 never gets an all-shooter wave' },
    @{ Name='theme-of-running-wave-in-intermission'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='_phase == ZombieWavePolicy.Phase.Wave ? _waveNumber : _waveNumber + 1;'; New='_waveNumber;'; Failure='special waves are announced once before and once at their start' },
    @{ Name='status-theme-lost'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='_waveStatusText = WithTheme(_waveStatusText, themedWave, theme);'; New=';'; Failure='status line names the running special wave' },
    @{ Name='start-callout-plain'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='$"WAVE {_waveNumber}: {ThemeLabel(theme)}"'; New='$"WAVE {_waveNumber}"'; Failure='special waves are announced once before and once at their start' },
    @{ Name='tally-counts-failed-dispatch'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='if (dispatched)'; New='if (true)'; Failure='a rejected dispatch is not tallied as a draw of the wave' },
    @{ Name='tally-not-reset-per-wave'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='ResetTally(wave);'; New='_tallyWave = wave;'; Failure='the tally starts again for every wave' },
    @{ Name='tally-line-repeats'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='if (_tallyLogged || _tallyWave <= 0'; New='if (_tallyWave <= 0'; Failure='a wave''s composition line is written once, not again when the wave ends' },
    @{ Name='tally-hard-as-normal'; Project='Runtime'; File='Modes/ZombieMode.cs'; Property='ModeSource'; Old='difficulty == ZombieRoleMix.Hard ? 2 : 1'; New='1'; Failure='the composition line counts the dispatched draws by difficulty' }
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
