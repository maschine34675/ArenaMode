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

$loadingReset = '_loading = false;'
if ([regex]::Matches($source, [regex]::Escape($loadingReset)).Count -ne 1) {
    throw 'Expected exactly one loading-state reset; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'load-recovery' ($source.Replace($loadingReset, '_loading = true;')) `
    'A failed first load must retry only its failed clip; healthy cached clips must survive.'

$raceGuard = 'if (IsReady(previous))'
if ([regex]::Matches($source, [regex]::Escape($raceGuard)).Count -ne 1) {
    throw 'Expected exactly one late-readiness guard; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'late-readiness-race' ($source.Replace($raceGuard, 'if (false)')) `
    'Late-ready cached clip survives; its unnecessary downloaded replacement is destroyed.'

$forbiddenContracts = @(
    'System.Reflection',
    'BindingFlags',
    'AudioSource',
    'BetterAudio',
    '_outputCheck',
    'LeaseUiPriority',
    'RestoreUiPriority',
    'internal static void Tick'
)
foreach ($forbidden in $forbiddenContracts) {
    if ($source.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "Native UI-only source contract still contains forbidden machinery: $forbidden"
    }
}

$nativeCall = 'Singleton<GUISounds>.Instance.PlaySound('
if ([regex]::Matches($source, [regex]::Escape($nativeCall)).Count -ne 1) {
    throw 'Expected exactly one direct native GUISounds.PlaySound call.'
}

$pcmMaterialization = 'AudioClip pcm = TryMaterializePcm(decoded, key, out string pcmProblem);'
if ([regex]::Matches($source, [regex]::Escape($pcmMaterialization)).Count -ne 1) {
    throw 'Expected exactly one PCM materialization assignment; refusing an ambiguous source-copy mutation.'
}
$pcmBypass = 'AudioClip pcm = null; string pcmProblem = "injected PCM bypass";'
Invoke-NegativeCase 'pcm-materialization' ($source.Replace($pcmMaterialization, $pcmBypass)) `
    'Native UI primary must receive an Arena-owned materialized PCM clip.'

$singleFlag = 'single: true,'
if ([regex]::Matches($source, [regex]::Escape($singleFlag)).Count -ne 1) {
    throw 'Expected exactly one native single:true flag; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'native-single-contract' ($source.Replace($singleFlag, 'single: false,')) `
    'Native UI primary must request GUISounds single:true replacement semantics.'

$commonUiFlag = 'commonUiSound: false,'
if ([regex]::Matches($source, [regex]::Escape($commonUiFlag)).Count -ne 1) {
    throw 'Expected exactly one native commonUiSound:false flag; refusing an ambiguous source-copy mutation.'
}
Invoke-NegativeCase 'native-mixer-contract' ($source.Replace($commonUiFlag, 'commonUiSound: true,')) `
    'Native UI primary must request commonUiSound:false and preserve explicit volume.'

$finalHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($sourcePath)))
if ($sourceHash -ne $finalHash) { throw 'Production source changed during negative verification.' }
& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'The unchanged production service failed its final positive check.' }
Write-Output "ARENA_ANNOUNCER_REGRESSION_VERIFIED (load, race, PCM and native-route mutants rejected; production source unchanged; evidence: $scratch)"
