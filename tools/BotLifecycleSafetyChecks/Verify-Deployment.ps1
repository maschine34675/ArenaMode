param([string]$SptPath = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function ExternalHashes {
    $hashes = @{}
    foreach ($relative in @('BepInEx/plugins/SAIN', 'BepInEx/plugins/pitFireTeam', 'SPT_Runtime/user/mods/Solarint-SAIN-ServerMod')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $SptPath $relative) -File -Recurse) {
            $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    foreach ($relative in @('BepInEx/plugins/DrakiaXYZ-BigBrain.dll', 'BepInEx/config/me.sol.sain.cfg')) {
        $path = Join-Path $SptPath $relative
        if (Test-Path -LiteralPath $path) { $hashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    return $hashes
}
$before = ExternalHashes
foreach ($project in @('tools/CorpseCleanupChecks', 'tools/PitFireTeamRoundLifecycleChecks', 'tools/SainPersonalityChecks')) {
    & dotnet run --project (Join-Path $repo $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Existing regression failed: $project" }
}
# This inspected existing oracle runs spawn/PFT/expansion checks and then the
# FINAL client Release build, validates live/client/Core hashes and optional SAIN.
& pwsh -NoProfile -File (Join-Path $repo 'tools/SainPersonalityChecks/Verify-Deployment.ps1') -SptPath $SptPath
if ($LASTEXITCODE -ne 0) { throw 'Existing Release/deployment checks failed' }
$after = ExternalHashes
if ($before.Count -ne $after.Count) { throw 'External mod files changed during validation' }
foreach ($file in $before.Keys) { if ($before[$file] -ne $after[$file]) { throw "External mod file changed: $file" } }
Add-Type -Path (Join-Path $SptPath 'BepInEx/core/Mono.Cecil.dll')
$client = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptPath 'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll'))
try {
    if (@($client.MainModule.AssemblyReferences | Where-Object { $_.Name -match 'SAIN|BigBrain|pitFireTeam' }).Count -ne 0) { throw 'Client acquired hard external bot dependency' }
    $plugin = $client.MainModule.GetType('ArenaMode.Plugin')
    foreach ($pair in @(@('Awake','Enable'), @('OnDestroy','Shutdown'))) {
        $method = $plugin.Methods | Where-Object Name -eq $pair[0]
        $calls = ($method.Body.Instructions.Operand | ForEach-Object { if ($_ -ne $null) { $_.ToString() } }) -join "`n"
        foreach ($type in @('ArenaBotLifecycleSafety','ArenaSainActionSafety')) {
            if (!$calls.Contains($type + '::' + $pair[1] + '(')) { throw "Safety lifecycle wiring missing: $type $($pair[1])" }
        }
    }
    Write-Output "ARENA_BOT_LIFECYCLE_DEPLOYMENT_PASSED ($($before.Count) unchanged external files; Release/live hashes match)"
}
finally { $client.Dispose() }
