param([string]$ManifestPath)
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $ManifestPath) {
    $arenaBuild = @(& pwsh -NoProfile -File (Join-Path $arenaRepo 'tools\DoubleJumpChecks\Build-Staged.ps1'))
    $arenaBuild | Write-Output
    if ($LASTEXITCODE -ne 0) { throw 'Staged Arena client build failed.' }
    $arenaClientLine = @($arenaBuild | Where-Object { $_ -like 'Staged client: *' })
    if ($arenaClientLine.Count -ne 1) { throw 'Expected exactly one staged client output.' }
    $ManifestPath = Join-Path (Split-Path (Split-Path $arenaClientLine[0].Substring(15))) 'manifest.json'
}
$arenaManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $arenaManifest.ClientPath -Algorithm SHA256).Hash -ne $arenaManifest.ClientSha256) {
    throw 'Client build manifest does not match its DLL.'
}
$arenaImport = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SOURCE-IMPORT.json') -Raw | ConvertFrom-Json
$arenaExpected = @{}
foreach ($arenaEntry in $arenaImport.files | Where-Object { $_.unchanged -eq $true }) {
    $arenaName = 'ArenaMode.Hud.InfoPanels.Icons.' + [IO.Path]::GetFileName($arenaEntry.target)
    if ($arenaExpected.ContainsKey($arenaName)) { throw "Duplicate imported resource: $arenaName" }
    $arenaExpected[$arenaName] = $arenaEntry.sourceSha256
    if ((Get-FileHash -LiteralPath (Join-Path $arenaRepo $arenaEntry.target) -Algorithm SHA256).Hash -ne $arenaEntry.sourceSha256) {
        throw "Imported sprite changed without updating its provenance: $arenaName"
    }
}
if ($arenaExpected.Count -ne 26) { throw 'The original 26 runtime icons must all be accounted for.' }

# Icons authored inside ArenaMode after the import have their own record, so the
# import provenance above stays exactly as strict. A name may appear in only one
# of the two records, and every authored file must match its recorded hash.
$arenaAuthored = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ARENA-ICONS.json') -Raw | ConvertFrom-Json
$arenaAuthoredCount = 0
foreach ($arenaEntry in $arenaAuthored.files) {
    $arenaName = 'ArenaMode.Hud.InfoPanels.Icons.' + [IO.Path]::GetFileName($arenaEntry.target)
    if ($arenaExpected.ContainsKey($arenaName)) { throw "Authored icon collides with an imported or authored one: $arenaName" }
    if ((Get-FileHash -LiteralPath (Join-Path $arenaRepo $arenaEntry.target) -Algorithm SHA256).Hash -ne $arenaEntry.sha256) {
        throw "Authored sprite changed without updating ARENA-ICONS.json: $arenaName"
    }
    $arenaExpected[$arenaName] = $arenaEntry.sha256
    $arenaAuthoredCount++
}

# Read PE metadata without loading Unity or executing any game/plugin code.
$arenaStream = [IO.File]::OpenRead($arenaManifest.ClientPath)
$arenaPe = [System.Reflection.PortableExecutable.PEReader]::new($arenaStream)
try {
    $arenaMetadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($arenaPe)
    $arenaResources = $arenaPe.GetSectionData($arenaPe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
    $arenaFound = @{}
    foreach ($arenaHandle in $arenaMetadata.ManifestResources) {
        $arenaResource = $arenaMetadata.GetManifestResource($arenaHandle)
        $arenaName = $arenaMetadata.GetString($arenaResource.Name)
        if (-not $arenaName.StartsWith('ArenaMode.Hud.InfoPanels.Icons.')) { continue }
        if (-not $arenaExpected.ContainsKey($arenaName) -or -not $arenaResource.Implementation.IsNil) {
            throw "Unexpected or non-embedded panel resource: $arenaName"
        }
        $arenaReader = $arenaResources.GetReader([int]$arenaResource.Offset, 4)
        $arenaLength = $arenaReader.ReadInt32()
        $arenaBytes = $arenaResources.GetContent([int]$arenaResource.Offset + 4, $arenaLength)
        $arenaHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$arenaBytes))
        if ($arenaHash -ne $arenaExpected[$arenaName]) { throw "DLL sprite bytes differ: $arenaName" }
        $arenaFound[$arenaName] = $arenaHash
    }
    if ($arenaFound.Count -ne $arenaExpected.Count) { throw 'The built client is missing panel sprites.' }
    foreach ($arenaHandle in $arenaMetadata.AssemblyReferences) {
        $arenaName = $arenaMetadata.GetString($arenaMetadata.GetAssemblyReference($arenaHandle).Name)
        if ($arenaName -match 'RaidInfoPanels') { throw "Standalone dependency remains: $arenaName" }
    }
    $arenaPluginTypes = @()
    foreach ($arenaHandle in $arenaMetadata.TypeDefinitions) {
        $arenaType = $arenaMetadata.GetTypeDefinition($arenaHandle)
        foreach ($arenaAttributeHandle in $arenaType.GetCustomAttributes()) {
            $arenaAttribute = $arenaMetadata.GetCustomAttribute($arenaAttributeHandle)
            if ($arenaAttribute.Constructor.Kind -ne 'MemberReference') { continue }
            $arenaMember = $arenaMetadata.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$arenaAttribute.Constructor)
            if ($arenaMember.Parent.Kind -ne 'TypeReference') { continue }
            $arenaAttributeType = $arenaMetadata.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$arenaMember.Parent)
            if ($arenaMetadata.GetString($arenaAttributeType.Namespace) -eq 'BepInEx' -and
                $arenaMetadata.GetString($arenaAttributeType.Name) -eq 'BepInPlugin') {
                $arenaPluginTypes += $arenaMetadata.GetString($arenaType.Namespace) + '.' + $arenaMetadata.GetString($arenaType.Name)
            }
        }
    }
    if ($arenaPluginTypes.Count -ne 1 -or $arenaPluginTypes[0] -ne 'ArenaMode.Plugin') {
        throw "Unexpected plugin registrations: $($arenaPluginTypes -join ', ')"
    }
}
finally { $arenaPe.Dispose(); $arenaStream.Dispose() }
Write-Output "Verified 26 embedded source-identical icons, $arenaAuthoredCount Arena-authored icons, one Arena plugin and no standalone dependency. Manifest: $ManifestPath"
Write-Output 'ARENA_INFO_PANEL_BUILD_VERIFIED'
