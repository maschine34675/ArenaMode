#Requires -Version 7.0
# Shared, non-destructive owned asset/document payload for builds and release ZIPs.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$Destination,
    [string]$SourceRevision,
    [string]$CoreSourceRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Arena-NoticeLayout.ps1')
$noticeMoves = Get-ArenaNoticeMoves
$SourceRoot = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath $SourceRoot).Path)
$Destination = [IO.Path]::GetFullPath($Destination)
if (-not $CoreSourceRoot) { $CoreSourceRoot = Join-Path $SourceRoot '../Redeploy' }
$CoreSourceRoot = (Resolve-Path -LiteralPath $CoreSourceRoot).Path
$gitRoot = & git -C $SourceRoot rev-parse --show-toplevel 2>$null
$ownCheckout = $LASTEXITCODE -eq 0 -and [IO.Path]::GetFullPath($gitRoot) -eq $SourceRoot
if (-not $SourceRevision) {
    if (-not $ownCheckout) { throw 'Archived source requires an explicit SourceRevision.' }
    $SourceRevision = (& git -C $SourceRoot rev-parse --verify HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the Arena source revision.' }
}
if ($SourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'SourceRevision must be a complete Git commit SHA.' }

# A normal checkout may contain local experiments: copy only tracked files.
# git archive snapshots have no repository of their own and are already filtered.
$tracked = $null
if ($ownCheckout) {
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $trackedFiles = & git -C $SourceRoot -c core.quotepath=false ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked Arena assets.' }
    foreach ($name in $trackedFiles) { [void]$tracked.Add($name) }
}
$requiredNotices = @('docs/licenses/audio/ANNOUNCER-ADDITIONS-20260917.json',
    'docs/licenses/audio/ANNOUNCER-SOURCES.json', 'docs/licenses/audio/AUDIO-LICENSES.md',
    'docs/licenses/audio/HITMARKER-SOURCES.json', 'docs/licenses/audio/PICKUP-SOURCES.json',
    'docs/licenses/CC-BY-3.0.txt', 'docs/licenses/CC0-1.0.txt')
$documents = [ordered]@{
    'LICENSE' = 'docs/LICENSE'
    'README.md' = 'docs/README.md'
    'THIRD-PARTY-NOTICES.md' = 'docs/THIRD-PARTY-NOTICES.md'
}
foreach ($mapping in @(
    @{Source='ArenaMode.Client/Assets/ArenaMode'; Dest=''},
    @{Source='ArenaMode.Client/Assets/MapPreviews'; Dest='MapPreviews/'}
)) {
    $base = Join-Path $SourceRoot $mapping.Source
    foreach ($file in (Get-ChildItem -LiteralPath $base -File -Recurse)) {
        if ($file.Name -eq '.gitattributes') { continue }
        $relative = [IO.Path]::GetRelativePath($base, $file.FullName).Replace('\', '/')
        # Unused recordings remain in source/local installs, never in new payloads.
        if ($mapping.Dest -eq '' -and $relative.StartsWith('announcements/reserve/', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $sourceRelative = "$($mapping.Source)/$relative"
        if ($null -ne $tracked -and -not $tracked.Contains($sourceRelative)) { continue }
        $target = "$($mapping.Dest)$relative"
        if ($mapping.Dest -eq '' -and -not $relative.Contains('/')) {
            throw "Unclassified root asset: $relative"
        }
        $documents[$sourceRelative] = $target
    }
}
foreach ($name in @('NOTICE.md', 'SOURCES.json', 'MIT-LICENSE.txt')) {
    $documents["ArenaMode.Client/Assets/Ui/$name"] = "Ui/$name"
}
# The Arena session's shipped default whitelist (docs/ARENA-SESSION-20261021.md, section 3) lives in the
# repository's assets folder, not in the client asset tree. It is named explicitly like the root documents
# above (no tracked-file filter, never optional) and installed beside the client DLL, where the client and
# the preloader gate read it. A user's own list is a different file in BepInEx/config and is never written.
$documents['assets/arena-whitelist.json'] = 'arena-whitelist.json'
foreach ($key in @($documents.Keys)) {
    $current = $documents[$key]
    if ($noticeMoves.Contains($current)) { $documents[$key] = $noticeMoves[$current] }
}
foreach ($name in $requiredNotices) {
    if (-not $documents.Contains("ArenaMode.Client/Assets/ArenaMode/$name")) {
        throw "Missing owned asset notice: $name"
    }
}

function Assert-NoReparsePath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    $isLeaf = $true
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a reparse-point payload path: $cursor"
            }
            if (-not $isLeaf -and -not $item.PSIsContainer) {
                throw "Payload parent is not a directory: $cursor"
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
        $isLeaf = $false
    }
}

function Convert-AssetLinks([string]$Body, [string]$SourceRelative, [string]$DestinationRelative) {
    $sourceFolder = Split-Path (Join-Path $SourceRoot $SourceRelative)
    $destinationFolder = Split-Path (Join-Path $Destination $DestinationRelative)
    return [regex]::Replace($Body, '\]\(([^)]+)\)', {
        param($match)
        $target = $match.Groups[1].Value.Trim('<', '>')
        if ($target -match '^[a-z]+:|^#') { return $match.Value }
        $parts = $target.Split('#', 2)
        $resolved = [IO.Path]::GetFullPath((Join-Path $sourceFolder ([Uri]::UnescapeDataString($parts[0]))))
        $sourceTarget = [IO.Path]::GetRelativePath($SourceRoot, $resolved).Replace('\', '/')
        if ($sourceTarget.StartsWith('../') -or -not (Test-Path -LiteralPath $resolved)) {
            throw "Broken source reference in ${SourceRelative}: $target"
        }
        if ($documents.Contains($sourceTarget)) {
            $relativeTarget = [IO.Path]::GetRelativePath($destinationFolder, (Join-Path $Destination $documents[$sourceTarget])).Replace('\', '/')
            $encoded = ($relativeTarget.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
        } else {
            $encoded = 'https://github.com/maschine34675/ArenaMode/blob/' + $SourceRevision + '/' +
                (($sourceTarget.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/')
        }
        if ($parts.Length -eq 2) { $encoded += '#' + $parts[1] }
        return "]($encoded)"
    })
}

# Preflight every destination and source before changing anything. No recursive
# deletion or cleanup is performed: custom files, drafts and user settings stay.
$pending = [ordered]@{}
$audio = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($entry in $documents.GetEnumerator()) {
    $inputFile = Join-Path $SourceRoot $entry.Key
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing owned asset: $($entry.Key)" }
    Assert-NoReparsePath $inputFile
    $outputFile = Join-Path $Destination $entry.Value
    Assert-NoReparsePath $outputFile
    if (Test-Path -LiteralPath $outputFile -PathType Container) { throw "Asset destination is a directory: $outputFile" }
    if ($pending.Contains($entry.Value)) { throw "Duplicate asset destination: $($entry.Value)" }
    $bytes = [IO.File]::ReadAllBytes($inputFile)
    if ([IO.Path]::GetExtension($entry.Key) -eq '.md') {
        $originalBody = Get-Content -LiteralPath $inputFile -Raw
        $body = Convert-AssetLinks $originalBody $entry.Key $entry.Value
        if ($entry.Key -eq 'ArenaMode.Client/Assets/Ui/NOTICE.md') {
            $body = $body.Replace('This directory contains eighteen PNGs with two distinct licenses.',
                'The client assembly maschine-ArenaMode.Client.dll embeds eighteen PNGs with two distinct licenses. This folder contains their notices and license texts.')
        }
        if ($body -cne $originalBody) { $bytes = $utf8.GetBytes($body) }
    } elseif ($entry.Key -match '/(HITMARKER|PICKUP)-SOURCES\.json$') {
        $category = $Matches[1]
        $record = Get-Content -LiteralPath $inputFile -Raw | ConvertFrom-Json
        $clips = if ($category -eq 'HITMARKER') { @($record.files) } else { @($record) }
        foreach ($clip in $clips) {
            if ($clip.file -cnotmatch '^\.\./\.\./\.\./sounds/[a-z]+\.wav$') { throw "Unsafe effect path: $($clip.file)" }
            $sourceClip = [IO.Path]::GetRelativePath($SourceRoot,
                [IO.Path]::GetFullPath((Join-Path (Split-Path $inputFile) $clip.file))).Replace('\', '/')
            if (-not $documents.Contains($sourceClip) -or
                (Get-FileHash -LiteralPath (Join-Path $SourceRoot $sourceClip)).Hash -ine $clip.sha256) {
                throw "Missing audio or provenance hash mismatch: $sourceClip"
            }
        }
        if ($record.licenseFile -cne '../CC0-1.0.txt' -or
            (Get-FileHash -LiteralPath (Join-Path (Split-Path $inputFile) $record.licenseFile)).Hash -ine $record.licenseSha256) {
            throw 'Effect license path or hash mismatch.'
        }
        $bytes = $utf8.GetBytes(($record | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
    } elseif ($entry.Key -eq 'ArenaMode.Client/Assets/Ui/SOURCES.json') {
        $record = Get-Content -LiteralPath $inputFile -Raw | ConvertFrom-Json
        $record | Add-Member -NotePropertyName packageScope -NotePropertyValue 'Asset file names identify resources embedded in maschine-ArenaMode.Client.dll, not loose files beside this notice.' -Force
        $bytes = $utf8.GetBytes(($record | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
    } elseif ($entry.Key -match '/ANNOUNCER-(SOURCES|ADDITIONS-20260917)\.json$') {
        $record = Get-Content -LiteralPath $inputFile -Raw | ConvertFrom-Json
        $shippedClips = @()
        foreach ($clip in $record.files) {
            if ($clip.file -notmatch '^announcements/[A-Za-z0-9_/-]+\.ogg$' -or $clip.file.Contains('..')) {
                throw "Unsafe source audio reference: $($clip.file)"
            }
            # Intake 'usage' predates promotion of several cues. Filter by their
            # current path, not usage, so those active root recordings stay shipped.
            if ($clip.file.StartsWith('announcements/reserve/', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $clipSource = "ArenaMode.Client/Assets/ArenaMode/$($clip.file)"
            if (-not $documents.Contains($clipSource) -or -not $audio.Add($clip.file)) {
                throw "Missing or duplicate owned audio reference: $($clip.file)"
            }
            if ((Get-FileHash -LiteralPath (Join-Path $SourceRoot $clipSource) -Algorithm SHA256).Hash -ine $clip.sha256) {
                throw "Audio provenance hash mismatch: $($clip.file)"
            }
            $clip.file = '../../../' + $clip.file
            $shippedClips += $clip
        }
        $record.files = $shippedClips
        $record | Add-Member -NotePropertyName filePathBase -NotePropertyValue 'Paths are relative to this manifest.' -Force
        $record | Add-Member -NotePropertyName packageScope -NotePropertyValue 'Only shipped recordings are listed. Unused announcements/reserve recordings remain in the source repository and are excluded from build/deploy/release payloads.' -Force
        $bytes = $utf8.GetBytes(($record | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
    } elseif ($entry.Key -eq 'assets/arena-whitelist.json') {
        # Stop the build, deployment or package instead of shipping a default list that the gate and the
        # client would refuse (that makes every Arena session unavailable). The bytes are copied unchanged.
        $policy = Get-Content -LiteralPath $inputFile -Raw | ConvertFrom-Json
        $policyProperties = @($policy.PSObject.Properties.Name)
        if ($policyProperties -cnotcontains 'schema' -or $policy.schema -ne 1 -or $policyProperties -cnotcontains 'mods' -or @($policy.mods).Count -eq 0) {
            throw 'The Arena default whitelist is not a schema 1 policy with entries.'
        }
        foreach ($mod in @($policy.mods)) {
            $names = @($mod.PSObject.Properties.Name)
            if (($names -ccontains 'guid') -eq ($names -ccontains 'patcher')) {
                throw 'Every Arena default whitelist entry needs exactly one of guid and patcher.'
            }
        }
    }
    $pending[$entry.Value] = $bytes
}
$ownedOgg = @($documents.Values | Where-Object { $_.EndsWith('.ogg', [StringComparison]::OrdinalIgnoreCase) })
if ($ownedOgg.Count -ne $audio.Count) { throw 'Unrecorded or missing announcer audio file.' }
$coreLicense = Join-Path $CoreSourceRoot 'LICENSE'
Assert-NoReparsePath $coreLicense
# Compare the license TEXT: line endings are a checkout detail (core.autocrlf=true
# turns LF into CRLF in a fresh clone or worktree), not a different license.
# .gitattributes pins LICENSE to LF in both repositories as well.
function Get-LicenseText([string]$path) { [IO.File]::ReadAllText($path).Replace("`r`n", "`n") }
if ((Get-LicenseText $coreLicense) -cne (Get-LicenseText (Join-Path $SourceRoot 'LICENSE'))) {
    throw 'Core license differs from the shared project notice; include its separate notice before packaging.'
}
foreach ($entry in $pending.GetEnumerator()) {
    $outputFile = Join-Path $Destination $entry.Key
    New-Item -ItemType Directory -Path (Split-Path $outputFile) -Force | Out-Null
    # Avoid needless writes to unchanged files in synchronized development installs.
    if (Test-Path -LiteralPath $outputFile -PathType Leaf) {
        $existing = [IO.File]::ReadAllBytes($outputFile)
        if ([Convert]::ToBase64String($existing) -ceq [Convert]::ToBase64String($entry.Value)) { continue }
    }
    [IO.File]::WriteAllBytes($outputFile, $entry.Value)
}
Write-Output "ARENA_ASSETS_READY $Destination ($($pending.Count) owned files; user files preserved)"
