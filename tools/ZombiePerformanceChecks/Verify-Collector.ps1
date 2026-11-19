# No-game step for the collector's node tests (ring reader, poller against the FrameStats simulation, run integrity, run summary).
# The folder's Verify.ps1 checks the compiled performance patches; nothing else ran these tests before they were registered here.
# Fails when node is missing, when a test fails and when fewer tests ran than the 36 known at registration (a test file that stops
# being collected would otherwise pass in silence).
$ErrorActionPreference = 'Stop'
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) { throw 'node is not on PATH; the collector tests need it.' }
$tests = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.test.mjs' | Sort-Object Name | ForEach-Object { $_.FullName })
if ($tests.Count -lt 4) { throw "Expected at least 4 test files in $PSScriptRoot, found $($tests.Count)." }
$output = & node --test @tests 2>&1 | Out-String
$code = $LASTEXITCODE
Write-Host $output
if ($code -ne 0) { throw "node --test failed with exit code $code" }
$pass = [int]([regex]::Match($output, '(?m)^\S+ pass (\d+)').Groups[1].Value)
$fail = [int]([regex]::Match($output, '(?m)^\S+ fail (\d+)').Groups[1].Value)
$skipped = [int]([regex]::Match($output, '(?m)^\S+ skipped (\d+)').Groups[1].Value)
if ($fail -ne 0 -or $skipped -ne 0 -or $pass -lt 36) { throw "Collector tests: pass=$pass fail=$fail skipped=$skipped (expected at least 36 passed, none failed or skipped)." }
Write-Host "ZOMBIE_COLLECTOR_TESTS_PASSED $pass"
