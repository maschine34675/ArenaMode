param([string]$SptPath = 'D:\SPT41')
function Get-ArenaExternalHashes {
    $hashes = @{}
    foreach ($relative in @(
        'BepInEx/plugins/SAIN', 'BepInEx/plugins/pitFireTeam',
        'BepInEx/plugins/acidphantasm-brightlasers',
        'SPT_Runtime/user/mods/Solarint-SAIN-ServerMod',
        'SPT_Runtime/user/mods/acidphantasm-progressivebotsystem',
        'SPT_Runtime/user/mods/acidphantasm-brightlasers')) {
        $folder = Join-Path $SptPath $relative
        if (!(Test-Path -LiteralPath $folder)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $folder -File -Recurse) {
            $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    foreach ($relative in @('BepInEx/plugins/DrakiaXYZ-BigBrain.dll',
        'BepInEx/config/me.sol.sain.cfg', 'BepInEx/config/com.acidphantasm.brightlasers.cfg',
        'SPT_Runtime/user/mods/ArenaMode/config/kits.json', 'SPT_Runtime/user/mods/ArenaMode/config/ladder.json')) {
        $path = Join-Path $SptPath $relative
        if (Test-Path -LiteralPath $path) { $hashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    return $hashes
}
