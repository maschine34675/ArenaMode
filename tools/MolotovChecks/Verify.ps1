$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/molotov-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
$runtime = Join-Path $repo 'ArenaMode.Client/Combat/Fire/ArenaFireRuntime.cs'
$policy = Join-Path $repo 'ArenaMode.Client/Combat/Fire/ArenaFirePolicy.cs'
$supply = Join-Path $repo 'ArenaMode.Client/Combat/Fire/ArenaFireSupply.cs'
function Run-Check([string]$name, [string]$runtimePath, [string]$policyPath, [string]$failure = '', [string]$supplyPath = $supply) {
    $dir = Join-Path $stage $name
    [void][IO.Directory]::CreateDirectory($dir)
    $build = & dotnet build (Join-Path $PSScriptRoot 'MolotovChecks.csproj') -c Release --nologo "-p:RuntimeSource=$runtimePath" "-p:PolicySource=$policyPath" "-p:SupplySource=$supplyPath" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
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
    @{name='convert-existing-rgd'; file=$runtime; find='|| !ArenaFireSupply.Owns(owner, controller.Item)'; replacement=''; fail='ordinary local RGD-5 remains explosive'},
    @{name='unpooled-grant'; file=$supply; find='if (!EnsurePreload(world)) return;'; replacement=''; fail='missing native pool factory cannot grant unpooled item'},
    @{name='no-native-preference'; file=$supply; find='equipment.TopPriorityGrenade = item;'; replacement='equipment.TopPriorityGrenade = equipment.TopPriorityGrenade;'; fail='native G preference is supplied Molotov'},
    @{name='native-action-refusal'; file=$supply; find=' || !inventory.CanExecute(plan.Value)'; replacement=''; fail='native CanExecute refusal preserved'},
    @{name='toggle-regrant'; file=$runtime; find='if (Enabled?.Value != true || !Context(out var match, out var world)) { Reset(); return; }'; replacement='if (Enabled?.Value != true || !Context(out var match, out var world)) { if (Enabled?.Value != true) ArenaFireSupply.ForgetOutsideRaid(null); Reset(); return; }'; fail='off on cannot duplicate bonus'}
)
foreach ($fault in $faults) {
    $source = [IO.File]::ReadAllText($fault.file)
    if ($source.Split(@($fault.find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous fault anchor: $($fault.name)" }
    $copy = Join-Path $stage ($fault.name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($fault.find, $fault.replacement))
    if ($fault.file -eq $runtime) { Run-Check $fault.name $copy $policy $fault.fail }
    elseif ($fault.file -eq $supply) { Run-Check $fault.name $runtime $policy $fault.fail $copy }
    else { Run-Check $fault.name $runtime $copy $fault.fail }
}
Run-Check 'production-final' $runtime $policy
Write-Output "ARENA_MOLOTOV_CHECKS_PASSED; $($faults.Count) compiled negative controls; evidence $stage"
