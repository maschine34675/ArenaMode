param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$reloadRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$reloadProject = Join-Path $PSScriptRoot 'ArenaReloadChecks.csproj'
$reloadEvidence = Join-Path $reloadRepo ('artifacts\arena-reload-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $reloadEvidence -Force | Out-Null
$reloadFile = Join-Path $reloadRepo 'ArenaMode.Client\Movement\ArenaReloadHandling.cs'
$reloadSource = [IO.File]::ReadAllText($reloadFile).Replace("`r`n", "`n")
function Run-Check([string]$Name, [string]$Override = '') {
    $reloadArguments = @('run', '--project', $reloadProject, '-c', 'Release')
    if ($Override) { $reloadArguments += "-p:ReloadSource=$Override" }
    $reloadPrevious = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $reloadText = (& dotnet @reloadArguments 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $reloadCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $reloadPrevious }
    [IO.File]::WriteAllText((Join-Path $reloadEvidence ($Name + '.log')), $reloadText)
    return @{ Code = $reloadCode; Output = $reloadText }
}
$reloadContracts = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify-InstalledContracts.ps1') -SptPath $SptPath 2>&1
if ($LASTEXITCODE -ne 0) { throw ($reloadContracts -join "`n") }
$reloadContracts | Set-Content -LiteralPath (Join-Path $reloadEvidence 'installed-contracts.log')
Write-Output $reloadContracts
$reloadResult = Run-Check 'production'
if ($reloadResult.Code -ne 0 -or !$reloadResult.Output.Contains('ARENA_RELOAD_CHECKS_PASSED')) { throw $reloadResult.Output }
Write-Output $reloadResult.Output
$reloadFaults = @(
    @('foreign-player', '!ArenaMovementScope.IsLocal(operation.Player)', 'false', 'foreign player reload is excluded'),
    @('missing-restore', 'SetSpeedReload(value.Animator, value.Original)', 'SetSpeedReload(value.Animator, value.Written)', 'native arm and body baselines preserved'),
    @('compounding', 'Write(lease.Arms, lease.Arms.Original * factor);', 'Write(lease.Arms, lease.Arms.Written * factor);', 'both reload animators use their own baseline'),
    @('foreign-clobber', 'if (StillOwned(value)) AnimationControllerParametersTable.SetSpeedReload', 'if (true) AnimationControllerParametersTable.SetSpeedReload', 'foreign parameter writer is preserved while owned body restores'),
    @('lost-body-speed', 'Write(lease.Body, bodyBase * factor);', 'Write(lease.Body, bodyBase);', 'both reload animators use their own baseline'),
    @('early-write-claim', "AnimationControllerParametersTable.SetSpeedReload(value.Animator, changed);`n            value.Written = changed;", "value.Written = changed;`n            AnimationControllerParametersTable.SetSpeedReload(value.Animator, changed);", 'native arm and body baselines preserved'),
    @('missing-diagnostic-owner', 'ObserveReload(controller);', '/* observation removed */', 'diagnostic distinguishes request from subsequent readback'),
    @('missing-write-request-log', 'TraceReload(controller, "write-requested");', '/* request diagnostic removed */', 'diagnostic distinguishes request from subsequent readback')
)
try {
    foreach ($reloadFault in $reloadFaults) {
        if (($reloadSource.Split([string[]]@($reloadFault[1]), [StringSplitOptions]::None)).Count -ne 2) { throw "Expected one mutation site $($reloadFault[0])" }
        $reloadMutant = Join-Path $reloadEvidence ($reloadFault[0] + '.cs')
        [IO.File]::WriteAllText($reloadMutant, $reloadSource.Replace($reloadFault[1], $reloadFault[2]))
        $reloadResult = Run-Check $reloadFault[0] $reloadMutant
        if ($reloadResult.Code -eq 0 -or !$reloadResult.Output.Contains('ASSERT: ' + $reloadFault[3])) { throw $reloadResult.Output }
        Write-Output "NEGATIVE CONTROL PASSED: $($reloadFault[0])"
    }
}
finally {
    $reloadRestore = & dotnet build $reloadProject -t:Rebuild -c Release --nologo -v:quiet 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($reloadRestore -join "`n") }
    $reloadRestore | Set-Content -LiteralPath (Join-Path $reloadEvidence 'restore.log')
}
$reloadResult = Run-Check 'production-final'
if ($reloadResult.Code -ne 0 -or !$reloadResult.Output.Contains('ARENA_RELOAD_CHECKS_PASSED')) { throw $reloadResult.Output }
Write-Output $reloadResult.Output
(Get-FileHash -LiteralPath $reloadFile -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $reloadEvidence 'production.sha256')
Write-Output "Evidence: $reloadEvidence"
