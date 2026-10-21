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
    mode      = Join-Path $repo 'ArenaMode.Client\Session\ArenaSessionMode.cs'
    admission = Join-Path $repo 'ArenaMode.Client\Session\ArenaWhitelistAdmission.cs'
    switch    = Join-Path $repo 'ArenaMode.Client\Session\ArenaSessionSwitch.cs'
    side      = Join-Path $repo 'ArenaMode.Client\Session\ArenaSideSelectionPatch.cs'
    notice    = Join-Path $repo 'ArenaMode.Client\Session\ArenaAvailabilityNotice.cs'
    optional  = Join-Path $repo 'ArenaMode.Client\Compat\ArenaOptionalMods.cs'
}
# How a mutated copy reaches the harness: an MSBuild property (compiled sources) or an environment variable (pinned sources).
$channel = @{
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
    'if (server != null) foreach (var unit in server.Excluded) AddExcluded(excluded, listedGuids, unit);' '' `
    'A server mod that is not whitelisted refuses Arena and offers the session'
Invoke-Mutant 'admission-names-by-name-only' 'admission' `
    'if (string.IsNullOrEmpty(unit.Guid) || listedGuids.Add(unit.Guid)) names.Add(unit.DisplayName);' 'names.Add(unit.DisplayName);' `
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
Invoke-Mutant 'admission-always-pings' 'admission' `
    'if (!ArenaSessionMode.IsArenaSession && Current().Reason == null) return;' '' `
    'A clean normal session needs no ping to open the screen'
Invoke-Mutant 'admission-notes-uncapped' 'admission' 'private const int MaxWarningsListed = 4;' 'private const int MaxWarningsListed = 40;' `
    'Notes are capped at four lines and the rest counted'
Invoke-Mutant 'admission-policy-size-unchecked' 'admission' 'if (info.Length > MaxPolicyFileBytes)' 'if (false)' `
    'A policy file above 256 KB is refused as a policy and reported'
Invoke-Mutant 'admission-session-failure-fails-open' 'admission' `
    "return ArenaSessionMode.IsArenaSession`n                ? ""Arena could not verify the session state. Return to the normal session and check the client log.""`n                : null;" `
    'return null;' `
    'An internal failure refuses Arena in an Arena session'
Invoke-Mutant 'admission-offer-for-any-reason' 'admission' `
    'string.Equals(reason, outcome.Reason, StringComparison.Ordinal)' 'true' `
    'The action belongs to the whitelist refusal only'
Invoke-Mutant 'admission-policy-hash-dropped' 'admission' 'policyHash = outcome.Policy.Hash;' 'policyHash = string.Empty;' `
    'The request carries the effective policy'
Invoke-Mutant 'admission-all-server-mods-allowed' 'admission' `
    'outcome.AllowedServerMods.AddRange(server.Allowed);' 'outcome.AllowedServerMods.AddRange(ServerUnits(serverMods));' `
    'Only the allowed server mods go into the session, in ping order'

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

# ---- side screen, notice and chain wiring (source pins)
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
    'else if (ArenaWhitelistAdmission.CanOfferSessionStart(reason))' 'else if (true)' `
    'START ARENA SESSION appears only on the whitelist refusal of a normal session'
Invoke-Mutant 'side-warnings-always-shown' 'side' `
    'else if (warnings != null && ArenaMenuSelection.IsSelected)' 'else if (warnings != null)' `
    'whitelist warnings show only once ARENA is selected'
Invoke-Mutant 'side-pmc-fallback-in-session' 'side' `
    "                if (!ArenaSessionMode.IsArenaSession)`n                    __instance._pmcButton.IsToggled = true;`n                else`n                    SetNextAvailable(__instance, false);" `
    '                __instance._pmcButton.IsToggled = true;' `
    'a failed Arena click falls back to PMC outside an Arena session only'
Invoke-Mutant 'side-start-direction-wrong' 'side' `
    '() => SwitchSession(screen, ArenaSessionContract.DirectionEnter));' '() => SwitchSession(screen, ArenaSessionContract.DirectionLeave));' `
    'the actions request the matching direction'
Invoke-Mutant 'side-profile-not-passed' 'side' 'try { profileId = Controller(screen)?.Session?.Profile?.Id; }' 'try { }' `
    'the confirmed action passes the logged-in profile'
Invoke-Mutant 'notice-rich-text' 'notice' '_text.richText = false;' '_text.richText = true;' `
    'foreign text and the action label stay literal'
Invoke-Mutant 'notice-button-rich-text' 'notice' '_buttonLabel.richText = false;' '_buttonLabel.richText = true;' `
    'foreign text and the action label stay literal'
Invoke-Mutant 'notice-single-click' 'notice' 'if (!confirmed) return;' '' 'the action needs two clicks'
Invoke-Mutant 'notice-confirm-label' 'notice' '"CLICK AGAIN TO RESTART THE GAME"' '"RESTART"' `
    'the confirmation label is exactly CLICK AGAIN TO RESTART THE GAME'
Invoke-Mutant 'notice-depends-on-core' 'notice' 'using TMPro;' "using TMPro;`nusing Redeploy.Core;" `
    'the notice stays independent of Core, settings and adapters'
Invoke-Mutant 'chain-whitelist-removed' 'optional' `
    "        var whitelistFailure = ArenaWhitelistAdmission.GetRejectionReason();`n        if (whitelistFailure != null) return whitelistFailure;`n" '' `
    'the whitelist is the first provider of the admission chain'

foreach ($key in $paths.Keys) {
    if ((Get-FileHash -LiteralPath $paths[$key] -Algorithm SHA256).Hash -ne $hashes[$key]) {
        throw "Mutation verification changed $($paths[$key])"
    }
}
Write-Output "ARENA_SESSION_CLIENT_VERIFIED ($($script:mutants) mutants rejected on their named assertion; evidence: $scratch)"
