#requires -Version 7.0
param([string]$SptRuntime)
$ErrorActionPreference = 'Stop'
$checkRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$SptRuntime) { $SptRuntime = [IO.Path]::GetFullPath((Join-Path $checkRepo '../../SPT_Runtime')) }
$checkProject = Join-Path $PSScriptRoot 'SettingsPollLogChecks.csproj'
$checkSource = Join-Path $checkRepo 'ArenaMode.Server/Patches/ArenaSettingsRequestLogPatch.cs'
$checkMutants = Join-Path $PSScriptRoot 'obj/log-policy-mutants'
New-Item -ItemType Directory -Path $checkMutants -Force | Out-Null

function Invoke-PollChecks([string]$Source, [string]$ExpectedFailure) {
    $checkOutput = & dotnet run --project $checkProject -c Release "-p:SettingsPatchSource=$Source" "-p:SptRuntime=$SptRuntime" -- $SptRuntime 2>&1
    $checkExit = $LASTEXITCODE
    $checkText = $checkOutput | Out-String
    if ($ExpectedFailure) {
        if ($checkExit -eq 0 -or !$checkText.Contains($ExpectedFailure)) {
            throw "Negative control did not fail at its intended assertion: $ExpectedFailure`n$checkText"
        }
        Write-Output "SETTINGS_POLL_NEGATIVE_CONTROL_REJECTED $ExpectedFailure"
    }
    elseif ($checkExit -ne 0 -or !$checkText.Contains('SETTINGS_POLL_LOG_CHECKS_PASSED assertions=')) {
        throw "Settings poll checks failed ($checkExit):`n$checkText"
    }
    else { Write-Output $checkText.Trim() }
}

Invoke-PollChecks $checkSource ''
$checkOriginal = [IO.File]::ReadAllText($checkSource)
$caseMutant = Join-Path $checkMutants 'case-insensitive.cs'
[IO.File]::WriteAllText($caseMutant, $checkOriginal.Replace('StringComparison.Ordinal)', 'StringComparison.OrdinalIgnoreCase)'))
Invoke-PollChecks $caseMutant 'non-poll retains native request line'
$failureMutant = Join-Path $checkMutants 'hide-failure.cs'
[IO.File]::WriteAllText($failureMutant, $checkOriginal.Replace('if (status is < 200 or >= 300 || aborted)', 'if (false)'))
Invoke-PollChecks $failureMutant 'final HTTP 100 observability'
# Leave the local harness output built against production source, not a mutant.
Invoke-PollChecks $checkSource ''
Write-Output 'SETTINGS_POLL_LOG_VERIFICATION_PASSED'
