$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repo 'ArenaMode.Client/Diagnostics/ArenaLog.cs'
$source = [IO.File]::ReadAllText($sourcePath)
$project = Join-Path $PSScriptRoot 'LoggingChecks.csproj'
$runId = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo ('artifacts/logging-checks/' + $runId)
[IO.Directory]::CreateDirectory($evidence) | Out-Null

function Invoke-LoggingCheck([string]$Name, [string]$SourceFile, [string]$ExpectedFailure = '') {
    $log = Join-Path $evidence ($Name + '.log')
    # Unique configurations isolate compiled mutations while retaining the SDK's
    # normal obj exclusion. No project/source/deployed DLL is overwritten.
    $configuration = 'Logging_' + $runId + '_' + $Name
    & dotnet run --project $project -c $configuration "-p:ArenaLogSource=$SourceFile" *> $log
    $exitCode = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($ExpectedFailure) {
        if ($exitCode -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or
            $output -notmatch ('InvalidOperationException: ' + [regex]::Escape($ExpectedFailure))) {
            throw "Logging mutant $Name did not compile and fail the intended behavior assertion: $log`n$output"
        }
        Write-Output "Rejected compiled logging mutant: $Name ($ExpectedFailure)"
    }
    else {
        if ($exitCode -ne 0 -or $output -notmatch 'ARENA_LOGGING_CHECKS_PASSED \(7\)') {
            throw "Production logging checks failed: $log`n$output"
        }
        Write-Output "ARENA_LOGGING_CHECKS_PASSED (7): $Name"
    }
}

Invoke-LoggingCheck 'production-before' $sourcePath
try {
    $mutants = @(
        @{ Name = 'unguarded-developer'; Find = 'if (DeveloperEnabled)'; Replace = 'if (true)';
            Failure = 'unbound developer mode is quiet' },
        @{ Name = 'developer-always-disabled'; Find = 'Config.Settings.DeveloperMode?.Value == true'; Replace = 'false';
            Failure = 'enabled probe appears at Info with its developer label' }
    )
    foreach ($mutant in $mutants) {
        if ([regex]::Matches($source, [regex]::Escape($mutant.Find)).Count -ne 1) {
            throw "Missing or ambiguous logging mutation anchor: $($mutant.Name)"
        }
        $mutantPath = Join-Path $evidence ($mutant.Name + '.cs')
        [IO.File]::WriteAllText($mutantPath, $source.Replace($mutant.Find, $mutant.Replace))
        Invoke-LoggingCheck $mutant.Name $mutantPath $mutant.Failure
    }
}
finally {
    Invoke-LoggingCheck 'production-after' $sourcePath
}
Write-Output "ARENA_LOGGING_MUTATION_CHECKS_PASSED (2 compiled mutants); evidence: $evidence"
