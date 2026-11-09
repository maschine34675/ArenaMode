# Posthumous kills across a round boundary (issue 2): native run on GPU-2 (2026-11-09)

Host SPT-GPU-2 (`C:\SPT41`, SPT 4.1.6), 09:13 to 09:41 UTC (10:13 to 10:41 local). Build
`0.5.0+dacad6fa` (the commit "Keep a multi-round match valid when a kill is credited to an earlier
round's body"), client and server DLL installed by hand on D:\SPT41 and rolled out with
`gpu-run deploy-wait --host host2 --server --files …` (2 files identical after 2 s). The client was
built against the installed **Core 1.0.21** with the Redeploy checkout detached at `bebf49b` for the
build: Redeploy `main` already carries Core 1.0.22, and a first build stamped against it disabled
the client at startup ("installed Redeploy.Core 1.0.21.0 is older than the required minimum 1.0.22",
ARENA UNAVAILABLE on the raid-side screen). Profile **SPT-GPU-2** (`6ab7db1c64f04b565cf78be9`,
rewards ON since 2026-11-08, epoch `e34fafb2…`).

The host now carries mods outside the whitelist (TSD Logistics, TSD Technologies and their
companion plugins), so the normal session's ARENA toggle is disabled ("MOD CONFLICT · Restart with
filtered mods") and the run used the Arena session: `START ARENA · RESTART` → `CONFIRM RESTART`
(`gpu-run click … --confirm "CONFIRM RESTART" --wait-restart`, a new process at the main menu 60 s
after the confirm), the match, then `RETURN TO SPT · RESTART` → `CONFIRM RESTART` (66 s).

1. `gpu-run raid --host host2 --mode deathmatch --map labyrinth --rounds 3 --round-time 5
   --kills off --participants 8` in the Arena session (10:22 local): `/arenamode/rewards/begin`
   successful, "Arena match started (Deathmatch)", round 1 at 10:22:22, round 2 at 10:27:38,
   round 3 at 10:32:53. Godmode on, the local player idle (0 kills / 0 deaths).
2. 10:28:59 local, 81 s into round 2, the issue's case (Debug): `Arena reward capture: the kill of
   body 56 is credited to body 8, a dead body this round never registered (a tripwire or grenade
   of an earlier life); recorded without a killer.` Exactly one such line in the match. On the
   unpatched client this one kill would have voided the match.
3. 10:38:09: "Arena match ended: 0 kills / 0 deaths, score 0, best streak 0, 15.8 min",
   `/arenamode/rewards/capture` and `/arenamode/raid-end` successful (the raid-end restore
   deferred to post-raid processing as usual). The signed claim `inbox/075db6b9….json` decodes
   to state Completed, mode deathmatch, three rounds with 39, 43 and 53 deaths (135 in all);
   round 2 holds 42 combat deaths and the one environment death without a killer, round 3 four
   environment deaths without a killer. No ArenaMode Warning or Error in the process apart from
   the known Labyrinth pickup-placement Warnings at the start of round 2.
4. Return to the normal session (10:39 to 10:40); the decision `decisions/075db6b9….json` (10:40
   local, sequence 9): `"reason":"no-eligible-reward","eligibleKills":0,"roundWins":0`, that is
   NoReward after a complete validation, where the issue's three voided matches had ended as
   `run-not-completed`. The normal server restart at 09:41 UTC logged `Startup reward: AlreadyPaid
   (reward-inbox-already-imported)` for the profile and no Warning.

Not run: a payout into this run (the local player made no kill; the payout path was run on
2026-11-08 for Domination), a Deathmatch round ended at the kill target by such a kill (the
residual case named in REWARDS-RESULTS). Also observed, not examined: the profile's runs 7 and 8 of
2026-11-08 18:40 and 18:46 (another session's Tactical team-arena runs) are `run-not-completed` in
`decisions/`; whether that was this issue (Tactical has rounds too) or a left raid is open.
Evidence: the host's BepInEx log of the Arena session process
`BepInEx\logs\LogOutput-SPT-GPU-2-2026-11-09_10-21-08.log` (lines 17501 to 18441), the server day
log `spt-SPT-GPU-2-20261109.log`, and the claim and decision files named above.
