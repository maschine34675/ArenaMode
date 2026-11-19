$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$gate=Join-Path $repo 'ArenaMode.Client/Modes/TacticalOpeningBandGate.cs'
$reply=Join-Path $repo 'ArenaMode.Client/Modes/TacticalRoundReply.cs'
$protocol=Join-Path $repo 'ArenaMode.Shared/Protocol.cs'
$tactical=Join-Path $repo 'ArenaMode.Client/Modes/TacticalMode.cs'
$sender=Join-Path $repo 'ArenaMode.Client/Session/ArenaTacticalRoundClient.cs'
$server=Join-Path $repo 'ArenaMode.Server/Services/ArenaSessionService.cs'
function Fault($name,$path,$property,$find,$replace,$failure,$environment) {
    $fault=@{Name=$name;Path=$path;Find=$find;Replace=$replace;Failure=$failure}
    if($property){$fault.Property=$property}
    if($environment){$fault.Environment=$environment}
    $fault
}
# Gate faults compile the copy into the program (Property) and point the source pins at it (Environment).
function GateFault($name,$find,$replace,$failure) { Fault $name $gate 'GateSource' $find $replace $failure 'ARENA_OPENING_GATE_SOURCE' }
function TacticalFault($name,$find,$replace,$failure) { Fault $name $tactical $null $find $replace $failure 'ARENA_OPENING_TACTICAL_SOURCE' }
function SenderFault($name,$find,$replace,$failure) { Fault $name $sender $null $find $replace $failure 'ARENA_OPENING_CLIENT_SOURCE' }
$sendLine='byte[] payload = await RequestHandler.HttpClient.PostAsync(Protocol.TacticalRoundRoute, body).ConfigureAwait(false);'
$faults=@(
    # --- the hold and its cap -------------------------------------------------------------------
    (GateFault 'cap-removed' 'if (_heldRound == round && _clock() - _heldAt >= HoldCapSeconds)' 'if (false)' 'the hold ends at the 3 second cap, whatever the ack'),
    (GateFault 'cap-thirty-seconds' 'public const float HoldCapSeconds = 3f;' 'public const float HoldCapSeconds = 30f;' 'the hold ends at the 3 second cap, whatever the ack'),
    (GateFault 'hold-clock-never-stamped' '_heldAt = _clock();' '_heldAt = 0f;' 'a round whose gear level differs from the acknowledged round''s waits for the ack'),
    (GateFault 'hold-clock-restamped' "if (_heldRound == round)`n                return;" "if (false)`n                return;" 'later MarkHeld calls keep the first stamp'),
    (GateFault 'nothing-outstanding-holds' "if (!Outstanding)`n                return TacticalBandRelease.NothingOutstanding;" "if (false)`n                return TacticalBandRelease.NothingOutstanding;" 'ShouldRelease is true when no report is outstanding'),
    (GateFault 'failing-gate-holds' "LogFault(""deciding the hold"", ex);`n            return TacticalBandRelease.NothingOutstanding;" "LogFault(""deciding the hold"", ex);`n            return TacticalBandRelease.Hold;" 'a gate whose gear numbers fail releases the held request'),
    # --- which rounds wait ------------------------------------------------------------------------
    (GateFault 'every-round-waits' 'return ackedLevel != roundLevel;' 'return true;' 'round 1 never waits for the acknowledgement (no acknowledged round counts as round 1)'),
    (GateFault 'no-ack-counts-as-round-three' 'Math.Max(1, _ackedRound)' 'Math.Max(3, _ackedRound)' 'round 1 never waits for the acknowledgement (no acknowledged round counts as round 1)'),
    (GateFault 'no-numbers-waits' "if (_gearContext() is not TacticalGearContext context)`n            return false;" "if (_gearContext() is not TacticalGearContext context)`n            return true;" 'a map without gear numbers holds nothing'),
    # --- what an acknowledgement is ---------------------------------------------------------------
    (GateFault 'any-round-settles' 'public bool Settled(int round) => _ackedRound == round;' 'public bool Settled(int round) => _ackedRound > 0;' 'an acknowledged report settles exactly its own round'),
    (GateFault 'ack-takes-the-requested-round' '_ackedRound = reply.Round;' '_ackedRound = _newestRound;' 'the stored round of the reply is trusted over the request''s'),
    (GateFault 'duplicate-is-no-ack' 'if (reply.Seq >= _newestSeq && reply.Seq >= _ackedSeq)' 'if (!reply.Duplicate && reply.Seq >= _newestSeq && reply.Seq >= _ackedSeq)' 'a Duplicate reply counts as acknowledged'),
    (GateFault 'older-answer-counts' 'if (reply.Seq >= _newestSeq && reply.Seq >= _ackedSeq)' 'if (reply.Seq >= _ackedSeq)' 'a late answer to an older report does not set the client back'),
    (GateFault 'reset-keeps-sends' "_sends.Clear();`n        _applies = true;" '_applies = true;' 'Reset ignores a late completion of the earlier match'),
    # --- resend, lost, budget ---------------------------------------------------------------------
    (GateFault 'resend-without-delay' 'if (_clock() - _lastSendAt >= ResendSeconds && !HasLiveSend())' 'if (!HasLiveSend())' 'a failed report is resent only after 2 seconds'),
    (GateFault 'unlimited-sends' 'if (_sendsOfNewest < MaxSends)' 'if (true)' 'READY pressed at once with a dead route: released, still at the last acknowledged round'),
    (GateFault 'five-sends' 'public const int MaxSends = 4;' 'public const int MaxSends = 5;' 'READY pressed at once with a dead route: released, still at the last acknowledged round'),
    (GateFault 'nothing-is-ever-lost' 'else if (!record.Lost && now - record.SentAt >= InFlightLostSeconds)' 'else if (false)' 'a send without an answer counts as lost after 8 seconds, and the report is sent again with the same seq'),
    (GateFault 'lost-after-one-second' 'public const float InFlightLostSeconds = 8f;' 'public const float InFlightLostSeconds = 1f;' 'the hold lasts 3 seconds from the first stamp'),
    (GateFault 'first-failure-keeps-the-feature' 'if (_ackedRound < 1)' 'if (false)' 'a first report that never reaches the server ends the feature, with one warning'),
    (GateFault 'later-failure-ends-the-feature' 'if (_ackedRound < 1)' 'if (true)' 'a failure after an earlier success keeps the feature, stays at the last acknowledged round, and warns once'),
    (GateFault 'warning-every-time' "if (_warnedOnce)`n            return;" "if (false)`n            return;" 'one warning per raid for a failing route'),
    (GateFault 'reset-keeps-the-warning' "_warnedOnce = false;`n        _faultLogged = false;" '_faultLogged = false;' 'the once-per-raid warning is written again in the next raid'),
    # --- refusals ---------------------------------------------------------------------------------
    (GateFault 'refusal-ignored' 'EndFeature($"the server refused the round report ({reply.Reason ?? "no reason"})");' '' 'a refusal ends the feature for the raid: nothing is resent, nothing is warned'),
    (GateFault 'not-applying-ignored' 'EndFeature("the server writes no Tactical stage band for this raid");' '' 'applies:false ends the feature for the raid without a warning'),
    (GateFault 'raid-ending-is-final' 'if (string.Equals(reply.Reason, Protocol.TacticalRoundRaidEndingRefusal, StringComparison.Ordinal))' 'if (false)' 'the raid-ending refusal is transient: the feature stays on, nothing is warned, nothing is acknowledged'),
    (GateFault 'raid-ending-warns' "if (!_transportFailure)`n            return;" "if (false)`n            return;" 'the raid-ending refusal never warns, even when the send budget is spent'),
    (GateFault 'refusal-keeps-the-level' 'if (!_applies || _ackedRound < 1 || _gearContext() is not TacticalGearContext context' 'if (_ackedRound < 1 || _gearContext() is not TacticalGearContext context' 'after a refusal there is no gear level to enforce'),
    # --- seq and token ----------------------------------------------------------------------------
    (GateFault 'seq-per-gate' 'int seq = Interlocked.Increment(ref s_lastSeq);' 'int seq = _newestSeq + 1;' 'the seq counter is process-wide: a new match and a new gate report above every earlier seq'),
    (GateFault 'seq-unbounded' 'if (seq < 1 || seq > Protocol.TacticalMaxSeq)' 'if (false)' 'a seq outside 1..1,000,000 is never sent'),
    (GateFault 'blank-token-sent' "            string token = _armToken();`n            if (string.IsNullOrWhiteSpace(token))" "            string token = _armToken();`n            if (false)" 'a blank arm token sends nothing and ends the feature'),
    # --- never throws -----------------------------------------------------------------------------
    (GateFault 'report-throws' 'LogFault("reporting the round", ex);' 'throw;' 'a failing report never throws into the match start'),
    (GateFault 'tick-throws' 'LogFault("polling the round report", ex);' 'throw;' 'a failing poll never throws into the frame loop'),
    # --- the gear level the stage after this one reads --------------------------------------------
    (GateFault 'scav-has-a-level' '|| !context.SquadIsPmc' '|| false' 'a scav squad is reported and acknowledged but has no gear level'),
    (GateFault 'flat-map-has-a-level' '|| !ArenaDifficultyPolicy.TacticalOpeningDiffers(context.Step, context.StartLevel, context.EndLevel))' '|| false)' 'a map whose opening equals the full level has no gear level'),
    # --- the shared constant, the reply parser ----------------------------------------------------
    (GateFault 'gate-own-refusal-text' 'Protocol.TacticalRoundRaidEndingRefusal, StringComparison.Ordinal))' '"raid is ending", StringComparison.Ordinal))' 'the gate compares the raid-ending refusal through the shared constant'),
    (Fault 'server-own-refusal-text' $server $null '? Protocol.TacticalRoundRaidEndingRefusal' '? "raid is ending"' 'the server writes the shared raid-ending constant, not its own copy of the text' 'ARENA_OPENING_SERVER_SOURCE'),
    (Fault 'protocol-refusal-text-changed' $protocol 'ProtocolSource' 'public const string TacticalRoundRaidEndingRefusal = "raid is ending";' 'public const string TacticalRoundRaidEndingRefusal = "raid ends";' 'the raid-ending refusal text is the wire contract (the server''s xunit test pins the same text)'),
    (Fault 'protocol-seq-bound-changed' $protocol 'ProtocolSource' 'public const int TacticalMaxSeq = 1_000_000;' 'public const int TacticalMaxSeq = 2_000_000;' 'the seq bound is the server''s 1,000,000'),
    (Fault 'reply-accepted-defaults-true' $reply 'ReplySource' 'Accepted = Required<bool>(root, "accepted"),' 'Accepted = root["accepted"]?.Value<bool>() ?? true,' 'a reply without a usable accepted, applies, round and seq is malformed: {"applies":true,"round":1,"seq":1}'),
    (Fault 'reply-seq-optional' $reply 'ReplySource' 'Seq = Required<int>(root, "seq"),' 'Seq = root["seq"]?.Value<int>() ?? 0,' 'a reply without a usable accepted, applies, round and seq is malformed: {"accepted":true,"applies":true,"round":1}'),
    # --- the glue in TacticalMode.cs --------------------------------------------------------------
    (TacticalFault 'endround-no-report' '_openingBand.Report(RoundNumber);' '' 'EndRound reports the new round right after RoundNumber++'),
    (TacticalFault 'endround-reports-before-increment' @('_openingBand.Report(RoundNumber);', "        RoundNumber++;`n`n        // The next round's gear band") @('', "        _openingBand.Report(RoundNumber);`n        RoundNumber++;`n`n        // The next round's gear band") 'EndRound reports the new round right after RoundNumber++'),
    (TacticalFault 'endround-resets-the-gate' "_bandHeldRound = 0;`n        _phase = Phase.Aftermath;" "_bandHeldRound = 0;`n        _openingBand.Reset();`n        _phase = Phase.Aftermath;" 'a round end must not drop the report it has just made'),
    (TacticalFault 'dispatch-no-hold-stamp' '_openingBand.MarkHeld(round);' '' 'the band gate sits between the guard and the squad request: it stamps its clock, asks, and returns while it holds'),
    (TacticalFault 'dispatch-no-hold' 'if (!_openingBand.ShouldRelease(round))' 'if (false)' 'the band gate sits between the guard and the squad request: it stamps its clock, asks, and returns while it holds'),
    (TacticalFault 'dispatch-request-before-gate' '_openingBand.MarkHeld(round);' "RequestSoloSquad(retry: false);`n        _openingBand.MarkHeld(round);" 'the band gate sits between the guard and the squad request: it stamps its clock, asks, and returns while it holds'),
    (TacticalFault 'tick-hold-after-evaluate' @("if (_bandHeldRound == RoundNumber)`n            {", 'if (spawnDecision == SoloTacticalSpawnDecision.Waiting) return;') @("if (_bandHeldRound == -1)`n            {", "if (spawnDecision == SoloTacticalSpawnDecision.Waiting) return;`n            if (_bandHeldRound == RoundNumber) return;") 'the band hold returns before SoloTacticalSpawnPolicy.Evaluate, which would answer Retry and send the request ahead of the ack'),
    (TacticalFault 'tick-hold-does-not-return' "if (band == TacticalBandRelease.Hold)`n                    return;" "if (band == TacticalBandRelease.Hold)`n                    { }" 'while the gate holds TickLive returns, and once it lets go the request is dispatched and the frame ends'),
    (TacticalFault 'tick-dispatch-falls-through' "DispatchOpeningSquad(RoundNumber, SoloTacticalSquadRelease.BodyLanded, 0f);`n                return;" 'DispatchOpeningSquad(RoundNumber, SoloTacticalSquadRelease.BodyLanded, 0f);' 'while the gate holds TickLive returns, and once it lets go the request is dispatched and the frame ends'),
    (TacticalFault 'hold-log-no-seconds' '{_openingBand.HeldSeconds(RoundNumber):0.000}s' '0.000s' 'the Debug line of a held request carries the real held seconds and whether the cap released it'),
    (TacticalFault 'hold-log-no-cap' 'released by the cap: {band == TacticalBandRelease.CapExpired}' 'released' 'the Debug line of a held request carries the real held seconds and whether the cap released it'),
    (TacticalFault 'start-no-reports' 'BeginOpeningReports();' '' 'OnMatchStart starts the reports in the solo branch only, after the opening kit is wired'),
    (TacticalFault 'start-reports-before-the-branch' @("        BeginOpeningReports();`n    }", "_match = match;`n        _teamArena = PitFireTeamCompat.TeamArenaActive;") @('    }', "_match = match;`n        BeginOpeningReports();`n        _teamArena = PitFireTeamCompat.TeamArenaActive;") 'OnMatchStart starts the reports in the solo branch only, after the opening kit is wired'),
    (TacticalFault 'start-no-first-report' '_openingBand.Report(1);' '' 'the match start resets the gate, publishes it and reports round 1, and nothing in it can throw'),
    (TacticalFault 'start-not-published' 'ArenaTacticalOpening.BindToCurrentWorld(_openingBand);' '' 'the match start resets the gate, publishes it and reports round 1, and nothing in it can throw'),
    (TacticalFault 'start-can-throw' "catch (Exception ex)`n        {`n            Plugin.ModLogger.LogWarning(`$""Tactical opening rounds could not start" "catch (InvalidOperationException ex)`n        {`n            Plugin.ModLogger.LogWarning(`$""Tactical opening rounds could not start" 'the match start resets the gate, publishes it and reports round 1, and nothing in it can throw'),
    (TacticalFault 'tick-no-poll' "if (!_teamArena)`n            _openingBand.Tick();" '' 'the report''s answer is polled every frame, before the native-menu early return'),
    (TacticalFault 'tick-polls-after-the-menu' @("if (!_teamArena)`n            _openingBand.Tick();", "case Phase.Staging:`n                TickTeamStaging();") @('', "case Phase.Staging:`n                if (!_teamArena)`n            _openingBand.Tick();`n                TickTeamStaging();") 'the report''s answer is polled every frame, before the native-menu early return'),
    (TacticalFault 'end-keeps-the-gate' "_bandHeldRound = 0;`n        _openingBand.Reset();`n        ArenaTacticalOpening.Unbind(_openingBand);" "_bandHeldRound = 0;`n        ArenaTacticalOpening.Unbind(_openingBand);" 'the match end clears the held round, resets the gate and unpublishes it'),
    (TacticalFault 'gate-on-scaled-time' '() => Time.unscaledTime,' '() => Time.time,' 'the gate runs on unscaled time with the HTTP sender'),
    (TacticalFault 'gate-debug-at-info' 'message => Plugin.ModLogger.LogDebug(message),' 'message => Plugin.ModLogger.LogInfo(message),' 'the gate''s lines are Debug and its one-per-raid failure a Warning, never Info'),
    # --- the HTTP sender --------------------------------------------------------------------------
    (SenderFault 'sender-uses-the-wrapper' $sendLine 'byte[] payload = Encoding.UTF8.GetBytes(RequestHandler.PostJson(Protocol.TacticalRoundRoute, Encoding.UTF8.GetString(body)));' 'the report goes through RequestHandler.HttpClient.PostAsync inside Task.Run, without the wrapper''s Info lines'),
    (SenderFault 'sender-not-in-task-run' 'return Task.Run(async () =>' 'return Task.Factory.StartNew(async () =>' 'the report goes through RequestHandler.HttpClient.PostAsync inside Task.Run, without the wrapper''s Info lines'),
    (SenderFault 'sender-logs-info' 'return Task.Run(async () =>' "Plugin.ModLogger.LogInfo(""report"");`n        return Task.Run(async () =>" 'the report goes through RequestHandler.HttpClient.PostAsync inside Task.Run, without the wrapper''s Info lines'),
    (SenderFault 'sender-blocks' $sendLine 'byte[] payload = RequestHandler.HttpClient.PostAsync(Protocol.TacticalRoundRoute, body).Result;' 'the report never blocks the calling thread on its task'),
    (SenderFault 'sender-wrong-route' 'Protocol.TacticalRoundRoute, body)' 'Protocol.StatusRoute, body)' 'the report is posted to the Tactical round route'),
    (SenderFault 'sender-body-without-seq' 'new { armToken, round, seq }' 'new { armToken, round }' 'the report body carries armToken, round and seq')
)
Test-ArenaSourceFaults (Join-Path $PSScriptRoot 'TacticalOpeningChecks.csproj') 'ARENA_TACTICAL_OPENING_CHECKS_PASSED' @() $faults -RequireSingleSite
Write-Output 'ARENA_TACTICAL_OPENING_CHECKS_VERIFIED'
