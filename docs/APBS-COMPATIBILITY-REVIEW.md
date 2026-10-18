# APBS 2.4.1 compatibility — 2026-09-10

## User TDM acceptance, 10:53–11:06 local (UTC+02:00)

User reports the test looked good. The archived client log
`D:/SPT41/BepInEx/logs/LogOutput-2026-09-10_11-06-50.log` comes from MASCHINE11
(13900KF/RTX 4090), with SHA256
`2571BE7F8C0F4E9760C0C9AD981246C4F173DAAD3939F90395F163E6FA4EEE03`.
Frozen client/server/APBS generation logs are under
`artifacts/apbs-compat/playtest-20260910-110650/`.
Frozen server SHA256:
`AA9235CFF67A51DA798ED57CF7CECC1C99B5B9C2405BB565C9E7BC94B523F5D0`;
frozen APBS generation JSON SHA256:
`95963E9A26929631A70B232AB42FCC01ED06F8570589D8B93A34D228C25718ED`.

- APBS config migration succeeded at 08:53:08 UTC; the exact-version Arena
  adapter enabled at 08:53:09. No server error appeared during this test window.
- Customs independent TDM reached ready 5v5 at 10:55:58.754 local and ended
  normally at 11:06:03.013: 600.0 playing seconds, USEC 35:22 BEAR. Player:
  17 kills, six deaths, score 3900, best streak five.
- Applied Arena ranges progressed from 1–6 to 61–71 (anchor 66), with the
  600-second match clock. The first logged full-range replacement is at
  11:04:12.441 local. APBS's own records include Level 65/Tier 7 at 11:03:16,
  Level 69/Tier 7 at 11:04:12 and Level 71/Tier 7 at 11:05:49. This verifies
  actual generated levels/tiers, not only the adapter's requested range.
- One initial BEAR activation failed at 10:55:38.690 and recovered at
  10:55:45.098. No subsequent activation failure or match abort. There are
  61 preparation records, median `BotCreationData.Create` 1.906s, maximum
  8.635s. These are preparation durations, not total raid-loading times.
- The early `/arenamode/raid-end` warning at 11:06:11.846 was transient: native
  end followed at 11:06:11.907, snapshot restore at .928 and protected end with
  persisted snapshot at .960. Ordinary post-raid processing was skipped.

One client NRE remains, **after** successful protected end: at 11:06:12.097,
`SAIN.Components.BotComponent.OnDisable` throws during native pool/GameWorld
unloading. No client error was logged during live play. The stack identifies
SAIN cleanup; it does not prove whether Arena's body lifecycle contributed.
Do not suppress the error or claim an APBS cause without further evidence.

Movement endpoint samples have nine displacements below 2m among 146 samples
spaced at least 25s apart; eight are `SeekCover`, one `DogFight`. Bot 50 has three
such samples, with one detail report showing no active path. This does not prove
continuous immobility or a navigation fault. Separately, five airborne-mantle
attempts still reject `UnityAnimatorWrapper`; this run does not accept mantle.

This log predates the preload change `5cae5e7` (committed 12:43 local); it cannot
validate that change. The successful acceptance here covers finite independent
TDM and APBS startup migration, not solo legacy modes, protected PFT transitions
or a subsequent ordinary raid. No code/build/deployment change followed this
read-only diagnosis; only these acceptance notes were committed.

## Cause and supported boundary

APBS 2.4.1 computes its bot level range from `RaidInformation.CurrentRaidLevel`
instead of the `BotGenerationDetails.PlayerLevel` supplied by Arena. Its
`UseGroundZeroSplit` setting also controls whether **all** PMC location overrides
are honored, despite its map-specific name. With the default enabled, our range
can collapse to an edge; disabling it can bypass Arena's equipment progression.
Switching between Weighted and Delta does not repair that ownership mismatch.

The optional server adapter supports **APBS metadata version 2.4.1** only. Its
assembly version is still 2.0.0 and is unsuitable for this check. It resolves the
exact loaded mod GUID and assemblies from SPT's mod list, checks that APBS's
native level prefix is active, and validates all three synchronous signatures
before installing anything. Absent APBS keeps the existing SPT path. An
unsupported or inactive APBS produces a bounded warning and retains APBS's own
selection. Failed installation disables range application and attempts rollback;
a rollback exception cannot abort the remaining Arena patches.

No APBS code or DLL is copied into Arena, and no APBS config, global raid level,
profile, saved companion or shared bot template is modified. The implementation
is an original Harmony integration against the installed API. Reference sources:

- [APBS 2.4.1 level generation](https://github.com/acidphantasm/progressivebotsystem-csharp/blob/2.4.1/Patches/GenerateBotLevel.cs)
- [APBS 2.4.1 release](https://github.com/acidphantasm/progressivebotsystem-csharp/releases/tag/2.4.1)

## Ownership and behavior

`ArenaBotTierScalePatch` still chooses the existing Arena anchor and +/-5 level range.
Finite independent TDM uses its frozen playing clock and reaches the end of the
configured curve at 80% of match duration. Other solo modes retain server raid
time. Protected PFT teams and maps with tier scaling disabled remain excluded.

The adapter replaces only the result of APBS's private synchronous
`GetRelativePmcBotLevelRange`. APBS still rolls the final level and calculates
experience, tier metadata, prestige and subsequent inventory. Arena's requested
range takes precedence over Weighted/Delta selection only for explicitly owned
generations. The template minimum and experience-table ceiling (at most 79)
remain bounded. This is **not** a fix for contradictory custom template bounds
that already throw inside APBS before its range method can return.

Native `GenerateBotWave` returns deferred parallel enumeration. Each
`TryGenerateSingleBot` then clones the wave details. A wave-only reference marker
or a thread scope around the wave would therefore be insufficient:

1. Record an immutable permit against the exact wave details after accepted
   Arena scaling. Clear previous ownership before any early return.
2. Inside synchronous `TryGenerateSingleBot`, shadow the thread's pending permit
   even for ordinary/invalid nested calls. No scope crosses the deferred wave.
3. `PrepareAndGenerateBot` consumes that pending permit once, compares session,
   role, faction, level, range and map, and marks only its exact details clone.
4. The APBS hook revalidates the exact service, private session-entry reference,
   arm, raid start time, live state, map and current opt-in. The generation
   finalizer removes clone ownership; the outer finalizer restores thread state.

Session-entry identity rejects a replacement raid even if a caller reuses the
same arm string. Weak tables do not extend the lifetime of obsolete requests.
Normal raids, player Scavs, ordinary/menu companion generation and protected PFT
never receive a cloned permit. Existing equipment/cache/clock behavior remains.

Startup reports `APBS 2.4.1 Arena level-range adapter enabled`. Actual generation
reports `APBS Arena range applied` when the anchor changes, including applied
minimum/maximum. This distinguishes a requested ramp from a range actually used.

## Installed evidence and checks

Installed APBS DLL SHA256:
`1E6AE7DE6B5FDCECF396C656C976B8910C391DEDDD69CB27EF09FAA878C7F525`.
Installed SPT Core SHA256:
`C502D59B03C625E918EFB4CEA5F836D26FC6D99D0A5BE1DF38C90F0A8098EC88`.

`tools/ApbsCompatibilityChecks/Verify.ps1` runs the complete server suite with
deployment redirected. The installed APBS integration test must actually pass,
not skip. It loads APBS only into an isolated test process. It executes native
deferred/parallel wave enumeration, native FastCloner, production ownership
patches and APBS's actual level/XP/tier code. Inventory construction and cache
persistence are stubbed; this is not complete bot generation or a GPU raid.

Coverage includes Delta/Weighted, both override switch states, profile levels
1/70, beginning/midpoint/end of a finite match, USEC/BEAR/Scav, enabled prestige,
24 parallel bots, ordinary/PFT/opt-out pass-through, same-token rearm before
deferred enumeration, mismatched/reused clones, exceptions and nested guards.
An adapter-disabled control reproduces the global-level mismatch.
`Verify-Faults.ps1` runs a pristine positive control and removes session identity
validation only in an ignored source copy; stale/rearmed/finalizing assertions
must fail. `Verify-Contracts.ps1` reads installed IL and confirms the clone order,
synchronous range/roll/tier path and unchanged armband integration.

Independent adversarial review found startup rollback and nested-scope exception
paths needing hardening; both were corrected and the latter has a forced-error
regression. Final counts, source commit and delivery hashes are in HANDOFF.md and
the ignored `artifacts/apbs-compat` verification/delivery receipts.

## Configuration and remaining user acceptance

Before the update's first startup, the installed root config was pre-migration: new keys were
absent, while 2.4.1's installed defaults and tier data were present. APBS applies
its new model defaults and migrates this file on server startup. Arena leaves it
untouched. Existing health/skill normalization remains off; PMC generation on,
ordinary PMC loot off and both front/back plate safeguards on.

No further armband, health, cache or configuration adaptation was indicated.
APBS's loose ammo and medical changes can affect bot inventories; these are
balance observations for playtesting, not demonstrated compatibility failures.
The separate upstream PFT menu-hiring level behavior remains outside Arena's
ownership boundary; stored companions are not rerated here.

The server-only delivery and the finite TDM/startup acceptance are recorded
above. Remaining or additional user checks:

- Optional longer TDM inventory checks: available magazines/meds and equipment
  appearance. Logged levels/tiers already passed the finite TDM test.
- One solo legacy mode and one protected PFT round transition with saved companions.
- A normal PMC raid after Arena: no `APBS Arena range applied` lines for it.

No automated live raid, full mirror/hash verification, push or branch merge is implied.
