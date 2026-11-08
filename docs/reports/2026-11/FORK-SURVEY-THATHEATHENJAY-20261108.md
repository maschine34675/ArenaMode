# Survey of the fork thatHeathenJay/ArenaMode (2026-11-08)

https://github.com/thatHeathenJay/ArenaMode, branch `master` at `d3e3f874` (pushed 2026-11-04).
Read-only survey from a scratchpad clone; nothing was built, run or copied. Five readers (co-op,
live UI, modes and bots, kits and server, small changes) and a coverage critic, 60 findings.

## What the fork is

- Not a git descendant: its first commit `2125bbd4` imports our `master` of 2026-11-01 (our
  `65007e74`, the Domination map-markers merge) as one commit; 109 commits by thatHeathenJay
  follow on 2026-11-02 and 2026-11-03. Against that base it changes 211 files, +95k/-10.8k lines;
  53k of those lines are reference JSON under `tools/live-data`, 29k the generated
  `ArenaMode.Server/defaults/metascore.json`.
- No documentation, README, CHANGELOG, test or harness change in 109 commits; two harnesses no
  longer compile against the fork's client (`ArenaLabyrinthAccessChecks` stand-ins) and
  `PftKitEntryChecks` pins the opposite of a changed entry-kit contract.
- Identity: the fork still calls itself ArenaMode 0.4.1 with our plugin GUID and package names
  (our 0.4.1 is a real release); its companion plugin is `com.maschine.ArenaMode.Fika` 0.1.0; its
  csproj references our private `..\..\Redeploy\Redeploy.Core` path. A build of it is
  indistinguishable from ours in the log header.
- No binary asset is committed. The live-Arena menus, HUD, result screen, announcer and hit
  sounds are rebuilt at runtime from a pack the player rips from their own EFT Arena install
  (`tools/Rip-ArenaUi.ps1`, `Rip-ArenaBundles.ps1`, `arena-ui-export.py`, `pack-arena-ui.py`,
  a TextMeshPro shim): BSG prefabs, sprites, fonts and clips. The pack's presence, not
  `ArenaSession.IsActive`, gates those screens.

## Areas and verdicts

### Co-op (Fika): a different architecture from ours

`ArenaMode.Fika` (818 lines; Fika 2.4.6 only, fails closed on other versions) plus about 800
client and server lines. Every lethal hit downs the player through Fika's downed/revive
transport with the revive gameplay suppressed; the player comes back in the same body, re-kitted
and restocked in place; the chosen kit is written into the real PMC profile before the raid
exists so Fika syncs it; the host is the judge of a TeamFight round state sent as one snapshot
packet (no match id, sequence or authentication); the server mutates a Fika-served location in
place at the protected start; a client seam `ArenaCoop` replaces every Fika refusal, and the
whitelist's `HasFika` returns false once the companion announced itself.

Our branch `feature/fika-admission` (another session) keeps the Fika refusal in the shipping
Arena, retires the body per life behind HMAC barriers, never writes a normal profile and runs a
server-side full-profile gate with snapshot restoration. The two designs cannot be mixed; this
is an architecture decision for the Fika work, not a cherry-pick. Worth taking from the fork
there: the fact that Fika's Host/Join lobby lives on the confirmation screen (our
`SkipPreRaidScreens` must not skip it under Fika), the inversion seam idea (a companion fills
delegates, the client refuses until it does), and the point where a Fika-served location would
need the arena mutation. Not to take: the profile write of the entry kit (gear stays in the
stash after a crash or a Fika leave), the downed-state transport (no corpse, loot or death
event for other clients), the whitelist bypass, the global `LocalGame` alias to
`BaseLocalGame<EftGamePlayerOwner>` (changes the meaning of every `is LocalGame` in the client).

### Live-Arena UI: not adoptable, except two asset-free pieces

Main menu overlay, battle flow (SELECT GAME TYPE / MODE / ARENA), presets list and view,
customization, armory, deploy screen, in-match HUD, death card, result screen, exit popup, ESC
menu, announcer bank, hit-marker clips and the match-end sting: all drawn from the ripped pack,
so none of it can ship in our package (THIRD-PARTY-NOTICES: no extracted EFT Arena graphics or
audio). The ESC-menu replacement is also not Arena-gated (pack present + raid + MainMenu screen)
and the damage recorder of the death card runs in every raid: both break our bit-identical rule.

Asset-free and worth adapting with our own layout: the **hold-Tab team table** (an EFT
`InputNode` that swallows `ToggleInventory` on BattleUI; tap forwards the inventory, a hold of
0.25 s shows the table; 79 lines, hard-coded `KeyCode.Tab` instead of the bound key), and the
**kit editor on EFT's own inventory UI** (`ArenaKitInventoryEditor`, `ArenaStockBuildEditor`,
about 830 lines: EFT's `EquipmentTab`/`ContainersPanel` cloned over the preset's equipment with an
endless armory grid as a fake stash, every drag and swap through EFT's own drag-and-drop, state
gated and fail-open). The latter is the best-engineered block of the fork and the only new
capability without BSG art; it needs our own host window, our preset storage contract and
class/budget rules we have not defined (about eight days).

### Modes and bots: the Tactical objective package

"TeamFight" is a rounds layer on Team Deathmatch (kill limit reused as rounds, one life per
round, first to N, 90 s opening, a capture point, elimination or timeout win), host-judged under
Fika and local-only solo. Relative to our Tactical (best-of-N elimination with a buy phase) the
new parts are independent of Fika and of the rip:

| Piece | Fork | Verdict |
|---|---|---|
| `TeamFightRoundPolicy` (144 lines) | pure clock state machine: capture win, "more players standing at window end" tiebreak, 0:0 draw | adapt into `TeamArenaRoundPolicy` (keep our draw cap and NoContest), harness with mutants |
| Capture point in an elimination round | one random Domination point, 10 s alone to win, contested resets | adapt via our `DominationOccupancy` and path-verified point choice |
| Visible capture zone (45 lines) | translucent disc plus beacon, tinted by holder, no assets | adapt (materials leak per round; create only under `IsActive`; GPU check) |
| Round reset (74 lines) | closes every `Door` at the countdown, clears corpses, learns spawn gates by an 8 s timing heuristic | adapt the door closing; gates through our MapPort gate lease, corpses as Tactical does |
| Opening rush (65 lines) | bots fan out across lanes perpendicular to the team centroids, then to the point; reuses our Domination BigBrain bridge | adapt; stop the bridge at round end; GPU check on narrow centres |
| Persistent scoreboard seats (60 lines) | stable seat ids per bot slot, dead rows stay, a replacement takes the seat's name | adopt (display-level name, not `Profile.Info.Nickname`) plus a harness pin |
| No bot backpacks (server, 10 lines) | TDM/Domination bots lose the Backpack subtree with the armband swap, as in live Arena | adopt behind a server switch; harness update; watch bot ammo in long rounds |
| TDM retire diagnostics (15 lines) | the retire log names which admission gate failed | adopt at Debug |
| Arena SAIN preset override (50 lines) | forces a named SAIN preset by reflection, not Arena-gated, no preset values shipped | adapt only Arena-gated with restore and our SAIN version contract |

### Kits, presets, armory data

- Server preset editor (653 lines) with `/arenamode/presets/edit`, `/armory`, `/metascore`:
  copy a stock preset into "Custom n", swap slots, attachments by path, ammo, supplies, rename,
  delete; stored per session outside the profile (`config/custom-presets/<sessionId>.json`), so
  the profile rule holds, but nothing enforces class access or the 7,500-10,000 metascore band at
  play time (bars only). Custom ids derive the class from the stock id prefix, which fits only the
  fork's replaced kit catalogue. Adapt together with the kit editor if custom presets are wanted.
- Metascore (per-item Firepower/Defense/Utility, generated), `classes.json` and the 53k-line
  "Codex arena reference package" are transcribed from a Japanese community wiki
  (wikiwiki.jp/eft/Arena) and BSG patch notes, with "category seeds" (invented values) where no
  source exists, plus AI handover files committed under `tools/`. Not redistributable with a
  clear licence, not current, not ours: no.
- Generated "Arena starter kits" replace our 20 authored kits with 12: no.
- The three "kit-packing fixes" are not bugs in our master; one of them replaces the profile's
  pockets with the kit's pocket template in the shared protected entry path and reverses the
  contract `PftKitEntryChecks` pins ("preserve original pocket template"): no.

### Small server and client changes

- MapVariants allowlist (`ReviewedEarlyCallbacks` with `MapVariants.Server.Variants.VariantSnapshot`
  in the old Watermark+1 reward step): the same problem as our issue #1, superseded by the
  general fix of 2026-11-05 (reward step at Watermark, only callbacks that really run before it
  are refused). Follow-up for us: confirm a MapVariants user enrols on 0.5.0.
- Config keys (`TeamFight/*`, `Bots/SainPreset`, `Kits/FavoritePresets`): only with the features.
- Gameplay logging at Info throughout (kit selection on every pick, respawn steps, menu shows):
  against our Debug-only rule; anything lifted must be re-levelled.

## Recommendation

1. Quick wins (one or two days together, each with a harness pin): TDM retire diagnostics,
   persistent scoreboard seats, no bot backpacks behind a server switch, hold-Tab for our
   scoreboard.
2. The Tactical objective package (capture point with tiebreak, visible zone, door reset,
   opening rush, round policy) as one feature with its review, harness and a GPU run: about a
   week, the largest gameplay gain.
3. Custom presets with the EFT-inventory kit editor as a project of its own, only if wanted: our
   own host window, storage contract and class/budget rules first.
4. Fika: leave to the `feature/fika-admission` work; take the confirmation-screen fact and the
   companion/seam idea, not the in-place respawn or the profile write.
5. Watch the identity collision: the fork ships as ArenaMode 0.4.1 with our GUIDs; a support
   request about "0.4.1 with Fika" is the fork.
