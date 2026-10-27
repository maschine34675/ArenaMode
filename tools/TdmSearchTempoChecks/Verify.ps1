param()
$ErrorActionPreference = 'Stop'
$tempoRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$tempoProject = Join-Path $PSScriptRoot 'TdmSearchTempoChecks.csproj'
$tempoWork = Join-Path $tempoRepo ('artifacts\tdm-search-tempo-checks\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempoWork) | Out-Null
$tempoPath = Join-Path $tempoRepo 'ArenaMode.Client\Bots\TdmSearchTempo.cs'
$tempoSource = [IO.File]::ReadAllText($tempoPath)
$tempoMutants = 0
function Invoke-Checks([string[]]$Extra = @()) {
    $tempoBuild = @(& dotnet build $tempoProject -t:Rebuild -c Release --nologo @Extra 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($tempoBuild -join [Environment]::NewLine) }
    $tempoOutput = @(& dotnet run --project $tempoProject -c Release --no-build 2>&1)
    return @{ Exit = $LASTEXITCODE; Text = ($tempoOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine }
}
function Assert-Production([string]$Name) {
    $tempoResult = Invoke-Checks
    [IO.File]::WriteAllText((Join-Path $tempoWork ($Name + '.log')), $tempoResult.Text)
    Write-Output $tempoResult.Text
    if ($tempoResult.Exit -ne 0 -or $tempoResult.Text -notmatch 'TDM_SEARCH_TEMPO_CONTRACTS_PASSED') { throw 'Production-linked tempo checks failed' }
}
function Test-Mutant([string]$Name, [string]$Before, [string]$After, [string]$Assertion) {
    if (-not $tempoSource.Contains($Before)) { throw "Missing mutation anchor: $Name" }
    $tempoMutant = Join-Path $tempoWork ($Name + '.cs')
    [IO.File]::WriteAllText($tempoMutant, $tempoSource.Replace($Before, $After))
    $tempoResult = Invoke-Checks @("-p:TempoSource=$tempoMutant")
    [IO.File]::WriteAllText((Join-Path $tempoWork ($Name + '.log')), $tempoResult.Text)
    if ($tempoResult.Exit -eq 0 -or -not $tempoResult.Text.Contains('ASSERT: ' + $Assertion)) { throw "Mutant did not fail named assertion: $Name" }
    $script:tempoMutants++
    Write-Output "Tempo negative control rejected: $Name -> $Assertion"
}
try {
    Assert-Production 'production-before'
    Test-Mutant 'delay-unchanged' '__1 = MaximumDelay;' '__1 = original;' 'both search arguments capped'
    Test-Mutant 'old-five-second-cap' 'private const float MaximumDelay = 3f;' 'private const float MaximumDelay = 5f;' 'both search arguments capped'
    Test-Mutant 'short-delay-increased' '__1 <= MaximumDelay' '__1 < 0' 'short or invalid delay preserved'
    Test-Mutant 'phase-ignored' 'mode.IsSearchActive' 'true' 'reject not live'
    Test-Mutant 'pft-ignored' '!PitFireTeamCompat.HasFrozenTeamRoster' 'true' 'reject PFT roster'
    Test-Mutant 'native-pair-ignored' 'TeamDeathmatchRelations.OwnsAwareness(source, target, controller, __0, native)' 'true' 'reject native stop'
    Test-Mutant 'search-owner-ignored' 'ReferenceEquals(_decider.GetValue(search), __instance)' 'true' 'reject foreign decider'
    Test-Mutant 'patch-rollback-removed' 'Patches.UnpatchSelf();' '' 'partial patch transaction rolled back'
}
finally { Assert-Production 'production-after' }
@{ Source = (Get-FileHash $tempoPath).Hash; Mutants = $tempoMutants; Utc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $tempoWork 'evidence.json')
Write-Output "Tempo evidence: $tempoWork"
Write-Output "TDM_SEARCH_TEMPO_CHECKS_PASSED: $tempoMutants named negative controls"
