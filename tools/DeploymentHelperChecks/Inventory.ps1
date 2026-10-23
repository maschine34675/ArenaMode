# Inventory of the installed ArenaMode payload, compared by DeploymentHelperChecks (Read-AnnouncerDeliveryInventory
# has no caller left since the helper's retirement and stays for a reader of a live install). Written for
# the AnnouncerDelivery helper (retired on 2026-10-23: it pinned ProductVersion 0.1.0+ and threw on every
# later build); the Announcer* function names are kept so the checks read as before.
. (Join-Path $PSScriptRoot '../Read-ArenaInstalledSnapshot.ps1')
. (Join-Path $PSScriptRoot '../Arena-NoticeLayout.ps1')

function Read-AnnouncerDeliveryInventory([string]$SptRoot) {
    $inventory = @{}
    $plugins = Join-Path $SptRoot 'BepInEx/plugins'
    if (Test-Path -LiteralPath $plugins) {
        foreach ($file in Get-ChildItem -LiteralPath $plugins -File -Recurse -Filter '*.dll') {
            $inventory[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
        }
    }
    foreach ($folder in @('BepInEx/plugins/ArenaMode', 'BepInEx/plugins/maschine-ArenaMode')) {
        $root = Join-Path $SptRoot $folder
        if (Test-Path -LiteralPath $root) {
            foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse) {
                $inventory[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
            }
        }
    }
    foreach ($path in Get-ArenaInstalledLayoutPaths $SptRoot) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { $inventory[$path] = (Get-FileHash -LiteralPath $path).Hash }
    }
    $server = Join-Path $SptRoot 'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll'
    $inventory[$server] = (Get-FileHash -LiteralPath $server).Hash
    return $inventory
}

function ConvertTo-AnnouncerCanonicalInventory([hashtable]$Inventory, [string]$SptRoot) {
    $canonical = [IO.Path]::GetFullPath((Join-Path $SptRoot 'BepInEx/plugins/maschine-ArenaMode'))
    $legacy = [IO.Path]::GetFullPath((Join-Path $SptRoot 'BepInEx/plugins/ArenaMode')) + [IO.Path]::DirectorySeparatorChar
    $flat = [IO.Path]::GetFullPath((Join-Path $SptRoot 'BepInEx/plugins'))
    $result = @{}
    $priorities = @{}
    $moves = Get-ArenaNoticeMoves
    $retired = Get-ArenaRetiredNotices
    foreach ($path in $Inventory.Keys) {
        $target = $path
        $priority = 8
        if ($path.StartsWith($legacy, [StringComparison]::OrdinalIgnoreCase)) {
            $target = Join-Path $canonical $path.Substring($legacy.Length)
            $priority = 7
        } elseif ([IO.Path]::GetDirectoryName($path) -eq $flat -and
            [IO.Path]::GetFileName($path) -match '^maschine-ArenaMode\.(Client|BigBrainCompat)\.(dll|pdb)$') {
            $target = Join-Path $canonical ([IO.Path]::GetFileName($path))
            $priority = 1
        }
        if ($target.StartsWith($canonical + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            $relative = [IO.Path]::GetRelativePath($canonical, $target).Replace('\', '/')
            if ($relative -in $retired) { continue }
            if ($moves.Contains($relative)) {
                $target = Join-Path $canonical $moves[$relative]
                # Most recent canonical docs beat the earlier root alias. All
                # final paths still beat every alias, independent of hash order.
                $priority = if ($priority -eq 8) { 4 } else { 2 }
                if ($relative.StartsWith('docs/', [StringComparison]::OrdinalIgnoreCase)) { $priority++ }
            }
        }
        # A canonical copy wins during migration; the shared deployer performs
        # its own conflict checks and archives redundant retired copies.
        if (!$result.ContainsKey($target) -or $priority -gt $priorities[$target]) {
            $result[$target] = $Inventory[$path]
            $priorities[$target] = $priority
        }
    }
    return $result
}

function Assert-AnnouncerInventory([hashtable]$Expected, [hashtable]$Actual) {
    if ($Expected.Count -ne $Actual.Count) { throw 'Installed inventory file count differs from the reviewed migration/payload.' }
    foreach ($path in $Expected.Keys) {
        if (!$Actual.ContainsKey($path) -or $Expected[$path] -ne $Actual[$path]) {
            throw "Unexpected installed file change: $path"
        }
    }
}
