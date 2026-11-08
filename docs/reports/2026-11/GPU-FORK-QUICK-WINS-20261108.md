# Fork quick wins: native run on GPU-2 (2026-11-08)

One Team DM cycle with the development build of commit `74c4d580` (the quick-wins commit,
[review](../../reviews/FORK-QUICK-WINS-20261108.md)) on SPT-GPU-2 (`192.168.178.109`,
`C:\SPT41`, profile `SPT-GPU-2`), driven from the VM with gpu-run, UiBridge 0.9.9 and
Windows-MCP. Dated evidence, not proof about any other build.

## Build and delivery

- `dotnet build ArenaMode.sln -c Release` on the VM at 13:31 deployed client and server
  `0.5.0+74c4d580bdf8ba179263bb6f4355970d04699576` into `D:\SPT41`; Core on disk is
  `1.0.20+ade752a` (Redeploy `main`). `gpu-run deploy-wait --host host2 --server` with the
  plugin, patcher and server folders and the Core DLL: 79 files identical on the host after
  20 s, the host's SPT server stopped and started (pid 8088). `gpu-run start --host host2`:
  main menu after 66 s, `16/16` maschine plugins identical (VM build = host disk = loaded),
  `2/2` libraries identical, plugin load 40 ok (uibridge_status 13:34). Client log
  `C:\SPT41\BepInEx\LogOutput-SPT-GPU-2-2026-11-08_13-33-28.log`, server log
  `spt-SPT-GPU-2-20261108.log` (run started 12:32:38 UTC).

## Raid

`gpu-run raid --host host2 --mode teamdeathmatch --map factory --expect-clean ArenaMode`
(13:35): TEAM DM, FACTORY (day), 8 participants (4v4), TEAM KILLS 25, TIME LIMIT 5 min; raid
started, `log since start: warnings 0; errors 0`, ArenaMode clean, location 14.1 s, game
started 28.5 s. The seven bots were admitted between 13:34:58 and 13:35:15,
"Team Deathmatch ready: 4v4." at 13:35:19. Godmode (UiBridge test hook) kept the local player
alive for the checks. Result at 13:40:20: "Team Deathmatch result: Lost; Time limit reached;
USEC 3:8 BEAR; played=300.0s.", exit committed 13:40:35 (Survived), skills restored,
"Arena match ended: 0 kills / 0 deaths" 13:40:36, `gpu-run menu`: ExitStatus → KillList →
SessionStatistics → MainMenu (13:41). EFT was left in the main menu.

## Checks

| Piece | Evidence | Verdict |
|---|---|---|
| No bot backpacks | Server log, 15 lines `[ArenaMode] Team bot loadout: <Bear|Usec> armband set, 1 backpack(s) removed; n -> n-1 items.` between 13:34:52 and 13:40:10 (every generated Team DM bot of the raid, 5 initial BEAR/USEC and 10 replacements; the two USEC bots admitted at 13:34:58 and 13:35:14 have no line of their own in this window). 0 "armband skipped" lines. | passed |
| Fixed seats | Client log, 11 lines `Team Deathmatch seat <usec-n|bear-n> reoccupied; the replacement wears the seat's name.` (13:36:00 bear-0, 13:36:08 usec-2, 13:37:04 usec-0, 13:38:16 usec-0, 13:38:17 usec-2, 13:38:37 usec-2, 13:39:01 bear-2, 13:39:11 usec-1, 13:39:42 usec-1, 13:39:52 bear-0, 13:40:12 usec-0), each directly before its "Team Deathmatch admitted … request N" line. F9 panel at 13:38:30 (screenshot `artifacts/quickwins/gpu2-f9-seats-133830.png`): four USEC and four BEAR rows under their first names; the seat "Glukhar_111" shows LIVE with 1 kill and 2 deaths while uibridge_bots names its current body "1nco"; "_物是人非" LIVE with 1/1/1 while its body is "IhanaMies"; footer "Seats are fixed: a replacement bot takes over its seat, name and running stats. Team totals include earlier lives." Kills 1-4, match points 231-974 at that time. | passed |
| Hold-to-peek | A PowerShell script on the host (`keybd_event`, Tab down 8 s) launched through Windows-MCP at 13:36:05: client log "Arena scoreboard peek shown (inventory key held)." 13:36:08.236 and "Arena scoreboard peek hidden." 13:36:15.982; screenshot at 13:36:10 (`artifacts/quickwins/gpu2-hold-tab-peek-133608.png`): the Team Deathmatch title, both team tables with the live rows (Glukhar_111 1/0/0 200, Kronzky 0/1/1 31, SPT-GPU-2 (YOU), EpicRangeTime; _物是人非 1/1/0 200, IdiotTurtle 0/0/1 98, Shibdib, stckytwl) over the game with the weapon, score panel and status widgets visible, no window frame, no cursor; the screen stayed BattleUI, the inventory did not open. | passed |
| Tap opens the inventory | `gpu-run key tab` 13:36:44: screen history BattleUI → Inventory (32 s) → BattleUI; the second `gpu-run key tab` (13:37:17) closed it through the inventory's own key. The bound key was Tab, press type as shipped. | passed |
| F9 still works | `gpu-run key f9` opened the panel with cursor and controls at 13:38:28, `gpu-run key f9` closed it at 13:38:41. | passed |
| Named arrival gates | No arrival was refused in this raid ("rejected" / "retired" lines: 0); the gate texts are pinned offline only (TeamDeathmatchRuntimeChecks). | not exercised |
| Clean log | `uibridge_log level=warning`: three HarmonyX generic-method warnings of other mods at plugin load and one Unity `Animator.GotoState` line at the exit; no ArenaMode Warning or Error in the raid. | passed |

## Limits

- One raid, one map, Tab with the default press type; the Release press type, a chord binding, a
  rebind during the raid, a mouse-bound inventory key, the Tactical buy panel and Domination were
  not run natively (Domination seats and the server mode gate are pinned offline).
- The killfeed line of a replacement body was not captured (no kill of or by one was observed on
  screen); the display name goes through the same `RoleClassifier.DisplayName` the F9 table
  showed, and `RoleLabelChecks` pins it.
- No whole-profile comparison was made for this feature run (the raid-end stayed deferred as
  usual, "Arena raid-end remains pending … deferred"); the protection layers were not touched by
  this commit.
- The two initial USEC bots without a server loadout line were not inspected; the armband pass
  applied to every bot it rewrote and logged nothing for an untouched one by design.
