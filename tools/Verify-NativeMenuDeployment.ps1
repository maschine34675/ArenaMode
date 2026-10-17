$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$installation = [System.IO.Path]::GetFullPath((Join-Path $repo '..\..'))
function Read-Sha256([string] $path) {
    $stream = [System.IO.File]::OpenRead($path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$pairs = @(
    @('ArenaMode.Client\bin\Release\maschine-ArenaMode.Client.dll', 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Client.dll'),
    @('build\server\Release\maschine-ArenaMode.Server\maschine-ArenaMode.Server.dll', 'SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll')
)
foreach ($pair in $pairs) {
    $built = Join-Path $repo $pair[0]
    $deployed = Join-Path $installation $pair[1]
    $expected = Read-Sha256 $built
    $actual = Read-Sha256 $deployed
    if ($expected -ne $actual) { throw "Deployment mismatch: $deployed" }
    Write-Output ((Split-Path -Leaf $deployed) + ' SHA256=' + $actual)
}
Write-Output 'ARENA_DEPLOYMENT_VERIFIED'
