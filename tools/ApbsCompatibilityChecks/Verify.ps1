#requires -Version 7.0
param([string]$SptRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$SptRoot) { $SptRoot = [IO.Path]::GetFullPath((Join-Path $repo '../..')) }
$stage = Join-Path $repo ('artifacts/apbs-compat/tests/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$paths = @(
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'BepInEx/plugins/maschine-ArenaMode.Client.dll',
    'BepInEx/plugins/maschine-ArenaMode.BigBrainCompat.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'BepInEx/plugins/maschine-Redeploy.Client.dll',
    'SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/acidphantasm-progressivebotsystem.dll',
    'SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/config.json'
)
$before = @{}
foreach ($path in $paths) { $before[$path] = (Get-FileHash -LiteralPath (Join-Path $SptRoot $path) -Algorithm SHA256).Hash }
$oldTestRoot = $env:ARENA_APBS_TEST_ROOT
try {
    $env:ARENA_APBS_TEST_ROOT = $SptRoot
    & dotnet test (Join-Path $repo 'ArenaMode.Tests') -c Release --nologo "-p:SPTUserPath=$stage/install" --logger 'trx;LogFileName=results.trx' --results-directory $stage 2>&1 | Tee-Object (Join-Path $stage 'test.log')
    if ($LASTEXITCODE -ne 0) { throw 'Server/APBS tests failed' }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $stage 'results.trx') -Raw
    $integration = @($trx.TestRun.Results.UnitTestResult | Where-Object testName -Like '*InstalledApbsUsesArenaRangeThroughRealNativeCloneAndLevelPipeline')
    if ($integration.Count -ne 1 -or $integration[0].outcome -ne 'Passed') { throw 'Installed APBS test missing or skipped' }
} finally {
    $env:ARENA_APBS_TEST_ROOT = $oldTestRoot
    foreach ($path in $paths) {
        if ((Get-FileHash -LiteralPath (Join-Path $SptRoot $path) -Algorithm SHA256).Hash -ne $before[$path]) {
            throw "Verification altered installed file: $path"
        }
    }
    $before | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'installed-hashes.json')
}
Write-Output "Evidence: $stage"
Write-Output 'ARENA_APBS_CHECKS_PASSED'
