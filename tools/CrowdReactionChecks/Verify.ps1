# Crowd reactions (docs/reviews/CROWD-REACTIONS-20261102.md): the baseline, then hand-made mutants
# of the production sources, each of which must fail its named assertion. Every case builds with the
# no-deploy targets and a sentinel that must not exist (like its siblings, although this project
# references no mod project), into its own output folder under artifacts/. A mutant anchor is compared
# with LF line endings and must match exactly one site; production files are never rewritten.
# -Stage Baseline runs the baseline only (Run-ArenaVerification.ps1 -Harness CrowdReactionChecks:Baseline).
# The program's last stage loads the installed ArenaMapPort client read-only (installed data, like
# IcebergChecks): it must exist and carry crowd reactions phase A (MapPort d70acd2 or later), and its
# hash must not change during the check.
param([ValidateSet('All', 'Baseline')][string]$Stage = 'All', [string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) { $PSNativeCommandUseErrorActionPreference = $false }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'CrowdReactionChecks.csproj'
$mapPortClient = Join-Path $SptRoot 'BepInEx/plugins/maschine-ArenaMapPort/maschine-ArenaMapPort.Client.dll'
if (!(Test-Path -LiteralPath $mapPortClient)) {
    throw "Installed ArenaMapPort client is missing: $mapPortClient. The real-surface stage of CrowdReactionChecks needs an ArenaMapPort client with crowd reactions phase A (D:\ArenaMapPort d70acd2 or later)."
}
$mapPortHash = (Get-FileHash -LiteralPath $mapPortClient -Algorithm SHA256).Hash
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
        "-p:MapPortClientDll=$mapPortClient",
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
Write-Output (($baseline.Text -split "`n") | Where-Object { $_ -match 'CROWD_REACTION_REAL_SURFACE|CROWD_REACTION_CHECKS_PASSED' })
function Assert-MapPortUnchanged {
    if ((Get-FileHash -LiteralPath $mapPortClient -Algorithm SHA256).Hash -ne $mapPortHash) {
        throw "The installed ArenaMapPort client changed during the check: $mapPortClient"
    }
}
if ($Stage -eq 'Baseline') { Assert-MapPortUnchanged; Write-Output "CROWD_REACTION_CHECKS_BASELINE: evidence $evidence"; return }

# Fault control: without the installed client the program fails (it never skips the real surface)
# and names MapPort's requirement. The later -p wins over the installed path Invoke-Case passes.
$absentClient = Join-Path $evidence 'no-mapport/maschine-ArenaMapPort.Client.dll'
$absent = Invoke-Case 'mapport-client-absent' 'MapPortClientDll' $absentClient
if ($absent.Exit -eq 0 -or $absent.Text -match 'error CS' -or $absent.Text -match 'CROWD_REACTION_CHECKS_PASSED' -or
    -not $absent.Text.Contains('FAIL: the installed ArenaMapPort client is missing') -or -not $absent.Text.Contains('d70acd2')) {
    throw "A missing ArenaMapPort client did not fail the real-surface stage loudly:`n$($absent.Text)"
}
Write-Output 'CROWD_REACTION_REAL_SURFACE_FAULT_REJECTED: absent ArenaMapPort client -> FAIL naming phase A (d70acd2 or later)'

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
       Find='if (!HasRoundEvent) return;'; Replace='return;';
       Failure="a Finish drops the frame's Approve and Disapprove" },
    @{ Name='moments-not-held'; Property='CrowdServiceSource';
       Find="            send = _held;`n            _held = _pending;`n            _pending = send;`n"; Replace='';
       Failure='an own kill waits one tick for a result published a tick later' },
    @{ Name='held-moment-survives-result'; Property='CrowdServiceSource';
       Find="            _held.Clear();`n            _pending.Resolve();"; Replace='            _pending.Resolve();';
       Failure='a deciding kill one tick before its result gives Finish only' },
    @{ Name='finish-latch-ignores-epoch'; Property='CrowdServiceSource';
       Find='int epoch = RoundEpoch();'; Replace='int epoch = 0;';
       Failure='a round without a RoundStart cue still sends its result' },
    @{ Name='cue-subscription-missing'; Property='CrowdServiceSource';
       Find="            _cueListening = true;`n            match.OnAnnouncerCue += OnAnnouncerCue;`n"; Replace="            _cueListening = true;`n";
       Failure='a bound reaction part listens to the match' },
    @{ Name='murmur-ignores-no-contest'; Property='CrowdServiceSource';
       Find='if (!_cueListening || _silenced) return;'; Replace='if (!_listening || _silenced) return;';
       Failure='0.4.0 surface: no contest calms the murmur at once' },
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
       Find='if (kill.KilledByLocal)'; Replace='if (kill.KilledByLocal || kill.KillerIsFriendly.HasValue)';
       Failure='bot-versus-bot kills are ignored' },
    @{ Name='teammate-kills-react'; Property='CrowdPolicySource';
       Find='if (kill.KilledByLocal)'; Replace='if (kill.KilledByLocal || kill.KillerIsFriendly == true && kill.VictimIsFriendly == false)';
       Failure="a teammate's kill of an opponent is silent" },
    @{ Name='unscored-kill-reacts'; Property='CrowdPolicySource';
       Find='if (kill == null || !kill.ScoreEligible) return false;'; Replace='if (kill == null) return false;';
       Failure='unscored kills are ignored' },
    @{ Name='terminal-round-end-sends-finish'; Property='CrowdServiceSource';
       Find='if (rounds == null || rounds.IsComplete) return;'; Replace='if (rounds == null) return;';
       Failure='terminal Deathmatch round end itself sends nothing' },
    @{ Name='second-finish-per-round'; Property='CrowdServiceSource';
       Find='if (_finishEpoch == epoch) return false;'; Replace='';
       Failure='one Finish per round' },
    @{ Name='key-off-ignored'; Property='CrowdServiceSource';
       Find='if (!ArenaSession.CrowdReactionsSelection)'; Replace='if (false)';
       Failure='key off: nothing' },
    @{ Name='excitement-every-frame'; Property='CrowdServiceSource';
       Find='_nextSnapshotAt = now + SnapshotPollSeconds;'; Replace='_nextSnapshotAt = now;';
       Failure='excitement polls at most once per second' },
    @{ Name='bedless-map-written'; Property='CrowdServiceSource';
       Find='if (contract.IsAvailable != null && !contract.IsAvailable()) return;'; Replace='';
       Failure='no excitement write while the bed is unavailable' },
    # Without the parameter check the double React makes CreateDelegate throw (bind failed), so the
    # first assertion to fail is the changed-member one; the narrower mutant below keeps value-type
    # mismatches out and proves the contravariant object claim on its own.
    @{ Name='parameter-type-unchecked'; Property='CrowdBinderSource';
       Find='if (parameters[index].ParameterType != args[index]) return null;'; Replace='if (false) return null;';
       Failure='a changed reaction member switches reactions off with one warning per process' },
    @{ Name='contravariant-parameter-binds'; Property='CrowdBinderSource';
       Find='if (parameters[index].ParameterType != args[index]) return null;';
       Replace='if (!parameters[index].ParameterType.IsAssignableFrom(args[index])) return null;';
       Failure='contravariant object claim is not the exact string contract' },
    @{ Name='return-type-unchecked'; Property='CrowdBinderSource';
       Find='if (method == null || method.ReturnType != returns || method.ContainsGenericParameters) return null;';
       Replace='if (method == null || method.ContainsGenericParameters) return null;';
       Failure='a changed return type switches only the reactions off' },
    @{ Name='presence-public-only'; Property='CrowdBinderSource';
       Find='private static bool Present(Type type, string name) => type.GetMember(name, AnyMember).Length > 0;';
       Replace='private static bool Present(Type type, string name) => type.GetMember(name, Declared).Length > 0;';
       Failure='an internal reaction member is a changed member, not an older MapPort' },
    @{ Name='no-version-warning-missing'; Property='CrowdBinderSource';
       Find='Warn(ref _warnedNoVersion, '; Replace='if (false) Warn(ref _warnedNoVersion, ';
       Failure='a contract without an int ContractVersion property binds nothing' },
    @{ Name='kind-shift-unchecked'; Property='CrowdBinderSource';
       Find='internal static int Bit(int kind) => kind >= LowestKind && kind <= HighestKind ? 1 << kind : 0;';
       Replace='internal static int Bit(int kind) => 1 << kind;';
       Failure='kinds outside 1..31 have no bit' },
    @{ Name='owner-longer-than-forty'; Property='CrowdServiceSource';
       Find='private const int OwnerHexDigits = 31;'; Replace='private const int OwnerHexDigits = 32;';
       Failure='owner is unique per match and exactly 40 characters' },
    @{ Name='controller-tick-missing'; Property='ControllerText';
       Find="        Audio.ArenaCrowdReactions.Tick();`n"; Replace='';
       Failure='the crowd ticks every frame right after the announcer' },
    @{ Name='key-not-frozen-at-arm'; Property='SessionText';
       Find="        CrowdReactionsSelection = Settings.CrowdReactions?.Value == true;`n"; Replace='';
       Failure='the key is frozen at arm and cleared with the announcer selection' },
    # Revised plan (ArenaMapPort 08964c1): a lost lead below the 0.4 band, and the Lull trigger (D14).
    @{ Name='lead-lost-at-death-strength'; Property='CrowdPolicySource';
       Find='ArenaAnnouncerCue.LostTheLead => (Disapprove, LeadLostStrength),';
       Replace='ArenaAnnouncerCue.LostTheLead => (Disapprove, DeathStrength),';
       Failure='cue mapping LostTheLead' },
    @{ Name='lull-survives-round-event'; Property='CrowdPolicySource';
       Find="            _strength[Lull] = 0f;`n"; Replace='';
       Failure="a Finish drops the frame's Lull" },
    @{ Name='lull-clock-survives-release'; Property='CrowdServiceSource';
       Find="        _quietSince = null;`n        ClearBuffers();`n        if (claim != null && claim.Release())";
       Replace="        ClearBuffers();`n        if (claim != null && claim.Release())";
       Failure='a release clears the quiet clock: a new match counts its quiet afresh' },
    @{ Name='lull-rate-limit-removed'; Property='CrowdServiceSource';
       Find=' || now < _nextLullAt) return;'; Replace=') return;';
       Failure='no second Lull within 90 s of the first' },
    @{ Name='lull-ignores-kill-reset'; Property='CrowdServiceSource';
       Find="            ResetQuiet();`n            if (kill.LocalIsVictim && !TakeDeath(kill.VictimBodyId)) return;";
       Replace='            if (kill.LocalIsVictim && !TakeDeath(kill.VictimBodyId)) return;';
       Failure='a bot-versus-bot kill resets the quiet clock' },
    @{ Name='lull-while-not-live'; Property='CrowdServiceSource';
       Find='if (!ArenaCrowdReactionPolicy.IsLiveCombat(snapshot)) { _quietSince = null; return; }'; Replace='';
       Failure='no Lull outside live combat: preparation, a result, no snapshot' },
    @{ Name='lull-not-restarted-at-start'; Property='CrowdServiceSource';
       Find='if (kind == Start) _quietSince = Time.unscaledTime;'; Replace='if (false) _quietSince = Time.unscaledTime;';
       Failure='a Start restarts the quiet clock' },
    @{ Name='lull-not-cleared-at-finish'; Property='CrowdServiceSource';
       Find='else if (kind == Finish) _quietSince = null;'; Replace='';
       Failure='a Finish stops the quiet clock' },
    # Flush and ReactionClaim.Push also skip a kind outside the claimed mask, so without this gate a
    # Lull would still never be sent; what the gate keeps is the dormancy: no snapshot read for it.
    @{ Name='lull-without-mask-bit'; Property='CrowdServiceSource';
       Find='bool lull = !_lullOff && _claim != null && _claim.Has(Lull);';
       Replace='bool lull = !_lullOff && _claim != null;';
       Failure='a claim without the Lull bit keeps the Lull dormant: no snapshot read, no Lull' },
    @{ Name='lull-not-wanted'; Property='CrowdServiceSource';
       Find='| Bit(Finish) | Bit(Lull);'; Replace='| Bit(Finish);';
       Failure='a Lull-only mask is claimed' },
    # Review of 484b095a: a kill or a local death withdraws a decided, unsent Lull from either frame,
    # and the 90 s gap runs from a Lull that went out.
    @{ Name='lull-not-withdrawn-at-kill'; Property='CrowdServiceSource';
       Find="        _pending.TryTake(Lull, out _, out _);`n"; Replace='';
       Failure="a kill in the Lull's tick withdraws it: only the kill's Approve goes out" },
    @{ Name='held-lull-not-withdrawn'; Property='CrowdServiceSource';
       Find="        _held.TryTake(Lull, out _, out _);`n"; Replace='';
       Failure='a bot-versus-bot kill one tick after the Lull withdraws the held Lull' },
    @{ Name='lull-gap-from-decision'; Property='CrowdServiceSource';
       Find='_pending.Add(Lull, ArenaCrowdReactionPolicy.LullStrength, "no kill for 60 s");';
       Replace='_pending.Add(Lull, ArenaCrowdReactionPolicy.LullStrength, "no kill for 60 s"); _nextLullAt = now + ArenaCrowdReactionPolicy.LullGapSeconds;';
       Failure='a withdrawn Lull does not count against the 90 s gap' },
    @{ Name='lull-gap-not-stamped'; Property='CrowdServiceSource';
       Find='if (kind == Lull) _nextLullAt = now + ArenaCrowdReactionPolicy.LullGapSeconds;'; Replace='';
       Failure='no second Lull within 90 s of the first' },
    # One mood per flush (MapPort's roar layer, e3e4290): the service's call, the rule itself, the tie,
    # the waiting frame's share of the decision, and the drop from the waiting frame.
    @{ Name='two-moods-per-flush'; Property='CrowdServiceSource';
       Find='if (ArenaCrowdReactionPolicy.Frame.KeepOneMood(send, _held, out int dropped, out float droppedStrength, out string droppedSource))';
       Replace='if (false && ArenaCrowdReactionPolicy.Frame.KeepOneMood(send, _held, out int dropped, out float droppedStrength, out string droppedSource))';
       Failure='a trade in one tick sends the death only: one mood per flush' },
    @{ Name='mood-rule-disabled'; Property='CrowdPolicySource';
       Find='if (!(approve > 0f) || !(disapprove > 0f)) return false;'; Replace='return false;';
       Failure='one mood per frame: the weaker Approve is dropped for the death' },
    @{ Name='mood-tie-boos'; Property='CrowdPolicySource';
       Find='dropped = approve >= disapprove ? Disapprove : Approve;'; Replace='dropped = approve > disapprove ? Disapprove : Approve;';
       Failure='on a tie Approve stays (plan 4.1 order)' },
    @{ Name='mood-ignores-waiting-frame'; Property='CrowdPolicySource';
       Find='float disapprove = Math.Max(going._strength[Disapprove], waitingDisapprove);'; Replace='float disapprove = going._strength[Disapprove];';
       Failure='across the frames: the going Approve yields to the waiting death' },
    @{ Name='mood-kept-in-waiting-frame'; Property='CrowdPolicySource';
       Find='            waiting?.Drop(dropped);'; Replace='';
       Failure='across the frames: the strongest entry decides, the other kind leaves both frames, the drop names its strongest entry' },
    # The mood window across flushes: no window, a window that drops the stronger opposite mood too,
    # and a Start or Finish that does not end it.
    @{ Name='mood-window-ignored'; Property='CrowdServiceSource';
       Find='if (_moodKind != 0 && _moodKind != kind && now - _moodAt < ArenaCrowdReactionPolicy.MoodWindowSeconds';
       Replace='if (_moodKind != 0 && _moodKind != kind && false';
       Failure='a weaker kill within a second of the death is dropped' },
    @{ Name='mood-window-drops-stronger'; Property='CrowdServiceSource';
       Find='                    && strength <= _moodStrength)'; Replace='                    )';
       Failure='a stronger death within a second of the kill goes out: the crowd changes its mind' },
    @{ Name='round-event-keeps-mood-window'; Property='CrowdServiceSource';
       Find='            else _moodKind = 0;   // a Start, a Finish or a Lull is a new moment'; Replace='';
       Failure='a Start ends the window: the kill right after it goes out' },
    @{ Name='mood-memory-overwritten'; Property='CrowdServiceSource';
       Find='if (!weakerSameKind)'; Replace='if (true)';
       Failure='a weaker same-kind mood inside the window keeps the stronger memory: the death is still dropped' },
    @{ Name='mood-memory-outlives-match'; Property='CrowdServiceSource';
       Find="        _moodKind = 0;`n        _moodStrength = _moodAt = 0f;`n"; Replace='';
       Failure='a new match starts without a mood memory' },
    # Real surface (installed ArenaMapPort, phase A): the stand-ins record whatever kind ArenaMode
    # sends, so a Start/Finish swap passes them all; only MapPort's own numbering rejects it.
    @{ Name='kind-numbers-not-mapport'; Property='CrowdBinderSource';
       Find='internal const int Approve = 1, Disapprove = 2, TakeCover = 3, Start = 4, Finish = 5, Lull = 6;';
       Replace='internal const int Approve = 1, Disapprove = 2, TakeCover = 3, Start = 5, Finish = 4, Lull = 6;';
       Failure="real surface: ArenaMode's kind numbers are MapPort's CrowdReactionKind numbers" }
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
Assert-MapPortUnchanged
Write-Output "CROWD_REACTION_CHECKS_VERIFIED: $($cases.Count) mutants rejected; evidence $evidence"
