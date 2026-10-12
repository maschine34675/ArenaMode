$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/molotov-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
$runtime = Join-Path $repo 'ArenaMode.Client/Combat/Fire/ArenaFireRuntime.cs'
$policy = Join-Path $repo 'ArenaMode.Client/Combat/Fire/ArenaFirePolicy.cs'
function Run-Check([string]$name, [string]$runtimePath, [string]$policyPath, [string]$failure = '') {
    $dir = Join-Path $stage $name
    [void][IO.Directory]::CreateDirectory($dir)
    $build = & dotnet build (Join-Path $PSScriptRoot 'MolotovChecks.csproj') -c Release --nologo "-p:RuntimeSource=$runtimePath" "-p:PolicySource=$policyPath" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "Check $name must compile: $build" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $run = & dotnet (Join-Path $dir 'out/MolotovChecks.dll') 2>&1 | Out-String
        $exit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $dir 'run.log'), $run)
    if ($failure) {
        if ($exit -eq 0 -or !$run.Contains('FAILED: ' + $failure)) { throw "Fault $name did not hit its intended assertion: $run" }
        Write-Output "Rejected $name at $failure"
    } else {
        if ($exit -ne 0 -or !$run.Contains('ARENA_MOLOTOV_PRODUCTION_PASSED:')) { throw "Production failed: $run" }
        Write-Output $run.Trim()
    }
}
Run-Check 'production' $runtime $policy
$faults = @(
    @{name='missing-companion'; file=$runtime; find='!BotEscapeAvailable || '; replacement=''; fail='companion required'},
    @{name='overlap'; file=$runtime; find='if (!damageTick) continue;'; replacement='if (false) continue;'; fail='staggered fire cannot add off-cycle damage'},
    @{name='projectile-retirement'; file=$runtime; find='grenade.StopTimer(); grenade.OnExplosion();'; replacement='grenade.StopTimer();'; fail='disable retires pending native projectile'},
    @{name='thrower'; file=$runtime; find='Player = fire.Source.Bridge'; replacement='Player = null'; fail='attributed exact fire scope'},
    @{name='floor'; file=$policy; find='Math.Abs(dy) <= .6f'; replacement='true'; fail='upper floor safe'},
    @{name='environment-multiplier'; file=$policy; find='amount * maximum / total / environmentMultiplier'; replacement='amount * maximum / total'; fail='normalized first damage tick'},
    @{name='cancelled-retry'; file=$runtime; find='!source.Cancelled && Current()'; replacement='Current()'; fail='cancelled projectile cannot ignite in new round'},
    @{name='victim-retention'; file=$runtime; find='Victims.Clear(); Ticked.Clear();'; replacement='Victims.Clear();'; fail='disable clears transient victim references'},
    @{name='donor-conversion'; file=$policy; find='template == ArenaMolotovItem.TemplateId'; replacement='template == ArenaMolotovItem.DonorTemplateId'; fail='ordinary donor untouched'},
    @{name='explosive-fallback'; file=$runtime; find='return !ArenaFirePolicy.IsMolotov(grenade.WeaponSource?.TemplateId);'; replacement='return true;'; fail='ordinary custom cannot explode'},
    @{name='tactical-award-veto'; file=$runtime; find='var health = victim.ActiveHealthController;'; replacement='if (_match.Mode is TacticalMode tactical && !tactical.IsOpposingAwardKill(victim, fire.Source.Owner)) return; var health = victim.ActiveHealthController;'; fail='friendly damage reaches common mutator'},
    @{name='respawn-immunity'; file=$runtime; find='Ticked.Add(victim);'; replacement='if (victim.ProfileId == fire.Source.Owner.ProfileId && !ReferenceEquals(victim, fire.Source.Owner)) continue; Ticked.Add(victim);'; fail='respawn protection receives old fire'},
    @{name='view-constructor-resets-damage'; file=$runtime; find='try { fire.View = new ArenaFireView(cells); } catch (Exception ex) { Warn(ex); }'; replacement='try { fire.View = new ArenaFireView(cells); } catch (Exception ex) { Warn(ex); Reset(); }'; fail='view constructor failure preserves damage'},
    @{name='view-tick-resets-damage'; file=$runtime; find='catch (Exception ex) { Warn(ex); fire.Dispose(); }'; replacement='catch (Exception ex) { Warn(ex); Reset(); }'; fail='view tick failure preserves damage'},
    @{name='damage-after-death'; file=$runtime; find='if (!LiveActor(victim)) break;'; replacement='if (false) break;'; fail='dead actor receives no later part dispatch'},
    @{name='cross-round-ignition'; file=$runtime; find='&& _round == Round(match)'; replacement=''; fail='previous round projectile cannot create fire before tick'},
    @{name='foreign-physical-source'; file=$runtime; find=' || !ReferenceEquals(grenade.Player?.iPlayer, owner)'; replacement=''; fail='foreign physical bridge cannot own fire'},
    @{name='substitute-source-weapon'; file=$runtime; find='!ReferenceEquals(grenade.WeaponSource, controller.Item) || '; replacement=''; fail='substitute weapon instance cannot own fire'}
)
foreach ($fault in $faults) {
    $source = [IO.File]::ReadAllText($fault.file)
    if ($source.Split(@($fault.find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.name)" }
    $copy = Join-Path $stage ($fault.name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($fault.find, $fault.replacement))
    if ($fault.file -eq $runtime) { Run-Check $fault.name $copy $policy $fault.fail }
    else { Run-Check $fault.name $runtime $copy $fault.fail }
}
Run-Check 'production-final' $runtime $policy
Write-Output "ARENA_MOLOTOV_CHECKS_PASSED; $($faults.Count) compiled negative controls; evidence $stage"
