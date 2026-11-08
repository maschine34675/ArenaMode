# Domination profile rewards: native run on GPU-2 (2026-11-08)

Host SPT-GPU-2 (`C:\SPT41`, SPT 4.1.6), 10:55 to 11:04. Build `0.5.0+b3dd3983` (the commit
"Domination earns profile rewards for eligible kills"), client and server DLLs installed by hand on
D:\SPT41 and rolled out with `gpu-run deploy-wait --host host2 --server --files …` (2 files identical
after 2.1 s, server restarted); the release Core 1.0.20 stayed as it was. Profile **SPT-GPU-1**
(`6ab7db1164f04b565cf78be7`, level 1, enrolled since 2026-10-29 with rewards OFF), chosen because
SPT-GPU-2's reward authority is still withheld after its wipe (see the handoff).

1. Game started with `gpu-run start --host host2 --profile SPT-GPU-1`; the server's startup reward
   pass ran as usual (SPT-GPU-2's known Warning, nothing paid).
2. Arena map screen: the PROFILE REWARDS card showed `OFF` with the rate text; one click on the
   switch → `ON`, the detail changed to "At the next normal server start: RUB by mail, XP credited
   directly", the client posted `/arenamode/rewards/consent` (request successful).
3. `gpu-run raid --mode domination --map factory --participants 8`: the DOMINATION row (300-point
   default on the menu harness; this host's cfg keeps its 300 since the 0.5.0 install) started a
   match; at the raid start the client posted `/arenamode/rewards/begin` (successful, no
   "reward capture was refused" Warning), `Arena match started (Domination)`, Countdown → Live
   with `killLimit=300, timeLimit=600s`, ownership lines A/B/C.
4. With godmode on, the player walked to the BEAR bot Agnotology (`uibridge_walk`), tracked its
   chest (`uibridge_look track=true`) and fired the SVDS: four shots without line of sight, then
   three at 1.9 m with the bot visible; the bot died and the HUD read `score 200 · kills 1 ·
   deaths 0`.
5. DISCONNECT: `/arenamode/rewards/capture` (the final packet) and `/arenamode/raid-end`
   ("raid result still pending — restore deferred to post-raid processing"), profile snapshot
   restored, "Protected Arena end completed … rewards are verified separately"; the claim appeared
   as `user/arena-rewards/6ab7db11…/inbox/2da30751….json`; client: `Arena match ended: 1 kills /
   0 deaths, score 200`.
6. EFT quit, `gpu-run server stop` / `server start`: the startup reward pass logged
   **`Arena reward: 1000 RUB delivered by mail; 100 XP credited.`** for SPT-GPU-1 (Information),
   exactly one kill, no win bonus. No ArenaMode Warning or Error in the client or server log for
   this profile.

Not run: a Domination match ending at the score limit with rewards (the monotonic reward clock
was reasoned and pinned, not observed at a score-limit finish), a time-limit finish, a give-up
(local forfeit) in Domination, and the payout mail opened in the game. Evidence: the host's
BepInEx log `LogOutput-SPT-GPU-2-2026-11-08_10-56-49.log` and server day log
`spt-SPT-GPU-2-20261108.log`.
