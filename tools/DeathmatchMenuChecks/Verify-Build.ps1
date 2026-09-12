$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# Hash all installed Arena audio and configs around the staged build. Only a
# later explicit DLL delivery is allowed to alter the installation.
$install = [IO.Path]::GetFullPath((Join-Path $repo '../..'))
$protected = @{}
foreach ($folder in @('BepInEx/plugins/ArenaMode', 'SPT_Runtime/user/mods/ArenaMode/config')) {
    Get-ChildItem -LiteralPath (Join-Path $install $folder) -File -Recurse | ForEach-Object { $protected[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
}
Get-ChildItem -LiteralPath (Join-Path $install 'BepInEx/config') -Filter '*ArenaMode*' -File | ForEach-Object { $protected[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
$protected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repo 'artifacts/deathmatch-protected-files.json')
& (Join-Path $repo 'tools/DurationDifficultyChecks/Build-Staged.ps1')
foreach ($path in $protected.Keys) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $protected[$path]) { throw "Staged build changed protected file: $path" }
}
if ($LASTEXITCODE -ne 0) { throw 'Staged build failed' }
Write-Output 'DEATHMATCH_STAGED_BUILD_PASSED'
