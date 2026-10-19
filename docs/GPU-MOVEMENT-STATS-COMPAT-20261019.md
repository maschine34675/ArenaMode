# Classic Movement and StatTrack: native probe, 2026-10-19

Candidate: `ebb1039ae545ff33705cbdcf34dd83ed3dddd21b`, ProductVersion
`0.1.1+ebb1039ae545ff33705cbdcf34dd83ed3dddd21b`. SPT-GPU-2, dedicated test
profile, EFT 0.16.9.5 / SPT 4.1.6. Author releases Classic Movement 1.1.3 and
StatTrack 2.1.1, with their server components, were installed temporarily.
UiBridge confirmed all loaded plugin/library MVIDs match the development and
host files. Both compatibility adapters reported ready during startup.

## First run: valid gameplay evidence, interrupted lifecycle

The first ordinary Factory PMC raid had Classic Movement's actual `OldIdleState`
and `OldRunState`. An existing loaded MP5 was equipped from the test stash;
godmode and the ammunition/condition-preserving test hook protected its gear.
Eleven real shots and a real headshot kill produced identical StatTrack instance
and template records: 11 shots, one hit, one kill, one headshot kill, damage
55.63221, no boss kill and no loss. Regular Gate 3 extraction produced one
successful `/stattrack/save`. This is a positive ordinary persistence control,
not a synthetic call to a StatTrack method.

The following Arena Factory Deathmatch used four participants, endless scoring,
killcam and FAST MOVEMENT enabled. Valid gameplay observations from that process
(the two later respawns occurred after the server restart, so they do not prove
that the protected server-side lifecycle survived it):

- The local native movement cache was retained during loading. The actual player
  used `EFT.IdlePlayerState` and `EFT.MovePlayerState`; a moving bot also used
  `EFT.MovePlayerState`. Merely observing native sprint/jump classes would not
  establish this, since Classic Movement can use those in ordinary raids too.
- The actual 22-entry local state cache had native idle, run and prone classes.
- Real shots, hits, kills and initial-body retirement exercised all four
  StatTrack guard diagnostics. The Arena raid dictionary remained empty and
  the two existing permanent records retained their ordinary values.
- Twelve local shots across MCX, AK-103 and SVDS included one real local headshot
  kill. Two intentional test-hook deaths exercised real killcam and physical
  player respawn; each replacement body again used native idle/run classes.
- Native input commands produced confirmed renewed double-jump impulses and
  a timed bunnyhop (`hop 1`, carry 3.57 m/s, cap 4.67 m/s). No foreign movement
  method was invoked manually.
- FAST MOVEMENT was switched off through its ordinary config entry, movement
  continued with native idle/run, and the same jump inputs added no bonus-jump
  diagnostic. The original enabled value was restored.

The StatTrack server JSON remained byte-identical to the ordinary baseline
through Arena combat and client teardown (SHA256
`6B50F4F6388B87F56456E3F883F33A05F7BFE98F2544867370933FBAE6F2CCE4`).
Only the ordinary save request appeared; no Arena save was issued.

However, another full SPT server startup began at 23:39:54 during this raid.
The native tester issued no server command after its initial startup. EFT logged
a failed keepalive at 23:39:56. The old server's process-local protected Arena arm
was consequently unavailable at exit: at 23:41:51 the Arena end route reported
`restored: false, state: Idle`, and the exact native-end guard rejected the stale
server ID. EFT retried while remaining on an empty screen. The guard was not
weakened and no synthetic end request/profile edit was used to bypass it.

This interruption prevents acceptance of a clean Arena end and the required
following ordinary raid from this first run. It is not evidence that either
new adapter caused the server to restart. A clean lifecycle retest is required.

## Other messages

- Two EFT animation-event messages, at 23:37:56 and 23:38:11, said
  `FATAL There is no handler for hash:0`; neither included a stack trace. The
  native emitter logs an unknown animation-event key, without throwing. This is
  the event's function-name hash, not a movement-state/transition hash. Identical
  messages appeared in the earlier healing/loot and bot-effects tests, before
  these adapters; see also `GPU-FEEDBACK-20261018.md` and
  `LOG-SOURCE-AUDIT-20261018.md`. Cause remains unproven; no suppression or claimed
  fix is part of this change.
- Existing empty `LateToTheParty` and `Skwizzy.LootingBots` server directories
  caused startup `No Assemblies found` messages. They predate this batch and
  were not removed by this tester.
- Factory spawn-marker repairs, `JetMaterial == null` and unknown custom-item
  type messages appeared in EFT's own log. No new Classic Movement/StatTrack
  exception or failed StatTrack save was observed before the external restart.

## Boundaries and restoration

This is a bounded Factory test, not certification of every map, mod version,
late callback, insurance delivery, asynchronous ordinary-save race or tooltip.
The two intentional Arena deaths came from the test hook, not enemy fire.
The non-native fault-injection suite is separate evidence.

After the interruption, no more host mutations were performed while exclusive
host availability is being clarified. Restoration and final lifecycle retest are
pending. Temporary differences still to restore are DeveloperMode, the remembered
Deathmatch mode, its killcam setting, Factory solo participants (4 instead of 8),
and the test MP5's equipment slot. FAST MOVEMENT is already restored to enabled;
godmode/infinite ammo/walking/tracking are no longer active. The pending native
end retries must not be bypassed by weakening profile protection.

A subsequent read-only status reached `ExitStatus` with no active raid. Another
session was still recently active; this observation is not evidence of a clean
protected end, restored equipment/settings, or an available test host. The
temporary Classic Movement and StatTrack client/server folders remain installed
on development and GPU2 until coordinated cleanup. Existing server/Core/CH/
UiBridge hashes were rechecked after the interruption and remain unchanged.

Raw results, exact temporary
payload manifest, file snapshots and test-profile identifiers are kept only under
ignored `artifacts/movement-stats-compat-20261019/native/`.
