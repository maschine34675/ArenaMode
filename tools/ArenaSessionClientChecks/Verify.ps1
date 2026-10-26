$ErrorActionPreference = 'Stop'
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# Client side of the Arena session feature (docs/ARENA-SESSION-20261021.md, section 7).
#
# 1. Runs the harness against the production sources: the session state, the whitelist admission
#    and the session switch are compiled unchanged; the game, BepInEx and the server ping are
#    stand-ins. The Unity/Harmony wiring is pinned by source text.
# 2. Mutates one production seam at a time, in a copy outside the repository (a compiler input only,
#    never a rewrite of production files), and requires the harness to fail on the NAMED assertion.
#    A mutant that does not compile, or fails some other way, is itself a failure of this script.
# 3. Hash-checks every production file afterwards.
#
# Every build passes the no-deploy properties, so nothing here can reach a game installation.
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'ArenaSessionClientChecks.csproj'
$noDeploy = Join-Path $repo 'tools\AirPitChecks\NoDeploy.targets'
$mustNotExist = Join-Path $repo 'artifacts\nodeploy\must-not-exist'
$safety = @("-p:CustomAfterMicrosoftCommonTargets=$noDeploy", "-p:SPTUserMods=$mustNotExist")

$paths = [ordered]@{
    decision  = Join-Path $repo 'ArenaMode.Shared\Session\ArenaWhitelistDecision.cs'
    mode      = Join-Path $repo 'ArenaMode.Client\Session\ArenaSessionMode.cs'
    admission = Join-Path $repo 'ArenaMode.Client\Session\ArenaWhitelistAdmission.cs'
    switch    = Join-Path $repo 'ArenaMode.Client\Session\ArenaSessionSwitch.cs'
    side      = Join-Path $repo 'ArenaMode.Client\Session\ArenaSideSelectionPatch.cs'
    notice    = Join-Path $repo 'ArenaMode.Client\Session\ArenaAvailabilityNotice.cs'
    optional  = Join-Path $repo 'ArenaMode.Client\Compat\ArenaOptionalMods.cs'
}
# How a mutated copy reaches the harness: an MSBuild property (compiled sources) or an environment variable (pinned sources).
$channel = @{
    decision  = @{ Property = 'ProductionDecisionSource' }
    mode      = @{ Property = 'ProductionModeSource' }
    admission = @{ Property = 'ProductionAdmissionSource' }
    switch    = @{ Property = 'ProductionSwitchSource'; Variable = 'ARENA_SESSION_SWITCH_SOURCE' }
    side      = @{ Variable = 'ARENA_SESSION_SIDE_SOURCE' }
    notice    = @{ Variable = 'ARENA_SESSION_NOTICE_SOURCE' }
    optional  = @{ Variable = 'ARENA_SESSION_OPTIONAL_SOURCE' }
}
$sources = @{}
$hashes = @{}
foreach ($key in $paths.Keys) {
    $sources[$key] = [IO.File]::ReadAllText($paths[$key]).Replace("`r`n", "`n")
    $hashes[$key] = (Get-FileHash -LiteralPath $paths[$key] -Algorithm SHA256).Hash
}
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('ArenaSessionClientMutation-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($scratch) | Out-Null

function Invoke-Harness([string]$name, [hashtable]$extraProperties, [hashtable]$environment) {
    $arguments = @('run', '--project', $project, '-c', 'Release') + $safety
    if ($name) {
        $arguments += "-p:OutputPath=$(Join-Path $scratch ($name + '-bin\'))"
        $arguments += "-p:IntermediateOutputPath=$(Join-Path $scratch ($name + '-obj\'))"
    }
    foreach ($property in $extraProperties.Keys) { $arguments += "-p:$property=$($extraProperties[$property])" }
    $environment = $environment.Clone()
    $environment['ARENA_REPO_ROOT'] = $repo
    foreach ($variable in $environment.Keys) { Set-Item -Path ('Env:' + $variable) -Value $environment[$variable] }
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & dotnet @arguments 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
        foreach ($variable in $environment.Keys) { Remove-Item -Path ('Env:' + $variable) -ErrorAction SilentlyContinue }
    }
    return @{ Exit = $exit; Text = (($output | ForEach-Object { [string]$_ }) -join "`n") }
}

$script:mutants = 0
function Invoke-Mutant([string]$name, [string]$kind, [string]$from, [string]$to, [string]$expected) {
    $source = $sources[$kind]
    $count = ($source.Split([string[]]@($from), [StringSplitOptions]::None)).Count - 1
    if ($count -ne 1) { throw "Expected exactly one mutation site for $name; found $count." }
    $copy = Join-Path $scratch ($name + '.cs')
    [IO.File]::WriteAllText($copy, $source.Replace($from, $to), [Text.UTF8Encoding]::new($false))
    $properties = @{}
    $environment = @{}
    # A compiled source that is also pinned by text (the switch) is replaced on both channels.
    if ($channel[$kind].Property) { $properties[$channel[$kind].Property] = $copy }
    if ($channel[$kind].Variable) { $environment[$channel[$kind].Variable] = $copy }
    $result = Invoke-Harness $name $properties $environment
    if ($result.Exit -eq 0 -or $result.Text -match 'error CS' -or $result.Text -notmatch 'ARENA_SESSION_CLIENT_CHECKS_FAILED' `
            -or $result.Text -notmatch [regex]::Escape($expected)) {
        throw "$name did not fail its intended assertion '$expected' (exit $($result.Exit)). Evidence: $scratch`n$($result.Text)"
    }
    $script:mutants++
    Write-Output "Rejected mutant: $name"
}

# Swaps two ADJACENT blocks of a production source (the checks of a chain run in source order, so the order is part of
# the contract). Block A runs from $startA up to $startB, block B from $startB up to $endB; all three markers must be unique.
function Invoke-SwapMutant([string]$name, [string]$kind, [string]$startA, [string]$startB, [string]$endB, [string]$expected) {
    $source = $sources[$kind]
    foreach ($marker in @($startA, $startB, $endB)) {
        if (($source.Split([string[]]@($marker), [StringSplitOptions]::None)).Count - 1 -ne 1) {
            throw "The swap marker of $name is not unique: $marker"
        }
    }
    $a = $source.IndexOf($startA, [StringComparison]::Ordinal)
    $b = $source.IndexOf($startB, [StringComparison]::Ordinal)
    $e = $source.IndexOf($endB, [StringComparison]::Ordinal)
    if (-not ($a -lt $b -and $b -lt $e)) { throw "The swap markers of $name are not in order." }
    $blockA = $source.Substring($a, $b - $a)
    $blockB = $source.Substring($b, $e - $b)
    Invoke-Mutant $name $kind ($blockA + $blockB) ($blockB + $blockA) $expected
}

$baseline = Invoke-Harness $null @{} @{}
if ($baseline.Exit -ne 0 -or $baseline.Text -notmatch 'ARENA_SESSION_CLIENT_CHECKS_PASSED') { throw $baseline.Text }
Write-Output ($baseline.Text -split "`n" | Where-Object { $_ -match 'PASSED' })

# ---- session state
Invoke-Mutant 'mode-flag-ignored' 'mode' `
    '_isArenaSession = ArenaSessionContract.HasSessionFlag(args);' '_isArenaSession = false;' `
    'The flag is case-insensitive; no published report means the gate did not run'
Invoke-Mutant 'mode-inactive-gate-counts' 'mode' `
    'return _isArenaSession && _report != null && _report.Active;' 'return _isArenaSession && _report != null;' `
    'An inactive report keeps the gate'
Invoke-Mutant 'mode-report-without-flag' 'mode' `
    "if (_isArenaSession)`n        {`n            if (string.IsNullOrWhiteSpace(gateReportJson))" `
    "if (true)`n        {`n            if (string.IsNullOrWhiteSpace(gateReportJson))" `
    'A process without the flag is no Arena session and has no gate state'

# ---- whitelist admission
Invoke-Mutant 'admission-fika-ignored' 'admission' 'if (HasFika())' 'if (false)' `
    'With Fika the whitelist admits and leaves the refusal to the Fika rule'
Invoke-Mutant 'admission-soft-dependency-pulls-in' 'admission' `
    '(dependency.Flags & BepInDependency.DependencyFlags.HardDependency) != 0' 'true' `
    'A soft dependency target stays excluded'
Invoke-Mutant 'admission-server-exclusions-dropped' 'admission' `
    'if (server != null) foreach (var unit in server.Excluded) AddExcluded(denied, unlisted, listedGuids, unit);' '' `
    'A server mod that is not whitelisted refuses Arena and offers the session'
Invoke-Mutant 'admission-names-by-name-only' 'admission' `
    'if (!string.IsNullOrEmpty(unit.Guid) && !listedGuids.Add(unit.Guid)) return;' '' `
    'A mod with differently named client and server parts is listed once'
Invoke-Mutant 'admission-offer-without-helper' 'admission' 'if (!File.Exists(helper))' 'if (false)' `
    'helper missing: the whitelist still refuses, without the session action'
Invoke-Mutant 'admission-offer-without-spt-server' 'admission' 'if (!File.Exists(server))' 'if (false)' `
    'no local SPT server: the whitelist still refuses, without the session action'
Invoke-Mutant 'admission-offer-foreign-mod-folder' 'admission' 'if (!HelperPathSafety.IsDirectChild(mod.File, modsRoot))' 'if (false)' `
    'server mod outside this installation: the whitelist still refuses, without the session action'
Invoke-Mutant 'admission-offer-without-server-mods' 'admission' 'if (allowedServerMods.Count == 0)' 'if (false)' `
    'no allowed server mod: the whitelist still refuses, without the session action'
Invoke-Mutant 'admission-offer-remote-backend' 'admission' 'if (!BackendIsLocal())' 'if (false)' `
    'remote backend: the whitelist still refuses, without the session action'
Invoke-Mutant 'admission-offer-on-arena-server' 'admission' 'if (ArenaServerClient.IsArenaInstance)' 'if (false)' `
    'already an arena server: the whitelist still refuses, without the session action'
Invoke-Mutant 'admission-session-gate-ignored' 'admission' 'if (!ArenaSessionMode.GateActive)' 'if (false)' `
    'A session whose gate never ran refuses Arena'
Invoke-Mutant 'admission-arena-instance-ignored' 'admission' `
    'if (ArenaServerClient.IsServerModKnownAvailable && !ArenaServerClient.IsArenaInstance)' 'if (false)' `
    'A server that does not say arenaInstance:true refuses Arena in a session'
Invoke-Mutant 'admission-no-cache' 'admission' 'if (_outcome == null || key != _outcomeKey)' 'if (true)' `
    'The policy files are not re-read within one ping generation'
Invoke-Mutant 'admission-stale-cache' 'admission' 'if (_outcome == null || key != _outcomeKey)' 'if (_outcome == null)' `
    'Without a session-capable server the refusal asks to remove the mods'
Invoke-Mutant 'admission-warnings-logged-every-time' 'admission' `
    'if (!LoggedOnce.Add(key)) return;' 'LoggedOnce.Add(key);' `
    'A new ping does not log the same warning again'
Invoke-Mutant 'admission-names-not-capped' 'admission' `
    'if (unique.Count <= limit) return string.Join(separator, unique);' 'return string.Join(separator, unique);' `
    'Twelve names are listed, the rest is counted'
Invoke-Mutant 'admission-names-unsorted' 'admission' 'unique.Sort(StringComparer.OrdinalIgnoreCase);' '' `
    'Names are sorted and a mod on both sides is listed once'
Invoke-Mutant 'admission-refusal-text' 'admission' `
    'Start an Arena session to play Arena without them; the game restarts.' 'Start a session.' `
    'The spec''s refusal text, with the session sentence'
Invoke-Mutant 'admission-policy-edit-unnoticed' 'admission' `
    'InvalidateIfPolicyChanged("screen");' '' `
    'A policy file edited while the screen was closed is read again when it opens'
Invoke-Mutant 'admission-policy-stamp-ignores-user-file' 'admission' `
    '=> FileStamp(DefaultPolicyPath()) + "|" + FileStamp(UserPolicyPath());' '=> FileStamp(DefaultPolicyPath());' `
    'A policy file edited while the screen was closed is read again when it opens'
Invoke-Mutant 'admission-policy-stamp-ignores-default-file' 'admission' `
    '=> FileStamp(DefaultPolicyPath()) + "|" + FileStamp(UserPolicyPath());' '=> FileStamp(UserPolicyPath());' `
    'An edited default file is noticed as well'
Invoke-Mutant 'admission-policy-stamp-never-recorded' 'admission' `
    '_outcomePolicyStamp = PolicyStamp();' '' `
    'An unchanged policy keeps the outcome'
Invoke-Mutant 'admission-policy-always-dropped' 'admission' `
    'if (string.Equals(PolicyStamp(), _outcomePolicyStamp, StringComparison.Ordinal)) return;' '' `
    'An unchanged policy keeps the outcome'
Invoke-Mutant 'admission-policy-edit-unnoticed-in-session' 'admission' `
    'InvalidateIfPolicyChanged("screen");' 'if (!ArenaSessionMode.IsArenaSession) InvalidateIfPolicyChanged("screen");' `
    'A policy file edited while the Arena session runs refuses at the next screen'
Invoke-Mutant 'admission-session-start-stale-policy' 'admission' `
    'InvalidateIfPolicyChanged("session start");' '' `
    'The session start is built from the policy of the moment'
# Review 2026-10-23: the patchers the gate let run are judged with the policy of the moment as well.
Invoke-Mutant 'admission-ran-patchers-not-judged' 'admission' `
    'var ranExcluded = PatchersNowExcluded(policy, ArenaSessionMode.GateReport.PatchersAllowed);' 'var ranExcluded = new List<string>();' `
    'A patcher the gate let run and the current policy denies refuses Arena in the session'
Invoke-Mutant 'admission-ran-patchers-judged-by-listing' 'admission' `
    'if (string.IsNullOrEmpty(file) || policy.IsAllowedPatcher(file)) continue;' 'if (string.IsNullOrEmpty(file) || policy.FindPatcher(file) != null) continue;' `
    'In blacklist mode an unlisted patcher the gate let run refuses nothing'
Invoke-Mutant 'admission-ran-patchers-singular-only' 'admission' `
    '(files.Count > 1 ? "This game ran the preloader patchers " : "This game ran the preloader patcher ")' '"This game ran the preloader patcher "' `
    'Several denied patchers are listed once each, sorted, in the plural'
Invoke-SwapMutant 'admission-ran-patchers-before-early-patchers' 'admission' `
    '// A not-allowed patcher that sorts before the gate has run its initializer' `
    '// The preloader patchers the gate let run are judged with the policy as it reads now as well' `
    '// The plugins this game loaded are judged with the policy as it reads now, like the server' `
    'A patcher that ran before the gate is refused ahead of a patcher the policy excludes now'
Invoke-SwapMutant 'admission-ran-patchers-before-plugins' 'admission' `
    '        // The preloader patchers the gate let run are judged with the policy as it reads now as well' `
    '        // The plugins this game loaded are judged with the policy as it reads now, like the server' `
    '        if (ArenaServerClient.IsServerModKnownAvailable && !ArenaServerClient.IsArenaInstance)' `
    'A patcher the policy excludes now is refused ahead of a loaded plugin it excludes'
Invoke-Mutant 'admission-ran-patchers-unlogged' 'admission' `
    'LogOnce("patcherexcluded:" + outcome.Reason, true, "Arena session refused: " + outcome.Reason);' '' `
    'The patcher refusal is logged once, as a Warning'
Invoke-Mutant 'admission-ran-patchers-names-uncapped' 'admission' `
    '+ JoinLimited(files, MaxNamesListed) + ", which the current policy excludes: " + PolicyChangedText;' '+ JoinLimited(files, 1000) + ", which the current policy excludes: " + PolicyChangedText;' `
    'Twelve denied patcher names are listed, the rest is counted'
Invoke-Mutant 'admission-ran-patchers-duplicates-counted' 'admission' `
    'if (seen.Add(file)) excluded.Add(file);' 'excluded.Add(file);' `
    'A patcher listed twice is one file, in the singular'
Invoke-Mutant 'admission-misses-server-only-drift' 'admission' `
    'if (!ArenaSessionMode.IsArenaSession && Current().Policy == null) return;' `
    'if (!ArenaSessionMode.IsArenaSession && (Current().Reason == null || Current().Policy == null)) return;' `
    'A clean normal session probes once for server-only version differences'
Invoke-Mutant 'admission-notes-uncapped' 'admission' 'private const int MaxWarningsListed = 4;' 'private const int MaxWarningsListed = 40;' `
    'Notes are capped at four lines and the rest counted'
Invoke-Mutant 'admission-policy-oversize-unreported' 'admission' `
    'problem = label + " is larger than " + (ArenaPolicyFiles.MaxBytes / 1024) + " KB and was ignored";' 'problem = null;' `
    'A policy file above 256 KB is refused as a policy and reported'
Invoke-Mutant 'admission-session-failure-fails-open' 'admission' `
    "return ArenaSessionMode.IsArenaSession`n                ? ""Arena could not verify the session state. Return to the normal session and check the client log.""`n                : ""Arena could not evaluate its mod whitelist ("" + ex.Message + ""). Check the client log; ordinary PMC/Scav raids remain available."";" `
    'return null;' `
    'An internal failure refuses Arena in a normal session'
Invoke-Mutant 'admission-missing-policy-fails-open' 'admission' `
    'else outcome.Reason = PolicyUnavailableRefusal(policyProblem);' '' `
    'Without a policy file a normal session refuses Arena and names the missing file'
Invoke-Mutant 'admission-offer-for-any-reason' 'admission' `
    'string.Equals(reason, outcome.Reason, StringComparison.Ordinal)' 'true' `
    'The action belongs to the whitelist refusal only'
Invoke-Mutant 'admission-policy-hash-dropped' 'admission' 'policyHash = versionPlan?.PolicyHash ?? outcome.Policy.Hash;' 'policyHash = string.Empty;' `
    'The request carries the effective policy'
Invoke-Mutant 'admission-all-server-mods-allowed' 'admission' `
    'outcome.AllowedServerMods.AddRange(server.Allowed);' 'outcome.AllowedServerMods.AddRange(ServerUnits(serverMods));' `
    'Only the allowed server mods go into the session, in ping order'
Invoke-Mutant 'admission-server-dependencies-dropped' 'admission' `
    'units.Add(new ArenaSessionUnit(mod.Guid, mod.Name, mod.Version, mod.Folder, mod.Dependencies));' `
    'units.Add(new ArenaSessionUnit(mod.Guid, mod.Name, mod.Version, mod.Folder, null));' `
    'A server mod that needs a denied library is blocked with it, under Blocked for Arena'
Invoke-Mutant 'admission-blacklist-text-ignored' 'admission' `
    'outcome.Reason = FormatRefusal(denied, unlisted, outcome.CanStartSession, policy.AllowsUnlisted);' `
    'outcome.Reason = FormatRefusal(denied, unlisted, outcome.CanStartSession, false);' `
    'In blacklist mode the refusal names the denied mods only and does not claim a whitelist'
Invoke-Mutant 'admission-blacklist-text-always' 'admission' `
    'outcome.Reason = FormatRefusal(denied, unlisted, outcome.CanStartSession, policy.AllowsUnlisted);' `
    'outcome.Reason = FormatRefusal(denied, unlisted, outcome.CanStartSession, true);' `
    'Without a session-capable server the refusal asks to remove the mods'

# ---- Arena session review (2026-10-22): patchers that ran before the gate, the server's mods, the policy hash
Invoke-Mutant 'admission-patchers-ran-before-gate-ignored' 'admission' `
    'if (ranBeforeGate != null && ranBeforeGate.Count > 0)' 'if (false)' `
    'A patcher that ran before the gate refuses Arena in the session'
Invoke-Mutant 'admission-patchers-ran-before-gate-singular' 'admission' `
    'bool several = files.Count > 1;' 'bool several = false;' `
    'Several patchers are listed once each, sorted, in the plural'
Invoke-Mutant 'admission-patchers-ran-before-gate-wording' 'admission' `
    '" ran before the Arena gate could stop it. "' '" ran early. "' `
    'A patcher that ran before the gate refuses Arena in the session'
Invoke-Mutant 'admission-patchers-ran-before-gate-unlogged' 'admission' `
    'LogOnce("ranbeforegate:" + outcome.Reason, true,' 'LogOnce("ranbeforegate:" + outcome.Reason, false,' `
    'The patcher refusal is logged once, as a Warning'
Invoke-Mutant 'admission-session-server-exclusions-ignored' 'admission' `
    'if (decision.Excluded.Count > 0)' 'if (false)' `
    'A server mod the policy does not name refuses Arena in the session'
Invoke-Mutant 'admission-session-server-names-unsorted' 'admission' `
    '"The Arena session server runs " + JoinLimited(names, MaxNamesListed)' '"The Arena session server runs " + string.Join(", ", names.ToArray())' `
    'A default-denied, a user-denied and an unlisted server mod all refuse, sorted'
Invoke-Mutant 'admission-session-server-refusal-unlogged' 'admission' `
    'LogOnce("serverexcluded:" + outcome.Reason, true,' 'LogOnce("serverexcluded:" + outcome.Reason, false,' `
    'The server-mod refusal is logged once, as a Warning'
Invoke-Mutant 'admission-session-hash-ignored' 'admission' `
    '!string.Equals(gateHash, serverHash, StringComparison.Ordinal)' 'false' `
    'A server built for another policy hash refuses Arena in the session'
Invoke-Mutant 'admission-session-hash-case-insensitive' 'admission' `
    'string.Equals(gateHash, serverHash, StringComparison.Ordinal)' 'string.Equals(gateHash, serverHash, StringComparison.OrdinalIgnoreCase)' `
    'The hashes are compared exactly, not ignoring case'
# The older session scenarios ping without a hash (an older server), so they fail before the dedicated assertion does.
Invoke-Mutant 'admission-session-hash-unknown-is-different' 'admission' `
    ' && !string.IsNullOrWhiteSpace(serverHash)' '' `
    'An Arena session with the override is admitted'
Invoke-Mutant 'admission-session-gate-hash-blank-is-different' 'admission' `
    '!string.IsNullOrWhiteSpace(gateHash) && ' '' `
    'A gate report without a hash leaves nothing to compare'
Invoke-Mutant 'admission-session-hash-refusal-unlogged' 'admission' `
    'LogOnce("policyhash:" + serverHash + "|" + gateHash, true,' 'LogOnce("policyhash:" + serverHash + "|" + gateHash, false,' `
    'The refusal is logged once, as a Warning, with both hashes'

# ---- Arena session: the policy has to load, and the plugins that are running are judged with it (third pass, 2026-10-22)
# Without a policy the session used to admit: every comparison below the gate check was skipped for a missing policy.
Invoke-Mutant 'admission-session-missing-policy-admits' 'admission' `
    ('            outcome.Reason = PolicyUnavailableRefusal(policyProblem) + " Return to the normal session.";' + "`n" `
        + '            LogOnce("sessionpolicy:" + outcome.Reason, true, "Arena session refused: " + outcome.Reason);' + "`n" + '            return;') `
    '            return;' `
    'A session whose policy cannot be loaded refuses Arena, with the way back'
Invoke-Mutant 'admission-session-policy-refusal-without-way-back' 'admission' `
    'PolicyUnavailableRefusal(policyProblem) + " Return to the normal session.";' 'PolicyUnavailableRefusal(policyProblem);' `
    'A session whose policy cannot be loaded refuses Arena, with the way back'
Invoke-Mutant 'admission-session-policy-refusal-unlogged' 'admission' `
    'LogOnce("sessionpolicy:" + outcome.Reason, true,' 'LogOnce("sessionpolicy:" + outcome.Reason, false,' `
    'The policy refusal is logged once, as a Warning'
Invoke-Mutant 'admission-normal-policy-refusal-with-way-back' 'admission' `
    'else outcome.Reason = PolicyUnavailableRefusal(policyProblem);' `
    'else outcome.Reason = PolicyUnavailableRefusal(policyProblem) + " Return to the normal session.";' `
    'A normal session without a policy keeps its own refusal, without the way back'
Invoke-Mutant 'admission-session-client-exclusions-ignored' 'admission' `
    'if (loaded.Excluded.Count > 0)' 'if (false)' `
    'A loaded plugin the policy does not name refuses Arena in the session'
Invoke-Mutant 'admission-session-client-needs-ping' 'admission' `
    'if (loaded.Excluded.Count > 0)' 'if (loaded.Excluded.Count > 0 && ArenaServerClient.ServerMods != null)' `
    'The plugin refusal does not wait for the server''s ping'
Invoke-Mutant 'admission-session-client-names-unsorted' 'admission' `
    'JoinLimited(ExcludedNames(loaded), MaxNamesListed)' 'string.Join(", ", ExcludedNames(loaded).ToArray())' `
    'A default-denied, a user-denied and an unlisted plugin all refuse, sorted'
Invoke-Mutant 'admission-session-client-names-uncapped' 'admission' `
    'JoinLimited(ExcludedNames(loaded), MaxNamesListed)' 'JoinLimited(ExcludedNames(loaded), 1000)' `
    'Twelve plugin names are listed, the rest is counted'
Invoke-Mutant 'admission-session-client-wording' 'admission' `
    '"This game loaded "' '"Loaded plugins: "' `
    'A loaded plugin the policy does not name refuses Arena in the session'
Invoke-Mutant 'admission-session-client-refusal-unlogged' 'admission' `
    'LogOnce("clientexcluded:" + outcome.Reason, true,' 'LogOnce("clientexcluded:" + outcome.Reason, false,' `
    'The plugin refusal is logged once, as a Warning'
# The gate's report already carries the plugin notes; the session's own evaluation must not add them a second time.
Invoke-Mutant 'admission-session-client-warnings-added' 'admission' `
    'AddNotes(notes, noteIdentities, ArenaSessionMode.GateReport.Warnings, gateVersions);' `
    ('AddNotes(notes, noteIdentities, ArenaSessionMode.GateReport.Warnings, gateVersions);' + "`n" + '        notes.AddRange(loaded.Warnings);') `
    'The gate''s report is the only source of the plugin notes in a session'
# The order of the session checks (spec section 7): gate, policy, patchers that ran early, loaded plugins, Arena instance, server mods, hash.
Invoke-SwapMutant 'admission-session-policy-before-gate' 'admission' `
    '        if (!ArenaSessionMode.GateActive)' '        // The gate filtered with the policy it read when this game started.' `
    '        var versionPlan = ArenaSessionMode.GateReport.VersionPlan;' `
    'An inactive gate is reported ahead of a missing policy'
Invoke-SwapMutant 'admission-session-patchers-before-policy' 'admission' `
    '        // The gate filtered with the policy it read when this game started.' '        // A not-allowed patcher that sorts before the gate has run its initializer' `
    '        // The preloader patchers the gate let run are judged with the policy as it reads now as well' `
    'A missing policy is reported ahead of a patcher that ran before the gate'
Invoke-SwapMutant 'admission-session-plugins-before-patchers' 'admission' `
    '        // A not-allowed patcher that sorts before the gate has run its initializer' '        // The plugins this game loaded are judged with the policy as it reads now' `
    '        if (ArenaServerClient.IsServerModKnownAvailable && !ArenaServerClient.IsArenaInstance)' `
    'A patcher that ran before the gate is reported ahead of a loaded plugin the policy excludes'
Invoke-SwapMutant 'admission-session-instance-before-plugins' 'admission' `
    '        // The plugins this game loaded are judged with the policy as it reads now' '        if (ArenaServerClient.IsServerModKnownAvailable && !ArenaServerClient.IsArenaInstance)' `
    '        var gateVersions = ArenaWhitelistDecision.Evaluate(policy, ArenaWhitelistSide.Client,' `
    'A loaded plugin the policy excludes is reported ahead of a server that is no Arena instance'

# ---- preloader patchers are units of the normal-session decision
Invoke-Mutant 'admission-patchers-ignored' 'admission' `
    'foreach (var unit in ExcludedPatchers(policy)) AddExcluded(denied, unlisted, listedGuids, unit);' '' `
    'An unlisted preloader patcher refuses Arena in a normal session and is named'
Invoke-Mutant 'admission-patchers-top-level-only' 'admission' `
    'SearchOption.AllDirectories' 'SearchOption.TopDirectoryOnly' `
    'An unlisted preloader patcher refuses Arena in a normal session and is named'
Invoke-Mutant 'admission-patchers-every-file' 'admission' `
    'Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories)' 'Directory.GetFiles(root, "*", SearchOption.AllDirectories)' `
    'Patchers are matched by file name, case-insensitively, once per name, and only *.dll files count'
# The first scenario of the harness already has the installed patchers on the list, so it fails first.
Invoke-Mutant 'admission-patcher-judged-as-plugin' 'admission' `
    'if (policy.IsAllowedPatcher(file)) continue;' 'if (policy.IsAllowedPlugin(file)) continue;' `
    'A normal session whose plugins are all whitelisted may play Arena without a restart'
Invoke-Mutant 'admission-patcher-reason-lost' 'admission' `
    'Reason = policy.ExclusionReason(file)' 'Reason = ArenaWhitelistDecision.ReasonNotOnWhitelist' `
    'A patcher the user denied is blocked, an unlisted one is not on the whitelist'
Invoke-Mutant 'admission-patchers-missing-folder-throws' 'admission' `
    'if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return excluded;' '' `
    'A missing patchers folder is no refusal'
# A DLL below BepInEx\patchers that cannot be a patcher (no TargetDLLs member in its bytes) is a library, not a unit.
Invoke-Mutant 'admission-library-counts-as-patcher' 'admission' `
    ('            if (!MayBePatcher(path)) continue;' + "`n") '' `
    'A library in the patchers folder is not a patcher and does not block a direct start'
Invoke-Mutant 'admission-library-copy-hides-patcher-copy' 'admission' `
    ('            if (!MayBePatcher(path)) continue;' + "`n" + '            seen.Add(file);') `
    ('            seen.Add(file);' + "`n" + '            if (!MayBePatcher(path)) continue;') `
    'A library copy of a file name does not hide the patcher copy of it'
Invoke-Mutant 'admission-unreadable-file-passes' 'admission' `
    ('            return true;' + "`n" + '        }' + "`n" + '    }' + "`n" + "`n" + '    private static bool ContainsBytes(Stream stream, byte[] needle)') `
    ('            return false;' + "`n" + '        }' + "`n" + '    }' + "`n" + "`n" + '    private static bool ContainsBytes(Stream stream, byte[] needle)') `
    'A file that cannot be read counts as a patcher'
Invoke-Mutant 'admission-member-name-lost-between-reads' 'admission' `
    'carried = Math.Min(needle.Length - 1, total);' 'carried = 0;' `
    'The patcher member name at byte 65541 of a large file marks a patcher'
Invoke-Mutant 'admission-member-name-wrong' 'admission' `
    'Encoding.ASCII.GetBytes("TargetDLLs")' 'Encoding.ASCII.GetBytes("TargetDlls")' `
    'An unlisted preloader patcher refuses Arena in a normal session and is named'

# ---- deny wins over hard dependencies: the dependants of a denied mod are blocked, not merely unlisted
Invoke-Mutant 'admission-requires-denied-counted-as-unlisted' 'admission' `
    'if (string.Equals(unit.Reason, ArenaWhitelistDecision.ReasonNotOnWhitelist, StringComparison.Ordinal)) unlisted.Add(unit.DisplayName);' `
    'if (string.Equals(unit.Reason, ArenaWhitelistDecision.ReasonNotOnWhitelist, StringComparison.Ordinal) || ArenaWhitelistDecision.IsRequiresDeniedReason(unit.Reason)) unlisted.Add(unit.DisplayName);' `
    'A server mod that needs a denied library is blocked with it, under Blocked for Arena'

# ---- denied mods (a deny list, the default's or the user's) are named apart from the merely unlisted ones
$unlistedSplit = "        if (string.Equals(unit.Reason, ArenaWhitelistDecision.ReasonNotOnWhitelist, StringComparison.Ordinal)) unlisted.Add(unit.DisplayName);`n        else denied.Add(unit.DisplayName);"
Invoke-Mutant 'admission-denied-counted-as-unlisted' 'admission' $unlistedSplit `
    '        unlisted.Add(unit.DisplayName);' `
    'A user deny entry excludes a default mod and names it as blocked'
Invoke-Mutant 'admission-unlisted-counted-as-denied' 'admission' $unlistedSplit `
    '        denied.Add(unit.DisplayName);' `
    'Without a session-capable server the refusal asks to remove the mods'
Invoke-Mutant 'admission-blocked-part-dropped' 'admission' `
    'if (denied.Count > 0) text.Append(" Blocked for Arena: ")' 'if (false) text.Append(" Blocked for Arena: ")' `
    'A soft dependency target stays excluded'
Invoke-Mutant 'admission-unlisted-part-dropped' 'admission' `
    'if (unlisted.Count > 0) text.Append(" Not on the whitelist: ")' 'if (false) text.Append(" Not on the whitelist: ")' `
    'Without a session-capable server the refusal asks to remove the mods'
Invoke-Mutant 'admission-blocked-label' 'admission' '" Blocked for Arena: "' '" Denied: "' `
    'A user deny entry excludes a default mod and names it as blocked'
Invoke-Mutant 'admission-parts-swapped' 'admission' `
    "        if (denied.Count > 0) text.Append(`" Blocked for Arena: `").Append(JoinLimited(denied, MaxNamesListed)).Append('.');`n        if (unlisted.Count > 0) text.Append(`" Not on the whitelist: `").Append(JoinLimited(unlisted, MaxNamesListed)).Append('.');" `
    "        if (unlisted.Count > 0) text.Append(`" Not on the whitelist: `").Append(JoinLimited(unlisted, MaxNamesListed)).Append('.');`n        if (denied.Count > 0) text.Append(`" Blocked for Arena: `").Append(JoinLimited(denied, MaxNamesListed)).Append('.');" `
    'Blocked and unlisted mods are named in two separate parts'
Invoke-Mutant 'admission-blocked-names-uncapped' 'admission' `
    'JoinLimited(denied, MaxNamesListed)' 'JoinLimited(denied, 1000)' `
    'The blocked names are capped at twelve on their own, apart from the unlisted ones'
Invoke-Mutant 'admission-blacklist-lead-claims-a-whitelist' 'admission' `
    '? "Arena does not run with denied mods."' '? "Arena runs only with whitelisted mods."' `
    'In blacklist mode the refusal names the denied mods only and does not claim a whitelist'
Invoke-Mutant 'admission-whitelist-lead-dropped' 'admission' `
    ': "Arena runs only with whitelisted mods.");' ': "Arena runs with whitelisted mods.");' `
    'Without a session-capable server the refusal asks to remove the mods'

# ---- policy override ("unlisted": "allow"): the decision's one warning per side, its way into the notes line, the screen
Invoke-Mutant 'decision-override-note-missing' 'decision' `
    'if (policyOverride != null) result.Warnings.Add(policyOverride);' '' `
    'The policy override is shown first and never counted into the cap of four notes'
Invoke-Mutant 'decision-override-note-without-override' 'decision' `
    'if (!string.Equals(unit.Reason, ReasonUnlistedAllowed, StringComparison.Ordinal)) continue;' '' `
    'A normal session whose plugins are all whitelisted may play Arena without a restart'
Invoke-Mutant 'admission-override-note-in-the-cap' 'admission' `
    'if (!ArenaWhitelistDecision.IsPolicyOverrideWarning(note)) others.Add(note);' 'if (true) others.Add(note);' `
    'The policy override is shown first and never counted into the cap of four notes'
Invoke-Mutant 'admission-override-notes-sorted' 'admission' `
    'if (policyOverrides.Count > 0) parts.Add(string.Join("; ", policyOverrides.ToArray()));' `
    'if (policyOverrides.Count > 0) parts.Add(JoinLimited(policyOverrides, 10, "; "));' `
    'The policy override is announced once per side, client first'
Invoke-Mutant 'admission-override-note-twice' 'admission' `
    'else if (!policyOverrides.Contains(note)) policyOverrides.Add(note);' 'else policyOverrides.Add(note);' `
    'Formatting duplicate policy-override notes keeps one notice'
Invoke-Mutant 'side-refusal-without-notes' 'side' `
    'string text = WithWarnings(reason, ArenaWhitelistAdmission.WarningLine);' 'string text = reason;' `
    'a refusal carries the whitelist notes, the policy override included, as its second line'
Invoke-Mutant 'side-selected-notice-without-notes' 'side' `
    "        else if (warnings != null)`n            ShowVersionNotes(screen, warnings);" `
    "        else if (false)`n            ShowVersionNotes(screen, warnings);" `
    'an Arena click shows the whitelist notes, the policy override included, as an information notice'

# ---- session switch
Invoke-Mutant 'switch-quit-before-launch' 'switch' `
    "        if (!LaunchHelper(helper, direction, requestPath, out int helperPid, out error))`n            return false;" `
    "        QuitGame();`n        if (!LaunchHelper(helper, direction, requestPath, out int helperPid, out error))`n            return false;" `
    'The game does not quit when the helper did not start'
Invoke-Mutant 'switch-request-not-latched' 'switch' '_requested = true;' '' `
    'The restart is latched'
Invoke-Mutant 'switch-token-ignored' 'switch' 'if (IsSafeProfileId(token)) return token;' '' 'The -token argument wins'
Invoke-Mutant 'switch-id-not-the-helpers-rule' 'switch' `
    'internal static bool IsSafeProfileId(string id) => HelperRequest.IsValidProfileId(id);' `
    'internal static bool IsSafeProfileId(string id) => !string.IsNullOrEmpty(id);' `
    'A token the helper would refuse falls back to the logged-in profile'
Invoke-Mutant 'switch-argv0-kept' 'switch' 'for (int i = 1; i < commandLine.Count; i++)' 'for (int i = 0; i < commandLine.Count; i++)' `
    'argv[0] is dropped and nulls are skipped'
Invoke-Mutant 'switch-gate-window-unbounded' 'switch' '_armedUntil = now + WindowSeconds;' '_armedUntil = float.MaxValue;' `
    'The window is five seconds'
Invoke-Mutant 'switch-gate-fresh-armed' 'switch' 'internal bool IsArmed(float now) => now <= _armedUntil;' 'internal bool IsArmed(float now) => true;' `
    'A fresh gate is not armed'
Invoke-Mutant 'switch-request-file-not-written' 'switch' 'WriteRequestFile(requestPath, requestJson);' '' `
    'The helper starts with the request file already in place'
Invoke-Mutant 'switch-request-unchecked' 'switch' `
    'if (!HelperRequest.TryParse(requestJson, direction, out _, out string refusal))' `
    'if (!HelperRequest.TryParse(requestJson, direction, out _, out string refusal) && false)' `
    'A request the helper''s parser refuses neither writes the file, nor starts the helper, nor quits the game'
Invoke-Mutant 'switch-enter-inside-session' 'switch' 'if (enter == ArenaSessionMode.IsArenaSession)' 'if (false)' `
    'Leaving needs an Arena session'
Invoke-Mutant 'switch-policy-hash-not-sent' 'switch' 'request.PolicyHash = policyHash;' '' 'The policy hash'
Invoke-Mutant 'switch-incomplete-mods-kept' 'switch' `
    'if (unit == null || string.IsNullOrEmpty(unit.Guid) || string.IsNullOrEmpty(unit.File)) continue;' 'if (unit == null) continue;' `
    'The request file has the documented keys in the documented order; incomplete mods are left out'
Invoke-Mutant 'switch-helper-unverified' 'switch' 'if (process.HasExited)' 'if (false)' `
    'the real launcher verifies the helper process and shows no window'
Invoke-Mutant 'switch-helper-window' 'switch' 'CreateNoWindow = true,' 'CreateNoWindow = false,' `
    'the real launcher verifies the helper process and shows no window'

# ---- the helper's acknowledgement (spec sections 6 and 7): the game quits only after it, never blocks, and stays on a failure
Invoke-Mutant 'switch-quit-when-helper-started' 'switch' `
    '        Plugin.ModLogger?.LogDebug("Arena session: " + pending.Summary' `
    ('        QuitGame();' + "`n" + '        Plugin.ModLogger?.LogDebug("Arena session: " + pending.Summary') `
    'A helper that has only started does not close the game: it has to acknowledge first'
Invoke-Mutant 'switch-quit-without-acknowledgement' 'switch' `
    'if (IsAcknowledged()) return ArenaHandshakeStatus.Accepted;' 'return ArenaHandshakeStatus.Accepted;' `
    'The wait goes on while the helper is silent'
Invoke-Mutant 'switch-acknowledgement-of-any-process' 'switch' `
    '&& pid == _helperPid;' ';' `
    'Another helper''s acknowledgement is ignored'
Invoke-Mutant 'switch-oversized-acknowledgement-read' 'switch' `
    'info.Length > MaxAcknowledgementBytes' 'false' `
    'Not an acknowledgement'
Invoke-Mutant 'switch-helper-exit-ignored' 'switch' `
    'if (helperHasExited(_helperPid)) return ArenaHandshakeStatus.HelperExited;' '' `
    'A helper that is gone ends the wait at the next look'
Invoke-Mutant 'switch-acknowledgement-of-a-dead-helper' 'switch' `
    ('        if (helperHasExited(_helperPid)) return ArenaHandshakeStatus.HelperExited;' + "`n" + '        if (IsAcknowledged()) return ArenaHandshakeStatus.Accepted;') `
    ('        if (IsAcknowledged()) return ArenaHandshakeStatus.Accepted;' + "`n" + '        if (helperHasExited(_helperPid)) return ArenaHandshakeStatus.HelperExited;') `
    'A helper that acknowledged and then vanished cannot switch anything, so the game stays'
Invoke-Mutant 'switch-no-timeout' 'switch' `
    'return now >= _deadline ? ArenaHandshakeStatus.TimedOut : ArenaHandshakeStatus.Waiting;' 'return ArenaHandshakeStatus.Waiting;' `
    'At fifteen seconds the wait is over'
Invoke-Mutant 'switch-timeout-too-long' 'switch' 'internal const float TimeoutSeconds = 15f;' 'internal const float TimeoutSeconds = 150f;' `
    'At fifteen seconds the wait is over'
Invoke-Mutant 'switch-timeout-too-short' 'switch' 'internal const float TimeoutSeconds = 15f;' 'internal const float TimeoutSeconds = 10f;' `
    'The wait goes on while the helper is silent'
Invoke-Mutant 'switch-deadline-waits-for-a-look' 'switch' `
    'if (now < _nextPoll && now < _deadline) return ArenaHandshakeStatus.Waiting;' 'if (now < _nextPoll) return ArenaHandshakeStatus.Waiting;' `
    'At fifteen seconds the wait is over, even between two looks'
Invoke-Mutant 'switch-looks-every-frame' 'switch' `
    'if (now < _nextPoll && now < _deadline) return ArenaHandshakeStatus.Waiting;' '' `
    'The request itself and the first frame the coroutine gets inside it do not look at the helper'
Invoke-Mutant 'switch-looks-too-often' 'switch' 'internal const float PollIntervalSeconds = 0.25f;' 'internal const float PollIntervalSeconds = 0.125f;' `
    'Three frames inside a quarter second look at nothing'
Invoke-Mutant 'switch-looks-too-rarely' 'switch' 'internal const float PollIntervalSeconds = 0.25f;' 'internal const float PollIntervalSeconds = 2f;' `
    'A helper that is gone ends the wait at the next look'
Invoke-Mutant 'switch-failure-silent' 'switch' `
    'try { pending.OnFailure?.Invoke(message); }' 'try { }' `
    'The player is told that the helper stayed silent, that the game keeps running and where the log is'
Invoke-Mutant 'switch-failure-not-logged' 'switch' `
    'Plugin.ModLogger?.LogWarning("Arena session switch (" + pending.Direction + ") not completed: " + message);' '' `
    'The failure is logged once as a Warning'
Invoke-Mutant 'switch-failure-keeps-pending' 'switch' `
    'if (ReferenceEquals(_pending, pending)) _pending = null;' '' `
    'A silent helper leaves the game running and the switch unlatched'
Invoke-Mutant 'switch-accept-keeps-pending' 'switch' `
    ('                _pending = null;' + "`n" + '                _requested = true;') '                _requested = true;' `
    'The restart is latched'
Invoke-Mutant 'switch-second-helper-allowed' 'switch' `
    ('        if (HelperPending)' + "`n") ('        if (false)' + "`n") `
    'A second request while the helper is starting is refused and starts no second helper'
Invoke-Mutant 'switch-no-coroutine' 'switch' 'plugin.StartCoroutine(WaitForHelper(pending));' 'WaitForHelper(pending);' `
    'The switch started its wait as a coroutine on the plugin'
# Unity runs a new coroutine up to its first yield inside StartCoroutine, so the wait has to be registered before it starts.
Invoke-Mutant 'switch-wait-registered-after-it-started' 'switch' `
    ('        _pending = pending;' + "`n" + '        try' + "`n" + '        {' + "`n" + '            StartWaiting(pending);' + "`n" + '        }') `
    ('        try' + "`n" + '        {' + "`n" + '            StartWaiting(pending);' + "`n" + '            _pending = pending;' + "`n" + '        }') `
    'The game quits only after the helper acknowledged, exactly once'
Invoke-Mutant 'switch-request-not-withdrawn' 'switch' `
    ('            default:' + "`n" + '                WithdrawRequest(pending);' + "`n") ('            default:' + "`n") `
    'A request that no helper took over is withdrawn when the wait times out'
# A client that reports failure leaves nothing armed: a helper that read the request earlier watches the file.
Invoke-Mutant 'switch-request-kept-when-helper-stopped' 'switch' `
    ('            case ArenaHandshakeStatus.HelperExited:' + "`n" + '                WithdrawRequest(pending);' + "`n") ('            case ArenaHandshakeStatus.HelperExited:' + "`n") `
    'A request is taken back when the helper stopped'
Invoke-Mutant 'switch-request-kept-when-the-wait-fails' 'switch' `
    ('                WithdrawRequest(pending);' + "`n" + '                Fail(pending, "The Arena session switch failed while it waited for the helper: " + ex.Message);') `
    '                Fail(pending, "The Arena session switch failed while it waited for the helper: " + ex.Message);' `
    'A wait that failed takes its request back too'
Invoke-Mutant 'switch-request-kept-when-scheduling-fails' 'switch' `
    ('            _pending = null;' + "`n" + '            WithdrawRequest(pending);' + "`n") ('            _pending = null;' + "`n") `
    'The helper that was started is already running, so the request is taken back'
Invoke-Mutant 'switch-wait-ends-after-one-look' 'switch' `
    ('            if (finished) yield break;' + "`n" + '            yield return null;') `
    ('            if (finished) yield break;' + "`n" + '            yield break;') `
    'The game quits only after the helper acknowledged, exactly once'
Invoke-Mutant 'switch-plugin-unchecked' 'switch' ('        if (Plugin.Instance == null)' + "`n") ('        if (false)' + "`n") `
    'Without the plugin object there is nothing to wait on: no request file, no helper, no quit'
Invoke-Mutant 'switch-schedule-failure-swallowed' 'switch' `
    ('            error = "The game could not wait for the Arena session helper (" + ex.Message + "), so it keeps running.";' + "`n" + '            return false;') `
    '            error = null;' `
    'A wait that cannot be scheduled keeps the game running and is reported'
Invoke-Mutant 'switch-stale-acknowledgement-kept' 'switch' ('        DeleteStaleAcknowledgement(requestPath);' + "`n") '' `
    'A stale acknowledgement is deleted before the helper is started'
Invoke-Mutant 'switch-look-failure-quits' 'switch' `
    'Fail(pending, "The Arena session switch failed while it waited for the helper: " + ex.Message);' 'QuitGame();' `
    'A look that throws ends the wait with a failure and never closes the game'
Invoke-Mutant 'switch-missing-process-is-alive' 'switch' `
    'catch (ArgumentException) { return true; } // no process with this id' 'catch (ArgumentException) { return false; } // no process with this id' `
    'A process id nobody has counts as a helper that is gone'
Invoke-Mutant 'switch-game-time-clock' 'switch' 'Clock = () => Time.realtimeSinceStartup;' 'Clock = () => Time.time;' `
    'the wait runs on real time, not on game time that a pause or a slow frame would stretch'
Invoke-Mutant 'switch-main-thread-sleeps' 'switch' `
    ('            if (finished) yield break;' + "`n" + '            yield return null;') `
    ('            if (finished) yield break;' + "`n" + '            System.Threading.Thread.Sleep(1); yield return null;') `
    'the wait for the helper is a coroutine on the plugin and never blocks the main thread'

# ---- side screen, notice and chain wiring (source pins)
Invoke-Mutant 'reward-prepare-case-mismatch' 'switch' 'nameof(RewardControlPrepareRequest.ControlId)' '"controlId"' `
    'Only approved opaque identities travel to the helper with the required new schema'
Invoke-Mutant 'entry-prepare-case-mismatch' 'switch' 'nameof(RewardEntryPrepareRequest.SessionId)' '"sessionId"' `
    'Accepted prepare starts the helper'
Invoke-Mutant 'side-lock-patch-ungated' 'side' `
    "        if (ArenaSessionMode.IsArenaSession)`n            new ArenaSideSelectionSessionLockPatch().Enable();" `
    '        new ArenaSideSelectionSessionLockPatch().Enable();' `
    'the PMC/Scav lock patch is installed in an Arena session only'
Invoke-Mutant 'side-ping-not-prepared' 'side' 'ArenaWhitelistAdmission.PrepareForScreen();' '' `
    'the side screen asks for the ping, prepares the Arena session and shows the notice'
Invoke-Mutant 'side-no-session-preselect' 'side' 'eligibility = PrepareArenaSession(__instance, eligibility);' '' `
    'the side screen asks for the ping, prepares the Arena session and shows the notice'
Invoke-Mutant 'side-preselect-without-ping' 'side' `
    "        if (!ArenaServerClient.IsServerModKnownAvailable)`n        {`n            SetNextAvailable(screen, false);`n            return eligibility;`n        }`n" '' `
    'the automatic selection waits for a server that has answered the bounded ping'
Invoke-Mutant 'side-pmc-big-button-open' 'side' 'screen._pmcBigButton.interactable = false;' '' `
    'PMC and Scav are not selectable in an Arena session: screen._pmcBigButton.interactable = false;'
Invoke-Mutant 'side-pmc-toggle-open' 'side' 'screen._pmcButton.SpawnedObject.interactable = false;' '' `
    'PMC and Scav are not selectable in an Arena session: screen._pmcButton.SpawnedObject.interactable = false;'
Invoke-Mutant 'side-scav-big-button-open' 'side' 'screen._savagesBigButton.interactable = false;' '' `
    'PMC and Scav are not selectable in an Arena session: screen._savagesBigButton.interactable = false;'
Invoke-Mutant 'side-scav-blocker-open' 'side' 'screen._savageBlocker.interactable = false;' '' `
    'PMC and Scav are not selectable in an Arena session: screen._savageBlocker.interactable = false;'
Invoke-Mutant 'side-scav-timer-relock-removed' 'side' 'try { LockCharacterChoices(__instance); }' 'try { }' `
    'the native Scav timer''s re-enabling is undone after every run'
Invoke-Mutant 'side-select-skips-whitelist' 'side' `
    "        var whitelist = ArenaWhitelistAdmission.GetRejectionReason();`n        if (!string.IsNullOrEmpty(whitelist))`n            throw new InvalidOperationException(whitelist);`n" `
    '' `
    'a click re-checks the whitelist with the fresh ping before it selects Arena'
Invoke-Mutant 'side-start-action-for-any-refusal' 'side' `
    'else if (ArenaWhitelistAdmission.CanOfferSessionStart(reason) || ArenaWhitelistAdmission.CanOfferVersionSessionStart())' 'else if (true)' `
    'START ARENA SESSION belongs to an eligible normal session'
Invoke-Mutant 'side-warnings-always-shown' 'side' `
    'else if (warnings != null && ArenaMenuSelection.IsSelected)' 'else if (warnings != null)' `
    'whitelist warnings show only once ARENA is selected'
Invoke-Mutant 'side-pmc-fallback-in-session' 'side' `
    "                if (!ArenaSessionMode.IsArenaSession)`n                    __instance._pmcButton.IsToggled = true;`n                else`n                    SetNextAvailable(__instance, false);" `
    '                __instance._pmcButton.IsToggled = true;' `
    'a failed Arena click falls back to PMC outside an Arena session only'
Invoke-Mutant 'side-start-direction-wrong' 'side' `
    '() => SwitchSession(screen, ArenaSessionContract.DirectionEnter)' '() => SwitchSession(screen, ArenaSessionContract.DirectionLeave)' `
    'the actions request the matching direction'
Invoke-Mutant 'side-profile-not-passed' 'side' 'try { profileId = Controller(screen)?.Session?.Profile?.Id; }' 'try { }' `
    'the confirmed action passes the logged-in profile'
Invoke-Mutant 'side-late-failure-ignored' 'side' `
    'ArenaSessionSwitch.Request(direction, profileId, out var error, failure => ShowSwitchFailure(screen, direction, failure))' `
    'ArenaSessionSwitch.Request(direction, profileId, out var error)' `
    'shows a failure that arrives later as well'
Invoke-Mutant 'side-waiting-notice-missing' 'side' `
    'if (ArenaSessionSwitch.HelperPending) ArenaAvailabilityNotice.ShowInfo(screen, ArenaSessionSwitch.WaitingText);' '' `
    'while the helper has not answered the screen says so'
Invoke-Mutant 'notice-rich-text' 'notice' '_text.richText = false;' '_text.richText = true;' `
    'foreign text and the action label stay literal'
Invoke-Mutant 'notice-button-rich-text' 'notice' '_buttonLabel.richText = false;' '_buttonLabel.richText = true;' `
    'foreign text and the action label stay literal'
Invoke-Mutant 'notice-single-click' 'notice' 'if (!confirmed) return;' '' 'the action needs two clicks'
Invoke-Mutant 'notice-confirm-label' 'notice' '"CONFIRM RESTART"' '"RESTART"' `
    'the confirmation label names the restart and shows its remaining seconds'
Invoke-Mutant 'notice-depends-on-core' 'notice' 'using TMPro;' "using TMPro;`nusing Redeploy.Core;" `
    'the notice stays independent of Core, settings and adapters'
Invoke-Mutant 'notice-reward-preference-returned' 'notice' 'namespace ArenaMode.Session;' `
    'namespace ArenaMode.Session; internal sealed class ArenaNoticeOption { }' `
    'reward controls belong to the Arena map menu, never the incompatible-mod notice'
Invoke-Mutant 'chain-whitelist-removed' 'optional' `
    "        var whitelistFailure = ArenaWhitelistAdmission.GetRejectionReason();`n        if (whitelistFailure != null) return whitelistFailure;`n" '' `
    'the whitelist is the first provider of the admission chain'

foreach ($key in $paths.Keys) {
    if ((Get-FileHash -LiteralPath $paths[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) {
        throw "Mutation verification changed $($paths[$key])"
    }
}
Write-Output "ARENA_SESSION_CLIENT_VERIFIED ($($script:mutants) mutants rejected on their named assertion; evidence: $scratch)"
