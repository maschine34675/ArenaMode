$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# Hash all installed Arena audio and configs around the staged build. Only a
# later explicit DLL delivery is allowed to alter the installation.
$install = [IO.Path]::GetFullPath((Join-Path $repo '../..'))
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
function Read-DeathmatchProtectedFiles {
    $protected = Read-ArenaInstalledSnapshot $install
    foreach ($folder in @('BepInEx/plugins/ArenaMode', 'BepInEx/plugins/maschine-ArenaMode', 'SPT_Runtime/user/mods/ArenaMode/config')) {
        $path = Join-Path $install $folder
        if (Test-Path -LiteralPath $path) {
            Get-ChildItem -LiteralPath $path -File -Recurse | ForEach-Object { $protected[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
        }
    }
    Get-ChildItem -LiteralPath (Join-Path $install 'BepInEx/config') -Filter '*ArenaMode*' -File | ForEach-Object { $protected[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
    return $protected
}
$protected = Read-DeathmatchProtectedFiles
$protected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repo 'artifacts/deathmatch-protected-files.json')
try {
    & (Join-Path $repo 'tools/DurationDifficultyChecks/Build-Staged.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Staged build failed' }
} finally {
    $after = Read-DeathmatchProtectedFiles
    if ($after.Count -ne $protected.Count) { throw 'Staged build changed protected inventory.' }
    foreach ($path in $protected.Keys) {
        if (!$after.ContainsKey($path) -or $after[$path] -ne $protected[$path]) { throw "Staged build changed protected file: $path" }
    }
}
Write-Output 'DEATHMATCH_STAGED_BUILD_PASSED'
