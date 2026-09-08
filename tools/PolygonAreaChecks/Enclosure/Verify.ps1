param()
$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$arenaProject = Join-Path $PSScriptRoot 'Enclosure.csproj'
$arenaWork = Join-Path $arenaRepo ('artifacts\polygon-checks\enclosure-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaWork -Force | Out-Null
$arenaPlan = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaEnclosurePlan.cs'))
$arenaBuilder = [IO.File]::ReadAllText((Join-Path $arenaRepo 'ArenaMode.Client\Areas\ArenaAreaEnclosure.cs'))

function Invoke-Enclosure([string[]]$Extra = @()) {
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaLines = @(& dotnet run --project $arenaProject -c Release @Extra 2>&1)
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    $arenaText = ($arenaLines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    Write-Output $arenaText
    if ($arenaExit -ne 0 -or $arenaText -notmatch 'ARENA_POLYGON_ENCLOSURE_CONTRACTS_PASSED') {
        throw "Production enclosure checks failed: $arenaExit"
    }
}
function Test-EnclosureMutant([string]$Name, [string]$Source, [string]$Before, [string]$After, [string]$Assertion, [string]$Property = 'PlanSource') {
    if (-not $Source.Contains($Before)) { throw "Missing exact mutation anchor: $Name" }
    $arenaCandidate = Join-Path $arenaWork ($Name + '.cs')
    [IO.File]::WriteAllText($arenaCandidate, $Source.Replace($Before, $After))
    $arenaPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $arenaLines = @(& dotnet run --project $arenaProject -c Release "-p:${Property}=$arenaCandidate" 2>&1)
        $arenaExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $arenaPreference }
    $arenaText = ($arenaLines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $arenaWork ($Name + '.log')), $arenaText)
    if ($arenaExit -eq 0 -or -not $arenaText.Contains('ASSERT: ' + $Assertion)) {
        Write-Output $arenaText
        throw "Mutation must fail at its named behavioral assertion: $Name"
    }
    Write-Output "Enclosure negative control rejected: $Name -> $Assertion"
}

Invoke-Enclosure
Test-EnclosureMutant 'legacy-wall-moved-inward' $arenaPlan 'float shift = rectangle ? -Thickness / 2 : 0;' 'float shift = 0;' 'legacy outside wall volume'
Test-EnclosureMutant 'polygon-corner-omitted' $arenaPlan 'if (!rectangle) corners.Add(' 'if (false) corners.Add(' 'polygon corner closure count'
Test-EnclosureMutant 'upper-cap-inverted' $arenaPlan 'triangles.Add(c+n); triangles.Add(b+n); triangles.Add(a+n);' 'triangles.Add(a+n); triangles.Add(b+n); triangles.Add(c+n);' 'upper cap winding points outward'
Test-EnclosureMutant 'unclipped-grid-strip' $arenaPlan 'polygon=Clip(polygon,axis,center+GridWidth/2,false);' '// removed upper strip clip' 'cap grid triangle remains inside one thin grid strip'
Test-EnclosureMutant 'convex-cap-fills-void' $arenaBuilder 'collider.convex = false;' 'collider.convex = true;' 'new actual builder intact' 'BuilderSource'
Test-EnclosureMutant 'visual-disable-undetected' $arenaBuilder '|| !_renderer.enabled' '' 'integrity detects visual disabled' 'BuilderSource'
Test-EnclosureMutant 'detached-objects-leaked' $arenaBuilder 'if (go != null) { go.SetActive(false); UnityEngine.Object.Destroy(go); }' 'if (false) { go.SetActive(false); UnityEngine.Object.Destroy(go); }' 'detached owned children are still destroyed' 'BuilderSource'
Test-EnclosureMutant 'unlimited-area-gets-floor' $arenaPlan 'bounds.LimitsHeight ? CreateCap(bounds, bounds.MinY - Thickness, bounds.MinY) : null' 'CreateCap(bounds, bounds.MinY - Thickness, bounds.MinY)' 'height unlimited plan has no physical or navigation caps'
Test-EnclosureMutant 'scene-height-flattened' $arenaPlan 'Include(a.Y); Include(b.Y); Include(c.Y); surface = true;' 'Include(0); surface = true;' 'side walls span overlapping low and high surfaces without interior vertices'
Test-EnclosureMutant 'exact-capsule-readback' $arenaBuilder 'Math.Abs(a - b) <= .0001f' 'a == b' 'small capsule readback deviation accepted: capsule collider' 'BuilderSource'
Test-EnclosureMutant 'capsule-drift-unchecked' $arenaBuilder 'Math.Abs(a - b) <= .0001f' 'true' 'changed capsule geometry reports object and readback values: capsule collider' 'BuilderSource'
Test-EnclosureMutant 'corner-navigation-height-halved' $arenaBuilder 'new Vector3(diameter, corner.Height, diameter)' 'new Vector3(diameter, corner.Height / 2f, diameter)' 'corner box preserves planned diameter and full height' 'BuilderSource'
Test-EnclosureMutant 'corner-navigation-width-halved' $arenaBuilder 'float diameter = 2f * ArenaAreaEnclosurePlan.CornerRadius;' 'float diameter = ArenaAreaEnclosurePlan.CornerRadius;' 'corner box preserves planned diameter and full height' 'BuilderSource'
# Recompile actual production sources after every mutated source has been exercised.
Invoke-Enclosure
Write-Output 'ARENA_POLYGON_ENCLOSURE_ORACLE_PASSED'
