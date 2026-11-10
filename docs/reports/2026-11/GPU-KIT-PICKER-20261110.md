# Kit screen and Tactical buy screen: native runs on GPU-2 (2026-11-10)

The kit screen and the card-based solo Tactical buy screen
([contract](../../reviews/KIT-PICKER-20261110.md)) on SPT-GPU-2 (`192.168.178.109`,
`C:\SPT41`, profile `SPT-GPU-2`, normal SPT session, backend `https://127.0.0.1:6969`),
driven from the VM with gpu-run, UiBridge 0.9.9 and Windows-MCP. Three runs with
working-tree builds found the three problems the contract lists under "Things the game
forces"; the last run is the one with the committed build. Dated evidence, not proof about
any other build.

## Runs before the commit (working-tree builds)

| Run | What it showed | Consequence |
|---|---|---|
| Menu, before the Tactical runs | KITS button in the mode panel head, the window over the map screen, cards with the game's pictures, Spartan picked ("Spartan, every life", SELECTED), Warlord marked FAV and alone in FAVOURITES, BACK closed it; no ArenaMode warning. | Stage 1 as designed. |
| Tactical, Factory, 13:10 | The buy screen drew in round 1 (250,000 ₽) and round 2 (330,000 ₽), UiBridge listed its buttons, but a click on a card did nothing. | `BattleUIScreen.Show` disables the UI event system; the screen now holds `UIEventSystem.SetTemporaryStatus` while drawn, with a shade under the window. |
| Tactical, 13:17 | Clicks worked: Minuteman BUYING, NO PURCHASE cleared it, Vanguard + READY gave "Tactical round 1: 5 enemies, kit 'vanguard' for 214,000, 36,000 left." (13:19:23). One `NullReferenceException` at 13:18:51 in `CameraManager.method_10` from `OpticSight.OnEnable` inside `ObjectsFactory.CreateItemAsync`: an icon render of a kit the raid had not warmed. | Pictures in a raid wait until every template of the kit is warmed. |
| Tactical, 13:25 | Cards without pictures at first, pictures as the warm-up reached each kit (raid-start swap 13:26:17, first kit warm 13:26:24, the last of 20 at 13:26:45); no error. CLOSE closed the screen; F9 reopened it and opened the scoreboard panel on top of it. | During a buy phase `ArenaPanel.Toggle` leaves F9 to the buy screen. |

## Run with the committed build

`dotnet build ArenaMode.Client` from `024565bd` on the VM: client
`0.5.0+024565bd6a0d4d85fc54326ad7881b186585e26a` (SHA256 `A915FF73…614455CF`), Core on disk
`1.0.22+b1d69e1`. `gpu-run deploy-wait --host host2 --game --force`: the client identical on
the host after 40 s, the game restarted (14:56:44), `16/16` maschine plugins identical (VM
build = host disk = loaded). Server untouched.

| Check | Evidence | Verdict |
|---|---|---|
| Handbook in the menu | 14:57:30.449 "Arena kit screen: game handbook loaded, 5036 item prices." (the go/no-go for pricing custom kits on the client; normal session only, see Limits) | passed |
| Kit screen | Opened from KITS: "Spartan, every life", scope line "Tactical: kits are bought in the raid.", Warlord FAV lit. A click on RANDOM EACH LIFE: "A random kit for every life", SELECTED on the random card; FAV on Warlord: mark off. BACK closed the window. | passed |
| F9 in the buy phase | Solo Tactical, Factory, buy phase 90 s (set in memory only). CLOSE, then `gpu-run key f9`: UiBridge lists the buy screen's NO PURCHASE, READY, CLOSE and cards, no IMGUI Arena panel. | passed |
| Take-back after a buy-phase death | Vanguard clicked (BUYING), `uibridge_hook kill` 14:58:52 ("Arena death #1 — respawn after optional killcam"): the new body carries an AK-74N (`5644bd2b…`, Vanguard's weapon); the cards then read WEARING Drifter, BUYING Vanguard. NO PURCHASE: no BUYING, the button disabled. READY: 14:59:12.686 "Tactical round 1: 5 enemies, kit '' for 0, 250,000 left.", 14:59:12.746 "Arena round respawn complete: Drifter.", the body's weapon a VPO-209 (`59e6687d…`, Drifter's). Nothing charged, the purchase not kept. | passed |
| Down player | `uibridge_hook kill` in the live round: round 2 buy screen "BUY · ROUND 2 0-1", 330,000 ₽, no card tagged WEARING, plan line "No purchase: automatically buy the best affordable kit; cheapest kit is free if you cannot afford any.", every card with its picture. | passed |
| Clean log | `uibridge_log level=warning` from the raid start: twelve Unity "referenced script … is missing" lines (bot objects, 14:59:20 to 14:59:23); no ArenaMode warning or error, no `CameraManager` exception. | passed |
| Settings restored | `com.maschine.ArenaMode.cfg` on disk: `RandomKit = true`, `SelectedKit = ` (set back from `spartan`, left by the first menu run), `FavoriteKits = `, `BuySeconds = 15` (90 was set with `persist=false` only). | passed |

The raid was left with `gpu-run leave` (DISCONNECT → ExitStatus → KillList →
SessionStatistics → MainMenu); EFT was left in the main menu.

## Limits

- Normal session only: the handbook line was not taken in an Arena session (private server
  tree); the custom-kit stage has to check it there before it relies on it.
- The plain IMGUI buy window (the fallback when the card screen cannot be built) was not
  forced natively; `tools/TacticalPolishChecks` compiles it.
- No whole-profile comparison for this feature run; neither protection layer was touched by
  these commits. Domination, Team DM and Deathmatch raids with a fixed kit were not run; the
  per-raid hold of the choice is pinned offline (`tools/KitPickerChecks`).
- One map (Factory), one resolution (1920x1080); the console was not opened while the buy
  screen held the event system.
