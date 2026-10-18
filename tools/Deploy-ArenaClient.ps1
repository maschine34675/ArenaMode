#Requires -Version 7.0
# Deploy only an owned Arena DLL; migrate old layouts without deleting user data.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$SptPath,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Arena-NoticeLayout.ps1')
$noticeMoves = Get-ArenaNoticeMoves
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).ProviderPath
$SptPath = (Resolve-Path -LiteralPath $SptPath).ProviderPath
$name = [IO.Path]::GetFileName($AssemblyPath)
$ownedDlls = @('maschine-ArenaMode.Client.dll', 'maschine-ArenaMode.BigBrainCompat.dll')
if ($name -cnotin $ownedDlls -or [Reflection.AssemblyName]::GetAssemblyName($AssemblyPath).Name -cne [IO.Path]::GetFileNameWithoutExtension($name)) {
    throw 'Only a matching ArenaMode client or companion assembly may be deployed.'
}
$plugins = [IO.Path]::GetFullPath((Join-Path $SptPath 'BepInEx/plugins'))
$legacy = Join-Path $plugins 'ArenaMode'
$canonical = Join-Path $plugins 'maschine-ArenaMode'
$backup = Join-Path $SptPath ('BepInEx/backups/maschine-ArenaMode-layout/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))

function Assert-SafePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($SptPath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Deployment path leaves the selected installation: $full"
    }
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Deployment will not follow a link/junction: $cursor"
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
function Get-SafeFiles([string]$Root) {
    Assert-SafePath $Root
    if (-not (Test-Path -LiteralPath $Root)) { return }
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force) {
        Assert-SafePath $item.FullName
        if ($item.PSIsContainer) { Get-SafeFiles $item.FullName } else { $item }
    }
}
function Assert-Unlocked([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $stream = $null
    try { $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw "Arena deployment stopped before migration: file is locked or not writable: $Path" }
    finally { if ($null -ne $stream) { $stream.Dispose() } }
}
function Archive-File([string]$Path, [string]$Relative) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $target = Join-Path $backup $Relative
    Assert-SafePath $Path
    Assert-SafePath $target
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Move-Item -LiteralPath $Path -Destination $target
}

Assert-SafePath $plugins
Assert-SafePath $backup
$oldFiles = @(Get-SafeFiles $legacy)
$newFiles = @(Get-SafeFiles $canonical)
$flatFiles = @($ownedDlls | ForEach-Object { Join-Path $plugins $_; Join-Path $plugins ([IO.Path]::ChangeExtension($_, '.pdb')) })
$targetDll = Join-Path $canonical $name
$targetPdb = [IO.Path]::ChangeExtension($targetDll, '.pdb')
$sourcePdb = [IO.Path]::ChangeExtension($AssemblyPath, '.pdb')
if ($Configuration -eq 'Debug' -and -not (Test-Path -LiteralPath $sourcePdb)) { throw "Debug symbols are missing: $sourcePdb" }

# Canonical user files are authoritative. Do not silently choose between two
# different custom drafts/layouts; installed source-owned assets can be refreshed.
$ownedAssets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$repo = Split-Path $PSScriptRoot
foreach ($spec in @(@{Source='ArenaMode.Client/Assets/ArenaMode'; Prefix=''}, @{Source='ArenaMode.Client/Assets/MapPreviews'; Prefix='MapPreviews/'})) {
    $base = Join-Path $repo $spec.Source
    foreach ($file in Get-ChildItem -LiteralPath $base -File -Recurse) {
        [void]$ownedAssets.Add($spec.Prefix + [IO.Path]::GetRelativePath($base, $file.FullName).Replace('\', '/'))
    }
}
foreach ($notice in @('NOTICE.md', 'SOURCES.json', 'MIT-LICENSE.txt', 'CC-BY-3.0.txt')) { [void]$ownedAssets.Add('Ui/' + $notice) }
foreach ($entry in $noticeMoves.GetEnumerator()) {
    [void]$ownedAssets.Add($entry.Key)
    [void]$ownedAssets.Add($entry.Value)
    foreach ($root in @($canonical, $legacy)) {
        $oldNotice = Join-Path $root $entry.Key
        if (Test-Path -LiteralPath $oldNotice -PathType Container) { throw "Notice source is a directory: $oldNotice" }
        $destination = Join-Path $root $entry.Value
        Assert-SafePath $destination
        if (Test-Path -LiteralPath $destination -PathType Container) { throw "Notice destination is a directory: $destination" }
        # Check legacy parents too: they may be merged into the canonical tree.
        $parent = Split-Path $destination
        while ($parent -ne $root) {
            if (Test-Path -LiteralPath $parent -PathType Leaf) { throw "Notice parent is a file: $parent" }
            $parent = Split-Path $parent
        }
    }
}
foreach ($root in @($canonical, $legacy)) {
    foreach ($relative in Get-ArenaRetiredNotices) {
        [void]$ownedAssets.Add($relative)
        $source = Join-Path $root $relative
        if (Test-Path -LiteralPath $source -PathType Container) { throw "Retired notice is a directory: $source" }
    }
}
foreach ($file in $oldFiles) {
    $relative = [IO.Path]::GetRelativePath($legacy, $file.FullName).Replace('\', '/')
    $other = Join-Path $canonical $relative
    if (Test-Path -LiteralPath $other) {
        $owned = $ownedAssets.Contains($relative) -or $relative -cin $ownedDlls -or $relative -cin @($ownedDlls | ForEach-Object { [IO.Path]::ChangeExtension($_, '.pdb') })
        if (-not $owned -and (Get-FileHash -LiteralPath $file.FullName).Hash -cne (Get-FileHash -LiteralPath $other).Hash) {
            throw "Different user files exist in both Arena folders; resolve before deployment: $relative"
        }
    }
}
foreach ($file in @($oldFiles | ForEach-Object FullName) + @($newFiles | ForEach-Object FullName) + $flatFiles) {
    if ($file) { Assert-SafePath $file; Assert-Unlocked $file }
}

New-Item -ItemType Directory -Path $plugins -Force | Out-Null
if (Test-Path -LiteralPath $legacy) {
    # Exact checked directories only. The old folder is moved, never recursively
    # deleted, and conflicting owned copies remain recoverable outside plugins.
    if (-not (Test-Path -LiteralPath $canonical)) {
        Move-Item -LiteralPath $legacy -Destination $canonical
    } else {
        foreach ($file in $oldFiles) {
            $target = Join-Path $canonical ([IO.Path]::GetRelativePath($legacy, $file.FullName))
            if (-not (Test-Path -LiteralPath $target)) {
                New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target
            }
        }
        $savedFolder = Join-Path $backup 'ArenaMode'
        Assert-SafePath $savedFolder
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        Move-Item -LiteralPath $legacy -Destination $savedFolder
    }
}
New-Item -ItemType Directory -Path $canonical -Force | Out-Null
foreach ($file in $flatFiles) {
    if (-not (Test-Path -LiteralPath $file)) { continue }
    $target = Join-Path $canonical ([IO.Path]::GetFileName($file))
    if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $file -Destination $target }
}

# Write a fully copied file before replacing the installed DLL. Old flat copies
# remain until this succeeds; no caller can silently deploy a missing source.
$sameDll = (Test-Path -LiteralPath $targetDll) -and (Get-FileHash -LiteralPath $targetDll).Hash -ceq (Get-FileHash -LiteralPath $AssemblyPath).Hash
if (-not $sameDll) {
$pending = Join-Path $canonical ($name + '.' + [guid]::NewGuid().ToString('N') + '.pending')
try {
    Copy-Item -LiteralPath $AssemblyPath -Destination $pending
    if ((Get-FileHash -LiteralPath $pending).Hash -cne (Get-FileHash -LiteralPath $AssemblyPath).Hash) { throw 'The staged DLL copy failed its hash check.' }
    if (Test-Path -LiteralPath $targetDll) {
        $savedDll = Join-Path $backup ('replaced/' + $name)
        New-Item -ItemType Directory -Path (Split-Path $savedDll) -Force | Out-Null
        Copy-Item -LiteralPath $targetDll -Destination $savedDll
    }
    [IO.File]::Move($pending, $targetDll, $true)
} finally {
    if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
}
}
if ($Configuration -eq 'Debug') { Copy-Item -LiteralPath $sourcePdb -Destination $targetPdb -Force }
else { Archive-File $targetPdb ('symbols/' + [IO.Path]::GetFileName($targetPdb)) }
foreach ($file in $flatFiles) { Archive-File $file ('flat/' + [IO.Path]::GetFileName($file)) }
foreach ($entry in $noticeMoves.GetEnumerator()) {
    $old = Join-Path $canonical $entry.Key
    if (-not (Test-Path -LiteralPath $old)) { continue }
    $new = Join-Path $canonical $entry.Value
    if (-not (Test-Path -LiteralPath $new)) {
        New-Item -ItemType Directory -Path (Split-Path $new) -Force | Out-Null
        Move-Item -LiteralPath $old -Destination $new
    } else { Archive-File $old ('notices/' + $entry.Key) }
}
foreach ($relative in Get-ArenaRetiredNotices) { Archive-File (Join-Path $canonical $relative) ('retired/' + $relative) }
foreach ($relative in @('MapPreviews/Source', 'MapPreviews', 'Ui', 'hitmarkers', 'pickups',
    'docs/licenses/hitmarkers', 'docs/licenses/pickups', 'sounds/reserve')) {
    $directory = Join-Path $canonical $relative
    Assert-SafePath $directory
    if ((Test-Path -LiteralPath $directory -PathType Container) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory
    }
}
Write-Output "ARENA_CLIENT_DEPLOYED $targetDll"
if (Test-Path -LiteralPath $backup) { Write-Output "ARENA_LAYOUT_BACKUP $backup" }
