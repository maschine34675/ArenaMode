param([string]$SptPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptPath
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {$ErrorActionPreference = 'Stop'
$assistRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assistStage = Join-Path $assistRepo ('artifacts/assist-regressions/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($assistStage)
$assistSpt = [IO.Path]::GetFullPath((Join-Path $assistRepo '../..'))
$assistInstalled = @{}
foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $path = Join-Path $assistSpt $relative
    if (Test-Path -LiteralPath $path) { $assistInstalled[$path] = (Get-ArenaObservedFileHash -LiteralPath $path).Hash }
}
Push-Location $assistRepo
try {
    # These are source-linked console projects. None references/deploys the native client.
    foreach ($project in @('AnnouncerChecks', 'AnnouncerCombatChecks', 'AnnouncementProgressChecks',
        'AnnouncementIntegrationChecks', 'ShotAwardHistoryChecks', 'PitFireTeamChecks',
        'TeamArenaExpansionChecks', 'TeamGiveUpChecks')) {
        & dotnet run --project ('tools/' + $project) -c Release 2>&1 |
            Tee-Object (Join-Path $assistStage ($project + '.log'))
        if ($LASTEXITCODE -ne 0) { throw "Assist regression failed: $project" }
    }
    foreach ($script in @('tools/RoleLabelChecks/Verify.ps1', 'tools/TdmMatchChecks/Run-Checks.ps1',
        'tools/TacticalPolishChecks/Verify.ps1')) {
        & pwsh -NoProfile -File $script 2>&1 |
            Tee-Object (Join-Path $assistStage ((Split-Path (Split-Path $script) -Leaf) + '.log'))
        if ($LASTEXITCODE -ne 0) { throw "Assist regression failed: $script" }
    }
    # The server project deploys only to an existing SPTUserMods directory. Point
    # both its root and exact destination to unique absent paths, never live SPT.
    $assistNoRoot = Join-Path $assistStage 'no-spt-user-root'
    $assistNoMods = Join-Path $assistStage 'no-spt-mods'
    & dotnet test ArenaMode.Tests -c Release "-p:SPTUserPath=$assistNoRoot" "-p:SPTUserMods=$assistNoMods" `
        --logger 'trx;LogFileName=units.trx' --results-directory $assistStage 2>&1 |
        Tee-Object (Join-Path $assistStage 'units.log')
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $assistNoRoot) -or (Test-Path -LiteralPath $assistNoMods)) {
        throw 'Assist unit suite or no-deployment sentinel failed'
    }
} finally {
    Pop-Location
    foreach ($path in $assistInstalled.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath $path).Hash -ne $assistInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
Write-Output "ASSIST REGRESSIONS PASSED; installed binaries unchanged; evidence: $assistStage"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
