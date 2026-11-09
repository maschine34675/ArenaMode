$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source=Join-Path $repo 'ArenaMode.Client/Session/RewardCaptureBuffer.cs'
# The capture's two fatal-event paths must take the rule from the world's living bodies (the harness cannot
# compile ArenaRewardCapture.cs, so these are source anchors, like ArenaBleedingChecks' prefix check).
$capture=[IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Session/ArenaRewardCapture.cs'))
foreach($anchor in @(
    'long? lastKiller = CreditedKiller(kill, AliveInWorld(kill.KillerBodyId));',
    'if (body == kill.KillerBodyId && alive) killerAlive = true;',
    'if (body == kill.VictimBodyId || body == kill.KillerBodyId && alive) Register(player);',
    'long? killer = CreditedKiller(kill, killerAlive);',
    'return player.HealthController?.IsAlive == true;')) {
    if(-not $capture.Contains($anchor)) { throw ('Capture call-site anchor missing: '+$anchor) }
}
# The posthumous-kill rule of issue 2 (CreditedKiller): each mutant must fail its named assertion.
$faults=@(
    @{Name='posthumous-credit-kept';Path=$source;Property='RewardBufferSource';
      Find='killer.HasValue && !aliveInWorld && !_actors.ContainsKey(killer.Value) ? null : killer';
      Replace='killer';Failure='Posthumous credit of an earlier life kept a killer'},
    @{Name='registered-unknown-dropped';Path=$source;Property='RewardBufferSource';
      Find='killer.HasValue && !aliveInWorld && !_actors.ContainsKey(killer.Value) ? null : killer';
      Replace='killer.HasValue && !_actors.ContainsKey(killer.Value) ? null : killer';Failure='A registered unknown killer was dropped (the defect detector)'},
    @{Name='known-dead-dropped';Path=$source;Property='RewardBufferSource';
      Find='killer.HasValue && !aliveInWorld && !_actors.ContainsKey(killer.Value) ? null : killer';
      Replace='killer.HasValue && !aliveInWorld ? null : killer';Failure='A known dead killer lost its credit'},
    @{Name='flag-inverted';Path=$source;Property='RewardBufferSource';
      Find='killer.HasValue && !aliveInWorld && !_actors.ContainsKey(killer.Value) ? null : killer';
      Replace='killer.HasValue && aliveInWorld && !_actors.ContainsKey(killer.Value) ? null : killer';Failure='Posthumous credit of an earlier life kept a killer'},
    @{Name='unknown-attacker-accepted';Path=$source;Property='RewardBufferSource';
      Find='|| killer.HasValue && !_actors.ContainsKey(killer.Value)';
      Replace='';Failure='Unknown attacker accepted'}
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'RewardClientChecks.csproj') 'REWARD_CLIENT_CHECKS passed=' @() $faults
Write-Output 'REWARD_CLIENT_CHECKS_VERIFIED'
