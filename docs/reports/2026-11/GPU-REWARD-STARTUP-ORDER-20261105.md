# Reward startup order: native run on GPU-2, 2026-11-05

Result: passed on SPT-GPU-2 (192.168.178.109, `C:\SPT41`), SPT 4.1.6 / EFT 0.16.9.5-40743,
after one failed first attempt that found a second defect (the session helper's lease
parser). Contract: [REWARD-STARTUP-ORDER-20261105.md](../reviews/REWARD-STARTUP-ORDER-20261105.md).
Issue: [maschine34675/ArenaMode#1](https://github.com/maschine34675/ArenaMode/issues/1).
Evidence (gitignored): `artifacts/reward-startup-order/` (harness logs, the card frame
`gpu2-private-card-withheld.png`). Times below are host local time (UTC+01:00); the
server log is `SPT_Runtime\user\logs\spt\spt-SPT-GPU-2-20261105.log`.

## Builds and probe

- Server DLL, branch `fix/reward-startup-order`: first `0.4.1+c5618dfe` (SHA-256
  `5f5e68bf…8726c880`), then `0.4.1+3b0dc30e` (SHA-256 `81838301…bf0013`), built with
  `dotnet build ArenaMode.Server/ArenaMode.Server.csproj -c Release` (server project only,
  so the client, the helper and Redeploy.Core stayed as installed) and carried by Syncthing
  to every host. Client on the hosts: the `feature/crowd-reactions` build `0.4.1+8d21e362`,
  unchanged, "16/16 identical" at every start.
- Probe server mod `StartupProbe.dll` (net10.0, SPTarkov 4.1.0 packages, one `IOnLoad` that
  logs one Info line; source kept only in the session scratchpad): variant **after** =
  `[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Watermark + 1)]`, GUID
  `com.zzz.StartupProbeAfter` (BetterFlea's case; SHA-256 `79f13b51…dbd541ee`); variant
  **first** = `TypePriority = OnLoadOrder.Watermark`, GUID `com.aaa.StartupProbeFirst`, which
  sorts before `com.maschine.ArenaMode` (SHA-256 `f297667b…665f66d`). Installed as
  `SPT_Runtime\user\mods\StartupProbe\StartupProbe.dll`, not on the whitelist, so normal
  Arena admission showed MOD CONFLICT and START ARENA · RESTART: exactly the reporter's
  constellation (a private session through the helper, a lease).
- Profile SPT-GPU-2 (`6ab7db1c…`): its reward authority has been lost since the wipe of
  2026-10-29 (every start logs the "rewards stay off … lost its reward ledger" Warning).
  All three profiles on the hosts are enrolled, so the "startup:" card sentence of a
  never-enrolled profile could not be shown natively; it is pinned offline
  (`RewardStartupRefusalTests`). The normal session's own card is not shown while a
  non-whitelisted server mod blocks normal admission, so the normal session's "Rewards are
  off: another mod's startup step …" sentence is pinned offline too (`RewardRuntimeTests`).

## Runs

1. **20:52, `c5618dfe`, probe after.** `gpu-run deploy-wait --host host2 --server --files
   …Server.dll,…StartupProbe.dll` (56 s to sync; the server had not been running). The
   reward step ran (per-profile outcomes 20:52:43.153 to .157, the usual Warning for
   `6ab7db1c…`), the probe's step ran after it (20:52:43.162), no
   `Startup rewards unavailable`. That is the fix for the issue's case.
2. **20:54, first Arena entry, failed in the helper.** `gpu-run start`, ESCAPE FROM TARKOV,
   the side screen showed "MOD CONFLICT · Restart with filtered mods" and
   START ARENA · RESTART; `gpu-run click "START ARENA" --confirm "CONFIRM RESTART"`. The
   normal server accepted the entry (20:55:20 "Profile rewards are unavailable for this
   Arena session (profile 6ab7db1c…): … (expected-object)", "Starting a private Arena
   session; the normal server has saved and will stop") and wrote
   `offline-sessions/2ab985b3…/lease.json` with 18 members (`"authorityUnavailable":
   "entry:expected-object"`). The helper then showed "ArenaMode - entry paused: Arena is
   waiting for SPT to finish saving" for 11 minutes, after Cancel "Arena startup could not
   finish", and its log (`C:\Users\Service\AppData\Local\maschine-ArenaMode\session\helper.log`)
   says "Arena entry paused; preserving the original server and saved handoff evidence" and
   "enter failed … no profile was copied". Cause: `HelperOfflineSession.ParseLease` parses
   the lease with `Parse(bytes, 17)` and refuses an 18th member. No private tree was
   created. Before the next start the two files this entry had left in the normal root
   (`offline-sessions/2ab985b3…/lease.json`, `offline-active.json`, both of 20:55) were
   removed over the share, because the next server build no longer reads that member and
   recovery would have stopped the start.
3. **21:14, `3b0dc30e` (the reason in `authority-unavailable.json`), probe after.**
   `deploy-wait --server`: reward step 21:14:30.588, probe 21:14:30.594, no refusal.
4. **21:17, Arena entry, passed.** START ARENA · RESTART: a new game process reached the
   main menu 118.6 s after the confirm, `Launch: … -arena-session · backend
   https://127.0.0.1:6970`. In the normal root: `offline-sessions/c5518522…/` with
   `lease.json` (17 members, nothing of the reason), `authority-unavailable.json` =
   `{"schema":1,"sessionId":"c5518522…","reason":"entry:expected-object"}` and
   `activation.json`; `offline-active.json` activated. ESCAPE FROM TARKOV, ARENA, NEXT: the
   Arena map screen's card (top right, x1278/y50) read "PROFILE REWARDS" OFF with the
   ordinary opt-in text. One click on the switch: ON, and the detail read
   **"Rewards are off for this profile: its reward records no longer match it or this
   installation. See the normal server's log."** (where 0.4.1 showed "Activation pending —
   return to the normal session, then re-enter Arena."), with the rates below it unchanged;
   frame in `gpu2-private-card-withheld.png`. One click back: OFF, the opt-in text again
   (the profile's consent revision moved by two and ends as found, OFF).
   RETURN TO SPT · RESTART: the normal session was back 55.5 s after the confirm; its server
   start (21:19:50) ran the reward step before the probe, no refusal, the usual per-profile
   lines, nothing about the session beyond the ordinary return.
5. **21:22, probe first.** `deploy-wait --server --game --files …StartupProbe.dll`
   (variant first). Server log: the probe's step 21:22:10.006, then 21:22:10.083
   `[Warning ArenaRewardStartup] [ArenaMode] Startup rewards unavailable
   (earlier-startup-callback: StartupProbe.ProbeLoad in StartupProbe.dll (priority 0));
   profiles will load normally. This recurs at every start while the installation stays as
   it is: no profile is enrolled, nothing is recorded or paid, and the reward card says
   'Rewards are off'.` Nothing else of the reward step for that start, as designed.
6. **21:25, probe removed.** `deploy-wait --server --game --absent
   SPT_Runtime/user/mods/StartupProbe` (gone on the host after 44 s): reward step
   21:25:23.786 to .790 with the usual per-profile lines, no probe, no refusal. Final state:
   server pid 2468's successor running on 6969, game at the main menu (instance
   `24eb3a05`), no second server, no helper window, `StartupProbe` absent on the host and
   on the VM.

## What the run shows and what it does not

- Shown: a mod at `Watermark + 1` no longer refuses the start (1, 3, 4); a mod that really
  runs first is refused with its type, assembly and priority and the recurring sentence
  (5); the entry of a profile without authority records the reason next to the lease and
  the private session's card shows it (4); a normal start, an Arena entry, a return and a
  start without the probe all work with the `3b0dc30e` server (3 to 6); the lease keeps its
  17 members, which the installed helper of 2026-11-02 accepts (4).
- Not shown: the `startup:` sentence on the card (no never-enrolled profile on the hosts),
  the normal session's "Rewards are off: another mod's …" card (not displayed while
  admission is blocked), the "temporarily unavailable" codes, and rewards being paid (the
  profile's authority is lost). No raid was played.
- Side effects: the probe reached SPT-GPU-1 and maschine11 through Syncthing for about 35
  minutes (20:51 to 21:25) and is removed there by the same sync; a normal server started
  on those machines in that window with variant first (21:22 to 21:25) would have logged
  the refusal once. The server DLL `0.4.1+3b0dc30e` is installed on every host; the client,
  the helper and Redeploy.Core are unchanged. No profile file was edited; the reward folder
  of `6ab7db1c…` is as before (its consent file carries two more revisions, same value).
