$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$installation = [System.IO.Path]::GetFullPath((Join-Path $repo '..\..'))
$reference = [System.IO.Path]::GetFullPath((Join-Path $repo '..\_reference\SPT-PitFireTeam-main'))

function Get-RequiredHash([string] $path) {
    if (-not [System.IO.File]::Exists($path)) {
        throw "Required file is missing: $path"
    }

    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

function Get-RelativePath([string] $basePath, [string] $path) {
    # Windows PowerShell 5.1 runs on .NET Framework, which has no
    # Path.GetRelativePath. URI relative paths work on both 5.1 and pwsh.
    $baseFullPath = [System.IO.Path]::GetFullPath($basePath).TrimEnd('\', '/') + '\'
    $pathFullPath = [System.IO.Path]::GetFullPath($path)
    $baseUri = [System.Uri]::new($baseFullPath)
    $pathUri = [System.Uri]::new($pathFullPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Assert-NoPitFireTeamBinary([string] $root) {
    if (-not [System.IO.Directory]::Exists($root)) {
        throw "Required verification directory is missing: $root"
    }

    $foreign = Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.Name -match '^(?:pitFireTeam(?:\.Server)?|SPT-PitFireTeam).*\.dll$'
    }
    if ($foreign) {
        $paths = ($foreign | ForEach-Object FullName) -join [Environment]::NewLine
        throw "ArenaMode output/deployment contains a PitFireTeam binary:$([Environment]::NewLine)$paths"
    }
}

function Assert-NoReferencePayload([string] $root, [hashtable] $referenceHashes) {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($referenceHashes.ContainsKey($hash)) {
            throw "ArenaMode output/deployment file '$($file.FullName)' is byte-identical to PitFireTeam reference payload '$($referenceHashes[$hash])'."
        }
    }
}

function Assert-OwnedFileClean([string] $path, [hashtable] $referenceHashes) {
    if (-not [System.IO.File]::Exists($path)) {
        throw "Required ArenaMode deployment file is missing: $path"
    }

    if ([System.IO.Path]::GetFileName($path) -match '^(?:pitFireTeam(?:\.Server)?|SPT-PitFireTeam).*\.dll$') {
        throw "ArenaMode deployment unexpectedly owns a PitFireTeam binary: $path"
    }

    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($referenceHashes.ContainsKey($hash)) {
        throw "ArenaMode deployment file '$path' is byte-identical to PitFireTeam reference payload '$($referenceHashes[$hash])'."
    }
}

function Get-ReferencePayloadHashes {
    if (-not [System.IO.Directory]::Exists($reference)) {
        throw "PitFireTeam reference tree is missing: $reference"
    }

    $extensions = @('.cs', '.csproj', '.md', '.ogg', '.png')
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $reference -Recurse -File | Where-Object { $extensions -contains $_.Extension }) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if (-not $hashes.ContainsKey($hash)) {
            $hashes[$hash] = Get-RelativePath $reference $file.FullName
        }
    }
    return $hashes
}

$pairs = @(
    @('client', 'ArenaMode.Client\bin\Release\maschine-ArenaMode.Client.dll', 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Client.dll'),
    @('bigbrain-compat', 'ArenaMode.BigBrainCompat\bin\Release\maschine-ArenaMode.BigBrainCompat.dll', 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.BigBrainCompat.dll'),
    @('server', 'build\server\Release\maschine-ArenaMode.Server\maschine-ArenaMode.Server.dll', 'SPT_Runtime\user\mods\ArenaMode\maschine-ArenaMode.Server.dll')
)

foreach ($pair in $pairs) {
    $label = $pair[0]
    $built = [System.IO.Path]::GetFullPath((Join-Path $repo $pair[1]))
    $deployed = [System.IO.Path]::GetFullPath((Join-Path $installation $pair[2]))
    $buildHash = Get-RequiredHash $built
    $deployHash = Get-RequiredHash $deployed
    Write-Output ("$label BUILD_SHA256=$buildHash")
    Write-Output ("$label DEPLOYED_SHA256=$deployHash")
    if ($buildHash -ne $deployHash) {
        throw "Deployment mismatch for $label`: $deployed"
    }
}

# Only ArenaMode-owned destinations are inspected. A separately installed
# PitFireTeam under BepInEx/plugins/pitFireTeam or user/mods/pitFireTeam-ServerMod
# is valid and must not make this verification fail.
$ownedTrees = @(
    (Join-Path $repo 'ArenaMode.Client\bin\Release'),
    (Join-Path $repo 'ArenaMode.BigBrainCompat\bin\Release'),
    (Join-Path $repo 'build\server\Release'),
    (Join-Path $installation 'BepInEx\plugins\maschine-ArenaMode'),
    (Join-Path $installation 'SPT_Runtime\user\mods\ArenaMode')
)
$referenceHashes = Get-ReferencePayloadHashes
foreach ($tree in $ownedTrees) {
    Assert-NoPitFireTeamBinary $tree
    Assert-NoReferencePayload $tree $referenceHashes
}

$ownedFiles = @(
    (Join-Path $installation 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.Client.dll'),
    (Join-Path $installation 'BepInEx\plugins\maschine-ArenaMode\maschine-ArenaMode.BigBrainCompat.dll')
)
foreach ($file in $ownedFiles) {
    Assert-OwnedFileClean $file $referenceHashes
}

$tracked = @(& git -C $repo ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed.'
}

$forbiddenTrackedBinary = $tracked | Where-Object {
    [System.IO.Path]::GetFileName($_) -match '^(?:pitFireTeam(?:\.Server)?|SPT-PitFireTeam).*\.dll$'
}
if ($forbiddenTrackedBinary) {
    throw ('A PitFireTeam binary is tracked by ArenaMode: ' + ($forbiddenTrackedBinary -join ', '))
}

$referencePath = $reference.TrimEnd('\') + '\'
foreach ($relative in $tracked) {
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $relative))
    if ($fullPath.StartsWith($referencePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The external PitFireTeam reference tree is tracked by ArenaMode: $relative"
    }
}

$projectFiles = $tracked | Where-Object {
    $_ -match '\.(?:csproj|props|targets)$' -and
    [System.IO.File]::Exists((Join-Path $repo $_))
}
foreach ($relative in $projectFiles) {
    $text = [System.IO.File]::ReadAllText((Join-Path $repo $relative))
    if ($text -match '<(?:Reference|ProjectReference)\b[^>]*(?:pitFireTeam|SPT-PitFireTeam)') {
        throw "ArenaMode has a compile-time PitFireTeam reference: $relative"
    }
}

$sourceFiles = $tracked | Where-Object {
    $_ -match '\.cs$' -and
    [System.IO.File]::Exists((Join-Path $repo $_))
}
foreach ($relative in $sourceFiles) {
    $text = [System.IO.File]::ReadAllText((Join-Path $repo $relative))
    if ($text -match '(?m)^\s*(?:namespace|using)\s+pitTeam(?:[.;\s{])') {
        throw "ArenaMode contains copied or eagerly linked pitTeam source: $relative"
    }
}

$payloadExtensions = @('.cs', '.csproj', '.md', '.ogg', '.png')
foreach ($relative in $tracked) {
    $fullPath = Join-Path $repo $relative
    if (-not [System.IO.File]::Exists($fullPath) -or $payloadExtensions -notcontains [System.IO.Path]::GetExtension($fullPath)) {
        continue
    }

    $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
    if ($referenceHashes.ContainsKey($hash)) {
        throw "Tracked file '$relative' is byte-identical to PitFireTeam reference payload '$($referenceHashes[$hash])'."
    }
}

Write-Output 'PITFIRETEAM_DEPLOYMENT_VERIFIED'
