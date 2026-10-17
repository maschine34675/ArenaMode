param(
    [Parameter(Mandatory=$true)][string]$ClientDll,
    [Parameter(Mandatory=$true)][string]$ContentRoot
)
$ErrorActionPreference = 'Stop'
$mapRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$mapAssets = Join-Path $mapRepo 'ArenaMode.Client\Assets\MapPreviews'
$required = @('customs-ground.png', 'ATTRIBUTION.md', 'MAP-SOURCES.json',
    'Source\Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md')

function Normalize-MapNoticeLinks([string]$Body) {
    # The shared payload writer URL-encodes Markdown destinations (including
    # spaces) without changing license prose or the referenced source. Compare
    # only that equivalent spelling; embedded resources remain byte-exact.
    return [regex]::Replace($Body, '\]\(([^)]+)\)', {
        param($match)
        return '](' + [Uri]::UnescapeDataString($match.Groups[1].Value.Trim('<', '>')) + ')'
    })
}

# Read PE metadata, never load the Unity-dependent client assembly into this
# PowerShell process. Check the real embedded bytes, not project-file intent.
$mapStream = [IO.File]::OpenRead([IO.Path]::GetFullPath($ClientDll))
$mapPe = [System.Reflection.PortableExecutable.PEReader]::new($mapStream)
try {
    $mapMetadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($mapPe)
    $mapSection = [byte[]]$mapPe.GetSectionData($mapPe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress).GetContent()
    $mapEmbedded = @{}
    foreach ($handle in $mapMetadata.ManifestResources) {
        $resource = $mapMetadata.GetManifestResource($handle)
        $name = $mapMetadata.GetString($resource.Name)
        if (-not $name.StartsWith('ArenaMode.MapPreviews.')) { continue }
        if (-not $resource.Implementation.IsNil) { throw "External map resource: $name" }
        $offset = [int]$resource.Offset
        if ($offset -lt 0 -or $offset + 4 -gt $mapSection.Length) { throw "Invalid resource offset: $name" }
        $length = [BitConverter]::ToInt32($mapSection, $offset)
        if ($length -lt 0 -or [long]$offset + 4 + $length -gt $mapSection.Length) { throw "Invalid resource length: $name" }
        $bytes = [byte[]]::new($length)
        [Array]::Copy($mapSection, $offset + 4, $bytes, 0, $length)
        $mapEmbedded[$name] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    }
    foreach ($relative in $required) {
        $source = Join-Path $mapAssets $relative
        $name = 'ArenaMode.MapPreviews.' + [IO.Path]::GetFileName($relative)
        if ($mapEmbedded[$name] -ne (Get-FileHash -LiteralPath $source).Hash) {
            throw "Embedded map or license differs from reviewed asset: $name"
        }
    }
    foreach ($source in Get-ChildItem -LiteralPath $mapAssets -File -Recurse | Where-Object Name -ne '.gitattributes') {
        $relative = [IO.Path]::GetRelativePath($mapAssets, $source.FullName)
        $destination = Join-Path $ContentRoot $relative
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Map source/licensing missing in staged content: $relative"
        }
        $same = (Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $source.FullName).Hash
        if (-not $same -and $source.Extension -eq '.md') {
            $same = (Normalize-MapNoticeLinks (Get-Content -LiteralPath $destination -Raw)) -ceq
                (Normalize-MapNoticeLinks (Get-Content -LiteralPath $source.FullName -Raw))
        }
        if (-not $same) {
            throw "Map source/licensing missing or changed in staged content: $relative"
        }
    }
    Write-Output 'AREA MAP PACKAGE PASSED: embedded artwork/license/provenance and complete loose source package match'
}
finally { $mapPe.Dispose(); $mapStream.Dispose() }
