$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath=Join-Path $repo 'ArenaMode.Client/Movement/ArenaSurgicalWalk.cs'
$source=[IO.File]::ReadAllText($sourcePath)
$evidence=Join-Path $repo ('artifacts/surgical-walk-checks/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
function Run-Case([string]$name,[string[]]$extra=@()) {
    $output=& dotnet run --project (Join-Path $PSScriptRoot 'SurgicalWalkChecks.csproj') -c $name @extra 2>&1 | Out-String
    $code=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence ($name+'.log')),$output)
    return @{Output=$output;Code=$code}
}
$baseline=Run-Case 'Release'
if($baseline.Code -ne 0 -or !$baseline.Output.Contains('SURGICAL_WALK_CHECKS_PASSED')){throw $baseline.Output}
Write-Output $baseline.Output.Trim()
$faults=@(
    @{Name='NativeTrue';From='bool original = context.CanWalk;';To='bool original = false;';Failure='native non-leg medical walking preserved'},
    @{Name='ForeignEffect';From='|| !ReferenceEquals(effect.MedItem, item)';To='';Failure='reject hands changed'},
    @{Name='OrdinaryMedicine';From='return item.HealthEffectsComponent?.AffectsAny(EDamageEffectType.DestroyedPart) == true';To='return true';Failure='reject non-leg splint exception'},
    @{Name='NonLegSplint';From='(effect.BodyPart == EBodyPart.LeftLeg || effect.BodyPart == EBodyPart.RightLeg)';To='true';Failure='reject non-leg splint exception'},
    @{Name='Collision';From='return context.ObstacleCollisionFacade?.CanMove() == true;';To='return true;';Failure='reject obstacle'},
    @{Name='NoCap';From='if (IsStationaryTreatment(context, requireInput: false)) ClampHorizontal(ref motion, deltaTime);';To='';Failure='leg splint capped at 1 m/s with gravity intact'},
    @{Name='LeakedCap';From='if (IsStationaryTreatment(context, requireInput: false)) ClampHorizontal(ref motion, deltaTime);';To='ClampHorizontal(ref motion, deltaTime);';Failure='native non-leg treatment has no added cap'}
)
foreach($fault in $faults) {
    if(!$source.Contains($fault.From)){throw "Missing mutation anchor $($fault.Name)"}
    $path=Join-Path $evidence ($fault.Name+'.cs')
    [IO.File]::WriteAllText($path,$source.Replace($fault.From,$fault.To))
    $result=Run-Case $fault.Name @("-p:WalkSource=$path")
    if($result.Code -eq 0 -or $result.Output -match 'error CS\d+' -or !$result.Output.Contains('ASSERT: '+$fault.Failure)){
        throw "Incorrect negative control $($fault.Name): $($result.Output)"
    }
    Write-Output "Rejected surgical fault: $($fault.Name)"
}
if([IO.File]::ReadAllText($sourcePath) -cne $source){throw 'Production source changed during checks'}
Write-Output "SURGICAL_WALK_REGRESSION_VERIFIED ($($faults.Count) negative controls; evidence $evidence)"
