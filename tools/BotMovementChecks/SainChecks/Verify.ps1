$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($scenario in @('valid', 'versions', 'future', 'bad-version', 'bad-shape', 'future-bad-shape')) {
    $arguments = @('run', '--project', (Join-Path $PSScriptRoot 'SainChecks.csproj'), '-c', 'Release')
    if ($scenario -ne 'valid') { $arguments += '--no-build' }
    & dotnet @arguments -- $scenario
    if ($LASTEXITCODE -ne 0) { throw "SAIN movement behavior failed: $scenario" }
}
# The other adapters compile their real production source against an isolated
# SAIN-shaped fixture. A future assembly identity must pass their full behavior
# checks too, including each adapter's existing contract-drift/rollback cases.
foreach ($project in @('SainPersonalityChecks', 'ArenaExitChecks', 'BotLifecycleSafetyChecks', 'BotTempoChecks', 'TdmSearchTempoChecks')) {
    $path = Join-Path $PSScriptRoot "../../$project/$project.csproj"
    & dotnet run --project $path -c SainFutureVersion -p:AssemblyVersion=4.6.0.0
    if ($LASTEXITCODE -ne 0) { throw "Future SAIN contract behavior failed: $project" }
}
$versionSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../../../ArenaMode.Client/Bots/ArenaSainVersion.cs'))
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaSainVersions-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
foreach ($fault in @(
    @('ceiling-restored', 'if (actual < Minimum)', 'if (actual != Minimum)', 'future', 'known SAIN without component leaves native bot alone'),
    @('minimum-removed', 'if (actual < Minimum)', 'if (false)', 'versions', 'missing, malformed, foreign or older identity rejected'),
    @('warning-spam', ' && Interlocked.Exchange(ref _reportedNewer, 1) == 0', '', 'versions', 'future warning is shared across six adapters and repeated binds')
)) {
    if (($versionSource.Split($fault[1]).Length - 1) -ne 1) { throw "SAIN version fault anchor changed: $($fault[0])" }
    $file = Join-Path $scratch ($fault[0] + '.cs')
    [IO.File]::WriteAllText($file, $versionSource.Replace($fault[1], $fault[2]))
    $result = @(& dotnet run --project (Join-Path $PSScriptRoot 'SainChecks.csproj') -c SainVersionFault "-p:SainVersionSource=$file" -- $fault[3] 2>&1)
    $message = $result -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $scratch ($fault[0] + '.log')), $message)
    if ($LASTEXITCODE -eq 0 -or !$message.Contains('Assertion failed: ' + $fault[4])) { throw "Wrong SAIN version fault result: $($fault[0])`n$message" }
    Write-Output "Rejected compiled SAIN version fault: $($fault[0])"
}
Write-Output 'BOT_MOVEMENT_SAIN_BEHAVIOR_CHECKS_PASSED'
