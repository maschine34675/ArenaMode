$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$evidence = Join-Path $repo ('artifacts\akimbo-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$project = Join-Path $PSScriptRoot 'AkimboChecks.csproj'
$source = [IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client\Combat\Akimbo\AkimboLease.cs'))
$faults = @(
  @('body', 'ReferenceEquals(_body, other._body)', 'true', 'replacement identity rejected 0'),
  @('model', 'ReferenceEquals(_model, other._model)', 'true', 'replacement identity rejected 6'),
  @('magazine', 'ReferenceEquals(_magazine, other._magazine)', 'true', 'replacement identity rejected 7'),
  @('failure-retry', '&& !_rejected.Matches(owner)', '', 'failed owner is not retried each frame'),
  @('duplicate-binding', 'Lease == null && owner.Valid', 'owner.Valid', 'stable owner never builds a second binding'),
  @('revolver-class', '&& string.Equals(weaponClass, "pistol", StringComparison.Ordinal)', '', 'non-handgun revolver category excluded: shotgun'),
  @('flare', '&& !flare', '', 'flare pistol excluded'),
  @('launcher', '&& !launcher', '', 'launcher excluded'),
  @('original', '|| !runOriginal', '', 'all shot guards required 29'),
  @('busy', '|| !ready', '', 'all shot guards required 27'),
  @('nested', '|| _emitting', '', 'nested bonus rejected'),
  @('reserve', 'if (NextLeft) return false;', '', 'free reservation cannot repeat before paid half'),
  @('paid', '&& !free', '', 'free shot cannot repeat indefinitely'),
  @('original-credit', '&& ranOriginal', '', 'skipped original cannot settle paid credit'),
  @('credit-redraw', 'Credit = credit ?? new();', 'Credit = new();', 'seven loaded rounds yield fourteen alternating shots')
)
try {
  $output = (& dotnet run --project $project -c Release 2>&1 | Out-String)
  if ($LASTEXITCODE -ne 0 -or !$output.Contains('AKIMBO_CHECKS_PASSED')) { throw $output }
  Write-Output $output.Trim()
  foreach ($fault in $faults) {
    if ($source.Split([string[]]@($fault[1]), [StringSplitOptions]::None).Count -ne 2) { throw 'Mutation site changed' }
    $file = Join-Path $evidence ($fault[0]+'.cs')
    [IO.File]::WriteAllText($file,$source.Replace($fault[1],$fault[2]))
    $output = (& dotnet run --project $project -c Release "-p:LeaseSource=$file" 2>&1 | Out-String)
    $code = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence ($fault[0]+'.log')),$output)
    if ($code -eq 0 -or $output.Contains('error CS') -or !$output.Contains($fault[3])) { throw $output }
    Write-Output "NEGATIVE_CONTROL_PASSED $($fault[0])"
  }
} finally { & dotnet build $project -c Release -t:Rebuild --nologo -v:quiet; if ($LASTEXITCODE -ne 0) { throw 'Restore failed' } }
& dotnet run --project $project -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Final production run failed' }
$cylinderProject=Join-Path $PSScriptRoot 'Cylinder/CylinderChecks.csproj'
$cylinderSource=[IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboAmmo.cs'))
try {
  $result=(& dotnet run --project $cylinderProject -c Release 2>&1 | Out-String)
  if($LASTEXITCODE -ne 0 -or !$result.Contains('AKIMBO_CYLINDER_CHECKS_PASSED')){throw $result}
  Write-Output $result.Trim()
  foreach($fault in @(
    @('native-source','clone.StackObjectsCount = 1; clone.IsUsed = true;','source.IsUsed = true; clone.StackObjectsCount = 1; clone.IsUsed = true;','free cylinder retains native source and index'),
    @('foreign-shell','!ReferenceEquals(_firing.Firearms, __instance)','false','foreign firearm visual untouched'),
    @('hammer','op.FirearmsAnimator.SetDoubleAction(Convert.ToSingle(op.Weapon.CylinderHammerClosed));','op.Weapon.CylinderHammerClosed = true; op.FirearmsAnimator.SetDoubleAction(Convert.ToSingle(op.Weapon.CylinderHammerClosed));','free chamber completion retains hammer selection')
  )) {
    if(!$cylinderSource.Contains($fault[1])){throw 'Cylinder mutation anchor missing'}
    $path=Join-Path $evidence ($fault[0]+'.cs')
    [IO.File]::WriteAllText($path,$cylinderSource.Replace($fault[1],$fault[2]))
    $result=(& dotnet run --project $cylinderProject -c Release "-p:CylinderSource=$path" 2>&1 | Out-String)
    if($LASTEXITCODE -eq 0 -or $result.Contains('error CS') -or !$result.Contains($fault[3])){throw $result}
    Write-Output "NEGATIVE_CONTROL_PASSED $($fault[0])"
  }
} finally { & dotnet build $cylinderProject -c Release -t:Rebuild --nologo -v:quiet; if($LASTEXITCODE -ne 0){throw 'Cylinder restore failed'} }
$timingProject=Join-Path $PSScriptRoot 'Timing/TimingChecks.csproj'
$cadence=[IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboCadence.cs'))
$presentation=[IO.File]::ReadAllText((Join-Path $repo 'ArenaMode.Client/Combat/Akimbo/AkimboPresentation.cs'))
try {
  $result=(& dotnet run --project $timingProject -c Release 2>&1 | Out-String)
  if($LASTEXITCODE -ne 0 -or !$result.Contains('AKIMBO_TIMING_CHECKS_PASSED')){throw $result}
  Write-Output $result.Trim()
  foreach($fault in @(
    @('cadence-factor','CadenceSource',$cadence,'Factor = 2f','Factor = 1f','native firing timer and Unity animator double together'),
    @('cadence-restore','CadenceSource',$cadence,'&& animator.speed == written','','foreign speed write preserved'),
    @('cadence-finished','CadenceSource',$cadence,'ReferenceEquals(operation, _operation)','false','finished fire restores before next reload operation'),
    @('cadence-fast','CadenceSource',$cadence,'body.ArmsUpdateMode == Player.EUpdateMode.Manual','true','unsupported fast auto mode does not halve timer alone'),
    @('visual-both','PresentationSource',$presentation,'if (left) _left = now; else _right = now;','_left = now; _right = now;','right shot cannot kick left gun'),
    @('visual-slide','PresentationSource',$presentation,'left == LastLeft && ','','only firing side receives native slide animation'),
    @('visual-neutral','PresentationSource',$presentation,'!firing && ','','neutral pose only captured in idle')
  )) {
    if(!$fault[2].Contains($fault[3])){throw 'Timing mutation anchor missing'}
    $path=Join-Path $evidence ($fault[0]+'.cs')
    [IO.File]::WriteAllText($path,$fault[2].Replace($fault[3],$fault[4]))
    $result=(& dotnet run --project $timingProject -c Release "-p:$($fault[1])=$path" 2>&1 | Out-String)
    [IO.File]::WriteAllText((Join-Path $evidence ($fault[0]+'.log')),$result)
    if($LASTEXITCODE -eq 0 -or $result.Contains('error CS') -or !$result.Contains($fault[5])){throw $result}
    Write-Output "NEGATIVE_CONTROL_PASSED $($fault[0])"
  }
} finally { & dotnet build $timingProject -c Release -t:Rebuild --nologo -v:quiet; if($LASTEXITCODE -ne 0){throw 'Timing restore failed'} }
& dotnet run --project $timingProject -c Release --no-build
if($LASTEXITCODE -ne 0){throw 'Final timing production run failed'}
& (Join-Path $PSScriptRoot 'View/Check-Mutants.ps1')
if($LASTEXITCODE -ne 0){throw 'View lifetime checks failed'}
Write-Output 'AKIMBO_REGRESSION_PASSED'
