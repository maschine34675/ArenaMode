param([string]$SptPath = 'D:\SPT41')
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptPath
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$botRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$botProject = Join-Path $PSScriptRoot 'BotMovementChecks.csproj'
$botEvidence = Join-Path $botRepo ('artifacts/bot-movement-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($botEvidence)
$botSourcePath = Join-Path $botRepo 'ArenaMode.Client/Movement/ArenaBotMovement.cs'
$botSource = [IO.File]::ReadAllText($botSourcePath).Replace("`r`n", "`n")
$botSourceHash = (Get-ArenaObservedFileHash -LiteralPath $botSourcePath -Algorithm SHA256).Hash
$botInputs = @{}
$botInputPaths = @($botSourcePath, (Join-Path $botRepo 'ArenaMode.Client/Movement/ArenaBotMovementSain.cs'))
$botInputPaths += @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs', '.csproj', '.ps1')
} | ForEach-Object { $_.FullName })
foreach ($botPath in $botInputPaths) { $botInputs[$botPath] = (Get-ArenaObservedFileHash -LiteralPath $botPath -Algorithm SHA256).Hash }
$botInstalled = @{}
foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll', 'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $botPath = Join-Path $SptPath $relative
    $botInstalled[$botPath] = (Get-ArenaObservedFileHash -LiteralPath $botPath -Algorithm SHA256).Hash
}
function Run-Behavior([string]$Name, [string]$Override = '') {
    $botArgs = @('build', $botProject, '-t:Rebuild', '-c', 'Release', '--nologo')
    if ($Override) { $botArgs += "-p:BotMovementSource=$Override" }
    $botBuild = @(& dotnet @botArgs 2>&1)
    $botBuild | Set-Content -LiteralPath (Join-Path $botEvidence ($Name + '-build.log'))
    if ($LASTEXITCODE -ne 0) { throw ($botBuild -join "`n") }
    $botRun = @(& dotnet run --project $botProject -c Release --no-build 2>&1)
    $botResult = @{ Exit = $LASTEXITCODE; Text = ($botRun | ForEach-Object { "$_" }) -join "`n" }
    [IO.File]::WriteAllText((Join-Path $botEvidence ($Name + '.log')), $botResult.Text)
    return $botResult
}
function Positive([string]$Name) {
    $botResult = Run-Behavior $Name
    if ($botResult.Exit -ne 0 -or !$botResult.Text.Contains('BOT_MOVEMENT_BEHAVIOR_PASSED')) { throw $botResult.Text }
    Write-Output $botResult.Text
}
$botFaults = @(
    @('non-ai-scope', 'player == null || !player.IsAI || player.IsYourPlayer', 'player == null || false || player.IsYourPlayer', 'non AI player unchanged'),
    @('bonus-step-cap', 'factor = Math.Min(factor, Math.Sqrt((maximumStepSquared - verticalSquared) / horizontalSquared));', 'factor = factor;', 'bonus respects native per-step ceiling'),
    @('cap-restoration', 'lease.Controller.SpeedLimit = lease.OriginalLimit;', 'lease.Controller.SpeedLimit = lease.WrittenLimit;', 'normal completion restores native cap'),
    @('native-corner-damping', 'factor = 1d + (factor - 1d) * blend;', 'factor = factor;', 'native near corner not accelerated')
)
$botRejected = 0
try {
    foreach ($botScript in @('Verify-NativeContract.ps1', 'Verify-SainContract.ps1')) {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot $botScript) -SptPath $SptPath 2>&1 |
            Tee-Object (Join-Path $botEvidence ($botScript + '.log'))
        if ($LASTEXITCODE -ne 0) { throw "Installed contract failed: $botScript" }
    }
    $botSainOutput = @(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'SainChecks/Verify.ps1') 2>&1)
    $botSainOutput | Set-Content -LiteralPath (Join-Path $botEvidence 'sain-behavior.log')
    if ($LASTEXITCODE -ne 0 -or !($botSainOutput -join "`n").Contains('BOT_MOVEMENT_SAIN_BEHAVIOR_CHECKS_PASSED')) { throw ($botSainOutput -join "`n") }
    Write-Output $botSainOutput
    Positive 'production-before'
    foreach ($botFault in $botFaults) {
        if ($botSource.Split([string[]]@($botFault[1]), [StringSplitOptions]::None).Count -ne 2) {
            throw "Expected exactly one production mutation anchor: $($botFault[0])"
        }
        $botMutantPath = Join-Path $botEvidence ($botFault[0] + '.cs')
        [IO.File]::WriteAllText($botMutantPath, $botSource.Replace($botFault[1], $botFault[2]))
        $botResult = Run-Behavior $botFault[0] $botMutantPath
        if ($botResult.Exit -eq 0 -or !$botResult.Text.Contains('ASSERT: ' + $botFault[3])) {
            throw "Fault did not fail its named assertion: $($botFault[0])`n$($botResult.Text)"
        }
        $botRejected++
        Write-Output "Negative control rejected: $($botFault[0]) -> $($botFault[3])"
    }
} finally {
    Positive 'production-after'
    if ((Get-ArenaObservedFileHash -LiteralPath $botSourcePath -Algorithm SHA256).Hash -ne $botSourceHash) {
        throw 'Production source changed during checks; re-run the exact final source'
    }
    foreach ($botPath in $botInputs.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath $botPath -Algorithm SHA256).Hash -ne $botInputs[$botPath]) {
            throw "Production adapter or harness changed during checks: $botPath"
        }
    }
    foreach ($botPath in $botInstalled.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath $botPath -Algorithm SHA256).Hash -ne $botInstalled[$botPath]) {
            throw "Installed binary changed during source-linked checks: $botPath"
        }
    }
}
@{ SourceSHA256 = $botSourceHash; InputsSHA256 = $botInputs; RejectedFaults = $botRejected; InstalledUnchanged = $botInstalled;
    CheckedUtc = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $botEvidence 'evidence.json')
Write-Output "BOT MOVEMENT CHECKS PASSED; $botRejected compiled negative controls; evidence: $botEvidence"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
