#Requires -Version 7.0
# Deploy one owned Arena session file (docs/ARENA-SESSION-20261021.md, section 9):
#   maschine-ArenaMode.Gate.dll      -> <SPT>\BepInEx\patchers\maschine-ArenaMode\   (preloader patcher)
#   maschine-ArenaMode.Session.exe   -> <SPT>\BepInEx\plugins\maschine-ArenaMode\    (helper, next to the client DLL)
# The shipped default whitelist (arena-whitelist.json) is not deployed here: Write-ArenaAssets.ps1 owns it,
# like every other file of the plugin folder, so build, deploy and the release ZIP stay identical.
#
# Both files are new in every installation, so unlike Deploy-ArenaClient.ps1 there is no layout to migrate.
# The same safety rules apply: only the two named files with a matching assembly identity, nothing outside
# the selected installation, no link or junction anywhere on the path, a locked target stops the deployment
# before anything is written, and the new bytes are copied and hash-checked before they replace the old file.
# MSBuild reaches this script through the targets DeployArenaGate and DeployArenaSessionHelper; both are
# emptied by tools\AirPitChecks\NoDeploy.targets like the other deployment targets.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$SptPath,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).ProviderPath
# GetFullPath as well: it expands 8.3 short names, which Resolve-Path keeps, and the containment check
# below compares this root with GetFullPath results.
$SptPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SptPath).ProviderPath)
$name = [IO.Path]::GetFileName($AssemblyPath)
# File name -> installation folder (relative to the SPT root). The gate lives where BepInEx discovers
# preloader patchers, the helper where the client looks for it (ArenaSessionContract.PluginFolderName).
$owned = @{
    'maschine-ArenaMode.Gate.dll'    = 'BepInEx/patchers/maschine-ArenaMode'
    'maschine-ArenaMode.Session.exe' = 'BepInEx/plugins/maschine-ArenaMode'
}
if (-not $owned.ContainsKey($name) -or
    [Reflection.AssemblyName]::GetAssemblyName($AssemblyPath).Name -cne [IO.Path]::GetFileNameWithoutExtension($name)) {
    throw 'Only a matching ArenaMode gate assembly or session helper may be deployed.'
}
$folder = [IO.Path]::GetFullPath((Join-Path $SptPath $owned[$name]))
$target = Join-Path $folder $name
$targetPdb = [IO.Path]::ChangeExtension($target, '.pdb')
$sourcePdb = [IO.Path]::ChangeExtension($AssemblyPath, '.pdb')
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
function Assert-Unlocked([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $stream = $null
    try { $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch { throw "Arena session deployment stopped: file is locked or not writable (close the game and any running session helper): $Path" }
    finally { if ($null -ne $stream) { $stream.Dispose() } }
}

if ($Configuration -eq 'Debug' -and -not (Test-Path -LiteralPath $sourcePdb)) { throw "Debug symbols are missing: $sourcePdb" }
foreach ($path in @($folder, $target, $targetPdb, $backup)) { Assert-SafePath $path }
foreach ($path in @($target, $targetPdb)) { Assert-Unlocked $path }
if (Test-Path -LiteralPath $folder -PathType Leaf) { throw "Deployment folder is a file: $folder" }
if (Test-Path -LiteralPath $target -PathType Container) { throw "Deployment target is a directory: $target" }

New-Item -ItemType Directory -Path $folder -Force | Out-Null
$sourceHash = (Get-FileHash -LiteralPath $AssemblyPath).Hash
$same = (Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -ceq $sourceHash
if (-not $same) {
    # A fully copied, hash-checked file replaces the old one in a single move.
    $pending = Join-Path $folder ($name + '.' + [guid]::NewGuid().ToString('N') + '.pending')
    try {
        Copy-Item -LiteralPath $AssemblyPath -Destination $pending
        if ((Get-FileHash -LiteralPath $pending).Hash -cne $sourceHash) { throw 'The staged copy failed its hash check.' }
        [IO.File]::Move($pending, $target, $true)
    } finally {
        if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
    }
}
if ($Configuration -eq 'Debug') {
    Copy-Item -LiteralPath $sourcePdb -Destination $targetPdb -Force
} elseif (Test-Path -LiteralPath $targetPdb) {
    # A stale Debug symbol file next to a Release binary would mislead a stack trace: archive it outside the
    # plugin and patcher folders instead of deleting it.
    $archived = Join-Path $backup ('symbols/' + [IO.Path]::GetFileName($targetPdb))
    Assert-SafePath $archived
    New-Item -ItemType Directory -Path (Split-Path $archived) -Force | Out-Null
    Move-Item -LiteralPath $targetPdb -Destination $archived
}
Write-Output "ARENA_SESSION_FILE_DEPLOYED $target"
if (Test-Path -LiteralPath $backup) { Write-Output "ARENA_LAYOUT_BACKUP $backup" }
