param(
    # Last committed service before the recovery/diagnostic change.
    [string]$BaselineRevision = '6938751c8c798f793b2ccea48045cf2ab7dcb513'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AnnouncerChecks.csproj'
$sourcePath = Join-Path $repoRoot 'ArenaMode.Client\Audio\AnnouncerService.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$sourceHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaAnnouncerMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-NegativeCase([string]$name, [string]$candidate, [string]$expectedFailure) {
    $copyPath = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copyPath, $candidate)
    $outputPath = Join-Path $scratch ($name + '-bin\')
    $intermediatePath = Join-Path $scratch ($name + '-obj\')
    $output = & dotnet run --project $project -c Release "-p:AnnouncerServiceSource=$copyPath" "-p:OutputPath=$outputPath" "-p:IntermediateOutputPath=$intermediatePath" 2>&1
    $resultCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Output $_ }
    if ($resultCode -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($expectedFailure)) {
        throw "$name did not fail at the expected recovery boundary (exit $resultCode). Evidence: $scratch"
    }
}

$baselineLines = & git -C $repoRoot show ($BaselineRevision + ':ArenaMode.Client/Audio/AnnouncerService.cs')
if ($LASTEXITCODE -ne 0) { throw 'Could not read the committed pre-change service.' }
Invoke-NegativeCase 'committed-baseline' ($baselineLines -join "`n") `
    'A failed first load must retry only its failed clip; healthy cached clips must survive.'

$raceGuard = 'if (IsReady(previous))'
if ([regex]::Matches($source, [regex]::Escape($raceGuard)).Count -ne 1) {
    throw 'Expected exactly one late-readiness guard; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'late-readiness-race' ($source.Replace($raceGuard, 'if (false)')) `
    'Late-ready cached clip survives; its unnecessary downloaded replacement is destroyed.'

$finalHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
if ($sourceHash -ne $finalHash) { throw 'Production source changed during negative verification.' }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'The unchanged production service failed its final positive check.' }
Write-Output "ARENA_ANNOUNCER_REGRESSION_VERIFIED (baseline and race mutant rejected; production source unchanged; evidence: $scratch)"
