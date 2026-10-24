param([ValidateSet('Baseline','Regression')][string]$Mode = 'Baseline')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stage = Join-Path $repo ('artifacts/reward-harness-integration/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$checks = @(
    @('AnnouncerCombatChecks', 'Verify.ps1', 'ANNOUNCER_COMBAT_PRODUCTION_PASSED: ([1-9][0-9]*) assertions', 'ANNOUNCER COMBAT CHECKS PASSED'),
    @('AssistChecks', 'Verify.ps1', 'ASSIST_PRODUCTION_PASSED: ([1-9][0-9]*) assertions', 'ASSIST SCORING CHECKS PASSED'),
    @('ArenaStartErrorChecks', 'Verify.ps1', 'ARENA_START_ERROR_CHECKS_PASSED \(([1-9][0-9]*) assertions\)', 'ARENA_START_ERROR_CHECKS_PASSED; 72 assertions and 6 compiled negative controls'),
    @('NativeMenuChecks', 'Verify-SessionPing.ps1', 'ARENA_MENU_CHECKS_PASSED \(([1-9][0-9]*) assertions\)', 'SESSION_PING_VERIFIED'),
    @('SpawnShieldScopeChecks', 'Verify.ps1', 'PASS: ([1-9][0-9]*) production-linked spawn-shield scope checks', 'ARENA_SPAWN_SHIELD_SCOPE_REGRESSION_PASSED'),
    @('TdmMatchMenuChecks', 'Run-Checks.ps1', 'TDM_MATCH_MENU_CHECKS_PASSED \(([1-9][0-9]*) assertions\)', 'TDM_MATCH_MENU_CHECKS_PASSED'),
    @('TeamDeathmatchRuntimeChecks', 'Verify-Regression.ps1', 'PASS: ([1-9][0-9]*) production TDM mode/relations runtime-model checks', 'ARENA_TDM_RUNTIME_REGRESSION_PASSED'),
    @('TeamGiveUpChecks', 'Verify-Regression.ps1', 'PASS: ([1-9][0-9]*) production-linked Give up, scoreboard, awards and exact-body checks', 'ARENA_TEAM_GIVE_UP_REGRESSION_PASSED'),
    @('ZombieWavePendingChecks', 'Verify.ps1', 'ZOMBIE_WAVE_PENDING_CHECKS_PASSED ([1-9][0-9]*) assertions', 'ZOMBIE_WAVE_PENDING_MUTATIONS_PASSED'),
    @('ZombieTeamChecks', 'Verify.ps1', 'ZOMBIE_TEAM_CHECKS_PASSED ([1-9][0-9]*) assertions', 'ZOMBIE_TEAM_MUTATION_CHECKS_PASSED'),
    @('ZombieSoloChecks', 'Verify.ps1', 'ZOMBIE_SOLO_CHECKS_PASSED ([1-9][0-9]*) assertions', 'ZOMBIE_SOLO_MUTATION_CHECKS_PASSED')
)
$total = 0
foreach ($check in $checks) {
    $name = $check[0]
    $log = Join-Path $stage ($name + '.log')
    Write-Output "Checking $name ($Mode)"
    if ($Mode -eq 'Baseline') {
        # --nologo is not a dotnet-run option: it would become a test-suite
        # argument, potentially selecting no assertions in older harnesses.
        $output = & dotnet run --project (Join-Path $repo "tools/$name/$name.csproj") -c Release 2>&1 | Out-String
    } else {
        $output = & pwsh -NoProfile -File (Join-Path $repo "tools/$name/$($check[1])") 2>&1 | Out-String
    }
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath $log -Encoding utf8
    if ($code -ne 0) { throw "$name $Mode failed ($code): $log`n$output" }
    if ($Mode -eq 'Baseline') {
        $match = [regex]::Match($output, $check[2])
        if (!$match.Success) { throw "$name did not prove a positive assertion count: $log" }
        $total += [int]$match.Groups[1].Value
        Write-Output $match.Value
    } else {
        if (!$output.Contains($check[3])) { throw "$name lacked regression completion: $log" }
        Write-Output (($output -split "`n" | Where-Object { $_.Contains($check[3]) } | Select-Object -Last 1).Trim())
    }
}
if ($Mode -eq 'Baseline') {
    # A misspelled suite must fail rather than produce a green zero-test run.
    $negative = & dotnet (Join-Path $repo 'tools/AssistChecks/bin/Release/net10.0/AssistChecks.dll') '__unknown_suite__' 2>&1 | Out-String
    $code = $LASTEXITCODE
    $negative | Set-Content -LiteralPath (Join-Path $stage 'AssistUnknownSuite.log') -Encoding utf8
    if ($code -eq 0 -or !$negative.Contains('Unknown assist check suite')) { throw 'Assist unknown-suite guard was not exercised.' }
    Write-Output "REWARD_HARNESS_BASELINES_PASSED ($($checks.Count) harnesses, $total assertions, unknown suite rejected); evidence: $stage"
} else {
    Write-Output "REWARD_HARNESS_REGRESSIONS_PASSED ($($checks.Count) mutation wrappers); evidence: $stage"
}
