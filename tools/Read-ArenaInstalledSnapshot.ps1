# Shared read-only guard for offline checks. Observe the two retired client
# layouts as well as the canonical layout, even when a file does not exist yet.
function Get-ArenaInstalledLayoutPaths([string]$SptPath) {
    $root = [IO.Path]::GetFullPath($SptPath)
    foreach ($folder in @('BepInEx/plugins', 'BepInEx/plugins/ArenaMode', 'BepInEx/plugins/maschine-ArenaMode')) {
        foreach ($name in @('maschine-ArenaMode.Client', 'maschine-ArenaMode.BigBrainCompat')) {
            foreach ($extension in @('.dll', '.pdb')) {
                Join-Path $root ($folder + '/' + $name + $extension)
            }
        }
    }
}

function Get-ArenaObservedFileHash {
    param([Parameter(Mandatory = $true)][string]$LiteralPath, [string]$Algorithm = 'SHA256')
    # Only these optional deployment copies may be absent. Required source,
    # native, server and external-mod inputs must still fail if they disappear.
    $path = [IO.Path]::GetFullPath($LiteralPath)
    if ($path -match '[\\/]BepInEx[\\/]plugins[\\/](?:(?:maschine-)?ArenaMode[\\/])?maschine-ArenaMode\.(?:Client|BigBrainCompat)\.(?:dll|pdb)$' -and
        !(Test-Path -LiteralPath $path)) {
        return [pscustomobject]@{ Path = $path; Algorithm = $Algorithm; Hash = '<absent>' }
    }
    Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $path -Algorithm $Algorithm -ErrorAction Stop
}

function Read-ArenaInstalledSnapshot([string]$SptPath) {
    $snapshot = @{}
    foreach ($path in Get-ArenaInstalledLayoutPaths $SptPath) {
        $snapshot[$path] = (Get-ArenaObservedFileHash -LiteralPath $path).Hash
    }
    return $snapshot
}

function Assert-ArenaInstalledSnapshotUnchanged([hashtable]$Before, [string]$SptPath) {
    $after = Read-ArenaInstalledSnapshot $SptPath
    foreach ($path in $Before.Keys) {
        if (!$after.ContainsKey($path) -or $after[$path] -ne $Before[$path]) {
            throw "Offline verification changed an Arena installation file: $path"
        }
    }
    if ($after.Count -ne $Before.Count) { throw 'Arena installation snapshot path set changed.' }
}
