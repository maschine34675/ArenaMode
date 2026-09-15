$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($scenario in @('valid', 'bad-version', 'bad-shape')) {
    $arguments = @('run', '--project', (Join-Path $PSScriptRoot 'SainChecks.csproj'), '-c', 'Release')
    if ($scenario -ne 'valid') { $arguments += '--no-build' }
    & dotnet @arguments -- $scenario
    if ($LASTEXITCODE -ne 0) { throw "SAIN movement behavior failed: $scenario" }
}
Write-Output 'BOT_MOVEMENT_SAIN_BEHAVIOR_CHECKS_PASSED'
