param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Snapshot,
    [switch]$Compare
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Source).TrimEnd([char[]]'\/')
$files = @{}
foreach ($relative in @('BepInEx/config', 'BepInEx/plugins', 'BepInEx/patchers',
    'SPT_Runtime/user/profiles', 'SPT_Runtime/user/Launcher', 'SPT_Runtime/user/mods',
    'SPT_Runtime/SPT_Data/configs', 'SPT_Runtime/user/sptsettings', 'NLog')) {
    $directory = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $directory)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
        if ($file.Extension -notin @('.dll','.exe','.cfg','.json','.spt-bak','.ini','.config')) { continue }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked source: $($file.FullName)" }
        $name = $file.FullName.Substring($root.Length + 1)
        $files[$name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}
foreach ($relative in @('doorstop_config.ini','SPT_Runtime/sptLogger.json')) {
    $path = Join-Path $root $relative
    if (Test-Path -LiteralPath $path) { $files[$relative.Replace('/','\')] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
if ($files.Count -eq 0) { throw 'Empty source comparison is not evidence.' }
if ($Compare) {
    $before = Get-Content -LiteralPath $Snapshot -Raw | ConvertFrom-Json
    if ($before.Source -ne $root) { throw 'Snapshot belongs to another installation.' }
    $expected = @{}
    foreach ($entry in $before.Files.PSObject.Properties) { $expected[$entry.Name] = $entry.Value }
    $added = @($files.Keys | Where-Object { -not $expected.ContainsKey($_) })
    $removed = @($expected.Keys | Where-Object { -not $files.ContainsKey($_) })
    $changed = @($files.Keys | Where-Object { $expected.ContainsKey($_) -and $files[$_] -ne $expected[$_] })
    if ($added.Count -or $removed.Count -or $changed.Count) {
        throw "Source differs: added=$($added.Count), removed=$($removed.Count), changed=$($changed.Count): $($added + $removed + $changed -join ', ')"
    }
    "ARENA_SOURCE_UNCHANGED ($($files.Count) files)"
} else {
    if (Test-Path -LiteralPath $Snapshot) { throw 'Refusing to replace original baseline.' }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Snapshot))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    @{ Source=$root; Files=$files; CapturedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Snapshot -Encoding utf8
    "ARENA_SOURCE_BASELINE ($($files.Count) files)"
}
