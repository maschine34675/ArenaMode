param([string]$SptPath = 'D:\SPT41')
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
$arenaSnapshotRoot = $SptPath
$arenaLayoutSnapshot = Read-ArenaInstalledSnapshot $arenaSnapshotRoot
try {
$ErrorActionPreference = 'Stop'
$botRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$botEvidence = Join-Path $botRepo ('artifacts/bot-movement-regressions/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($botEvidence)
$botInstalled = @{}
foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll', 'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $botPath = Join-Path $SptPath $relative
    $botInstalled[$botPath] = (Get-ArenaObservedFileHash -LiteralPath $botPath -Algorithm SHA256).Hash
}
Push-Location $botRepo
try {
    # Source-linked console executables only; no native client/server project or deployment target.
    foreach ($project in @('ArenaAirControlChecks', 'ArenaHandlingChecks', 'DoubleJumpChecks',
        'ArenaAirFireChecks', 'TdmSearchTempoChecks', 'TeamArenaPursuitRuntimeChecks')) {
        & dotnet run --project ('tools/' + $project) -c Release 2>&1 |
            Tee-Object (Join-Path $botEvidence ($project + '.log'))
        if ($LASTEXITCODE -ne 0) { throw "Bot movement regression failed: $project" }
    }
} finally {
    Pop-Location
    foreach ($botPath in $botInstalled.Keys) {
        if ((Get-ArenaObservedFileHash -LiteralPath $botPath -Algorithm SHA256).Hash -ne $botInstalled[$botPath]) {
            throw "Installed binary changed during source-linked checks: $botPath"
        }
    }
    $botInstalled | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $botEvidence 'installed-before-after.json')
}
Write-Output "BOT MOVEMENT REGRESSIONS PASSED; installed binaries unchanged; evidence: $botEvidence"

} finally {
    Assert-ArenaInstalledSnapshotUnchanged $arenaLayoutSnapshot $arenaSnapshotRoot
}
