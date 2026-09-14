$ErrorActionPreference = 'Stop'
$assistRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assistStage = Join-Path $assistRepo ('artifacts/assist-checks/' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($assistStage)
$assistBoard = Join-Path $assistRepo 'ArenaMode.Client/Modes/ArenaScoreboard.cs'
$assistMatch = Join-Path $assistRepo 'ArenaMode.Client/Modes/ArenaMatch.cs'
$assistOriginal = [IO.File]::ReadAllText($assistBoard)
$assistSources = @('ArenaMode.Client/Modes/ArenaMatch.cs', 'ArenaMode.Client/Modes/ArenaScoreboard.cs',
    'ArenaMode.Client/Combat/ArenaKill.cs', 'ArenaMode.Client/Combat/ArenaCombatAwards.cs',
    'ArenaMode.Client/Combat/ArenaShotAwardHistory.cs', 'ArenaMode.Client/Combat/ScoreRules.cs',
    'ArenaMode.Client/Modes/IArenaMode.cs', 'ArenaMode.Client/Modes/ArenaAnnouncerCue.cs',
    'tools/AnnouncerCombatChecks/Stubs.cs') + @(Get-ChildItem -LiteralPath $PSScriptRoot -File |
        ForEach-Object { 'tools/AssistChecks/' + $_.Name })
$assistHashes = @{}
foreach ($relative in $assistSources) {
    $assistHashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $assistRepo $relative)).Hash
}
$assistSpt = [IO.Path]::GetFullPath((Join-Path $assistRepo '../..'))
$assistInstalled = @{}
foreach ($relative in @('BepInEx/plugins/maschine-ArenaMode.Client.dll',
    'BepInEx/plugins/maschine-Redeploy.Core.dll',
    'SPT_Runtime/user/mods/ArenaMode/maschine-ArenaMode.Server.dll')) {
    $path = Join-Path $assistSpt $relative
    if (Test-Path -LiteralPath $path) { $assistInstalled[$path] = (Get-FileHash -LiteralPath $path).Hash }
}
function Invoke-Assist([string]$Name, [string]$Source, [string]$Suite = 'all', [string]$Failure = '') {
    $directory = Join-Path $assistStage $Name
    [void][IO.Directory]::CreateDirectory($directory)
    $build = & dotnet build (Join-Path $PSScriptRoot 'AssistChecks.csproj') -c Release --nologo `
        "-p:ScoreboardSource=$Source" "-p:BaseIntermediateOutputPath=$directory/obj/" "-p:OutputPath=$directory/out/" 2>&1 | Out-String
    $buildExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $directory 'build.log'), $build)
    if ($buildExit -ne 0) { throw "$Name must compile before behavioral verification: $build" }
    $result = & dotnet (Join-Path $directory 'out/AssistChecks.dll') $Suite 2>&1 | Out-String
    $resultExit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $directory 'result.log'), $result)
    if ($Failure) {
        if ($resultExit -eq 0 -or !$result.Contains('FAILED: ' + $Failure)) {
            throw "$Name did not reject its intended behavioral fault: $result"
        }
        Write-Output "Rejected $Name at: $Failure"
    } elseif ($resultExit -ne 0 -or !$result.Contains('ASSIST_PRODUCTION_PASSED:')) {
        throw "$Name failed: $result"
    } else { Write-Output $result.Trim() }
}
function Test-AssistFault([string]$Name, [string]$Find, [string]$Replacement, [string]$Suite, [string]$Failure) {
    if ($assistOriginal.Split(@($Find), [StringSplitOptions]::None).Length -ne 2) {
        throw "Missing or ambiguous assist fault anchor: $Name"
    }
    $source = Join-Path $assistStage ($Name + '.cs')
    [IO.File]::WriteAllText($source, $assistOriginal.Replace($Find, $Replacement))
    Invoke-Assist $Name $source $Suite $Failure
}
try {
    Invoke-Assist 'production' $assistBoard
    Test-AssistFault 'unknown-loss-omitted' 'victim.TotalDamage += damage.Damage;' `
        'if (damage.AttackerProfileId != null && _entriesByProfile.ContainsKey(damage.AttackerProfileId)) victim.TotalDamage += damage.Damage;' `
        'shares' 'healing ignored and unknown losses remain in denominator'
    $killerGuard = [regex]::Match($assistOriginal,
        'if \(string.Equals\(entry.Key, killer.Key, StringComparison.Ordinal\)\s*\|\| string.Equals\(entry.Key, victim.Key, StringComparison.Ordinal\)\) continue;')
    if (!$killerGuard.Success) { throw 'Missing logical-key killer exclusion for compiled identity fault' }
    Test-AssistFault 'killer-assisted-own-kill' $killerGuard.Value `
        'if (string.Equals(entry.Key, victim.Key, StringComparison.Ordinal)) continue;' `
        'shares' 'killer keeps full reward'
    $reset = [regex]::Match($assistOriginal, '(?s)internal void ResetAssists\(\)\s*\{(?<body>.*?)\n    \}')
    if (!$reset.Success -or !$reset.Groups['body'].Value.Contains('_assistVictims.Clear();')) {
        throw 'Missing ResetAssists implementation for compiled lifecycle fault'
    }
    Test-AssistFault 'round-carried-old-damage' $reset.Value `
        $reset.Value.Replace('_assistVictims.Clear();', '') 'lifecycle' 'assist ledger cleared at round'
    Invoke-Assist 'production-final' $assistBoard
    foreach ($relative in $assistSources) {
        if ((Get-FileHash -LiteralPath (Join-Path $assistRepo $relative)).Hash -ne $assistHashes[$relative]) {
            throw "Bound source changed; rerun required: $relative"
        }
    }
} finally {
    foreach ($path in $assistInstalled.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $assistInstalled[$path]) { throw "Installed binary changed: $path" }
    }
}
[ordered]@{ sourceHashes = $assistHashes; installedHashes = $assistInstalled;
    sourcesUnchanged = $true; installedUnchanged = $true; compiledFaultCount = 3;
    completedUtc = [DateTime]::UtcNow.ToString('o'); evidenceDirectory = $assistStage;
    limitation = 'Full production match, scoreboard, kill DTO, role rules and awards run with explicit native/mode boundary doubles. Native damage attribution and GPU UI require separate verification.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $assistStage 'manifest.json')
Write-Output "ASSIST SCORING CHECKS PASSED; three compiled faults rejected; source and installed binaries unchanged; evidence: $assistStage"
