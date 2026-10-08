param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$pickupRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pickupEvidence = Join-Path $pickupRepo ('artifacts/pickup-objective-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($pickupEvidence)
$source = Join-Path $pickupRepo 'ArenaMode.Client/Pickups'
$compat = Join-Path $pickupRepo 'ArenaMode.BigBrainCompat'
$inputs = @{}
foreach ($directory in @($source,$compat,$PSScriptRoot,(Join-Path $pickupRepo 'tools/ArenaPickupChecks'))) {
    foreach ($file in Get-ChildItem -LiteralPath $directory -File) { $inputs[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash }
}
function Invoke-PickupObjective([string]$name,[string]$client,[string]$companion,[string]$reject='') {
    $stage=Join-Path $pickupEvidence $name
    [void][IO.Directory]::CreateDirectory($stage)
    $build = & dotnet build (Join-Path $PSScriptRoot 'PickupObjectiveChecks.csproj') -c Release --nologo `
        "-p:PickupSourceRoot=$client" "-p:ObjectiveSourceRoot=$companion" "-p:BaseIntermediateOutputPath=$stage/obj/" "-p:OutputPath=$stage/out/" 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $stage 'build.log'),$build)
    if($LASTEXITCODE -ne 0){throw "$name did not compile: $build"}
    $run = & dotnet (Join-Path $stage 'out/PickupObjectiveChecks.dll') $SptRoot 2>&1 | Out-String
    $exitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $stage 'result.log'),$run)
    if($reject){
        if($exitCode -eq 0 -or !$run.Contains('ASSERT: '+$reject)){throw "$name did not reject intended defect: $run"}
        Write-Output "Rejected $name at: $reject"
    } else {
        if($exitCode -ne 0 -or !$run.Contains('PICKUP_OBJECTIVE_BEHAVIOR_PASSED')){throw "$name failed: $run"}
        Write-Output $run.Trim()
    }
}
Invoke-PickupObjective 'production' $source $compat
$faults=@(
    @{Name='rotated-target-stays-current';File='ArenaPickupTargets.cs';Find='&& entry.State.Appearance == target.Appearance';Replace='';Reject='exchange invalidates both bot appearance targets immediately'},
    @{Name='unbounded-heal';File='ArenaHealthPickup.cs';Find='internal const float Amount = 150f;';Replace='internal const float Amount = 300f;';Reject='health budget is 150 total'},
    @{Name='stale-health-utility';File='ArenaPickupTargets.cs';Find='Math.Min(ArenaHealthPickup.Amount, ArenaHealthPickup.Missing(player))';Replace='Math.Min(75f, ArenaHealthPickup.Missing(player))';Reject='bot values the full 150 HP benefit'},
    @{Name='destroyed-heal';File='ArenaHealthPickup.cs';Find='!health.IsBodyPartDestroyed(part)';Replace='true';Reject='destroyed and zero HP parts are not restored'},
    @{Name='stale-health-scan';File='ArenaPickupRuntime.cs';Find='if (generation != Generation) return;';Replace='';Reject='old healing scan preserves replacement generation'},
    @{Name='ignore-combat';File='ArenaPickupObjective.cs';Find='enemy.IsVisible ||';Replace='false ||';Reject='visible enemy blocks pickup travel'},
    @{Name='partial-nav';File='ArenaPickupObjective.cs';Find='|| path.status != NavMeshPathStatus.PathComplete';Replace='';Reject='partial route never assigned'},
    @{Name='claim-resurrection';File='ArenaPickupObjective.cs';Find='Claims[i].Released = true;';Replace='';Reject='pruned claim can never regain execution rights'},
    @{Name='pileup';File='ArenaPickupObjective.cs';Find='others >= 2';Replace='others >= 20';Reject='at most two competing bots reserve a pickup'},
    @{Name='door-progress';File='ArenaPickupObjective.cs';Find='if (handlingDoor || _handlingDoor)';Replace='if (false)';Reject='door distance cannot poison resumed route progress'},
    @{Name='door-revival';File='ArenaPickupLogic.cs';Find='if (BotOwner.Mover.CurrentState == EBotMoverState.NearDoor) ClearDefaultRoute();';Replace='';Reject='door entry detaches default route before delayed native return'},
    @{Name='unbounded-trip';File='ArenaPickupObjective.cs';Find='_expires = Time.time + 20f;';Replace='_expires = Time.time + 200f;';Reject='continuous progress cannot exceed twenty-second trip cap'}
)
foreach($fault in $faults){
    $root=Join-Path $pickupEvidence ($fault.Name+'/source')
    $client=Join-Path $root 'client';$companion=Join-Path $root 'compat'
    [void][IO.Directory]::CreateDirectory($client);[void][IO.Directory]::CreateDirectory($companion)
    Copy-Item -Path (Join-Path $source '*.cs') -Destination $client
    Copy-Item -Path (Join-Path $compat '*.cs') -Destination $companion
    $path=if(Test-Path -LiteralPath (Join-Path $client $fault.File)){Join-Path $client $fault.File}else{Join-Path $companion $fault.File}
    $text=[IO.File]::ReadAllText($path)
    if($text.Split(@($fault.Find),[StringSplitOptions]::None).Length -ne 2){throw "Nonunique fault anchor: $($fault.Name)"}
    [IO.File]::WriteAllText($path,$text.Replace($fault.Find,$fault.Replace))
    Invoke-PickupObjective $fault.Name $client $companion $fault.Reject
}
Invoke-PickupObjective 'production-final' $source $compat
foreach($path in $inputs.Keys){if((Get-FileHash -LiteralPath $path).Hash -ne $inputs[$path]){throw "Input changed: $path"}}
[ordered]@{Inputs=$inputs;Faults=$faults.Count;CompletedUtc=[DateTime]::UtcNow.ToString('o');
    Limitation='Source-linked native doubles and deliberate faults. Not live Unity navigation, health callbacks or rendered-marker acceptance.'} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $pickupEvidence 'manifest.json')
Write-Output "PICKUP OBJECTIVE CHECKS PASSED; evidence: $pickupEvidence"
