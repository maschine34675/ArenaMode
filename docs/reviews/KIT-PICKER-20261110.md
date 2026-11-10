# Kit screen and Tactical buy screen (2026-11-10)

The contract of the kit browser: the KITS screen of the Arena map screen, where the player
picks the kit of the base draw, and solo Tactical's buy screen, which draws the same cards
with the round's purse. The player's own kits and the kit editor are their own contract:
[CUSTOM-KITS-20261110.md](CUSTOM-KITS-20261110.md).

## What the player gets

- **KITS** in the head of the mode panel on the Arena map screen opens a window over that
  screen: one card per catalogue kit, cheapest first, with the game's own picture of the
  kit's weapon, its armour and helmet class and its value. The first card is the random
  draw. A click picks; the line under the title says what the next raid hands out.
  Hovering a card shows the kit on the right: weapon and ammunition, sidearm, armour,
  helmet, headset, spare magazines, supplies, backpack, and pictures of the gear.
- **FAV** on a card marks a favourite; **FAVOURITES** shows only those. Favourites only
  mark cards and fill that view: they reorder nothing and change nothing in a raid.
- A picked kit keeps the per-raid attachment draw of the server: the player picks the kit,
  not one build of it.
- **Solo Tactical's buy phase** shows the same cards with prices. A kit above the purse
  stays visible, greyed, with "(short)". A click buys (nothing is charged before the round
  starts), **NO PURCHASE** takes the purchase back, **READY** starts the round, **CLOSE**
  closes the screen so the player can walk; the panel hotkey opens it again while the buy
  phase runs. The purchase is tagged BUYING; WEARING marks the kit a round without a
  purchase hands out (rule 8), and nothing is tagged so while the player is down. The
  head shows the round, the score, the purse (an empty one reads 0 ₽) and the countdown.

## Rules

1. **One read point.** `KitCatalog.CurrentChoice` is the only place the base draw reads the
   choice from: `GetBaseKit` (raid start, every respawn, round breaks, the loading-screen
   reservation) and the catalogue warm-up decision in `ArenaController`. It is the pair the
   settings `Kits/RandomKit` and `Kits/SelectedKit` always expressed; no setting was added
   for the choice and F12 edits keep working.
2. **Held for a raid.** `ArenaKitChoiceLatch` answers with the live settings while no raid
   is armed or running. From the first question after an arm on (the loading screen asks
   first) it keeps that answer until the next arm, keyed on `ArenaSession.ArmEpoch`. Every
   life, the preload and the round breaks of one raid therefore agree, and an F12 edit made
   in a raid takes effect in the next one. Before this, such an edit changed the kit at the
   next respawn while the preload had warmed another one.
3. **The menu cannot change a running raid.** `Settings.TrySelectKit` hands the write to
   `ArenaKitChoiceWrite.Apply`: refused while a raid is armed or active and for an id that
   is no plain kit id (`ArenaKitFavorites.IsKitId`), `SelectedKit` written before
   `RandomKit`, both restored when a write throws (a setting's change handler throws after
   the value is set), success only when both read back. The random card leaves
   `SelectedKit` as it is, so switching back to a fixed kit in F12 returns to the last one.
   A catalogue kit whose id the choice cannot store (a hand-written kits.json with spaces or
   commas in an id) gets a card that cannot be clicked and carries no FAV mark; F12's Fixed
   Kit ID still takes it.
4. **Modes that do not hand out the picked kit say so.** The window's scope line names
   Gun Game (the ladder sets the weapon; without a ladder the kits are the rungs and the
   choice is not used), solo Tactical (kits are bought), PitFireTeam (own gear) and
   EQUIPMENT = Own gear. The choice is saved either way.
5. **Favourites are display state.** `Kits/FavoriteKits` is a comma-separated id list,
   read through `ArenaKitFavorites.Parse` (ids of `[A-Za-z0-9._-]`, at most 64 entries of
   at most 64 characters, duplicates dropped without case). It may change at any time and
   orders nothing.
6. **Card order is total**: price, authored order, id. `List.Sort` is unstable and the
   server's list order is no contract.
7. **Tactical: the panel owns the phase, the screen draws.** `TacticalBuyPanel` decides
   when the buy menu is open, holds the input block and the cursor vote, and keeps its
   plain window. `TacticalBuyScreen` draws the cards and forwards a click to
   `TacticalMode.TryBuy`, `ClearPurchase` or `ReadyUp`. When the card screen cannot be
   built or throws while drawing, it logs one Warning, stays away for the rest of the
   process, and the plain window draws as before. Prices, affordability and what a
   purchase does are the mode's and did not change.
8. **`ClearPurchase`** only un-picks: solo buy phase, something picked. Nothing was charged
   yet, so the purse is untouched and the round falls back to what
   `NoPurchaseDescription` says. For that to hold, the kept kit (`TacticalMode.Worn`: the
   kit of the last round start, the free starter before the first) is committed only
   outside the buy phase: a respawn in the buy phase still hands the purchase out at once
   (`IssueSoloKit`), but only `StartRound` makes it the kept kit, when it charges for it.
   Before, that respawn made the purchase the kept kit, and taking it back would have kept
   it for free. In that rare case (a death while alone on the map) the body wears the
   purchase while WEARING still marks the kept kit, which the round start then hands out.
9. **During a Tactical buy phase the panel hotkey belongs to the buy screen.**
   `ArenaPanel.Toggle` does not open the scoreboard panel then (closing always works); two
   windows on one key opened on top of each other.

## Things the game forces

- **No click reaches game UI in a raid by default.** `BattleUIScreen.Show` calls
  `UIEventSystem.Disable`. The buy screen holds `UIEventSystem.SetTemporaryStatus(true)`
  while it is drawn (re-asserted every frame, because a console closed in between switches
  it off) and releases it when it goes, unless the console is open, which releases it
  itself. This is the console's own mechanism; `Enable`/`Disable` belong to the screens.
- **A full-screen shade lies under the buy window.** With the event system on, a click that
  misses the window would otherwise reach the raid HUD. The shade also dims the HUD, and
  the window is opaque because the score strip lies behind its head.
- **Item pictures in a raid wait for the kit's pools.** The game renders a picture from a
  model of the item (`ItemIconCreator.RenderModel` → `ObjectsFactory.CreateItemAsync`). In
  a raid a model comes from the raid's pools; an item without a pool is instantiated bare
  (`InstantiateWithoutPool`), and a scope then reports itself to the optic camera without a
  weapon above it: `CameraManager.method_10` throws. Seen on GPU-2 on 2026-11-10, once per
  raid, before the gate. `ArenaKitBrowserView.PicturesReady` lets a host hold pictures
  back; the buy screen passes "every template of the kit is warmed"
  (`KitBundlePreloader.IsTemplateWarmed` over `KitPreloadPlan.TemplatesOf`, the template
  list cached per kit object so the gate allocates nothing while it polls). The raid's
  catalogue warm-up loads one kit after another from a few seconds into the raid; how soon
  it reaches the dearer kits depends on the machine and the bundle cache (on GPU-2 on
  2026-11-10 the warm-up began 7 s after the raid-start kit swap and the last of the 20
  kits was warm 28 s after it). Until then a card has its name, facts and price without a
  picture. The menu needs no gate: no optic camera listens there.
- **A picture that arrives late belongs to the kit it was asked for.** The detail pane
  unbinds its weapon picture on every kit change; hiding it was not enough, because the
  pending render of the previous kit showed itself under the new name.
- **The kit screen fetches the catalogue once per opening** (`KitCatalog.RefreshForMenu`,
  synchronous like every catalogue fetch) and keeps the loaded catalogue when that fails;
  clicks never fetch. It leaves the catalogue alone while a raid is armed or running.
- **The buy screen rebuilds a text only when what it says changed** and the cards only when
  the purse, the purchase, the kept kit, the round or the catalogue did: it is ticked every
  frame of the buy phase, in a raid that runs without collections.

## Pieces

| File | Role |
|---|---|
| `Kits/ArenaKitChoice.cs` | `ArenaKitChoice`, `ArenaKitChoiceWrite`, `ArenaKitChoiceLatch`, `ArenaKitFavorites`: pure. |
| `Kits/ArenaKitCards.cs` | `ForPicker`, `ForShop`, `ChoiceLine`, `Money`, `Purse`: pure, decides order, state and text of every card. |
| `Kits/KitFacts.cs` | The card line and detail rows of a kit; `KitPreviewItems` builds the detached items the pictures are asked for. A kit that fails `KitBuilder.ValidateKit` gets no pictures. |
| `Hud/ArenaKitUi.cs` | Small uGUI builders on the `ArenaUiTheme` palette, the hover relay, `ArenaKitItemIcon.Show` (shows `ItemViewFactory.LoadItemIcon` in an image; not named Bind, which ArenaConfigMenuLayoutTests reads as a config bind). |
| `Hud/ArenaKitBrowserView.cs` | Cards, scroll area, detail pane. Draws what it is given, reports clicks, reads no setting. Pictures are asked for two per frame; a failure switches pictures off, not the browser. |
| `Session/ArenaKitMenu.cs` | The KITS button and window. Hangs on `ArenaLocationSelectionPatch.ModePanelShown`/`ModePanelClosing`; no patch of its own. |
| `Hud/TacticalBuyScreen.cs` | The buy screen's canvas (sorting order 140, above the Arena HUD at 130). |
| `Session/ArenaLocationSelectionPatch.cs` | The two notifications; a companion that throws is logged and never fails the map screen. |

No game prefab is cloned and no game art is shipped or extracted: every picture is rendered
by the running game from the player's own installation, and the layout is built in code.

## Not done on purpose

- No character preview. Cold bundles took 10 to 22 s per kit in earlier measurements
  (`KitPreloadPlan`); it comes only after a measurement shows it can be afforded.
- No per-kit build choice: the server draws attachments per raid.
- The picker does not hide kits in modes that ignore the choice; it explains instead.
- PitFireTeam Tactical has no buy phase and no buy screen.

## Verification

- `tools/KitPickerChecks` (`Verify.ps1`): choice, its write and rollback, latch,
  favourites, both card presenters and the purse text, with mutants of the production
  source (72 assertions, 41 mutants on 2026-11-10, the own-kit cards of the next stage included).
- `tools/TacticalPolishChecks`: compiles `TacticalBuyPanel` and the extracted
  `TacticalMode` methods, now with `ClearPurchase` and `Worn`: the kept kit before the
  first round, a purchase taken back after a buy-phase respawn (neither charged nor kept),
  no take-back once the round runs or in the protected team, each with a mutant. Its
  double for `TacticalBuyScreen` never draws, so the plain window stays the subject there.
- The harnesses that compile the touched production files got the seams they now need:
  `TdmWarmupChecks` and `ZombieKitWarmupChecks` (`KitCatalog.CurrentChoice`, steered by
  `Settings.RandomKit` as before), `AreaConfigChecks` and `TdmMatchMenuChecks`
  (`ArenaKitChoice.cs` linked for `Settings.cs`), `NativeAreaHookChecks` and
  `DominationMenuPerformanceChecks` (`ArenaModeSelectionView.Root`).
- The views, the event-system hold and the picture gate are engine-bound: verified in a
  raid, recorded in
  [../reports/2026-11/GPU-KIT-PICKER-20261110.md](../reports/2026-11/GPU-KIT-PICKER-20261110.md).
