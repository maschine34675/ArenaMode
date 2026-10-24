param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'ArenaStartErrorChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Session'
$run = Join-Path $PSScriptRoot ('artifacts/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$files = @('ArenaServerClient.cs', 'ArenaLocalRaidStartedPatch.cs')
$original = @{}
$hashes = @{}
foreach ($file in $files) {
    $original[$file] = [IO.File]::ReadAllText((Join-Path $source $file)).Replace("`r`n", "`n")
    $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $source $file)).Hash
}
function Check([string]$name, [string]$file = '', [string]$find = '', [string]$replace = '', [string]$expected = '') {
    $copy = Join-Path $run $name
    New-Item -ItemType Directory -Path $copy | Out-Null
    foreach ($item in $files) {
        $text = $original[$item]
        if ($item -eq $file) {
            if ([regex]::Matches($text, [regex]::Escape($find)).Count -ne 1) { throw "Ambiguous or absent mutation anchor: $name" }
            $text = $text.Replace($find, $replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $item), $text)
    }
    $log = Join-Path $copy 'result.log'
    & dotnet run --project $project -c Release --no-restore --disable-build-servers -p:UseSharedCompilation=false `
        "-p:ProductionServerClientSource=$(Join-Path $copy 'ArenaServerClient.cs')" `
        "-p:ProductionLocalStartSource=$(Join-Path $copy 'ArenaLocalRaidStartedPatch.cs')" *> $log
    $code = $LASTEXITCODE
    $output = Get-Content -LiteralPath $log -Raw
    if ($expected) {
        if ($code -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains('ASSERT: ' + $expected)) {
            throw "Negative control $name did not fail the expected behavioral assertion: $log`n$output"
        }
        Write-Output "ARENA_START_ERROR_MUTANT_REJECTED: $name"
    } elseif ($code -ne 0 -or !$output.Contains('ARENA_START_ERROR_CHECKS_PASSED')) {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
if (!(Test-Path (Join-Path $PSScriptRoot 'obj/project.assets.json'))) {
    & dotnet restore $project --disable-build-servers -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Harness restore failed' }
}
Check 'baseline'
Check 'lost-server-reason' 'ArenaServerClient.cs' 'failureReason = parsed["reason"].Value<string>().Trim();' 'failureReason = "The server did not arm Arena profile protection.";' 'exact server reason reaches caller'
Check 'duplicated-wrapper' 'ArenaLocalRaidStartedPatch.cs' '"Arena start cancelled to protect your profile.", ex)' '"Arena start cancelled to protect your profile. " + ex.Message, ex)' 'wrapper contains context only'
Check 'discarded-cause' 'ArenaLocalRaidStartedPatch.cs' '"Arena start cancelled to protect your profile.", ex)' '"Arena start cancelled to protect your profile.")' 'original failure retained as inner cause'
Check 'discarded-uncertainty' 'ArenaServerClient.cs' "if (!ok)`n            {" "if (!ok)`n            {`n                HasPendingArm = false;" 'refusal retains uncertain exact arm'
Check 'stale-success-reason' 'ArenaServerClient.cs' 'else failureReason = null;' 'else failureReason = "stale refusal";' 'successful start has no failure reason'
Check 'skipped-cancellation' 'ArenaLocalRaidStartedPatch.cs' "if (ArenaServerClient.HasPendingArm)`n                ArenaServerClient.PostRaidCancel(startFailed: true);`n            // GamePrepare" "// MUTANT: do not settle the failed start.`n            // GamePrepare" 'failure cleans compatibility and cancels exact failed-start token'
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath (Join-Path $source $file)).Hash -ne $hashes[$file]) { throw "Production source changed during verification: $file" }
}
Write-Output "ARENA_START_ERROR_CHECKS_PASSED; 72 assertions and 6 compiled negative controls; evidence: $run"
