# Arena bot lifecycle safety

This client-only fix addresses two observed failures from the 2026-09-06
Labyrinth Gun Game log. The user identified that match as using the previous
build; the personality filter is not treated as the cause. The first dynamic
`Player.ApplyShot` exception does not expose its inner failing callback, so the
entire initial causal chain is not claimed as proven.

## What changes

- Native `BotEnemiesController.SetInfo` gets room for its next entry **and a
  null end marker**, before any native mutation. `Remove` also gets a marker
  when encountering an already-full array. The installed native array starts
  at 100 slots; unchecked insertion and removal's one-slot lookahead can
  otherwise throw. Capacity doubles as needed, preserving all references.
- Native dictionaries, order, priorities, counters, dispose calls and hostility
  logic are unchanged. There is no enemy pruning, arbitrary fixed replacement
  limit, retry of partially completed mutation or shared configuration change.
- Only SAIN 4.5.1 `SeekCoverAction.Stop` and `DogFightAction.Stop` are guarded.
  Both observed exceptions dereferenced a missing BotComponent. The original
  Stop still runs whenever a live cached, attached or registry component exists.
  Only a proven absent component allows the normal BigBrain transition to
  finish without that impossible component cleanup. Unknown lookup failures
  fail open, and original Stop exceptions are never swallowed.
- The dangerous `BotAction.Bot` getter is not used to test absence. Its cache,
  Unity attachment and managed registry are inspected separately, including
  destroyed Unity wrappers and destroyed managers retaining live registry data.
- Owner/body/world/game identities are captured at native `PreActivate` or an
  earlier enemy mutation in an armed/exact active Arena context. A weak stamp
  survives death and cleared raid singletons for the same body, but rejects a
  different non-null world/game or a replacement body. This protects late raid
  cleanup without authorizing a following ordinary raid. No profile-ID-only
  ownership or reset at Arena GameEnd is used.
- Logging is limited to two capacity expansions and one retired-action notice
  per owner, plus one failure warning per adapter. The stamps keep no world or
  player alive. Shutdown removes each adapter's own hooks and clears ownership.

SAIN remains optional and its files/presets are untouched. The action guard is
independent of `ExcludePassiveSainPersonalities`. There are no PFT/BigBrain
hooks or binaries bundled here. An already interrupted death cannot be rewound:
restart the game and test a fresh raid.

## Offline verification

```powershell
pwsh -NoProfile -File tools/BotLifecycleSafetyChecks/Verify-Contracts.ps1
pwsh -NoProfile -File tools/BotLifecycleSafetyChecks/Verify-Regression.ps1
pwsh -NoProfile -File tools/BotLifecycleSafetyChecks/Verify-Deployment.ps1
```

The harness links the **actual local decompiled BotEnemiesController and
BotData**, plus all three production safeguard files. The native source is not
copied into the repository or distributed. A build preparation step fixes only
an invalid decompiler-generated field/type name collision in an intermediate
copy; all enemy-list algorithms remain unchanged. Surrounding EFT/Unity/SAIN
objects are explicit test stand-ins, including Unity destroyed-object equality.
Its SAIN assembly identity exists solely for reflection tests, never deployment.

The checks first prove the unpatched native insertion/removal failures and a
missing-component action failure. They then exercise 1,100 additions, 500 mixed
priority changes/removals, full-array removals at first/middle/last positions,
reference/count/priority/disposal invariants, malformed counts, normal/foreign
contexts, retired wrappers, cleared globals, body replacement, both SAIN actions,
every component lookup route, original exceptions and partial hook rollback.
Eight temporary source mutants must fail their named behavior checks before
the final unchanged baseline passes.

Installed-assembly checks verify the array fields, indexed native writes and
removal lookahead, optional SAIN signatures, empty base Stop and exact observed
fault offsets. Independent reviews also checked all native array writers,
PreActivate binding order and fields retained by BotOwner.Dispose.

The deployment oracle runs existing corpse, PFT lifecycle, personality, spawn,
PFT and team-expansion checks before the final client Release build. It compares
deployed/client/Core hashes and verifies unchanged installed external bot-mod
files and SAIN configuration. Server/profile handling is not modified.

## Remaining runtime test

This host has no GPU. No real Unity/Harmony raid execution is claimed by the
stand-ins or static assembly checks. Test a fresh long Arena raid with many
replacement opponents, including bot deaths, player respawns and raid exit;
then an ordinary raid. Useful new log messages are `Arena enemy capacity` and
`Arena SAIN action teardown`. Neither safeguard promises recovery of already
frozen bots or proves that every possible source of freezes is fixed.
