$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaProject = Join-Path $PSScriptRoot 'TdmMatchChecks.csproj'
$arenaEvidence = Join-Path $arenaRepo ('artifacts/tdm-match-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($arenaEvidence) | Out-Null
$arenaSources = @('TeamDeathmatchMatchPolicy.cs', 'TdmMatchExit.cs') | ForEach-Object {
    Join-Path $arenaRepo ('ArenaMode.Client/Modes/' + $_)
}
$arenaSources += Join-Path $arenaRepo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
$arenaSourceHashes = @{}
foreach ($path in $arenaSources) { $arenaSourceHashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
$arenaSourceHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $arenaEvidence 'production-sources.json')
$arenaGuards = Join-Path $arenaEvidence 'RespawnGuards.cs'
& (Join-Path $PSScriptRoot 'Extract-RespawnGuards.ps1') -OutputPath $arenaGuards
function Invoke-Case([string]$name, [string[]]$extra = @()) {
    $output = & dotnet run --project $arenaProject -c $name "-p:RespawnGuardSource=$arenaGuards" @extra 2>&1 | Out-String
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $arenaEvidence ($name + '.log')), $output)
    return @{ Output=$output; Code=$code }
}
$baseline = Invoke-Case 'Release'
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('TDM_MATCH_POLICY_EXIT_PASSED')) { throw $baseline.Output }
$baseline.Output.Trim() | Write-Output
$policy = Join-Path $arenaRepo 'ArenaMode.Client/Modes/TeamDeathmatchMatchPolicy.cs'
$exit = Join-Path $arenaRepo 'ArenaMode.Client/Modes/TdmMatchExit.cs'
$driver = Join-Path $arenaRepo 'ArenaMode.Client/Respawn/ArenaRespawnDriver.cs'
$mutants = @(
    @{Name='EarlyCountdown';Path=$policy;Property='PolicySource';From='if (now < _countdownEndsAt) return;';To='if (false) return;';Failure='replacement readiness requires complete new countdown'},
    @{Name='LostReadiness';Path=$policy;Property='PolicySource';From='if (!rosterReady) { Phase = TdmMatchPhase.Assembling; return; }';To='if (false) { Phase = TdmMatchPhase.Assembling; return; }';Failure='readiness loss resets countdown'},
    @{Name='TerminalMutation';Path=$policy;Property='PolicySource';From='|| Phase == TdmMatchPhase.Finished';To='';Failure='terminal result and scores immutable'},
    @{Name='EarlyKill';Path=$policy;Property='PolicySource';From='>= KillLimit';To='>= KillLimit - 1';Failure='kill limit boundary remains live until reached'},
    @{Name='WrongWinner';Path=$policy;Property='PolicySource';From='friendlyKills > opponentKills ? TdmMatchResult.Won';To='friendlyKills > opponentKills ? TdmMatchResult.Lost';Failure='kill target selects actual leading faction'},
    @{Name='DrawLost';Path=$policy;Property='PolicySource';From='TdmMatchResult.Lost : TdmMatchResult.Draw';To='TdmMatchResult.Lost : TdmMatchResult.Lost';Failure='time limit compares team kills and preserves draws'},
    @{Name='PipelineStop';Path=$exit;Property='ExitSource';From='|| PipelineRunning ||';To='||';Failure='unsafe exit excluded 0'},
    @{Name='MissingOwner';Path=$exit;Property='ExitSource';From='if (owner == null || local == null || !ReferenceEquals(owner.Player, local)';To='if (false';Failure='unsafe exit excluded 8'},
    @{Name='LostExactRaid';Path=$exit;Property='ExitSource';From='|| !ArenaSession.IsExactActiveRaid(game, world)';To='';Failure='unsafe exit excluded 4'},
    @{Name='StandDownEarly';Path=$exit;Property='ExitSource';From='if (!committed) ArenaRespawnDriver.StandDown = false;';To='';Failure='precommit error restores death interception'},
    @{Name='PausedStop';Path=$exit;Property='ExitSource';From='|| UnityEngine.Time.timeScale <= 0f';To='';Failure='unsafe exit excluded 14'},
    @{Name='TerminalFailureFallback';Path=$driver;Property='DriverSource';From='if (IsTerminalTdmHold && Modes.ArenaMatchService.Current?.Mode is Modes.TeamDeathmatchMode terminal)';To='if (false && Modes.ArenaMatchService.Current?.Mode is Modes.TeamDeathmatchMode terminal)';Failure='terminal factory failure cannot bypass safe-owner exit'},
    @{Name='TerminalDeathBypass';Path=$driver;Property='DriverSource';From='(IsRespawnInProgress || IsTerminalTdmHold)';To='IsRespawnInProgress';Failure='terminal death interception survives driver lease completion'},
    @{Name='HeldBlackScreen';Path=$driver;Property='DriverSource';From='&& (!IsTerminalTdmHold || RespawnEngineState.IsPipelineRunning)';To='';Failure='result display must not retain black screen for held death'},
    @{Name='TerminalPreloadRelease';Path=$driver;Property='DriverSource';From='while (IsTerminalTdmHold && IsSameActiveRaid(game, raidEpoch) && !StandDown)';To='while (false)';Failure='terminal preload continuation waits without entering engine'}
)
foreach ($case in $mutants) {
    $source = [IO.File]::ReadAllText($case.Path)
    if (!$source.Contains($case.From)) { throw "Mutation anchor missing: $($case.Name)" }
    $copy = Join-Path $arenaEvidence ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($case.From, $case.To))
    if ($case.Property -eq 'DriverSource') {
        $guardsCopy = Join-Path $arenaEvidence ($case.Name + '-guards.cs')
        & (Join-Path $PSScriptRoot 'Extract-RespawnGuards.ps1') -OutputPath $guardsCopy -DriverPath $copy
        $result = Invoke-Case $case.Name @("-p:RespawnGuardSource=$guardsCopy")
    }
    else { $result = Invoke-Case $case.Name @("-p:$($case.Property)=$copy") }
    if ($result.Code -eq 0 -or $result.Output -match 'error CS\d+' -or !$result.Output.Contains($case.Failure)) { throw "Wrong fault-control failure $($case.Name): $($result.Output)" }
    Write-Output "Rejected fault control: $($case.Name)"
}
$final = Invoke-Case 'Release'
if ($final.Code -ne 0 -or !$final.Output.Contains('TDM_MATCH_POLICY_EXIT_PASSED')) { throw $final.Output }
foreach ($path in $arenaSources) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $arenaSourceHashes[$path]) { throw "Production source changed during checks: $path" }
}
Write-Output "Evidence: $arenaEvidence"
Write-Output 'TDM_MATCH_CHECKS_PASSED'
