# Only standalone linked-source harnesses and unique synthetic temporary profiles; no deploy targets.
param()
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projects = @(
    @{ Project = (Join-Path $PSScriptRoot 'RewardControlTransferChecks.csproj'); Expect = 'REWARD_CONTROL_TRANSFER_CHECKS PASS assertions=\d+ failures=0' },
    @{ Project = (Join-Path $repo 'tools\ArenaSessionClientChecks\ArenaSessionClientChecks.csproj'); Expect = 'ARENA_SESSION_CLIENT_CHECKS_PASSED \(\d+ assertions\)' }
)
foreach ($check in $projects) {
    $lines = @(& dotnet run --project $check.Project -c Release --no-launch-profile 2>&1 | ForEach-Object { "$_" })
    $exit = $LASTEXITCODE
    $lines | Write-Output
    if ($exit -ne 0 -or ($lines -join "`n") -notmatch $check.Expect) { throw "Standalone check failed: $($check.Project)" }
}
Write-Output 'REWARD_CONTROL_TRANSFER_VERIFIED'
