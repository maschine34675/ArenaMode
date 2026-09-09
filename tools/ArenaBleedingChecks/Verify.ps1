$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$combat=[IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/CombatPatches.cs'))
$start=$combat.IndexOf('private static bool Prefix(ActiveHealthController __instance, ref float damage', $combat.IndexOf('class TeamStagingDamagePatch'))
if($start -lt 0) { throw 'Actual health prefix not found' }
$open=$combat.IndexOf('{',$start); $depth=1; $end=$open+1
while($depth -gt 0 -and $end -lt $combat.Length) { if($combat[$end] -eq '{'){$depth++}; if($combat[$end] -eq '}'){$depth--}; $end++ }
if($depth -ne 0) { throw 'Unbalanced health prefix' }
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('arena-bleeding-prefix-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$prefix=Join-Path $scratch 'Prefix.cs'
[IO.File]::WriteAllText($prefix,"using EFT; using EFT.HealthSystem; using ArenaMode.Combat; using ArenaMode.Session; using ArenaMode.Modes; using ArenaMode.Bots; internal static partial class Program {`n"+$combat.Substring($start,$end-$start)+"`n}")
$source=Join-Path $repo 'ArenaMode.Client/Combat/ArenaBleedingDamage.cs'
$faults=@(
    @{Name='no-scale';Path=$source;Property='BleedingSource';Find='float result = damage * multiplier;';Replace='float result = damage;';Failure='sequential native tick scales exactly once'},
    @{Name='ordinary-leak';Path=$source;Property='BleedingSource';Find='!ArenaSession.IsExactActiveRaid(game, world)';Replace='false';Failure='ordinary stale protected local bodies remain unchanged'},
    @{Name='nonbleed-leak';Path=$source;Property='BleedingSource';Find='(type != EDamageType.LightBleeding && type != EDamageType.HeavyBleeding)';Replace='false';Failure='other damage untouched'},
    @{Name='retired-ai';Path=$source;Property='BleedingSource';Find='owner.BotState != EBotState.Active';Replace='false';Failure='retired or foreign AI body untouched'},
    @{Name='local-respawn-global';Path=$source;Property='BleedingSource';Find='|| mode == null || mode.IsComplete || ArenaRespawnDriver.StandDown';Replace='|| mode == null || mode.IsComplete || ArenaRespawnDriver.StandDown || ArenaRespawnDriver.IsRespawnInProgress';Failure='bots bleed during local respawn'},
    @{Name='prefix-unwired';Path=$prefix;Property='PrefixSource';Find='damage = ArenaBleedingDamage.Adjust(__instance, damage, damageInfo.DamageType);';Replace='';Failure='actual prefix forwards adjusted transaction to native'}
)
# MSBuild uses the final PrefixSource override for the prefix fault copy.
$bleedFaults=@($faults | Where-Object Property -eq 'BleedingSource')
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'ArenaBleedingChecks.csproj') 'ARENA_BLEEDING_BASELINE_PASSED' @("-p:PrefixSource=$prefix") $bleedFaults
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'ArenaBleedingChecks.csproj') 'ARENA_BLEEDING_BASELINE_PASSED' @("-p:PrefixSource=$prefix") @($faults[-1])
Write-Output 'ARENA_BLEEDING_CHECKS_PASSED'
