param([string]$PublishedAssembly)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $PSScriptRoot 'ContinuousLoadAmmoCompatChecks.csproj'
$source=Join-Path $repo 'ArenaMode.Client/Compat'
if(!$PublishedAssembly){$PublishedAssembly=Join-Path $repo 'artifacts/ammo-raid-health-compat-20261019/releases/cla/extracted/BepInEx/plugins/ozen.ContinuousLoadAmmo/ozen.ContinuousLoadAmmo.dll'}
if(!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)){throw 'Supply the separately downloaded published CLA 1.2.0 DLL; metadata only, never installed or loaded.'}
$run=[guid]::NewGuid().ToString('N');$evidence=Join-Path $repo "artifacts/continuous-load-ammo-checks/$run"
[IO.Directory]::CreateDirectory($evidence)|Out-Null
$files=@('ArenaContinuousLoadAmmoCompat.cs','ArenaContinuousLoadAmmoContract.cs','ArenaContinuousLoadAmmoScope.cs');$original=@{}
foreach($name in $files){$original[$name]=[IO.File]::ReadAllText((Join-Path $source $name))}
$faults=0
function Check([string]$Name,[string]$File='',[string]$Find='',[string]$Replace='',[string]$Failure=''){
 $copy=Join-Path $evidence $Name;[IO.Directory]::CreateDirectory($copy)|Out-Null
 foreach($sourceName in $files){$content=$original[$sourceName];if($sourceName -eq $File){if([regex]::Matches($content,[regex]::Escape($Find)).Count -ne 1){throw "Ambiguous mutation: $Name"};$content=$content.Replace($Find,$Replace)};[IO.File]::WriteAllText((Join-Path $copy $sourceName),$content)}
 $log=Join-Path $copy 'result.log'
 & dotnet run --project $project -c "Cla_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
 $exit=$LASTEXITCODE;$output=[IO.File]::ReadAllText($log)
 if($Failure){if($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")){throw "Fault failed wrong check: $Name ($log)`n$output"};$script:faults++;Write-Output "CLA_MUTANT_REJECTED: $Name"}
 elseif($exit -ne 0 -or $output -notmatch 'CONTINUOUS_LOAD_AMMO_COMPAT_PASSED \(\d+\)'){throw "Production checks failed ($log)`n$output"}
 else{Write-Output $output.Trim()}
}
Check 'before'
Check 'ordinary-authority' 'ArenaContinuousLoadAmmoScope.cs' '|| !exactRaid || !local' '|| !local' 'ordinary scope is not captured'
Check 'body-world' 'ArenaContinuousLoadAmmoScope.cs' '!ReferenceEquals(bodyWorld, currentWorld) || ' '' 'different physical world is not captured'
Check 'local-identity' 'ArenaContinuousLoadAmmoScope.cs' '|| !exactRaid || !local' '|| !exactRaid' 'bot scope is not captured'
Check 'main-identity' 'ArenaContinuousLoadAmmoScope.cs' '|| !ReferenceEquals(body, mainPlayer)' '' 'non-main body is not captured'
Check 'native-end-identity' 'ArenaContinuousLoadAmmoScope.cs' '&& Protocol.HasNativeEndIdentity(nativeIdentity, token)' '' 'unconfirmed server identity denied'
Check 'pending-arm' 'ArenaContinuousLoadAmmoScope.cs' 'active || armed && pending &&' 'active || armed &&' 'missing pending arm denied'
Check 'inventory-owner' 'ArenaContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.Inventory, inventory)' '' 'inventory identity check'
Check 'controller-owner' 'ArenaContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.Controller, controller)' '' 'controller identity check'
Check 'world-owner' 'ArenaContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.World, world)' '' 'world identity check'
Check 'body-owner' 'ArenaContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.Body, mainPlayer)' '' 'body identity check'
Check 'dispose-once' 'ArenaContinuousLoadAmmoScope.cs' 'if (lease == null || lease.Disposed) return false;' 'if (lease == null) return false;' 'retire exactly once'
Check 'static-successor' 'ArenaContinuousLoadAmmoScope.cs' '=> !(disposingOwned || currentOwned) || disposing != null && ReferenceEquals(disposing, current);' '=> true;' 'old Arena dispose cannot clear ordinary successor'
Check 'delayed-epoch' 'ArenaContinuousLoadAmmoScope.cs' '&& lease.Revision == ticket' '' 'new same-body load cancels delayed unlock'
Check 'active-load' 'ArenaContinuousLoadAmmoScope.cs' '&& current && !active && !multiSelect' '&& current && !multiSelect' 'active load prevents delayed restore'
Check 'multiselect-load' 'ArenaContinuousLoadAmmoScope.cs' '&& current && !active && !multiSelect' '&& current && !active' 'MultiSelect pending load preserves limit'
Check 'global-unsubscribe' 'ArenaContinuousLoadAmmoCompat.cs' '_contract.EndEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), controller, _contract.LoadingEnd));' '' 'unrelated global completion cannot notify Arena'
Check 'missing-rebind-dispose' 'ArenaContinuousLoadAmmoCompat.cs' 'if (lease != null && !lease.Disposed) _contract.Dispose.Invoke(old, null);' '' 'live kit swap disposes before rebinding'
Check 'global-broadcast' 'ArenaContinuousLoadAmmoCompat.cs' '=> _contract == null || Scope.ForTask(__0) == null;' '=> true;' 'Arena task does not broadcast to ordinary subscribers'
Check 'completion-owner' 'ArenaContinuousLoadAmmoCompat.cs' '!ArenaContinuousLoadAmmoScope.IsLatest(completion) || !Current(lease)' '!ArenaContinuousLoadAmmoScope.IsLatest(completion)' 'old task cannot notify following world'
Check 'task-epoch' 'ArenaContinuousLoadAmmoScope.cs' '&& completion.Operation == completion.Owner.Operation' '' 'old same-body task epoch rejected'
Check 'initial-profile' 'ArenaContinuousLoadAmmoCompat.cs' '&& ReferenceEquals(p.Profile, game.Profile)' '' 'initial profile object mismatch remains ordinary'
Check 'binding-revocation' 'ArenaContinuousLoadAmmoCompat.cs' ' || !ReferenceEquals(Scope.Find(lease.Controller), lease)' '' 'revoked binding cannot resume after reinitialization'
Check 'foreign-il-hook' 'ArenaContinuousLoadAmmoContract.cs' '.Concat(info.ILManipulators)' '' 'unknown foreign IL hook rejects Arena'
Check 'capture-order' 'ArenaContinuousLoadAmmoCompat.cs' 'if (captureIndex < 0 || broadcastIndex < 0 || captureIndex >= broadcastIndex)' 'if (false)' 'foreign broadcast before capture rejects Arena'
Check 'scope-cleanup' 'ArenaContinuousLoadAmmoScope.cs' '{ _controllers = new(); _inventories = new(); _tasks = new(); }' '{ }' 'scope cleanup releases leases'
Check 'after'
foreach($name in $files){if([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]){throw "Production source changed during checks: $name"}}
Write-Output "CONTINUOUS_LOAD_AMMO_COMPAT_CHECKS_OK; $faults compiled negative controls; evidence: $evidence"
