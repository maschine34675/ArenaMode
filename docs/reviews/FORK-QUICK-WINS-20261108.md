# Fork quick wins: named arrival gates, fixed seats, no bot backpacks, hold-to-peek table (2026-11-08)

Four small pieces adopted from the survey of the fork thatHeathenJay/ArenaMode
([report](../reports/2026-11/FORK-SURVEY-THATHEATHENJAY-20261108.md), section "Recommendation",
item 1), re-implemented against our own contracts; no fork code was copied. User decisions of
2026-11-08: all four; bot backpacks off by default and **without a switch**.

## 1. Named arrival gates (Team DM, Domination)

`TeamDeathmatchMode.Adopt` used to retire a refused native arrival with the one text "expired,
duplicate, over-capacity or inconsistent faction arrival", so the Warning in the log never said
which of nine gates had failed. `ArrivalRejection` now evaluates the same gates in the same order
and returns the first failing one by name: `stale request`, `request not admitting (preparing or
expired)`, `dead on arrival`, `faction <side|unknown>, expected <side>`, `empty profile id`,
`duplicate profile`, `over capacity (n/target)`, `group side or membership inconsistent`,
`combat relations refused registration`. The registration with the combat relations stays the last
gate and still runs only when every other gate passed (it has a side effect). The retire line stays a
Warning ("Team Deathmatch retired arrival rejected: <gate>; <state>"), as every administrative
retirement is; a body that is already dead when it is refused (`dead on arrival`, or any gate on a
dead body) gets no Warning from `Retire`, so `Adopt` names its gate in one Debug line instead.
Nothing about admission changed.

## 2. Fixed scoreboard seats (Team DM, Domination)

Live Arena's team table has five fixed names per side. Ours now has fixed **seats**: each faction's
bot slots are seats `usec-0..n` / `bear-0..n` (n = the faction's bot target minus one), assigned at
adoption as the lowest seat no living bot of that faction holds. The seat is the scoreboard's
`StableId`, so the row (`stable:<seat>`) and its running stats persist across bodies: a replacement
bot takes the seat, its name and its kills; a vacant seat (body dead, replacement not arrived) stays
on the table as OUT under the seat's name with the dead body's profile id. The seat's name is the
first occupant's display name. Replacement bodies are shown under that name everywhere a name is
shown (`RoleClassifier.DisplayName`: scoreboard, killfeed, death notices, assists) through the
match-local registry `TeamSeatNames`; **the bot profile is never written**, the registry is reset at
match start and end, and it never holds a human. Kill classification, tracer colours and the
friendly/opponent decision still run on profile ids (`_usecById`), unchanged. Posthumous kills of
an earlier body (a grenade of a dead bot) credit the seat, as the scoreboard's profile aliases did
before for stable PFT ids.

This replaces the earlier decision "new generated bots start a new personal run; they do not
impersonate a previous bot" ([TEAM-DEATHMATCH-REVIEW.md](TEAM-DEATHMATCH-REVIEW.md), amended there).
The F9 panel's footer says so. Tactical, Zombies, Deathmatch and Gun Game are untouched: their
rosters do not pass seats.

Limits: a seat is a presentation identity. The roster sync inside `Adopt` binds the seat before any
kill can reach the scoreboard, so a body that dies right after adoption is a vacant seat (OUT until
its replacement arrives, which takes the spawn pipeline's seconds), never a second row. The seat
name is the EFT nickname of the first body; the killcam and other mods that read
`Profile.Nickname` show the replacement body's own name. Two bodies of one seat can meet: a dead
body's grenade that kills its own replacement earns no kill, but the scoreboard now counts that
death and resets the seat's streak (`ArenaScoreboard.ReportKill`, same row, different body ids); a
true self-kill stays uncounted. Per body the match keeps one small entry in the seat map, the
name registry and the scoreboard's profile aliases (for posthumous credit) until the match ends;
the registry is also cleared at every raid boundary (`ArenaMatchService.OnRaidEnded`,
`RecoverAtNewRaidStart`), so a failed start or a missed GameEnd cannot carry a name into the next
raid.

## 3. No bot backpacks (Team DM, Domination; server)

`TeamDeathmatchArmbandPolicy.Apply`, which already replaced the generated PMC bot's armband after
APBS/vanilla inventory generation, now also removes the `Backpack` slot item under the equipment root
with all its descendants, in the same pass and under the same gates (live independent-team session,
exact arm token, PMC role/side, no PFT protection, consistent generation input; `IsIndependentTeam`
covers Domination). A bot whose band is already right but who carries a backpack is still rewritten.
No switch: live Arena issues no backpacks.

What the bot loses, verified in `../_decompiled` (2026-11-08): reload magazines come only from
`Inventory.FastAccessSlots` = Pockets and TacticalVest (`BotReload.GetMagazineForReload` via
`InventoryController.GetReachableItemsOfTypeNonAlloc`), so the backpack never supplied reload
magazines; loose-ammo top-up and internal-magazine reloads search Pockets, TacticalVest, Backpack and
SecuredContainer (`BotReload._availableEquipmentSlots`), and SPT generates the extra bullets into the
secure container (`BotWeaponGenerator.AddExtraMagazinesToInventory`, documented behaviour), so only
loose ammo that landed in the backpack is lost; meds and grenades in the backpack are lost, those in
rig, pockets and secure container keep working (`BotMedecine.anySlots`, `BotGrenadeController`). No
client bot code dereferences the Backpack slot item without a null check (`BotDeadBodyWork`,
`BotItemTaker`, `PatrolLootPointsData`, `InventoryEquipmentExtension`), and vanilla PMC bots already
spawn without a backpack part of the time (`SPT_Data/configs/bot.json`, `equipment.pmc.randomisation`:
`Backpack` chance 65 at PMC levels 1-14 and 80 at 15-22, no chance stated above, so the client copes
with an empty slot). The server logs one Debug line per bot whose loadout it rewrote ("Team bot
loadout: <side> armband set, k backpack(s) removed | no backpack; n -> m items"); a bot that already
wore the right band and had no backpack is returned unchanged and logs nothing.

## 4. Hold the inventory key for the team table (all Arena modes)

`ArenaInventoryKeyNode` (an EFT `InputNode` on the per-match HUD object, appended to the input tree
with `InputTree.Add`, which `InputNodeAbstract.TranslateInput` visits before every UI screen and
before the player owner) and `ArenaScoreboardPeek` (IMGUI, depth 85) implement live Arena's Tab: a
tap opens the inventory as always, a hold of 0.25 s shows the scoreboard tables until release,
without a window, cursor, input block or controls. The F9 panel keeps its controls and its
deliberate input block and wins over the peek. The decision clock is the pure `InventoryKeyHold`.

Key handling: the node resolves the first keyboard variant of the `EGameKey.Inventory` binding
(`ControlSettingsGroup.GetAllKeyBindings`, `InputSource.keyCode`, a chord), again whenever the
battle UI becomes current (a rebind in the in-raid settings is picked up on return). A binding on
mouse or joystick buttons, or none, leaves the key to the game (no hold, no block); only an
unreadable settings object falls back to Tab. The binding's press type is a user setting that is
not in the code, so both dispatches are handled: when `ToggleInventory` arrives while the key is
down (press type Press, the default) the node blocks it and forwards a short release to the game's
own started `EftGamePlayerOwner` of the living main player (`LocalGameAccessor.GetPlayerOwner`,
`State == Started`, the same gates the stock tree applies), so under that press type the inventory
opens on the release of a tap, at most 0.25 s after the press; when the command arrives on the
release (press type Release) it is blocked only after a hold, a tap passes untouched. The node is
eligible only in the battle UI of an active Arena session, outside the respawn pipeline
(`ArenaRespawnDriver.IsRespawnInProgress`, `RespawnEngineState.IsPipelineRunning`), while no Arena
panel blocks input (`ArenaInputBlocker.Blocked`, the F9 panel) and while the peek is healthy; in
every other state it ignores every command, so the inventory screen keeps receiving its own close
key and normal raids are untouched (the node exists only while an Arena match has a HUD). A key
that is already down when eligibility returns is not a press. Verified in `../_decompiled` on
2026-11-08: `InputNodeAbstract.cs:35-45`, `InputTree.cs:16-24`, `PlayerOwner.cs:207/221`,
`InputNode.cs:9-17, 51-82, 84-90` (explicit Block needed; `GetDefaultBlockResult` ignores
ToggleInventory), `TarkovApplication.cs:1679-1687` (screens sit under `UIInputRoot`),
`InventoryScreen.cs:581-593`, `GamePlayerOwner.cs:674-686, 764-801`, `EftGamePlayerOwner.cs:113-205`,
`ControlSettingsGroup.cs:123/243`, `InputSource.cs`.

Shared with the F9 panel: `ArenaScoreboardRows` (the row selection the panel used to keep privately)
and `ArenaScoreboardPresentation` (the table renderer). The panel's behaviour is unchanged. The peek
logs one Debug line when it appears and one when it hides (the native check reads them); a drawing
fault disables it for the match (one Warning) and hands the key back to the game.

## Verification

Offline, each with its mutants (`tools/Run-ArenaVerification.ps1`):

- `tools/TeamDeathmatchRuntimeChecks` (`SeatChecks.cs`: named gates for faction, role, foreign and
  mixed groups and a stale request; seats numbered from zero, vacant seat as OUT, replacement in the
  vacant seat with name and stats, seat-name registry for the replacement only, posthumous credit,
  no displacement of a living body, reset at match end, Domination seats, a body dead before its
  first tick) with the mutants `arrival-gate-unnamed`,
  `seat-per-body`, `vacant-seat-dropped`, `seat-renamed-by-replacement`, `seat-name-not-carried`,
  `seat-never-reused`, `seat-reused-under-living-body`, `seats-outlive-match`
  (`Verify-Regression.ps1`).
- `tools/RoleLabelChecks`: seat name for an AI body, never for a human, gone after reset; mutants
  `seat-name-ignored`, `seat-name-for-humans`.
- `tools/InventoryKeyHoldChecks` (new): the tap/hold clock with nine mutants.
- `ArenaMode.Tests/TeamDeathmatchArmbandTests.BackpackAndItsContentsAreRemovedWithTheArmbandSwap`
  (children listed before their parents, a second backpack) and `DominationBotsGetTheSameLoadoutMarks`,
  and `tools/TeamArmbandChecks` (mutants `BackpackKept`, `DominationExcluded`; the stale
  `OrdinaryModeLeak` anchor now pins `IsIndependentTeam`).
- `ArenaMode.Tests/ArenaScoreboardTests.AnEarlierBodyOfASeatKillingItsReplacementCostsTheLifeWithoutCredit`.
- The linked harnesses `SpawnShieldScopeChecks` and `TeamGiveUpChecks` compile the changed mode.

Native (one GPU-2 cycle, Team DM): a rejected arrival, if any, names its gate; the F9 table shows
fixed seats with a replacement under the first name and a vacant seat as OUT; the killfeed shows the
seat name for a replacement body; no backpack on bot bodies and the server's "backpack removed"
Debug lines; Tab tap opens the inventory, Tab hold shows the table while moving, the inventory's own
Tab closes it, F9 still works. The record goes to `docs/reports/2026-11/`.
