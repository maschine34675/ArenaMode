$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$statePath = Join-Path $arenaRepo 'ArenaMode.Client\Session\Preview\ArenaPreviewLifetimeState.cs'
$original = [IO.File]::ReadAllText($statePath)
$project = Join-Path $PSScriptRoot 'ArenaCharacterPreviewChecks.csproj'
$scratch = Join-Path $PSScriptRoot ('obj\mutations-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$mutants = @(
    @{ Name = 'release-during-body-init'; Old = 'IsDisposed || _loading || _released'; New = 'IsDisposed || _released' },
    @{ Name = 'load-restarted-on-transfer'; Old = '_started || IsDisposed'; New = 'IsDisposed' },
    @{ Name = 'double-release'; Old = '_released = true;'; New = '_released = false;' },
    @{ Name = 'stale-host-callback'; Old = 'generation == _generation'; New = 'generation > 0' },
    @{ Name = 'other-flow-admitted'; Old = 'ReferenceEquals(settings, _settings)'; New = '_settings != null' }
)
$rejected = 0
foreach ($mutant in $mutants) {
    if (-not $original.Contains($mutant.Old)) { throw ('Mutation anchor absent: ' + $mutant.Name) }
    $mutantPath = Join-Path $scratch ($mutant.Name + '.cs')
    [IO.File]::WriteAllText($mutantPath, $original.Replace($mutant.Old, $mutant.New))
    $caseRoot = Join-Path $scratch $mutant.Name
    $caseObj = Join-Path $caseRoot 'obj\'
    $caseBin = Join-Path $caseRoot 'bin\'
    $output = & dotnet run --project $project -c Release "-p:PreviewStateSource=$mutantPath" "-p:BaseIntermediateOutputPath=$caseObj" "-p:BaseOutputPath=$caseBin" -- $arenaRepo 2>&1
    $exit = $LASTEXITCODE
    if ($exit -eq 0) { throw ('Known defect was accepted: ' + $mutant.Name) }
    if (($output -join "`n") -notmatch 'InvalidOperationException') {
        throw ('Mutation did not reach assertion failure: ' + $mutant.Name + "`n" + ($output -join "`n"))
    }
    ++$rejected
    Write-Output ('Rejected production mutation: ' + $mutant.Name)
}
# Restore the built test to the actual production input for subsequent runs.
& dotnet run --project $project -c Release -- $arenaRepo
if ($LASTEXITCODE -ne 0) { throw 'Production preview baseline failed after mutation controls.' }
Write-Output ("PREVIEW_MUTATION_CONTROLS_PASSED: $rejected rejected")
