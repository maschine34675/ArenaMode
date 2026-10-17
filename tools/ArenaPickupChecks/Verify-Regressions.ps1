param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptPath
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$pickupRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pickupEvidence = Join-Path $pickupRepo ('artifacts/pickup-regressions/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($pickupEvidence)
$pickupInstalled = @{}
foreach ($path in @('BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll','BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $pickupInstalled[$path] = (Get-ArenaObservedFileHash -LiteralPath (Join-Path $SptPath $path)).Hash
}
$pickupInputs = @{}
Push-Location $pickupRepo
try {
    # Bind checked source, shared build configuration and all harness code. rg obeys
    # ignored bin/obj/artifacts directories; no generated evidence is an input.
    $inputPaths = @(& rg --files ArenaMode.Client ArenaMode.Shared ArenaMode.Server ArenaMode.Tests tools -g '*.cs' -g '*.csproj' -g '*.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source inputs.' }
    $inputPaths += 'Shared.props'
    foreach ($path in $inputPaths) { $pickupInputs[$path] = (Get-ArenaObservedFileHash -LiteralPath $path).Hash }
    # The selected console projects link production source, never native mod projects.
    $runs = @(
        @('AssistChecks','ASSIST_PRODUCTION_PASSED:'),
        @('AssistCaptureChecks','ASSIST_CAPTURE_PRODUCTION_PASSED:'),
        @('DeathmatchChecks','DEATHMATCH_RULES_RUNTIME_PASSED:'),
        @('BotMovementChecks','BOT_MOVEMENT_BEHAVIOR_PASSED'),
        @('ArenaAirControlChecks','ARENA_AIR_CONTROL_CHECKS_PASSED'),
        @('NativeArenaHudChecks','NATIVE_ARENA_HUD_PRODUCTION_PASSED'),
        @('RaidInfoPanelChecks','ARENA_INFO_PANEL_PRODUCTION_PASSED')
    )
    foreach ($run in $runs) {
        $text = & dotnet run --project ('tools/' + $run[0]) -c Release 2>&1 | Out-String
        $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $pickupEvidence ($run[0] + '.log')), $text)
        if ($code -ne 0 -or !$text.Contains($run[1])) { throw "Pickup regression failed: $($run[0]): $text" }
        Write-Output $text.Trim()
    }
    # This runner extracts the current native respawn guards, then source-links
    # them with TDM policy/exit tests and compiled negative controls. No deployment.
    $tdm = & pwsh -NoProfile -File tools/TdmMatchChecks/Run-Checks.ps1 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $pickupEvidence 'TdmMatchChecks.log'), $tdm)
    if ($code -ne 0 -or !$tdm.Contains('TDM_MATCH_CHECKS_PASSED')) { throw $tdm }
    Write-Output $tdm.Trim()
    # The unit suite references server code. Both deployment destination properties
    # are global overrides to unique absent paths; its Exists gate cannot deploy.
    $noRoot = Join-Path $pickupEvidence 'absent-spt-user-root'
    $noMods = Join-Path $pickupEvidence 'absent-spt-user-mods'
    if ((Test-Path -LiteralPath $noRoot) -or (Test-Path -LiteralPath $noMods)) { throw 'No-deploy sentinel must start absent.' }
    $units = & dotnet test ArenaMode.Tests -c Release "-p:SPTUserPath=$noRoot" "-p:SPTUserMods=$noMods" `
        --logger 'trx;LogFileName=units.trx' --results-directory $pickupEvidence 2>&1 | Out-String
    $code = $LASTEXITCODE; [IO.File]::WriteAllText((Join-Path $pickupEvidence 'units.log'), $units)
    if ($code -ne 0 -or (Test-Path -LiteralPath $noRoot) -or (Test-Path -LiteralPath $noMods)) { throw "Unit or no-deploy sentinel failed: $units" }
    if (!(Test-Path -LiteralPath (Join-Path $pickupEvidence 'units.trx'))) { throw 'Unit results were not emitted.' }
    Write-Output $units.Trim()
    foreach ($path in $pickupInputs.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath $path).Hash -ne $pickupInputs[$path]) { throw "Bound regression input changed: $path" }
    }
} finally {
    Pop-Location
    foreach ($path in $pickupInstalled.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath (Join-Path $SptPath $path)).Hash -ne $pickupInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
[ordered]@{ sourceHashes=$pickupInputs; installedHashes=$pickupInstalled; sourcesUnchanged=$true; installedUnchanged=$true;
    completedUtc=[DateTime]::UtcNow.ToString('o'); evidenceDirectory=$pickupEvidence;
    limitation='Targeted source-linked assist, combat, mode, movement and HUD regressions plus server/data units. Native client/game is not executed or deployed.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $pickupEvidence 'manifest.json')
Write-Output "ARENA PICKUP REGRESSIONS PASSED; installed binaries unchanged; evidence: $pickupEvidence"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
