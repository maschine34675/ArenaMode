param([string]$Database = 'D:\SPT41\SPT_Runtime\SPT_Data\database\templates\items.json')
$ErrorActionPreference = 'Stop'
$variantRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$variantProject = Join-Path $PSScriptRoot 'WeaponVariantChecks.csproj'
$variantSource = [IO.File]::ReadAllText((Join-Path $variantRoot 'ArenaMode.Server/Services/ArenaWeaponVariants.cs'))
$variantTypes = [IO.File]::ReadAllText((Join-Path $variantRoot '../_decompiled/EFT.InventoryLogic/JsonTypes.cs'))
$variantTypeTable = $variantTypes.Substring($variantTypes.IndexOf('Dictionary<string, Type> TypeTable'))
$variantTypeEnd = $variantTypeTable.IndexOf('CustomizationTypeTable')
if ($variantTypeEnd -lt 1) { throw 'Could not delimit native TypeTable contract.' }
$variantTypeTable = $variantTypeTable.Substring(0, $variantTypeEnd)
$variantCategoryBlock = [regex]::Match($variantSource, '(?s)NativeFilterCategories =.*?\{(.*?)\};').Groups[1].Value
$variantCategories = [regex]::Matches($variantCategoryBlock, '"([0-9a-f]{24})"') | ForEach-Object { $_.Groups[1].Value }
if ($variantCategories.Count -ne 53) { throw 'Native category contract unexpectedly changed.' }
foreach ($variantCategory in $variantCategories)
{
    if ($variantTypeTable -notmatch ('"' + $variantCategory + '"\s*,\s*typeof\('))
    { throw "Native JsonTypes category missing: $variantCategory" }
}
Write-Output 'Weapon variant native category contract: all 53 verified against local JsonTypes.TypeTable.'
dotnet run --project $variantProject -c Release -- $variantRoot $Database
if ($LASTEXITCODE -ne 0) { throw 'Weapon variant production checks failed.' }

# A deliberate incompatible-slot acceptance must be caught by the positive
# fixture, not merely by absence of changes in a random real-data draw.
$variantWork = Join-Path $PSScriptRoot '.work'
[IO.Directory]::CreateDirectory($variantWork) | Out-Null
$variantNeedle = '=> slot.Filters.All(f => f.Include.Any(x => FilterMatches(tpl, x)) && !f.Exclude.Any(x => FilterMatches(tpl, x)));'
if (-not $variantSource.Contains($variantNeedle)) { throw 'Negative control no longer matches production filter contract.' }
$variantMutant = Join-Path $variantWork 'ArenaWeaponVariants.mutant.cs'
[IO.File]::WriteAllText($variantMutant, $variantSource.Replace($variantNeedle, '=> true; // deliberate filter bypass'))
$variantOutput = & dotnet run --project $variantProject -c Release "-p:VariantSource=$variantMutant" -- $variantRoot $Database 2>&1
$variantExit = $LASTEXITCODE
if ($variantExit -eq 0 -or ($variantOutput -join "`n") -notmatch 'ASSERT: explicit incompatible slot rejected')
{ throw "Negative control did not fail at its intended assertion (exit $variantExit): $variantOutput" }
Write-Output 'Weapon variant incompatible-slot negative control: rejected as expected.'
dotnet run --project $variantProject -c Release -- $variantRoot $Database
if ($LASTEXITCODE -ne 0) { throw 'Production recheck after negative control failed.' }
# These exercise the actual SessionService lifecycle and the actual typed
# TemplateTable -> JSON projection used by LadderGenerator, not stand-ins.
dotnet test (Join-Path $variantRoot 'ArenaMode.Tests/ArenaMode.Tests.csproj') -c Release --filter 'FullyQualifiedName~WeaponVariantTests'
if ($LASTEXITCODE -ne 0) { throw 'Weapon variant server adapter/lifecycle tests failed.' }
Write-Output 'ARENA_WEAPON_VARIANTS_PASSED'
