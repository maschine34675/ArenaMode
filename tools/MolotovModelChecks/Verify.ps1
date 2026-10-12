$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/molotov-model-checks/' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($stage)
$sourcePath = Join-Path $repo 'ArenaMode.Client/Combat/Fire/ArenaMolotovModel.cs'
$native = [IO.Path]::GetFullPath((Join-Path $repo '../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
function Check-Model([string]$name, [string]$source, [string]$failure = '') {
    $dir = Join-Path $stage $name
    [void][IO.Directory]::CreateDirectory($dir)
    $build = & dotnet build (Join-Path $PSScriptRoot 'MolotovModelChecks.csproj') -c Release --nologo "-p:ModelSource=$source" "-p:BaseIntermediateOutputPath=$dir/obj/" "-p:OutputPath=$dir/out/" 2>&1 | Out-String
    [IO.File]::WriteAllText((Join-Path $dir 'build.log'), $build)
    if ($LASTEXITCODE -ne 0) { throw "Model check $name must compile: $build" }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $run = & dotnet (Join-Path $dir 'out/MolotovModelChecks.dll') $native 2>&1 | Out-String
        $exit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    [IO.File]::WriteAllText((Join-Path $dir 'run.log'), $run)
    if ($failure) {
        if ($exit -eq 0 -or !$run.Contains('FAILED: ' + $failure)) { throw "Model fault $name did not hit expected assertion: $run" }
        Write-Output "Rejected $name at $failure"
    } else {
        if ($exit -ne 0 -or !$run.Contains('ARENA_MOLOTOV_MODEL_PRODUCTION_PASSED:')) { throw "Model production failed: $run" }
        Write-Output $run.Trim()
    }
}
Check-Model 'production' $sourcePath
$faults = @(
    @{ name='template-leak'; find='item?.TemplateId == ArenaMolotovItem.TemplateId'; replace='true'; fail='donor request untouched' },
    @{ name='late-scope'; find='try { if (InArena) ApplyItem(instance); }'; replace='try { ApplyItem(instance); }'; fail='late completion after disarm untouched' },
    @{ name='role-model'; find='prefab.IsInPool || '; replace=''; fail='role model remains untouched' },
    @{ name='pool-mesh'; find='_filter.sharedMesh = _originalMesh;'; replace='_filter.sharedMesh = _mesh;'; fail='pool mesh restored without active arena' },
    @{ name='world-offset'; find='_originalMesh.bounds.center - new Vector3(0, 0, .04753137f)'; replace='new Vector3(0, 0, 0)'; fail='world baked mesh offset preserved' },
    @{ name='resource-leak'; find='if (_mesh != null) UnityEngine.Object.Destroy(_mesh);'; replace=''; fail='only owned resources destroyed' },
    @{ name='partial-material'; find='if (_materials.Contains(current[i]))'; replace='if (false)'; fail='partially replaced material array releases only unbound owned assets' }
)
$original = [IO.File]::ReadAllText($sourcePath)
foreach ($fault in $faults) {
    if ($original.Split(@($fault.find), [StringSplitOptions]::None).Length -ne 2) { throw "Ambiguous model fault anchor: $($fault.name)" }
    $copy = Join-Path $stage ($fault.name + '.cs')
    [IO.File]::WriteAllText($copy, $original.Replace($fault.find, $fault.replace))
    Check-Model $fault.name $copy $fault.fail
}
Check-Model 'production-final' $sourcePath
Write-Output "ARENA_MOLOTOV_MODEL_CHECKS_PASSED; seven compiled negative controls; evidence $stage"
