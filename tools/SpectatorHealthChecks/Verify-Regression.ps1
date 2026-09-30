param()
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SpectatorHealthChecks.csproj'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source = Join-Path $repo 'ArenaMode.Client/Spectator/SpectatorHealthReadout.cs'
$before = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('arena-spectator-health-mutations-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$effectsPath = Join-Path $repo 'ArenaMode.Client/Spectator/SpectatorHealthEffects.cs'
$effectsBefore = (Get-FileHash -LiteralPath $effectsPath -Algorithm SHA256).Hash
$effects = [IO.File]::ReadAllText($effectsPath)
$contextStart = $effects.IndexOf('    internal static bool HasExactReplayContext(', [StringComparison]::Ordinal)
if ($contextStart -lt 0) { throw 'Replay visual-context production method missing.' }
$openBrace = $effects.IndexOf('{', $contextStart)
$depth = 1
$contextEnd = $openBrace + 1
while ($contextEnd -lt $effects.Length -and $depth -gt 0) {
    if ($effects[$contextEnd] -eq '{') { $depth++ }
    elseif ($effects[$contextEnd] -eq '}') { $depth-- }
    $contextEnd++
}
if ($depth -ne 0) { throw 'Replay visual-context method boundary malformed.' }
$context = $effects.Substring($contextStart, $contextEnd - $contextStart)
$contextHeader = "using System; using ArenaMode.Session; using Comfort.Common; using EFT;`nnamespace ArenaMode.Spectator;`ninternal static class SpectatorHealthEffects {`n"
$replayContextSource = Join-Path $scratch 'ReplayEffectsScope.cs'
[IO.File]::WriteAllText($replayContextSource, $contextHeader + $context + "`n}")
if (!$effects.Contains('ReferenceEquals(BoundPlayerField.GetValue(controller), expectedDeadPlayer)') -or
    !$effects.Contains('return TryClearVisualState(expectedDeadPlayer, out problem);') -or
    !$effects.Contains('!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive')) {
    throw 'Shared visual cleanup must retain bound EffectsController and original PFT scope.'
}

function Invoke-Checks([string[]] $properties, [string] $contextSource = $replayContextSource) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet run --project $project --configuration Release "-p:ReplayContextSource=$contextSource" @properties 2>&1 |
            ForEach-Object { $_.ToString() } | Out-String -Width 10000
        return @{ Output = $output; Code = $LASTEXITCODE }
    }
    finally { $ErrorActionPreference = $previous }
}

$baseline = Invoke-Checks @()
if ($baseline.Code -ne 0 -or $baseline.Output -notmatch 'ARENA_SPECTATOR_HEALTH_CHECKS_PASSED') {
    throw ('Spectator health baseline failed: ' + $baseline.Output)
}

$cases = @(
    @{
        Name = 'same-profile-replacement';
        Find = '|| !ReferenceEquals(player, frozenBody)'; Replace = '|| false';
        Failure = 'same profile on a different frozen body cannot authorize target HP'
    },
    @{
        Name = 'ended-epoch';
        Find = '&& request.ReadEpoch() == request.Epoch'; Replace = '&& true';
        Failure = 'round epoch drift hides a live old-round body'
    },
    @{
        Name = 'camera-release';
        Find = '|| !ownsCamera'; Replace = '|| false';
        Failure = 'stop or killcam ownership hides the prior readout'
    },
    @{
        Name = 'wrong-body-part';
        Find = 'health.GetBodyPartHealth(EBodyPart.Common)'; Replace = 'health.GetBodyPartHealth(EBodyPart.Head)';
        Failure = 'native read must aggregate Common, not one body part'
    },
    @{
        Name = 'nonfinite-health';
        Find = 'float.IsNaN(total.Current) || float.IsInfinity(total.Current)'; Replace = 'false';
        Failure = 'malformed native health must replace old HP with unavailable'
    },
    @{
        Name = 'replaced-controller';
        Find = '!ReferenceEquals(health, player.HealthController)'; Replace = 'false';
        Failure = 'controller replacement during read cannot display prior controller HP'
    }
)

foreach ($case in $cases) {
    $text = [IO.File]::ReadAllText($source)
    if (($text.Split(@($case.Find), [StringSplitOptions]::None).Length - 1) -ne 1) {
        throw ('Mutation anchor must occur exactly once: ' + $case.Name)
    }
    $copy = Join-Path $scratch ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $text.Replace($case.Find, $case.Replace))
    $result = Invoke-Checks @('-p:ReadoutSource=' + $copy)
    if ($result.Code -eq 0 -or $result.Output -notmatch [regex]::Escape($case.Failure)) {
        throw ('Mutation did not fail for its intended assertion: ' + $case.Name + [Environment]::NewLine + $result.Output)
    }
    Write-Output ('PASS mutation: ' + $case.Name)
}

$scopeCases = @(
    @{ Name='ordinary-replay-visuals'; Find='!ArenaSession.IsActive || '; Replace=''; Failure='replay visuals reject ordinary raid' },
    @{ Name='replaced-world-visuals'; Find='&& ReferenceEquals(world.MainPlayer, expectedDeadPlayer)'; Replace=''; Failure='replay visuals reject replaced world body' },
    @{ Name='idle-camera-visuals'; Find='&& KillcamReplayCompat.ReadState(out _, out _) == OptionalCameraOwnerState.Busy'; Replace=''; Failure='replay visuals reject idle camera' }
)
foreach ($case in $scopeCases) {
    if (($context.Split(@($case.Find), [StringSplitOptions]::None).Length - 1) -ne 1) {
        throw ('Replay visual mutation anchor must occur once: ' + $case.Name)
    }
    $copy = Join-Path $scratch ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $contextHeader + $context.Replace($case.Find, $case.Replace) + "`n}")
    $result = Invoke-Checks @() $copy
    if ($result.Code -eq 0 -or $result.Output -match 'error CS' -or $result.Output -notmatch [regex]::Escape($case.Failure)) {
        throw ('Replay visual mutation did not fail intended assertion: ' + $case.Name + [Environment]::NewLine + $result.Output)
    }
    Write-Output ('PASS mutation: ' + $case.Name)
}
if ($effectsBefore -ne (Get-FileHash -LiteralPath $effectsPath -Algorithm SHA256).Hash) {
    throw 'Production visual-effects source changed during regression.'
}

if ($before -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
    throw 'Production source changed during the regression runner.'
}
$final = Invoke-Checks @()
if ($final.Code -ne 0 -or $final.Output -notmatch 'ARENA_SPECTATOR_HEALTH_CHECKS_PASSED') {
    throw ('Unchanged production source failed after mutation controls: ' + $final.Output)
}
Write-Output $final.Output.Trim()
Write-Output 'ARENA_SPECTATOR_HEALTH_REGRESSION_PASSED'
