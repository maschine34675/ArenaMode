$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'AnnouncerChecks.csproj'
$scratch = Join-Path $repo ('artifacts/announcer-prepare-review/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$servicePath = Join-Path $repo 'ArenaMode.Client/Audio/AnnouncerService.cs'
$policyPath = Join-Path $repo 'ArenaMode.Client/Audio/ArenaAnnouncementPolicy.cs'
$service = [IO.File]::ReadAllText($servicePath)
$policy = [IO.File]::ReadAllText($policyPath)
$bindings = @{}
foreach ($path in @($servicePath, $policyPath) + @(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object FullName)) {
    $bindings[$path] = (Get-FileHash -LiteralPath $path).Hash
}

function Check([string]$name, [string]$source, [string]$anchor, [string]$replacement,
    [string]$property, [string]$expected) {
    if ([regex]::Matches($source, [regex]::Escape($anchor)).Count -ne 1) { throw "Ambiguous mutation: $name" }
    $copy = Join-Path $scratch ($name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($anchor, $replacement))
    $output = & dotnet run --project $project -c Release "-p:${property}=$copy" '-p:NoWarn=CS0649' `
        "-p:OutputPath=$scratch/$name-bin/" "-p:IntermediateOutputPath=$scratch/$name-obj/" 2>&1
    $code = $LASTEXITCODE
    $transcript = $output -join "`n"
    [IO.File]::WriteAllText((Join-Path $scratch ($name + '.log')), $transcript)
    if ($code -eq 0 -or !$transcript.Contains('ARENA_ANNOUNCER_CHECKS_FAILED: ' + $expected) -or
        $transcript -match 'error (CS|MSB|NETSDK)\d+') { throw "Fault not rejected at intended boundary: $name`n$transcript" }
    Write-Output "REJECTED COMPILED PREPARATION FAULT: $name"
}

$positive = & dotnet run --project $project -c Release '-p:NoWarn=CS0649' 2>&1
if ($LASTEXITCODE -ne 0 -or ($positive -join "`n") -notmatch 'ARENA_ANNOUNCER_CHECKS_PASSED') {
    throw "Production preparation checks failed: $positive"
}
$positive | ForEach-Object { Write-Output $_ }
Check 'old-policy-cancellation' $policy `
    'emit(advancing ? ArenaAnnouncerCue.AdvanceStatus : ArenaAnnouncerCue.CancelStatus);' `
    'emit(ArenaAnnouncerCue.CancelStatus);' 'AnnouncementPolicySource' `
    'Fast preparation-to-live must not cut the playing PREPARE TO FIGHT recording.'
Check 'immediate-start-cutoff' $service 'if (PreparationPlaying)' 'if (false && PreparationPlaying)' `
    'AnnouncerServiceSource' 'Fast preparation-to-live must not cut the playing PREPARE TO FIGHT recording.'
Check 'queued-start-after-cancellation' $service 'if (!preservePreparation) _pendingStartKey = null;' '' `
    'AnnouncerServiceSource' 'Canceled preparation cannot release stale start later: readiness'
Check 'hard-cancellation-weakened' $service 'if (preservePreparation && PreparationPlaying) return;' `
    'if (PreparationPlaying) return;' 'AnnouncerServiceSource' `
    'Readiness loss cancels playing owned countdown/status without touching shared GUI audio.'
foreach ($path in $bindings.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $bindings[$path]) { throw "Source changed during checks: $path" }
}
Write-Output "ANNOUNCER PREPARATION CHECKS PASSED (4 compiled faults); evidence=$scratch"
