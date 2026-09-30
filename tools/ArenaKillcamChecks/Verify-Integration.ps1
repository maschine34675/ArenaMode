param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/arena-killcam-integration/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
Push-Location $repo
try {
    foreach ($script in @(
        'tools/TdmMatchMenuChecks/Run-Checks.ps1',
        'tools/DeathmatchChecks/Verify.ps1',
        'tools/TdmMatchChecks/Run-Checks.ps1',
        'tools/SpectatorHealthChecks/Verify-Regression.ps1',
        'tools/TeamGiveUpChecks/Verify-Regression.ps1',
        'tools/TeamDeathmatchRuntimeChecks/Verify-Regression.ps1')) {
        $name = Split-Path (Split-Path $script) -Leaf
        $output = & pwsh -NoProfile -File $script 2>&1
        $code = $LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $run ($name + '.log'))
        if ($code -ne 0) { throw ($output -join "`n") }
        Write-Output "$name passed."
    }
    foreach ($project in @('PitFireTeamChecks', 'TeamArenaExpansionChecks')) {
        $output = & dotnet run --project "tools/$project" -c Release 2>&1
        $code = $LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $run ($project + '.log'))
        if ($code -ne 0) { throw ($output -join "`n") }
        Write-Output "$project passed."
    }
    # The normal client build deploys. This existing staged build explicitly
    # disables those targets and proves installed dependencies remain unchanged.
    $output = & pwsh -NoProfile -File tools/DoubleJumpChecks/Build-Staged.ps1 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $run 'staged-build.log')
    if ($code -ne 0) { throw ($output -join "`n") }
    Write-Output "Evidence: $run"
    Write-Output 'ARENA_KILLCAM_INTEGRATION_PASSED'
}
finally { Pop-Location }
