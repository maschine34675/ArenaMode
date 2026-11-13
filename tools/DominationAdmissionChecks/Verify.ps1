$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$install = [IO.Path]::GetFullPath((Join-Path $repo '../..'))
$project = Join-Path $PSScriptRoot 'DominationAdmissionChecks.csproj'
$evidence = Join-Path $repo ('artifacts/domination-ported-20261026/admission-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$installed = @(
    'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Client.dll',
    'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.BigBrainCompat.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll',
    'BepInEx/patchers/maschine-ArenaMode/maschine-ArenaMode.Gate.dll',
    'BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe'
)
function Snapshot-Installed {
    @($installed | ForEach-Object {
        $path = Join-Path $install $_
        $item = Get-Item -LiteralPath $path
        [pscustomobject]@{ Path=$path; Length=$item.Length; SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    })
}
$before = Snapshot-Installed
$before | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'installed-before.json')
$catalog = Join-Path $repo 'ArenaMode.Shared/ArenaAreaCatalog.cs'
$sourceHash = (Get-FileHash -LiteralPath $catalog -Algorithm SHA256).Hash
function Invoke-Case([string]$name, [string]$copy = '', [string]$pinnedPath = '') {
    $arguments = @('run', '--project', $project, '-c', 'Release',
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')",
        "-p:SPTUserMods=$(Join-Path $repo 'artifacts/nodeploy/must-not-exist')",
        "-p:OutputPath=$(Join-Path $evidence ($name + '-bin/'))",
        "-p:IntermediateOutputPath=$(Join-Path $evidence ($name + '-obj/'))")
    $env:ARENA_REPO_ROOT = $repo
    if ($copy -and !$pinnedPath) { $arguments += "-p:ProductionAreaCatalogSource=$copy" }
    if ($pinnedPath) {
        $env:ARENA_ADMISSION_OVERRIDE_PATH = $pinnedPath
        $env:ARENA_ADMISSION_OVERRIDE_SOURCE = $copy
    }
    try {
        $output = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        Remove-Item Env:ARENA_REPO_ROOT, Env:ARENA_ADMISSION_OVERRIDE_PATH, Env:ARENA_ADMISSION_OVERRIDE_SOURCE -ErrorAction SilentlyContinue
    }
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $evidence ($name + '.log')), $text)
    return @{ Exit=$exitCode; Text=$text }
}
$baseline = Invoke-Case 'baseline'
if ($baseline.Exit -ne 0 -or $baseline.Text -notmatch 'Domination admission: \d+ checks passed') { throw $baseline.Text }
function Reject-Change([string]$name, [string]$path, [string]$from, [string]$to, [string]$assertion, [bool]$pinned = $false) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $path))
    if ($source.Split([string[]]@($from), [StringSplitOptions]::None).Count -ne 2) {
        throw "Expected one mutation anchor for $name"
    }
    $copy = Join-Path $evidence ($name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($from, $to))
    $result = if ($pinned) { Invoke-Case $name $copy $path } else { Invoke-Case $name $copy }
    if ($result.Exit -eq 0 -or $result.Text -match 'error CS' -or $result.Text -notmatch [regex]::Escape($assertion)) {
        throw "$name did not fail its intended assertion.`n$($result.Text)"
    }
    Write-Output "DOMINATION_ADMISSION_MUTANT_REJECTED: $name (source pin: $pinned)"
}
$maps = [ordered]@{ FactoryDayId='factory4_day'; FactoryNightId='factory4_night'; Bay5Id='arena_bay5';
    EquatorId='arena_equator'; AirPitId='arena_airpit'; BowlId='arena_bowl'; IcebergId='arena_iceberg';
    AutoServiceId='arena_autoservice'; LaboratoryId='laboratory'; LabyrinthId='labyrinth' }
foreach ($key in $maps.Keys) {
    Reject-Change ('reject-' + $maps[$key]) 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
        "string.Equals(map, ArenaMapCatalog.$key, StringComparison.OrdinalIgnoreCase)" `
        'string.Equals(map, "not-an-admitted-map", StringComparison.OrdinalIgnoreCase)' `
        ('reviewed map admits a full-map objective match: ' + $maps[$key])
}
Reject-Change 'allow-pft' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    'if (pitFireTeam || (wholeMap' 'if (false || (wholeMap' `
    'server authority refuses PFT even with a reviewed map'
# A bounded id continues into the area table: its map check is what keeps a Customs area off every other map.
Reject-Change 'area-map-check-removed' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    'if (!string.Equals(map, candidate.MapId, StringComparison.OrdinalIgnoreCase))' 'if (false)' `
    'the native area model offers exactly the reviewed whole map: factory4_day'
Reject-Change 'allow-whole-customs' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    '&& !string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase)' `
    '&& !string.Equals(map, ArenaMapCatalog.LabyrinthId, StringComparison.OrdinalIgnoreCase) && !string.Equals(map, ArenaMapCatalog.CustomsId, StringComparison.OrdinalIgnoreCase)' `
    'whole Customs stays refused for Domination'
Reject-Change 'allow-pft-on-lab' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    'if (pitFireTeam || (wholeMap' 'if (pitFireTeam && !string.Equals(map, ArenaMapCatalog.LaboratoryId, StringComparison.OrdinalIgnoreCase) || (wholeMap' `
    'server authority refuses PFT even with a reviewed map: laboratory'
# Domination reaches the Customs area table through the mode filter of Customs and the ports.
Reject-Change 'customs-area-mode-filter' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    '&& !string.Equals(mode, "domination", StringComparison.OrdinalIgnoreCase)' '' `
    'Customs area admitted for Domination with the shared definition and its faction starts'
Reject-Change 'domination-size-tdm-only' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    'if (!pitFireTeam && ArenaPopulationPolicy.IsIndependentTeam(mode))' 'if (!pitFireTeam && string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))' `
    'Domination negotiates the Customs area team size like Team Deathmatch'
Reject-Change 'stale-refusal-text' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    'Domination is not available for this map or area, or with a PitFireTeam squad.' 'Domination requires Factory or a supported Arena port, whole map, without PitFireTeam.' `
    'server authority refuses PFT even with a reviewed map'
Reject-Change 'allow-unreviewed-map' 'ArenaMode.Shared/ArenaAreaCatalog.cs' `
    '&& !string.Equals(map, ArenaMapCatalog.AutoServiceId, StringComparison.OrdinalIgnoreCase)' '&& false' `
    'unreviewed objective map rejected by shared authority'
# These explicitly labelled controls exercise source wiring, not executed native/server code.
Reject-Change 'pin-final-freshness' 'ArenaMode.Client/Session/ArenaMenuSelection.cs' `
    'var eligibility = CurrentEligibility;' 'var eligibility = CachedEligibility;' `
    'final validation refreshes eligibility before freezing the roster' $true
Reject-Change 'pin-native-final-arm-order' 'ArenaMode.Client/Session/ArenaLocalRaidStartedPatch.cs' `
    'ArenaMenuSelection.TryValidateAndFreezeRequest' 'ArenaMenuSelection.BypassValidation' `
    'final availability validation still precedes the first server-arm request' $true
Reject-Change 'pin-http-arm-map-guard' 'ArenaMode.Client/Session/ArenaServerClient.cs' `
    'ArenaAreaCatalog.TryResolve(areaId, map, mode, pitFireTeamTeam' 'OtherCatalog.TryResolve(areaId, map, mode, pitFireTeamTeam' `
    'shared map guard precedes pending-arm publication and HTTP' $true
Reject-Change 'pin-server-arm-map-guard' 'ArenaMode.Server/Services/ArenaSessionService.cs' `
    'ArenaAreaCatalog.TryResolve(areaId!, map, mode!, protectPitFireTeam, out var area, out var areaFailure)' `
    'OtherCatalog.TryResolve(areaId!, map, mode!, protectPitFireTeam, out var area, out var areaFailure)' `
    'server direct and preparation arms retain the shared map guard before session publication' $true
Reject-Change 'pin-validator-domination-rule' 'ArenaMode.Server/Rewards/RewardResultValidator.cs' `
    '"domination" => rule == RewardRoundRule.KillsOnly,' '"domination" => false,' `
    'server reward validator admits Domination as a KillsOnly mode with the local forfeit' $true
Reject-Change 'pin-coordinator-domination-rule' 'ArenaMode.Server/Rewards/RewardRuntimeCoordinator.cs' `
    '"domination" => rule == RewardRoundRule.KillsOnly,' '"domination" => false,' `
    'server reward begin admits Domination under the KillsOnly rule only' $true
Reject-Change 'pin-client-domination-rule' 'ArenaMode.Client/Modes/ArenaRewardModeState.cs' `
    'Rule = "KillsOnly", Target = 0, Limit = 0,' 'Rule = "TeamScore", Target = 0, Limit = 0,' `
    'Domination reward state is KillsOnly with no score target, no time limit and a monotonic reward clock' $true
Reject-Change 'pin-client-domination-monotonic-clock' 'ArenaMode.Client/Modes/ArenaRewardModeState.cs' `
    'if (elapsed > _dominationRewardElapsed) _dominationRewardElapsed = elapsed;' '_dominationRewardElapsed = elapsed;' `
    'Domination reward state is KillsOnly with no score target, no time limit and a monotonic reward clock' $true
if ((Get-FileHash -LiteralPath $catalog -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Production catalog changed during checks.' }
$after = Snapshot-Installed
$after | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'installed-after.json')
if (($before | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)) { throw 'Installed Arena binaries changed during the test.' }
Write-Output "DOMINATION_ADMISSION_VERIFIED: 18 compiled mutants, 8 wiring controls, installed hashes unchanged; evidence $evidence"
