$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Menu polish 2026-10-08, items 3+4: every mode's copy follows one schema
# (ArenaModeDefinition's doc comment). Each mutant is a source COPY of
# ArenaModeCatalog.cs handed to the harness through ProductionCatalogSource, so
# the production file is never rewritten. A mutant that re-introduces over-long
# or off-schema copy must be rejected by the named assertion.
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'NativeModeSelectionChecks.csproj'
$catalogPath = Join-Path $repoRoot 'ArenaMode.Client\Modes\ArenaModeCatalog.cs'
$catalog = [System.IO.File]::ReadAllText($catalogPath)
$catalogHash = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('ArenaCatalogSchemaMutation-' + [guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-SchemaMutant([string]$name, [string]$from, [string]$to, [string]$expected) {
    if ([regex]::Matches($catalog, [regex]::Escape($from)).Count -ne 1) {
        throw "Expected exactly one mutation site for $name; refusing an ambiguous mutant."
    }
    $copy = Join-Path $scratch ($name + '.cs')
    [System.IO.File]::WriteAllText($copy, $catalog.Replace($from, $to))
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet run --project $project -c Release `
            "-p:ProductionCatalogSource=$copy" `
            "-p:OutputPath=$(Join-Path $scratch ($name + '-bin\'))" `
            "-p:IntermediateOutputPath=$(Join-Path $scratch ($name + '-obj\'))" 2>&1
        $exit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    if ($exit -eq 0 -or $text -match 'error CS' -or $text -notmatch [regex]::Escape($expected)) {
        throw "$name did not fail its intended schema assertion (exit $exit). Evidence: $scratch`n$text"
    }
    Write-Output "Rejected mutant: $name"
}

# The wording the user complained about ("do not fit the box"): Zombie's old facts.
Invoke-SchemaMutant 'rules-too-long' `
    '"Survive the waves", "None", "Arena kits"' `
    '"Survive wave after wave; they only get bigger", "Instant", "Arena kits"' `
    'mode copy schema: RULES is the objective in <= 3 words and <= 18 characters: zombie'
Invoke-SchemaMutant 'respawn-vocabulary' `
    '"Survive the waves", "None", "Arena kits"' `
    '"Survive the waves", "Instant respawn, the wave keeps coming", "Arena kits"' `
    'mode copy schema: RESPAWN is exactly Instant, Next round, or None: zombie'
Invoke-SchemaMutant 'equipment-too-long' `
    '"Round elimination", "Next round", "Buy / retain kit"' `
    '"Round elimination", "Next round", "Buy or retain your kit between rounds"' `
    'mode copy schema: EQUIPMENT is the loadout source in <= 3 words and <= 18 characters: tactical'
Invoke-SchemaMutant 'description-without-period' `
    '"Every kill moves you one rung up the weapon ladder."' `
    '"Every kill moves you one rung up the weapon ladder"' `
    'mode copy schema: Description is one sentence of <= 80 characters ending with a period: gungame'
Invoke-SchemaMutant 'tactical-rules-roster' `
    '"Round elimination", "Next round"' `
    '"1v5 / Solo", "Next round"' `
    'mode copy schema: Tactical RULES carries no roster'

& dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'The unchanged production catalog failed its schema checks after mutation testing.'
}
if ((Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash -ne $catalogHash) {
    throw 'Mutation verification changed ArenaModeCatalog.cs.'
}
Write-Output "ARENA_MODE_COPY_SCHEMA_VERIFIED (5 off-schema catalog mutants rejected; production catalog unchanged; evidence: $scratch)"
