# Own gear in every mode: contract and verification (2026-11-09)

The player's own, protected equipment instead of an Arena kit was a Zombie option (item 4 of the
2026-10-06 follow-up) and a protected PitFireTeam Tactical choice (PFT-TACTICAL-KITS-20261018.md).
Since 2026-11-09 the left mode panel offers the same EQUIPMENT row ("Arena kits" / "Own gear") for
Deathmatch, Team Deathmatch, Domination and Zombies played alone. Protected squads are unchanged:
PFT Zombies keeps own gear without a choice, PFT Tactical keeps its `Tactical/TeamOwnGear` preference
and its entry-kit machinery. Solo Tactical had the option for a day (built, reviewed, run natively)
and lost it again the same day on the user's decision: its buy economy is the mode, the gear is
earned in the match. The native report keeps what that run found.

## What own gear means

- The raid-start kit swap is skipped. `ArenaSession.CompleteRaidStart` captures the PMC's live
  equipment once into `Kits/ArenaOwnGearLoadout` (the `TeamArenaLoadoutSnapshot` primitive the
  protected squad already uses: a detached JSON graph, fresh ids per restore, secure container,
  scabbard, armband and special slots carried over from the body being replaced) and warms the
  spawn cache, because unlike one-life Zombies these modes respawn.
- Every death respawn restores the entry loadout, ammunition included, on the driver's ORDINARY
  path (`ArenaRespawnDriver.RespawnRoutine`): the own-gear decision sits after the frozen-roster
  branch and before the kit draw, and the Team DM terminal hold, the team-box reservation, the
  area preflight and the failure routing are shared with kits. The driver's own-gear equipment
  strategy first fails closed when the dead body has no corpse (the kit path's `requireCorpse`
  skips silently there) and then calls `ArenaOwnGearLoadout.Reissue`, which settles the quick-slot
  ledger, restores and renames the ledger's items. EFT creates the corpse inside `Player.OnDead`,
  which runs before the game's `DiedEvent` handler Redeploy intercepts (the player subscribes in
  its `Init`, the game later), so the guard does not fire in practice.
- `LiveKitRespawn.AtRoundBreak` is own-gear aware: Deathmatch's round-break respawn ("Fresh Kit At
  Round End") restores the entry loadout instead of a kit (heal + resupply through the same live
  swap). An own-gear raid never reaches the kit catalogue from there; a frozen roster is refused
  outright (PFT Zombies drives `AtRoundBreakOwnGear` itself). Team DM and Domination have no round
  break; they rely on the death respawn alone. (Solo Tactical's survivor reissue would take the same
  branch, but its gate never opens: Tactical is not in the solo table.)
- The corpse cleanup leaves the retained slots and the pockets on the player's own corpse
  (`CorpseCleanupService.StripGear`, own corpse only; the heavy models still go). The restore reads
  the secure container, scabbard, armband and special pocket slots from the body being replaced and
  refuses when one is missing; Tactical's round-start clear trims that corpse before a held respawn
  runs, and on GPU-2 the (since withdrawn) own-gear Tactical attempt failed on the stripped bayonet
  (see the report). Redeploy's EquipmentSurgeon carries the same slots over in kit raids, so the
  held kit respawn of solo Tactical and any killcam-delayed respawn profit from the rule.
- Items picked up during a life (the Pickups mutator, loot) are not kept across a restore, as with
  kits. The secure container is never emptied (`ArenaSecureContainer.LeavesContentsAtHome`) and the
  slot stays visible; `Kits/EmptySecureContainer` is inert for own gear. The loading-screen kit
  preload warms nothing and reserves no random kit (`KitBundlePreloader.LoadingScreen`), the in-raid
  optional catalogue warm-up stays off (`ArenaController`), and the character preview and loading
  figure show the native equipment (`ArenaPreviewFlow.UsesOwnEquipment`).
- Nothing is lost or gained: the server's whole-profile snapshot/restore around the raid is
  unchanged, as are rewards. The server knows nothing of the choice (`ArenaMode.Server` and
  `ArenaMode.Shared` have no own-gear reference).

## Solo Tactical keeps its kit economy

Tactical played alone is a buy mode: the starter is free, kills and rounds pay roubles, the next
round's kit is bought or retained. Bringing one's own loadout would hollow that out, so the row is
not offered there (`ArenaOwnGearPolicy` leaves Tactical out of the solo table; `Settings` has no
`Tactical/OwnGear`) and `TacticalMode`/`TacticalBuyPanel` are the pre-2026-11-09 code. For one day
the option existed (no starter, no shop, no roubles, the buy countdown as the pause, the held respawn
restoring the loadout) and ran natively; the user withdrew it the same day because the gear is meant
to be earned in the match. What that run found, the corpse-strip rule below, stays, because it also
protects the retained slots the kit path carries over.

## Gun Game is excluded

The ladder is the loadout: a rung is a whole kit or the earned weapon merged onto the base kit
(`KitMerge.WithPrimaryWeapon`), so there is no "own" equipment a rung could be merged into. The row
stays hidden for Gun Game and its plain fact ("Assigned weapons") remains. `ArenaOwnGearPolicy`
names the four solo modes; the row and the raid read the same table.

## The PitFireTeam option is named again

Since the compact menu copy of 2026-10-26 a solo player saw no mention that Tactical and Zombies can
be played as a PitFireTeam squad (the catalog's long description, which named it, left the screen).
The second description line of solo Tactical ("Rounds; death / timeout loses; PFT optional.") and
the first of solo Zombies ("Survive escalating waves; PFT optional.") name it, abbreviated as the row
labels abbreviate it ("3v5 PFT", "ZOMBIES / PFT"); the catalog's Zombie description says
"PitFireTeam optional." too. Both lines stay inside the 44-character budget
(`ArenaCompactModeRules`, pinned by CompactModeUxChecks and TdmMatchMenuChecks).

## Settings, freezing and the gate

- One independent entry per mode and roster: `Deathmatch/OwnGear`, `Team Deathmatch/OwnGear`,
  `Domination/OwnGear` (all default false, Arena kits stay the Arena default), the existing
  `Zombie/OwnGear` (default true) and `Tactical/TeamOwnGear` (protected squad, default true).
  `Settings.EquipmentChoiceEntry(mode, protectedTeam)` dispatches; a protected party reads an entry
  only for Tactical, a solo player none for Tactical. The F12 layout carries the three new rows
  (`ArenaConfigMenuLayoutTests` enforces).
- `ArenaSession.MarkArmed` freezes `OwnGearSelection = ArenaOwnGearPolicy.SelectionAtArm(mode,
  HasFrozenTeamRoster, GetEquipmentChoiceOwnGear(mode, false))`: alone, the mode's own preference;
  with a protected squad, true for Zombies and false for Tactical (whose squad decides through
  `TacticalTeamOwnGearSelection`), with the same trimmed, case-insensitive comparison the gate uses.
  `OwnGearArmed` (against `ArmedMode`, loading-time) and `OwnGearActive` (against `ActiveMode`,
  live) are the two readers; the six reset sites of the old `ZombieOwnGearSelection` were renamed.
- `Modes/ArenaOwnGearPolicy` (was `ZombieOwnGearGate`): `IsOffered(mode, protectedTeam)`,
  `IsSelectedFor(mode, selection)`, `SelectionAtArm`. Pure; linked into ArenaMode.Tests.

## Failure semantics

A restore that fails poisons the snapshot's gate for the rest of the raid (as for the protected
squad). On the death path the driver routes it like any failed body replacement: the Team DM result
coordinator, the team-box or area abort, or, where plain Deathmatch and solo Tactical would otherwise
run the vanilla death screen on an owner the engine already destroyed, the verified-owner
`ScheduleFailedStartAbort`. A live reissue that fails keeps `SwapRoutine`'s ordinary recovery
(the player keeps the current body); when the snapshot is poisoned (`ArenaOwnGearLoadout.IsPoisoned`)
and the caller is `AtRoundBreak` (Deathmatch's round break: a mode that respawns on death),
`LiveKitRespawn` ends the match through the same abort at once rather than one
life later. Zombie's wave refill (`AtRoundBreakOwnGear`) does not ask for that abort and degrades
gracefully as before (one life, no death respawn to doom). A player who chose own gear is never
handed a random kit. The GC threshold monitor hears the new-body moment through
`ArenaEvents.OnOwnGearReissued`, raised after the own-gear death respawn and after every live swap
that restores a loadout (the round-break reissue, Zombie's wave refill, the protected Zombie squad's
restore), where the kit refill's `OnKitApplied` gives it in a kit raid.

The EQUIPMENT row's tooltip names the consequence per mode (Deathmatch: every respawn and the
round break; Team DM and Domination: every respawn; Zombies: after each cleared wave, one life;
protected Tactical: the PMC only).

## Design review (2026-11-09)

Five read-only critics (respawn/Team DM, Tactical economy, menu/preview/preload, PFT interplay,
harness anchors) read the design note against the source before implementation. Taken: the corpse
guard, the fatal fallback for plain modes, the poisoned-snapshot abort at a round break, the GC
monitor event, the explicit own-gear guards in Tactical (free kits stayed buyable at Money 0, the
cheapest-kit fallback ran for a dead player, the starter lookup could throw), the suppressed panel
auto-open, the normalized Zombie comparison at arm, `EquipmentChoiceEntry` returning null for a
protected party outside Tactical, and the harness list below. Rejected: an own-gear economy
(nothing to price) and a kit-only `AtRoundBreak` with per-mode call sites (one decision point,
documented). Noted: the design note's claim that the round break helps Team DM and Domination was
wrong (they have none). The implementation review (five finders, two skeptics per finding, 35
agents) confirmed one behavioural slip, the poisoned-snapshot abort reaching Zombie's wave refill,
fixed by letting only `AtRoundBreak` ask for it, and otherwise documentation and comment drift,
a vacuous harness assertion (fixed with a buyable kit and a compiled fault) and two Warning lines
for routine stand-downs (now Debug).

## Verification

Offline, no deploy: xunit (ArenaOwnGearPolicyTests, ArenaEquipmentChoiceTests with roster-aware
fixtures, ArenaConfigMenuLayoutTests), then the harnesses whose production files or anchors the
change touched: ZombieKitWarmupChecks (plus an own-gear case for the loading skip and the warm-up
in the four respawning modes and a fault on the new `ArenaController` return), TdmWarmupChecks,
PreviewHostLifecycleChecks (matrix with Domination and the two Tactical preferences),
ArenaCharacterPreviewChecks (real EFT API compile), ZombieTeam/Solo/WavePendingChecks,
TacticalPolishChecks (unchanged code since the withdrawal), CompactModeUxChecks and
NativeModeSelectionChecks (the PitFireTeam mention within the copy budgets),
AreaConfigChecks, ArenaKillcamChecks, BleedingCleanupChecks, GcThresholdChecks, DeathmatchChecks,
CrowdReactionChecks, PitFireTeamChecks (renamed anchors: the own-gear decision follows the frozen
PFT branch and precedes the kit draw), TeamArenaLoadoutChecks, TeamArenaExpansionChecks,
FailedStartAbortChecks, ArenaPostRaidChecks, TdmMatchChecks, TdmMatchMenuChecks (the row in every
solo mode but Gun Game, each entry independent), SoloTacticalSpawnChecks, PftTacticalKitChecks
integration. Numbers and the native GPU-2 run are in HANDOFF.md and the dated report.

Not covered offline: the driver lambda and `AtRoundBreak`'s own-gear branch run only in the game
(no csproj compiles them; they are text-anchored), so the native cycle has to see a death respawn
and a round-break reissue with own gear. It did, on GPU-2 on 2026-11-09
([report](../reports/2026-11/GPU-OWN-GEAR-20261109.md)): Deathmatch (raid start, death respawn,
round break), Team DM (three respawns, two of them bot kills after the killcam) and solo Tactical
(the held respawn to the next round, after the corpse-strip fix) each restored the entry loadout
with full ammunition; the server restored the profile after every raid. CorpseCleanupChecks carries
the own-corpse case and a compiled fault for the rule.

## Limits

- Own gear is the local PMC's; PFT companions keep theirs in every case. Solo Tactical and Gun
  Game have no own-gear option (above).
- A restore moves the player to a spawn point (the round break) or the respawn position, as kits
  do; mid-life pickups and loot are not kept.
- Zombie's one-life rule is unchanged.
