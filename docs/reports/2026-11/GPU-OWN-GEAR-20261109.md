# Own gear in every mode: native run on GPU-2 (2026-11-09)

Host SPT-GPU-2 (`C:\SPT41`, SPT 4.1.6), 12:06 to 12:38 UTC (13:06 to 13:38 local). Development build of the
own-gear working tree (contract [OWN-GEAR-ALL-MODES-20261109.md](../../reviews/OWN-GEAR-ALL-MODES-20261109.md)),
stamped `0.5.0+f33a3d2c` (the HEAD of the uncommitted tree; the commits that carry the change follow this
record), client and server DLL installed by hand on D:\SPT41 and rolled out with
`gpu-run deploy-wait --host host2 --server --files …`: first build client SHA-256 `568bfbad1d2f933e…`, second
build (with the corpse-strip fix below) client `0a1435527b85a6f4…`, server `895900142165ce1d…` both times.
Built against the installed **Core 1.0.21** with the Redeploy checkout detached at `bebf49b` for the build
(Redeploy `main` is at 1.0.22). Profile **SPT-GPU-2** (`6ab7db1c64f04b565cf78be9`, rewards ON). The host's
ARENA toggle was available in the normal session this time (no whitelist conflict), so no Arena session was
needed; the test ran in the normal session.

**Test gear.** The profile's PMC was naked (pockets, Gamma, bayonet). Before the first raid it was equipped
from its own stash through the inventory bridge: M4A1 (30-round STANAG), Trooper armour, TC-2002 helmet, two
spare STANAG magazines and an IFAK in the pockets (an AVS plate carrier was refused by the game over the armour,
an MBSS rig did not take). The gear stays equipped on the test profile after the run; every raid restored the
profile around it (`Profile snapshot restored for session …` and `Protected Arena end completed` in the server
log after each raid, no Warning or Error from ArenaMode in the server log). The F12 keys `Deathmatch/OwnGear`,
`Team Deathmatch/OwnGear` and `Tactical/OwnGear` were set to true for the run and back to false afterwards; the
values the menu recipe changed (`Match/GameMode` gungame → … → tactical, `Deathmatch/TimeLimitSeconds` 300 →
120, `Team Deathmatch/KillLimit` 25 → 0, `Map participants/factory4_day_solo` 10 → 8) were restored.

1. **Deathmatch, Factory, 3 rounds × 2 min, FFA, 8 participants** (`gpu-run raid --mode deathmatch
   --map factory --rounds 3 --round-time 2 --kills off --opponents ffa --participants 8`, 13:10 local).
   Loading screen: `Arena: nothing to preload in the loading screen — the player's own gear keeps the entry
   loadout.` Raid start: `Arena own gear: skipped the raid-start kit swap; the entry loadout is protected in
   process memory …` (Info); the player held the M4A1 (29/30 +1). After 21 shots (9 rounds left) the kill
   hook: `Arena death #1 — instant respawn.` → 0.5 s later `Arena own-gear respawn restored the entry loadout
   (epoch 1).`, magazine 29/30 +1 again, deaths 1; the Molotov mutator added its grenade to the restored
   loadout as it does to a kit (`Arena incendiary supplied`). After 16 more shots the round break at 13:12:38:
   `Arena Round 1: draw — Time limit` → `Arena round own-gear reissue complete.`, the player moved to a spawn
   point with 29/30 +1. Rounds 2 and 3 ran without the player's involvement (Provocator won round 3 on time);
   `Arena match ended: 0 kills / 1 deaths, score 0, best streak 0, 6.8 min.` No ArenaMode Warning or Error;
   only the known Unity `referenced script is missing` warnings of the Factory load.
2. **Team Deathmatch, Factory, 4v4, no kill limit, 5 min** (13:18 local). Same loading and start lines, M4A1
   29/30 +1. `Team Deathmatch phase: Countdown -> Live` at 13:19:21; 14 shots, kill hook → `Arena death #1 —
   respawn after optional killcam.` → 0.3 s later `Arena own-gear respawn restored the entry loadout (epoch
   1).`, magazine full again. Godmode ends with the hook's death, so bots killed the player twice more; both
   deaths respawned through the same path after the 3 s killcam (`epoch 2` 13:22:47, `epoch 3` 13:23:10).
   `Team Deathmatch result: Lost; Time limit reached; USEC 11:14 BEAR` (13:24:21), `Arena match ended: 0
   kills / 3 deaths … 5.6 min.` No ArenaMode Warning or Error.
3. **Solo Tactical, Factory, first attempt** (13:25 local, first build): `Tactical: match started with the
   player's own gear; the kit economy is off.`, HUD `Round 1 0-0 NEXT ROUND 9s [own gear]`, no buy panel
   auto-opened, round 1 start `Arena round own-gear reissue complete.` (13:25:56). 14 shots, kill hook →
   `Tactical round 1 lost — 0-1, own gear.`, HUD `Round 2 0-1 next round in 2s`. **At the round-2 start the
   held respawn failed:** `Arena respawn failed: … Arena own-gear respawn could not restore the entry loadout:
   loadout prepare failed: protected identity validation failed: 1 originally protected items left or
   disappeared from protected slots`, followed by the verified-owner abort (`Arena start is invalid; protected
   exit queued …`, `Arena invalid-start native teardown accepted`) and `Tactical ended: 0-1 over 2 round(s),
   own gear.` Cause: `StartRound` asks the corpse cleanup to clear every corpse at the round start
   (`Arena corpse cleanup: clearing every corpse (round 2 start)` one line before), and that trim strips the
   first-level gear of the player's own corpse too, the bayonet in the scabbard included; the own-gear restore
   validates the retained slots (secure container, scabbard, armband, special pocket slots) before it rebuilds
   anything. Deathmatch and Team DM never meet it: their respawn runs before any sweep. The abort worked as
   designed (the raid ended through the verified-owner path, the profile was restored).
4. **Fix and second attempt** (`CorpseCleanupService.StripGear`: the player's own corpse keeps the retained
   slots and the pockets, the heavy models still go; CorpseCleanupChecks gained the case and a compiled fault;
   rebuilt, reinstalled, EFT restarted at 13:34): start lines as before, round 1 reissue at 13:35:51, 14
   shots, kill hook → `Tactical round 1 lost — 0-1, own gear.`, HUD `Round 2 0-1 NEXT ROUND 1s [own gear, back
   next round]`, then at the round-2 start `Arena corpse cleanup: clearing every corpse (round 2 start)` →
   `Tactical round 2: 5 enemies, own gear reissued.` → `Arena own-gear respawn restored the entry loadout
   (epoch 2).` (13:36:25), magazine 29/30 +1, `Tactical round 2 live: 5 active opponents`. No ArenaMode
   Warning or Error. The raid was left through the pause menu (`gpu-run leave`); the server restored the
   profile (13:36:59).

Not run: Domination (its own-gear path is the Team DM path: the same mode class, the same death respawn, no
round break) and a Zombies regression raid (its own-gear code is a rename; ZombieSolo/Team/WavePending checks
cover it offline); a Team DM raid on a map with team boxes (Equator, Bay5; Factory has none, so the box lease
branch of the shared path was not exercised); the character preview and the row itself in the native menu
(the menu recipe knows no `--equipment` for these modes yet; the keys were set through the bridge and the
offline menu harness covers the row); a poisoned-snapshot abort; the buy panel opened by hotkey. Evidence: the
host's BepInEx logs `LogOutput-SPT-GPU-2-2026-11-09_13-06-07.log` (first build; raids 1 to 3) and
`…_13-34-08.log` (second build; raid 4), the server day log `spt-SPT-GPU-2-20261109.log`.
