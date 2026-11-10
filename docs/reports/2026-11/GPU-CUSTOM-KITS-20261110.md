# Own kits: native run on GPU-2 (2026-11-10)

The player's own kits and the kit editor ([contract](../../reviews/CUSTOM-KITS-20261110.md)) on
SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`, profile `SPT-GPU-2`, normal SPT session, backend
`https://127.0.0.1:6969`), driven from the VM with gpu-run, UiBridge 0.9.9 and Windows-MCP. One
cycle with the committed build, after two adversarial reviews and the offline checks. Dated
evidence, not proof about any other build.

## Build and install

`dotnet build` of `ArenaMode.Client` and `ArenaMode.Server` from `9543fed0` on the VM: client
`0.5.0+9543fed00979fd7712360f58af370ab804d8e9c4` (SHA256 `40E99530…`), server
`0.5.0+9543fed0…` (SHA256 `594A9E55…`), Core on disk unchanged at `1.0.22+b1d69e1`.
`gpu-run deploy-wait --host host2 --server --game`: both files identical on the host after
34.2 s, server and game restarted (16:30:43), `16/16` maschine plugins identical (VM build = host
disk = loaded).

## Checks

| Check | Evidence | Verdict |
|---|---|---|
| Handbook in the menu | 16:31:57 "Arena kit screen: game handbook loaded, 5036 item prices." | passed |
| Copy into an own kit | Spartan's detail pane, COPY TO MY KITS: the editor opened on "Spartan copy" with "Copied. Change a row to make it your own; every change is saved at once." on its status line; price 257,000 ₽, the served price of the stock Spartan (the client's rule agrees with the server's); "Within the stock cap of 669,000 ₽: handed out in every mode."; seven rows with the game's pictures, prices and armour classes. | passed |
| The weapon list | CHANGE on WEAPON: one frame of 115.5 ms while the list was built (every primary weapon priced by what its pick adds), 58.5 fps right after. Prices of the rows a pick builds: the default M4A1 64,551 ₽ in the list, the copied M4A1 with Spartan's attachments 65,611 ₽ on its row. | passed |
| A belt-fed machine gun | RPD picked: "WEAPON: RPD. Saved.", "Ready.", price 258,000 ₽. The record on the host: two `cartridges` nodes of 60 and 40 under the 100-round box, no `price` field. | passed |
| More edits | AMMUNITION: BP; ARMOUR: Korund-VM (class 5, 134,998 ₽ in the list); HELMET: NONE. Each "… Saved." | passed |
| A refused pick | RIG: ANA M2 (an armoured rig) with the Korund worn: "Not changed: ANA M2: TacticalVest is blocked by another item"; the list kept its scroll position (the row at the same place). | passed |
| Rename | "RPD Test" typed into the name field, then one click on DONE: the name saved and the editor closed in that click; the scope line back to "Tactical: kits are bought in the raid." | passed |
| Choice | The card "CUSTOM · RPD Test · armour 5 · 345,000 ₽" among the stock kits by price; a click: "RPD Test, every life". | passed |
| Deathmatch, Factory | 16:34:53 "Arena: preloading kit 'custom-10a245b6975bae4c' (31 templates) in the loading screen.", finished in 1.3 s; 16:35:10 "Arena raid-start kit swap complete: RPD Test." The body: RPD 100/100, Korund-VM, no headwear, ComTac IV, Poyas combo with AI-2, T-Bag with one spare box (60/100). `uibridge_hook kill`: instant respawn with the same kit (plus the Molotov of the Molotov mutator). | passed |
| Solo Tactical, 250,000 ₽ | The buy screen lists "CUSTOM · RPD Test · 345,000 ₽ (short)" among the stock cards, greyed. | passed |
| Solo Tactical, 400,000 ₽ | `StartMoney` 400,000 set in the running game only (`persist=false`). A click on the card: "Buying: RPD Test", and 16:38:59 "Kit 'custom-10a245b6975bae4c': 22 new item bundles preloaded." two seconds later, ahead of the catalogue warm-up. READY: 16:39:38 "Tactical round 1: 5 enemies, kit 'custom-10a245b6975bae4c' for 345,000, 55,000 left.", "Arena round respawn complete: RPD Test."; the body as in Deathmatch. | passed |
| Delete | MY KITS, DELETE: "CONFIRM DELETE"; a second click: "Deleted.", the choice back to "A random kit for every life", MY KITS empty. | passed |
| Settings restored | `RandomKit = true`, `SelectedKit = `, `FavoriteKits = `, `GameMode = tactical` as before the run; `BuySeconds` and `StartMoney` were changed in the running game only and set back there; the .cfg never held the test values. | passed |
| Log | Warnings from 16:30: three Unity "referenced script … is missing" lines at 16:32:32 (the weapon list's icon renders); one ArenaMode warning, see below. No ArenaMode error, no exception. | passed |

## Observations

- **Spare boxes of a belt-fed gun.** Of the kit's five spare magazines one fits the T-Bag, and it
  holds 60 of 100 rounds: KitBuilder's spare fill makes one stack of the round's stack size, the
  path stock kits take as well (the arming of the editor now fills a box in stacks). A follow-up
  for KitBuilder, not for the own kits.
- **"Arena rewards: the final result was not accepted (reward-capture-identity)"** at Warning when
  solo Tactical is left in its first buy phase (16:37:52). The same line is in the 13:15 run of
  the stage-2 build, so it predates the own kits. A follow-up for the reward capture.
- The engine check ran once per distinct kit content (four FillEntryGrids lines between 16:32:47
  and 16:33:34, none for the rename, whose content is the same).
- Syncthing mirrored the kit file to the VM a few minutes late; the host's copy is the record.

## Limits

- Normal session only. The handbook in an Arena session (the private server tree) was not
  checked; own kits there would load as "no price" without it.
- The PitFireTeam Tactical entry with an own kit, Team DM, Domination, Zombies and Gun Game's base
  kit were not run; the strict entry path's upd rules are pinned offline (`tools/CustomKitChecks`).
- The plain IMGUI buy window was not forced natively; its warm-up is pinned by
  `tools/TacticalPolishChecks`.
- No whole-profile comparison; neither protection layer is touched by these commits.
- Esc while the kit window is open was not tried (a known limit in the contract).
- The test left `BepInEx\config\com.maschine.ArenaMode.custom-kits.json` (an empty kit list) and
  its `.bak` (the deleted test kit) on the mirrored installations; neither existed before. The
  backup is read only when the main file is missing, so delete the `.bak` first.
