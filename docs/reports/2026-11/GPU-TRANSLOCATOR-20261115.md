# Selectable Translocator — development verification

2026-11-15, branch `codex/translocator-20261115`, based on `5be2fd3f`.
Unreleased optional mutator, default OFF. No version bump or release packaging.
[Feature contract](../../reviews/TRANSLOCATOR-20261115.md).

## Candidate identity

Candidate E was staged without deployment side effects, then only the client
DLL/PDB and server DLL were copied to the local installation and both test hosts.
It was built from the task's source manifest before commit; its base commit stamp
alone does not identify the uncommitted implementation.

| Artifact | SHA256 / identity |
|---|---|
| Client | `7AE80CEB5628E497E3A98FDB71E9319269FE44A81F75BDDF9745698B137E7CE8` |
| Client MVID | `9102b507-cfee-43f1-b3b8-f714ca608a81` |
| PDB | `55AF7669A9F27D4AC450F1B4E6552FBBA7E8B4231209AE2F0D3E290F32D50DFC` |
| Server | `EAE730FB3379CCB0920839EB7FDB4BD2BF31753BEA319ED57748F412D146CDF1` |
| Source manifest | `07CEEAC3E32F18DC25DBB1C71038B209E371C9551F714C11699B34B47C400128` |
| Unchanged released Core 1.0.24 | `AA46EC8495A7EA01A671E4612CC84E08A26AF4E4B9979860038D900E941B915B` |

The final source audit matched all 641 manifest entries. The manifest covers
Client/Server/Shared C# and project/property files plus CoreVersion.props; it is
not a complete build-environment lock. The two additional linked C# inputs match
E's PDB document checksums. Other build properties, Razor files, assets and
defaults are unchanged against the task base. SDK/NuGet/game references are not
time-bound by this source manifest.

Both native processes reported exact loaded/disk/local identity. They used their
normal localhost server and Arena's protected in-process raid path. This is not
a new session-helper restart or remote-backend compatibility test.

## Failures found and corrected

- C supplied, equipped, threw, recalled, holstered and cleared the private device.
  Ground-to-air relocation nevertheless left the idle native controller grounded
  at the old floor; the player floated for over twenty seconds. E invalidates old
  contact and runs the native ground query at the synchronized destination.
- D raised the hand but retained CanInteract's animation-IDLE predicate. That
  made the pose oscillate and blocked input. The installed graph confirms Fire
  selects `Hands.AIMING`. E admits ready IDLE/AIMING phases and their transition
  endpoints while retaining actual operation, Active and interaction guards.
- A suspected magenta donor-display leak was disproved: the donor display was
  inactive and the visible letters belonged to an Arena pickup. No speculative
  model change followed that observation.

## Native observations

Host2, Air Pit Deathmatch, two participants, continuous/no kill or time limit:

- Stable visible dark/cyan held device, native ADS false. Diagnostic ready/lease/
  fire true while native CanInteract is false in the actual AIMING phase.
- Six throws and relocations: one resting ground target, four ground-to-air
  targets, one relocation during a native jump. Fast Movement OFF for the first
  five, ON for the last. Every air relocation fell and landed without recovery input.
- Two air runs delivered secondary and Jump in the same command batch. A capped
  250-frame capture shows elevated camera and descent: all frames encoded, zero
  known misses/readback errors. `complete=false` denotes the requested frame limit.
  This is not frame-exact jump-credit or all movement-mutator acceptance.
- A click during the short post-jump hand transition was suppressed and retained
  the disc; a later click during the jump succeeded. Hand transitions remain gates.
- Scoped Arena warning/error query returned zero. Normal postraid menu contained
  no private item; runtime disc/owner/device references were null.

Host1, Customs Yard Deathmatch, same participant/rule settings:

- Stable AIMING pose and native primary/secondary actions, ADS false.
- Genuine blocked player-capsule case at a truck wall: natural throw at 21:10:50.306,
  secondary at 21:10:50.534 refused `target-obstructed`; player unchanged, disc
  retained, subsequently resting normally. No manufactured disc or collision state.
- An outward throw bounced off the area's physical enclosure and remained inside;
  relocation stayed inside. The enclosure's top is at y=38.4 m. A direct attempt
  beyond the polygon could not be reached naturally there; explicit outside-volume
  predicate negatives remain linked-source evidence, not a fabricated native test.
- Nine launches, two recalls, five confirmed relocations and one refused transfer.
  Firearm selection and inventory released the device's pose lease without losing
  its disc. Inventory primary/secondary did nothing; reselecting the same device
  restored the raised pose without ADS. Death cleared the old body and disc;
  respawn supplied one new device in a free quickslot, which launched normally.
- Leaving with a deployed disc cleared runtime and pose references. Postraid stash
  and equipment contained no private item. All 205 Arena log lines in the scoped
  test window were Debug: zero Warning/Error/Fatal. Native discard/no replacement
  was covered by C; E repeated the changed pose, inventory and respawn paths.

Both games and servers were closed normally. Full-profile comparisons before and
after restoring Syncthing passed on all three installations: only Health/Hideout
bookkeeping timestamps differ. All ten baseline profile files per installation,
including existing conflict copies, remain present; no new conflict copy appeared.
No profile was edited or repaired.

The final audit (`native/after-20261115-205600/`) checked 373 protected/config/
installed files per installation. Original configuration and PitFireTeam bytes
are intact; only the intended client DLL/PDB and server DLL differ, all at E's
exact hashes. Released Core is unchanged. Syncthing is restored to its original
unpaused state on all three machines; each reported idle, zero needed items/bytes
and zero errors (`native/sync-restored.json`).

## Offline regression

Safe client/server builds and automatic affected selection against `5be2fd3f`
passed: **74 registered harness steps plus the full xunit suite, 75/75 total,
zero failures, no cached verdicts**. Xunit: **3,929 passed, three existing
environment-gated skips, 3,932 total**. No deployed-file side effects occurred.
Only evidence-report/index/handoff wording and removal of a trailing blank line
in a test stub followed the run; production and test logic remain unchanged.

| Translocator harness | Baseline evidence | Rejected compiled faults |
|---|---|---:|
| Item | 10 unit cases, 60 harness assertions | 18 |
| Device | 103 behavior and 18 native contract assertions | 32 |
| Pose | 91 assertions including 12 native IL checks, six installed graph contracts | 32 |
| Teleport | 409 assertions plus the installed native contract | 36 |
| Runtime/disc | 43 assertions | 17 |

The compact menu passed 14,344 assertions and the TDM menu passed 2,899. Native
behavior is evidenced by the separate runs above, not inferred from these totals.

## Evidence and limits

Private logs, full profile comparisons, manifests and screenshots remain under
gitignored `artifacts/translocator-20261115/`. Five production-linked harnesses
cover item, device, pose, runtime/disc and relocation with compiled negative controls.
Native IL and asset-graph contracts describe this installed EFT build only.
An independent low-ceiling-only refusal and the item-inspection animation were
not exercised natively. Player-capsule wall refusal was exercised as described above.
Telefrag, destructible discs, bots and multiplayer are outside this first stage.
Zombie recovery's earlier pending native case is not closed by this feature test.
