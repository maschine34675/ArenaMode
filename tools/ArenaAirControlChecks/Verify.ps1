$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arenaProject = Join-Path $PSScriptRoot 'ArenaAirControlChecks.csproj'
$arenaSource = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaAirControl.cs'))
$arenaScope = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client/Movement/ArenaMovementScope.cs'))
$arenaScratch = Join-Path $arenaRepo ('artifacts/air-control-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($arenaScratch)
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Air control baseline failed' }
$arenaFaults = @(
 @{Name='NoAccumulation'; From='if (!flight.HasHeading)'; To='if (true)'; Failure='one second turns 60 degrees at 30 fps'},
 @{Name='RightOnly'; From='current + Math.Max(-max, Math.Min(max, delta))'; To='current + Math.Abs(Math.Max(-max, Math.Min(max, delta)))'; Failure='left input turns left at bounded rate'},
 @{Name='LongWay'; From='Math.Atan2(Math.Sin(target - current), Math.Cos(target - current))'; To='target - current'; Failure='shortest steering path crosses angle wrap'},
 @{Name='Vertical'; From='motion.x = (float)(flight.Heading.x * length);'; To='motion.y = 0; motion.x = (float)(flight.Heading.x * length);'; Failure='vertical motion preserved at 30 fps'},
 @{Name='TinyLift'; From='motion.x = (float)(flight.Heading.x * length);'; To='motion.y += 0.00001f; motion.x = (float)(flight.Heading.x * length);'; Failure='vertical motion preserved at 30 fps'},
 @{Name='SpeedBoost'; From='motion.x = (float)(flight.Heading.x * length);'; To='length *= 2; motion.x = (float)(flight.Heading.x * length);'; Failure='planar speed preserved at 30 fps'},
 @{Name='MissingCollision'; From='state.LimitMotion(ref motion, dt);'; To=''; Failure='native collision runs once per frame at 30 fps'},
 @{Name='DoubleCollision'; From='state.LimitMotion(ref motion, dt);'; To='state.LimitMotion(ref motion, dt); state.LimitMotion(ref motion, dt);'; Failure='native collision runs once per frame at 30 fps'},
 @{Name='FrameDependent'; From='Math.Min(dt, 0.1f)'; To='1f'; Failure='one second turns 60 degrees at 30 fps'},
 @{Name='OldInput'; From='flight.InputFrame == Time.frameCount'; To='flight.InputFrame >= 0'; Failure='previous frame input cannot steer'},
 @{Name='ForeignInputState'; From='&& ReferenceEquals(flight.InputState, state)'; To=''; Failure="different flight state cannot consume another state's sample"},
 @{Name='ChangedMoveState'; From='|| !ReferenceEquals(__state, state)'; To=''; Failure='native Move state transition cannot donate input to new state'},
 @{Name='SkippedMove'; From='!__runOriginal || '; To=''; Failure='skipped native Move invalidates older same-frame input'},
 @{Name='MissingInputGate'; From='ArenaMovementScope.IsLocal(player, requireInput: true) && !context.IsAxesIgnored'; To='true && !context.IsAxesIgnored'; Failure='input consumption rejects newly inventory open'},
 @{Name='AiLeak'; Scope=$true; From='player.IsAI || '; To=''; Failure='scope passthrough: AI player'},
 @{Name='WrongRaid'; Scope=$true; From='ArenaSession.IsExactActiveRaid(game, world)'; To='true'; Failure='scope passthrough: different world'},
 @{Name='IgnoredToggle'; From='Settings.AirControlEnabled?.Value != true || state == null'; To='state == null'; Failure='scope passthrough: air control disabled'},
 @{Name='Override'; From='context.OverridenState == null && '; To=''; Failure='scope passthrough: overridden state'},
 @{Name='EarlyLanding'; From='&& landedPhase'; To=''; Failure='nonlanding sample preserves heading: rising ray'},
 @{Name='LostShutdown'; From='finally { _flights = new(); }'; To='finally { }'; Failure='shutdown and enable cannot inherit old flight'},
 @{Name='AmbiguousBinding'; From='matches.Length != 1'; To='matches.Length == 0'; Failure='unknown native collision count rolls back: 2'}
)
foreach ($arenaFault in $arenaFaults) {
 $arenaText = if ($arenaFault.Scope) { $arenaScope } else { $arenaSource }
 if ([regex]::Matches($arenaText,[regex]::Escape($arenaFault.From)).Count -ne 1) { throw ('Fault anchor changed: '+$arenaFault.Name) }
 $arenaFile = Join-Path $arenaScratch ($arenaFault.Name+'.cs')
 [IO.File]::WriteAllText($arenaFile,$arenaText.Replace($arenaFault.From,$arenaFault.To))
 $arenaProperty = if ($arenaFault.Scope) { 'MovementScopeSource' } else { 'AirControlSource' }
 $arenaLog = & dotnet run --project $arenaProject -c ('AirControlFault'+$arenaFault.Name) "-p:${arenaProperty}=$arenaFile" 2>&1 | Out-String
 [IO.File]::WriteAllText((Join-Path $arenaScratch ($arenaFault.Name+'.log')),$arenaLog)
 if ($LASTEXITCODE -eq 0 -or !$arenaLog.Contains($arenaFault.Failure) -or $arenaLog -match 'error CS[0-9]') { throw ('Fault not rejected as intended: '+$arenaFault.Name+"`n"+$arenaLog) }
 Write-Output ('Rejected air control fault: '+$arenaFault.Name)
}
& dotnet run --project $arenaProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'Air control final baseline failed' }
Write-Output "ARENA_AIR_CONTROL_REGRESSION_PASSED ($($arenaFaults.Count) compiled faults; $arenaScratch)"
