$ErrorActionPreference = 'Stop'
$deathmatchRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deathmatchProject = Join-Path $PSScriptRoot 'DeathmatchChecks.csproj'
$deathmatchEvidence = Join-Path $PSScriptRoot ('evidence/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($deathmatchEvidence) | Out-Null
$deathmatchRules = Join-Path $deathmatchRepo 'ArenaMode.Client/Modes/DeathmatchRules.cs'
$deathmatchMode = Join-Path $deathmatchRepo 'ArenaMode.Client/Modes/RoundsMode.cs'
$deathmatchHashes = @{}
foreach ($path in @($deathmatchRules, $deathmatchMode)) { $deathmatchHashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
function Invoke-DeathmatchCase([string]$name, [string[]]$extra = @()) {
    $output = & dotnet run --project $deathmatchProject -c $name @extra 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $deathmatchEvidence ($name + '.log')), $output)
    return @{ Output = $output; Code = $exitCode }
}
$baseline = Invoke-DeathmatchCase 'Release'
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('DEATHMATCH_RULES_RUNTIME_PASSED')) { throw $baseline.Output }
$baseline.Output.Trim() | Write-Output
$faults = @(
    @{ Name='EarlyTarget'; Path=$deathmatchRules; Property='RulesSource'; From='count.Value >= Options.Kills'; To='count.Value >= Options.Kills - 1'; Failure='distinct bot kills must not aggregate into one opponent' },
    @{ Name='LateDeadline'; Path=$deathmatchRules; Property='RulesSource'; From='now - _startedAt >= Options.Seconds'; To='now - _startedAt > Options.Seconds'; Failure='deadline is checked before a late kill changes standings' },
    @{ Name='OpponentScoreLeak'; Path=$deathmatchRules; Property='RulesSource'; From='if (!Options.FreeForAll && !local) return false;'; To='if (false) return false;'; Failure='all-vs-player ignores opponent kill counts' },
    @{ Name='CounterOverflow'; Path=$deathmatchRules; Property='RulesSource'; From='if (_kills.Count >= ContestantCapacity)'; To='if (false)'; Failure='counter overflow must fail honestly without dropping a winner' },
    @{ Name='LostDeathHold'; Path=$deathmatchMode; Property='ModeSource'; From='public bool HoldRespawn => HasResult;'; To='public bool HoldRespawn => false;'; Failure='final local victim is counted before immutable terminal hold' },
    @{ Name='EarlyInitialStart'; Path=$deathmatchMode; Property='ModeSource'; From='Respawn.LiveKitRespawn.IsInitialSwapPending || Respawn.ArenaRespawnDriver.IsRespawnInProgress'; To='Respawn.ArenaRespawnDriver.IsRespawnInProgress'; Failure='initial kit loading cannot spend round clock' },
    @{ Name='StaleBreakCallback'; Path=$deathmatchMode; Property='ModeSource'; From='generation != _breakGeneration || !IsCurrentRaid()'; To='!IsCurrentRaid()'; Failure='old break callback cannot heal or reset later intermission' },
    @{ Name='PipelineExit'; Path=$deathmatchMode; Property='ModeSource'; From='if (!ResultOwnsCurrentRaid || TdmMatchExit.PipelineRunning) return;'; To='if (!ResultOwnsCurrentRaid) return;'; Failure='result exit waits for native replacement pipeline' },
    @{ Name='LostExactRaid'; Path=$deathmatchMode; Property='ModeSource'; From='&& ArenaSession.IsExactActiveRaid(_game, _world)'; To=''; Failure='ordinary raid cannot inherit result exit or damage protection' }
)
foreach ($fault in $faults) {
    $source = [IO.File]::ReadAllText($fault.Path)
    if (!$source.Contains($fault.From)) { throw "Missing fault-control anchor: $($fault.Name)" }
    $copy = Join-Path $deathmatchEvidence ($fault.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($fault.From, $fault.To))
    $result = Invoke-DeathmatchCase $fault.Name @("-p:$($fault.Property)=$copy")
    if ($result.Code -eq 0 -or $result.Output -match 'error CS\d+' -or !$result.Output.Contains($fault.Failure)) {
        throw "Wrong fault-control failure $($fault.Name): $($result.Output)"
    }
    Write-Output "Rejected compiled fault control: $($fault.Name)"
}
foreach ($path in $deathmatchHashes.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $deathmatchHashes[$path]) { throw "Production source changed during checks: $path" }
}
$deathmatchHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $deathmatchEvidence 'production-sources.json')
Write-Output "Evidence: $deathmatchEvidence"
Write-Output 'DEATHMATCH_CHECKS_PASSED'
