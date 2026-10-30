# Arena bot combat speech and tempo — 2026-10-27

The approved changes are deliberately narrow:

- Normal-personality Arena PMC bots may use SAIN's native combat taunts.
- The existing combat-strafe floor rises from 0.7 to 0.85. Native zero, invalid
  and higher values retain their original behavior.
- Independent Team Deathmatch and Domination cap the native search-delay input
  at three seconds instead of five. Native search predicates still decide whether
  a bot should actually search.

SAIN presets, talk probabilities, speech cooldowns, peaceful group chatter,
PFT follower speech routing, zombie voice budgets and general walk/sprint speed
are unchanged. The search-sprint path thresholds also remain unchanged.

The strafe and search adapters retain their existing exact-body, match-phase,
action-owner and native safety conditions. This does not force movement through
cover, override a medical/reload action, or change ordinary PMC/Scav raids.

## Speech ownership

The optional checked adapter substitutes only the `CanTaunt` boolean operand in
`EnemyTalk.ManualUpdate`. Native `enemyKilled` chatter, preset initialization,
cached eligibility, timing, probability, enemy checks, outer `CanTalk`, speech
output and hearing remain SAIN-owned. The source bot must be a living, active,
currently registered Arena PMC with the exact current SAIN/Talk instance and
Normal personality. The added exception requires `Mind.BotTaunts=true`; an
already-true native result is always retained. Followers,
PFT team members, zombies, old bodies and non-live phases are excluded.

Speech eligibility is independent of the bot movement switch. No extra setting
is added. Missing or incompatible SAIN leaves native behavior; ambiguous IL
binding rolls back the adapter. DeveloperMode records at most three distinct
eligible talk instances per world. Eligibility does not guarantee a spoken line.

## Offline verification and review

- Unit suite: **3,298 passed, three known environment-dependent integration
  skips**, zero failures. The verification runner confirmed no live-file changes.
- `BotTauntChecks`: **160 assertions, eight rejected compiled faults**, plus
  installed SAIN 4.5.1 metadata/IL checks.
- `BotTempoChecks`: **157 assertions, ten rejected compiled faults**.
- `TdmSearchTempoChecks`: **184 assertions, eight rejected compiled faults**.
- The parent independently reran all four leaf checks. The adversarial review
  found and closed an assembly-boundary mistake: SAIN's personality enum lives
  in `SAIN.Preset.Shared`, so binding derives its type from `Info.Personality`.
  A separate test assembly and deliberate regression cover that boundary.
- The search harness's pre-existing fixture drift was corrected with a partial
  session double, a current local-body side contract and the linked production
  `ArenaPopulationPolicy`. This changes no game code.
- Final client and companion build: zero errors, four existing nullable-context
  warnings in `ClientSettingsModels.cs`. No server/Core changes.

Local and GPU2 client DLLs were deployed together and checked for byte equality.
The inspected source/build manifest is
`artifacts/bot-talk-tempo-20261027/build-manifest.json`.

| DLL | MVID | SHA-256 |
|---|---|---|
| Client | `01bdeac4-a059-4557-8b76-e44bbdde6804` | `A6E96955A97F602C4269F2CDA803C3E2189EFFFB8A06FDD2AF565405CA5C3D28` |
| BigBrainCompat | `0a4836c8-0f9c-4b4a-a1b6-2add028296d3` | `E8B6FBBB9CBF0985B8D39AC3747CD2C5732364B1D590EB2019E128A4EFD2A640` |

## Native verification

GPU2 loaded both DLLs with the MVIDs above. The ordinary startup contained an
unlisted `ArenaWhitelistProbe`; the normal START ARENA / RESTART flow excluded
that probe in a private session. No foreign DLL or policy file was changed.

- Live Factory TDM: native search arguments from 9.5 to 75.3 seconds were capped
  at 3.0; a Normal bot with shared strafe speed 0.8 had a controller target of
  0.85. The native strafe application counter advanced from 216 to 1,667.
- Live Factory Domination: native search argument 19.6 became 3.0; 1,280 strafe
  applications were counted in the new world. These are native target inputs
  and hook observations, not movement-distance or difficulty measurements.
- Ordinary Factory PMC after returning to the normal backend: all three
  adapters remained bound, Arena was inactive and the strafe/taunt counters
  stayed zero. A Normal bot in natural combat/search retained controller target
  0.75, exactly its shared native strafe setting. The raid ended through Gate 3.

The live TDM observation exposed an existing SAIN distinction: Normal's shared
talk settings have `CanTaunt=false` and frequency 10, while newly created
EnemyTalk instances retain `CanTaunt=true` and frequency 30. The installed DLL
constructor supplies those private defaults; `BotBase.Init` only subscribes to
the preset-updated event, without initially calling the update. This matches
the observed private/shared difference; Arena does not repair or refresh it.
Consequently these fresh Normal bots already speak through SAIN's native path.
The new false-to-true operand branch has not been observed in the raid and
must not be claimed as native coverage. Its blocked-cache behavior is covered
by the source-linked executable-IL tests. No preset save, reset or alternate
preset is forced merely to produce that coverage.

The same private/shared taunt-cache difference occurred in the ordinary PMC
control. This is not evidence of an Arena leak. Normal bot speech clips were
observed, including `usec3_lostvisual_11_n` while the native speaker reported
speaking. No human listening or audio-quality acceptance is claimed.

The merged logs contain no warning from the new taunt/tempo adapters. They are
not clean overall: the recorded native weapon/material/animation and startup
messages include `JetMaterial == null`, handler hash 0 and missing foldable or
utility events. Those are retained in the test evidence without attributing
them to this change or claiming their broader causes have been resolved.

Raw evidence is under `artifacts/bot-talk-tempo-20261027/gpu/`. Final comparison
found all **228 Arena settings equal** to their fresh initial values. The normal
PMC raid ended as RUNNER through Gate 3. Test assists and temporary diagnostics
were cleared; EFT and the owned server were stopped, with no second game/server
or launcher remaining on GPU2. No canonical profile or foreign preset file was
manually restored or replaced.
