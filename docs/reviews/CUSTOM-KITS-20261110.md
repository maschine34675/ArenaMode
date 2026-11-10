# Own kits: the kit editor and where custom kits go (2026-11-10)

The contract of the player's own kits ("custom kits"), the third stage of the kit screen
([kit screen and Tactical buy screen](KIT-PICKER-20261110.md)). The owner's decisions of
2026-11-10, not reopened here: the handbook price is the only currency (no classes, no
metascore); solo Tactical sells an own kit at its price, every other mode hands one out only when
it costs at most what the dearest stock kit costs; a stock kit keeps its per-raid attachment draw,
an own kit is issued exactly as built; the editor lives in the KITS window and works per slot,
an item arriving as the game's own default build of its template; a 3D character preview only
after a load-time measurement.

## What the player gets

- **COPY TO MY KITS** in the detail pane of any kit card makes an own kit from it (named
  "<kit> copy", "<kit> copy 2", ...) and opens the editor. **MY KITS** lists only own kits; own
  kits stand among the stock kits by price everywhere else, tagged CUSTOM.
- **The editor** shows the name (typed into a field), the price, whether the kit is within the
  cap, and one row per slot with the game's own picture: WEAPON, AMMUNITION, ARMOUR, RIG,
  HELMET, HEADSET, BACKPACK. CHANGE opens the list of items that slot takes, cheapest first
  with class and price (of what the pick adds: inserts and rounds included); NONE empties armour,
  helmet, headset or backpack. A pick is checked and saved at once; one that fails is refused with
  its reason on the status line and the kit stays as it was, the list where it was scrolled to. A
  new name is saved when the field loses focus and drawn in place, so the click that took the
  focus away still lands. DONE returns to the cards, COPY copies the kit again, DELETE asks for a
  second click (CHANGE, NONE, a pick, DONE or a filter disarms it; a rename, drawn in place,
  leaves it as its label says).
- **Picking** an own kit is the ordinary card click: every life of a Deathmatch, Team DM,
  Domination, Zombies or PitFireTeam Tactical raid then gets it exactly as built, when it is
  within the cap. Above the cap the card reads "above the stock cap: solo Tactical only" and
  cannot be picked; a stored choice that the cap refuses later says so in the choice line and the
  raid uses the first buildable stock kit (one Warning per raid).
- **Solo Tactical's buy screen** lists the ready own kits next to the stock kits at their own
  price, without the cap. A bought own kit starts loading its bundles at once, in the card screen
  and in the plain fallback window alike (`TacticalBuyScreen.WarmBought`).

## Rules

1. **Storage.** One file for the installation:
   `BepInEx/config/com.maschine.ArenaMode.custom-kits.json`, `{"version":1,"kits":[...]}`, each
   record a kit's `id`, `name`, `items`, `fill` and its `saved` time. BepInEx/config is the same
   folder in a normal and an Arena session and is neither the profile nor the private server
   tree, so both sessions see the same kits and nothing is written back from the private tree
   (non-negotiable 1). Several profiles on one installation share the file; Syncthing mirrors it
   like the .cfg. A `price` or `order` in a record is never trusted.
2. **Writing** (`CustomKitStore`): every change is a read-modify-write of a fresh read under one
   lock, written to `<file>.tmp` and swapped in with `File.Replace` keeping `<file>.bak`, after
   checking that the file did not change since the read (the stamp is taken before the read; one
   retry, then the change is refused rather than written over someone else's). A record that
   cannot be read is written back as it was, unknown fields of an edited record survive. A file
   whose content cannot be read is moved to `<name>.corrupt-<utc>.json` (the newest three are
   kept) and the backup restored. A file another program holds open is tried three times over
   about 80 ms, then shown read-only and left alone: it is busy, not corrupt. A missing file with a
   backup gets the backup back with a banner (a save whose last fallback step failed, a sync tool;
   a player who deletes the file on purpose deletes the `.bak` too). A newer schema or more than
   64 kits load read-only. Limits: 1 MiB, 64 kits. The lock is per process; two game instances on
   one installation are caught by the stamp check, not serialised.
3. **Ids** (`ArenaCustomKitId`): `custom-` and 16 hex digits from a cryptographic source, never
   reused, never changed by a rename or an edit, never equal to a stock id or the M60 pickup's.
   They fit `ArenaKitFavorites.IsKitId`, so the stored choice and the favourites take them as
   they are. Whether a kit is custom is decided by membership in the loaded list, not by the prefix.
4. **Price** (`KitPriceRule`, shared with the server's `KitPricer`): the handbook price of every
   item once, the secure container and everything under it left out, rounded to whole thousands
   (banker's rounding, as the catalogue was always priced). The client reads the game's own
   handbook (`Singleton<Handbook>`, the same data the server prices from; loaded in the menu,
   5036 prices on GPU-2 on 2026-11-10). No handbook or a sum of 0 is "no price": such a kit is
   never sold or issued. Every item an own kit hands out must have a handbook price above 0
   (`CustomKitEditing.FirstUnpriced`, applied by tier 2): the rule counts an unknown template as
   free, so without this a hand edit could add one. Exempt are the equipment root, the secure
   container with its contents, the pockets, and the inserts the game puts into a locked slot
   (the built tree tells which; four helmet inserts of SPT 4.1 have no handbook entry). The config page's "Reload from disk" now prices the catalogue again
   (`KitPricer.Reprice`); before, every stock kit cost 0 after it.
5. **The cap** (`ArenaCustomKitPolicy`): the dearest stock price, a stock kit served at 0 priced
   by the rule instead. `Eligible` = ready, priced, cap known, price at most the cap. It is applied
   in exactly one place, `KitCatalog.GetBaseKit`, which every issuing path outside solo Tactical
   goes through (raid start, every respawn, round breaks, the loading-screen reservation, Gun
   Game's base kit, the PitFireTeam Tactical entry kit). Solo Tactical's shop has no cap.
6. **Who sees own kits.** `KitCatalog.Kits` stays the stock catalogue. Own kits reach a raid only
   through `KitCatalog.CustomView` (read by `GetBaseKit` for a kit chosen by id) and
   `KitCatalog.ShopKits` (the Tactical buy screen and its plain fallback window). So the free
   Tactical starter and the broke player's auto-buy (`SelectOpeningKit`, `Affordable`), the
   random draw, the Gun Game ladder fallback, pickups and the warm-up of the catalogue never pick
   an own kit. A chosen own kit is warmed by the loading-screen reservation and the respawn path
   like any chosen kit.
7. **Held for a raid** (`ArenaRaidHold`): `CustomView` is read, priced and checked at the first
   question after an arm and kept, deep copies included, until the next arm; it is not held while
   the stock catalogue is missing (the cap would be unknown). The file changing during a raid
   changes nothing in it; the kit window reads the file fresh on every opening and after every
   change.
8. **Two checks.** Tier 1, `CustomKitStructure` (pure): a well-formed id and name, at most 400
   items, unique ids, one root of the equipment template, every parent present, no cycle, one item
   per slot (a magazine's cartridges, a stack slot, hold several stacks), root slots only those
   the stock kits use (weapon, armour, rig, helmet, headset,
   backpack, pockets, secure container) with exactly one weapon, template ids in the database's
   lower-case spelling (the handbook and the exclusion list compare ids as text, so an upper-case
   spelling of a dear or excluded item would slip past both; a content mod with upper-case ids
   cannot be used in an own kit); upd keys only those the stock kits use, each with the value type
   KitBuilder reads (the strict PitFireTeam entry throws on a wrong one) and inside what a stock kit
   sets: a whole stack count of at least one, a durability only on the weapon and no higher than
   the stock weapons' (100), a fire mode a stock kit sets, a fold state as true or false, no MedKit
   resource; and the price rule's holes for a hand-edited file closed: a stack only on cartridges
   (the rule prices a node once), an empty secure container of a stock template, stock pockets,
   spare magazines and rig supplies inside what the stock kits carry. A stored kit that carries an
   item Arena does not hand out (rule 9's exclusions, read fresh because the server's list can
   change while the game runs) is unavailable too. Tier 2, `CustomKitProbe`
   (engine): the kit built as a detached equipment, child items first, with what KitBuilder skips
   (it attaches without restrictions): slot filters, blocking and conflicting slots, conflicting
   items, magazine capacity, required parts of every weapon and of the equipment, rounds the chamber
   takes, the supplies fitting rig and pockets as the strict entry path demands, and the price of
   every item (rule 4). Its verdict is remembered per kit object and by content, never while the
   item database or the handbook is missing. A pick that fails either tier is refused; a stored kit that fails either is shown greyed with its reason,
   kept in the file and never issued (an item of a mod the Arena session leaves out does that).
9. **What a pick adds** (`CustomKitItems`): the game's default preset of the template
   (`ItemFactory.GetPresetItem`), or for gear without one the template with its locked inserts
   filled (`CreateAndFillItem`); a weapon without a preset is not offered. Presets carry a magazine
   but no rounds, so a weapon is armed by us: a magazine from its slot filter when the preset has
   none, the magazine filled with the previous round when the new chamber takes it (else the
   weapon's default round) in stacks of at most the round's stack size, as the game loads one (a
   100-round belt box of a round that stacks to 40 takes 40 + 40 + 20), a round in every chamber
   and cylinder slot. Changing the ammunition keeps the stacks and cuts each to the new round's
   stack size. A weapon of another calibre
   drops the loose rounds of the old one from the supplies. Candidates per slot come from the
   equipment slot's filters, without the DO_NOT_USE placeholders, the seeded broken templates and
   the server's configured exclusions (`ArenaTemplateExclusions`, shared), without items with no
   model, stationary guns, the test weapon and anything without a handbook price.
10. **Locked while a raid is armed or running**: no copy, edit or delete, like every other menu
    choice. Deleting the chosen kit sets the choice back to the random draw and removes its
    favourite mark.

## Things the game forces

- Default presets of weapons hold no ammunition and no chambered round (274 presets in SPT 4.1's
  database; none has a `cartridges` node, and only the RSHG-2's ships its rocket in
  `patron_in_weapon`). The arming in rule 9 is ours and skips a chamber that is already filled.
  That rocket has no handbook price, so an RSHG-2 is refused by rule 4.
- Armour and helmet insert slots are locked: a normal `Slot.Add` refuses them. The probe checks
  compatibility, blocking and conflicts first and then adds without restrictions, as
  `ArenaPreviewEquipment` does and as the game's own `CreateAndFillItem` does.
- KitBuilder attaches every node without restrictions and only checks that templates exist, so
  an invalid tree would build an unusable loadout mid-raid. That is why tier 2 exists and why a
  kit that fails it is never issued.
- `ItemTemplates.GetAllChildTemplateIds` lives in the `JsonType` namespace; slot filters name
  categories as well as items, and both expansions are needed for a candidate list.

## Pieces

| File | Role |
|---|---|
| `Shared/KitPriceRule.cs` | The price rule, compiled into client and server. |
| `Shared/ArenaTemplateExclusions.cs` | The template exclusions (moved from the server; `ArenaTemplateEligibility` forwards). |
| `Kits/ArenaCustomKitPolicy.cs` | Ids, the cap, picker and shop verdicts, `ArenaRaidHold`. Pure. |
| `Kits/CustomKitStructure.cs` | Tier 1 and `CustomKitEnvelope` (what the stock kits carry). Pure. |
| `Kits/CustomKitEditing.cs` | Copy, replace or clear a root slot, rename, drop supplies, price rows. Every operation returns a new kit. Pure. |
| `Kits/CustomKitStore.cs` | The file. Pure I/O. |
| `Kits/CustomKitCandidates.cs` | The candidate rule. Pure. |
| `Kits/CustomKitEngine.cs` | Client handbook prices, candidate lists, default builds and arming, ammunition, tier 2. |
| `Kits/CustomKits.cs` | The view of the file for a session: entries with state, reason, price; the ready kits. |
| `Kits/KitCatalog.cs` | `CustomView`, `ShopKits`, `IsCustom`, the cap in `GetBaseKit`. |
| `Kits/ArenaKitCards.cs` | `Custom` and `Blocked` on card sources; CUSTOM tag, greyed blocked cards, the choice line. |
| `Session/ArenaKitWorkshop.cs` | Copy, apply, delete; editor rows and pick lists. |
| `Session/ArenaKitMenu.cs`, `Hud/ArenaKitEditorView.cs`, `Hud/ArenaKitBrowserView.cs` | MY KITS, the detail actions, the editor. |
| `Hud/TacticalBuyScreen.cs`, `Hud/TacticalBuyPanel.cs` | The shop on `ShopKits`; a bought own kit warmed at once. |

Nothing here adds a Harmony patch or a setting, and nothing is cloned from the game's prefabs or
shipped from its art: the pictures are the running game's icons.

## Not done on purpose

- **The game's weapon build screen** (attachment-level editing). It can host a detached weapon
  (`ItemUiContext.EditWeaponBuild`, `EditBuildScreen`), but safely only with about a dozen
  Harmony patches on obfuscated vanilla screens, every write path guarded, the map screen
  closing and reopening around it, and a fix-up of bare mods it returns. Planned as a stage 3b
  only on the owner's word: one `Arena…` patch per target installed all-or-nothing with
  `UnpatchSelf` on failure, every patch gated on the pending weapon by reference, in-screen
  confirmations cancelled rather than accepted, the result captured in the `Close` prefix and
  rejected on missing vital parts, every exit path ending idle, a kill switch with its
  ArenaConfigMenuLayout row, and the MIT attribution of the technique (thatHeathenJay's fork) in
  THIRD-PARTY-NOTICES.md. Until then a weapon is the default build of the chosen weapon.
- Holster, second primary, scabbard, eyewear and face cover slots; editing plates, pockets, the
  secure container, spare magazines or supplies. The holster needs KitBuilder.FillGrids to take
  the spare-magazine template from the primary weapon explicitly first (its own commit).
- A server route, server-side storage or pricing of own kits, and per-profile files.
- The 3D character preview (a measurement first), a search box, import and export.

## Known limits

- **The buy-time warm-up is not serialised with the round start's preload.** An own kit bought in
  the last seconds of the buy phase can be loading when the round start asks
  `KitBundlePreloader.Preload` for the same kit; both see its templates cold and both load them.
  The catalogue warm-up already overlaps a respawn draw the same way. Serialising it means an
  in-flight set in the preloader, a raid-start path about thirty harnesses link; not done for a
  duplicate load at worst.
- **Esc while the kit window is open** goes to the map screen as everywhere on it: the map screen
  closes with the window. A name being typed is lost then (every other change is already saved).
  The stage-2 window behaves the same; an input node that eats Esc while the window is open is
  the fix if it is wanted.

## Verification

- `tools/CustomKitChecks` (`Verify.ps1`): ids, the cap, the picker and shop verdicts, the hold,
  every structure rule and loophole pin (upd values, upper-case ids, duplicate ids), the tree
  edits, the unpriced-item and exclusion rules, the file (atomic write, backup, bad records kept,
  quarantine, a held file left alone, a missing file restored, read-only cases, the concurrent
  writer, the record index, a far-future version), the candidate rule: 125 assertions, 92 mutants of the production
  sources on 2026-11-10 (after the review fixes).
- `tools/KitPickerChecks`: the CUSTOM tag, a blocked own kit greyed with its reason and its
  SELECTED tag kept, the choice line, the shop selling above the cap and greying an unusable kit.
- `tools/TacticalPolishChecks`: the plain buy window compiled against `ShopKits` and `WarmBought`.
- xunit `KitPriceRuleTests` (the rule, banker's rounding, the shipped kits against an oracle and
  against the real handbook, the reload pricing again), `TemplateEligibilityTests` (the forwarders),
  `CustomKitStructureTests` (every shipped kit copies into a sound own kit; the envelope).
- The engine layer, the editor and the raid paths are engine-bound and verified natively on a
  GPU host.
