# Crowd reactions (docs/reviews/CROWD-REACTIONS-20261102.md): the baseline, then hand-made mutants
# of the production sources, each of which must fail its named assertion. Every case builds with the
# no-deploy targets and a sentinel that must not exist (like its siblings, although this project
# references no mod project), into its own output folder under artifacts/. A mutant anchor is compared
# with LF line endings and must match exactly one site; production files are never rewritten.
# -Stage Baseline runs the baseline only (Run-ArenaVerification.ps1 -Harness CrowdReactionChecks:Baseline).
param([ValidateSet('All', 'Baseline')][string]$Stage = 'All')
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'CrowdReactionChecks.csproj'
$evidence = Join-Path $repo ('artifacts/crowd-reaction-checks/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$noDeploy = Join-Path $evidence 'must-not-exist-no-deploy'
$noDeployTargets = Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets'
$sources = [ordered]@{
    CrowdBinderSource = Join-Path $repo 'ArenaMode.Client/Compat/ArenaMapPortSpectatorCrowd.cs'
    CrowdPolicySource = Join-Path $repo 'ArenaMode.Client/Audio/ArenaCrowdReactionPolicy.cs'
    CrowdServiceSource = Join-Path $repo 'ArenaMode.Client/Audio/ArenaCrowdReactions.cs'
    MatchServiceText = Join-Path $repo 'ArenaMode.Client/Modes/ArenaMatchService.cs'
    ControllerText = Join-Path $repo 'ArenaMode.Client/ArenaController.cs'
    SessionText = Join-Path $repo 'ArenaMode.Client/Session/ArenaSession.cs'
}
$hashes = @{}
foreach ($key in $sources.Keys) { $hashes[$key] = (Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash }

function Invoke-Case([string]$Name, [string]$Property, [string]$Copy) {
    $arguments = @('run', '--project', $project, '-c', 'Release', '--nologo',
        "-p:CustomAfterMicrosoftCommonTargets=$noDeployTargets", "-p:SPTUserMods=$noDeploy",
        "-p:OutputPath=$(Join-Path $evidence ($Name + '-bin/'))",
        "-p:IntermediateOutputPath=$(Join-Path $evidence ($Name + '-obj/'))")
    if ($Property) { $arguments += "-p:$Property=$Copy" }
    $output = & dotnet @arguments 2>&1
    $code = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $evidence ($Name + '.log')), $text)
    if (Test-Path -LiteralPath $noDeploy) { throw "The no-deploy sentinel was written by case $Name." }
    return @{ Exit = $code; Text = $text }
}

$baseline = Invoke-Case 'baseline' '' ''
if ($baseline.Exit -ne 0 -or $baseline.Text -notmatch 'CROWD_REACTION_CHECKS_PASSED') { throw $baseline.Text }
Write-Output (($baseline.Text -split "`n") | Where-Object { $_ -match 'CROWD_REACTION_CHECKS_PASSED' })
if ($Stage -eq 'Baseline') { Write-Output "CROWD_REACTION_CHECKS_BASELINE: evidence $evidence"; return }

$cases = @(
    @{ Name='claim-not-released-on-native-stop'; Property='CrowdServiceSource';
       Find='internal static void OnNativePresentationStop() => Teardown("native stop");';
       Replace='internal static void OnNativePresentationStop() { }';
       Failure='native presentation stop releases the claim without a tick' },
    @{ Name='match-service-native-stop-unwired'; Property='MatchServiceText';
       Find="            if (Current?.Mode is TeamDeathmatchMode team) team.OnNativePresentationStop();`n            Audio.ArenaCrowdReactions.OnNativePresentationStop();`n";
       Replace="            if (Current?.Mode is TeamDeathmatchMode team) team.OnNativePresentationStop();`n";
       Failure='ArenaMatchService releases the crowd on both early returns' },
    @{ Name='raid-end-detach-missing'; Property='MatchServiceText';
       Find="        // Before the null-match return: a native stop or failed start may have left no Current.`n        Try(Audio.ArenaCrowdReactions.Detach, `"crowd reactions detach`");`n";
       Replace='';
       Failure='raid end detaches the crowd before its null-match return' },
    @{ Name='finish-does-not-drop-approve'; Property='CrowdPolicySource';
       Find='if (_strength[Start] <= 0f && _strength[Finish] <= 0f) return;'; Replace='return;';
       Failure="a Finish drops the frame's Approve and Disapprove" },
    @{ Name='excitement-requires-reaction-members'; Property='CrowdBinderSource';
       Find='var excitement = excitementChanged ? Part.Incompatible : excite != null ? Part.Bound : Part.Absent;';
       Replace='var excitement = excitementChanged ? Part.Incompatible : excite != null && Present(type, "React") ? Part.Bound : Part.Absent;';
       Failure='ArenaMapPort 0.4.0 surface binds excitement without the reaction members' },
    @{ Name='no-gate-on-current-match'; Property='CrowdServiceSource';
       Find='if (!ReferenceEquals(match, ArenaMatchService.Current) || ArenaMatchService.TeamStartAbortPending';
       Replace='if (ArenaMatchService.TeamStartAbortPending';
       Failure='no push and release when the match is no longer current' },
    @{ Name='zombies-claims'; Property='CrowdServiceSource';
       Find='if (match.Mode is ZombieMode)'; Replace='if (false)';
       Failure='Zombies: no bind, no claim, no excitement' },
    @{ Name='death-reported-twice'; Property='CrowdServiceSource';
       Find='private static bool TakeDeath(long bodyId) => bodyId == 0 || HandledDeaths.Add(bodyId);';
       Replace='private static bool TakeDeath(long bodyId) => true;';
       Failure='local death notified in two frames still gives one Disapprove' },
    @{ Name='interlock-weakens-round-events'; Property='CrowdServiceSource';
       Find='bool weakened = announcer && ArenaCrowdReactionPolicy.IsInterlocked(kind);';
       Replace='bool weakened = announcer;';
       Failure='interlock halves Approve and Disapprove only' },
    @{ Name='claims-without-reaction-kinds'; Property='CrowdBinderSource';
       Find='if ((kinds & wanted) == 0) return null;'; Replace='';
       Failure='no claim on a map without reaction kinds' },
    @{ Name='unbounded-claim-retry'; Property='CrowdServiceSource';
       Find='if (now > _windowEndsAt)'; Replace='if (false)';
       Failure='claim attempts stay within the 30 s window, at most once per second' },
    @{ Name='react-refusal-keeps-claim'; Property='CrowdBinderSource';
       Find="if (_react(Handle, kind, strength)) return true;`n                Release();`n                return false;";
       Replace='return _react(Handle, kind, strength) || true;';
       Failure='refused reaction releases the claim' },
    @{ Name='excitement-not-reset-on-release'; Property='CrowdServiceSource';
       Find='try { excite(0f); }'; Replace='try { }';
       Failure='native presentation stop calms the murmur' },
    @{ Name='headshot-callout-at-award-strength'; Property='CrowdPolicySource';
       Find='"HEADSHOT" => StrongKillStrength,'; Replace='"HEADSHOT" => AwardStrength,';
       Failure='HEADSHOT stays on the strong-kill path' },
    @{ Name='bot-kills-react'; Property='CrowdPolicySource';
       Find='if (kill.KillerIsFriendly == true && kill.VictimIsFriendly == false)'; Replace='if (true)';
       Failure='bot-versus-bot kills are ignored' },
    @{ Name='unscored-kill-reacts'; Property='CrowdPolicySource';
       Find='if (kill == null || !kill.ScoreEligible) return false;'; Replace='if (kill == null) return false;';
       Failure='unscored kills are ignored' },
    @{ Name='terminal-round-end-sends-finish'; Property='CrowdServiceSource';
       Find='if (rounds == null || rounds.IsComplete) return;'; Replace='if (rounds == null) return;';
       Failure='terminal Deathmatch round end itself sends nothing' },
    @{ Name='second-finish-per-round'; Property='CrowdServiceSource';
       Find="if (!_finishArmed) return false;`n            _finishArmed = false;"; Replace='_finishArmed = false;';
       Failure='one Finish per round' },
    @{ Name='key-off-ignored'; Property='CrowdServiceSource';
       Find='if (!ArenaSession.CrowdReactionsSelection)'; Replace='if (false)';
       Failure='key off: nothing' },
    @{ Name='excitement-every-frame'; Property='CrowdServiceSource';
       Find='_nextExcitementAt = now + ExcitementPollSeconds;'; Replace='_nextExcitementAt = now;';
       Failure='excitement polls at most once per second' },
    @{ Name='bedless-map-written'; Property='CrowdServiceSource';
       Find='if (contract.IsAvailable != null && !contract.IsAvailable()) return;'; Replace='';
       Failure='no excitement write while the bed is unavailable' },
    @{ Name='convertible-signature-binds'; Property='CrowdBinderSource';
       Find='if (parameters[index].ParameterType != args[index]) return null;'; Replace='if (false) return null;';
       Failure='a changed reaction member switches reactions off with one warning per process' },
    @{ Name='kind-shift-unchecked'; Property='CrowdBinderSource';
       Find='internal static int Bit(int kind) => kind >= LowestKind && kind <= HighestKind ? 1 << kind : 0;';
       Replace='internal static int Bit(int kind) => 1 << kind;';
       Failure='kinds outside 1..31 have no bit' },
    @{ Name='owner-longer-than-forty'; Property='CrowdServiceSource';
       Find='private const int OwnerHexDigits = 31;'; Replace='private const int OwnerHexDigits = 32;';
       Failure="owner is unique per match and fits MapPort's 40 characters" },
    @{ Name='controller-tick-missing'; Property='ControllerText';
       Find="        Audio.ArenaCrowdReactions.Tick();`n"; Replace='';
       Failure='the crowd ticks every frame right after the announcer' },
    @{ Name='key-not-frozen-at-arm'; Property='SessionText';
       Find="        CrowdReactionsSelection = Settings.CrowdReactions?.Value == true;`n"; Replace='';
       Failure='the key is frozen at arm and cleared with the announcer selection' }
)
foreach ($case in $cases) {
    $source = [IO.File]::ReadAllText($sources[$case.Property]).Replace("`r`n", "`n")
    $find = ([string]$case.Find).Replace("`r`n", "`n")
    $sites = [regex]::Matches($source, [regex]::Escape($find)).Count
    if ($sites -ne 1) { throw "Mutation anchor matches $sites sites (expected exactly one): $($case.Name)" }
    $copy = Join-Path $evidence ($case.Name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($find, ([string]$case.Replace).Replace("`r`n", "`n")))
    $result = Invoke-Case $case.Name $case.Property $copy
    if ($result.Exit -eq 0 -or $result.Text -match 'error CS' -or
        -not $result.Text.Contains('FAIL: ' + $case.Failure)) {
        throw "Mutant was not killed by its named assertion: $($case.Name)`n$($result.Text)"
    }
    Write-Output "CROWD_REACTION_MUTANT_REJECTED: $($case.Name) -> $($case.Failure)"
}
foreach ($key in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) {
        throw "A production source changed during the check: $($sources[$key])"
    }
}
Write-Output "CROWD_REACTION_CHECKS_VERIFIED: $($cases.Count) mutants rejected; evidence $evidence"
