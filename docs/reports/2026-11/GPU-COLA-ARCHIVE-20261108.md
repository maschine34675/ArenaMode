# Lost reward authority with Tarkova Cola history: native run on GPU-2 (2026-11-08)

Host SPT-GPU-2 (`C:\SPT41`, SPT 4.1.6), 10:26 to 10:31 UTC (11:26 to 11:31 local). Build
`0.5.0+fd4543d9` (the commit "Archive a complete Tarkova Cola history with a wiped or moved
profile"), client and server DLL installed by hand on D:\SPT41 and rolled out with
`gpu-run deploy-wait --host host2 --server --files …` (2 files identical after 2 s, server
restarted); BigBrainCompat, Gate, the Session helper and Core 1.0.20 stayed the release files.
Profile **SPT-GPU-2** (`6ab7db1c64f04b565cf78be9`), wiped in the SPT launcher on 2026-10-29 with
Tarkova Cola reward history: before this run its reward folder held the runtime-managed enrollment
of 2026-10-24 (root `C:\SPT41\SPT_Runtime`), `consent.json` with rewards OFF (revision 15), journal,
candidate, approved and acknowledged records, the session control files, and `cola-plans` with two
plans and their two `.done.json`; the profile file (153,176 bytes) carried no reward block. Every
server start since 0.4.1 had logged the Warning "rewards stay off, because the profile lost its
reward ledger … and its reward records hold Tarkova Cola history (cola-plans), which stays
unchanged" for it, the last one at 10:03:57 UTC.

1. Server restart by `deploy-wait` (ready 10:26:59 UTC). The startup reward pass logged at
   Information: `Profile 6ab7db1c64f04b565cf78be9: the profile lost its reward ledger (profile
   wipe, restored backup or editor), and no reward was unsettled, so its old reward records were
   archived at user/arena-rewards/6ab7db1c64f04b565cf78be9.archived-20261108T102701Z-4d0d46d8
   (its completed Tarkova Cola reward history moved into the archive with the other records; the
   Cola progression itself stays in the Tarkova Cola mod's files). Rewards are switched off, so
   nothing was enrolled; your rewards setting was kept and switching them on enrolls with a fresh
   start.` No ArenaMode Warning or Error in that run; the Warning of every earlier start is gone.
2. On disk: the archive folder holds every old record (enrollment, enrollment plan, journal,
   candidate, approved, acknowledged, control, entry, handoff, return, `allocations`, `decisions`,
   `delegations`, `imported`, `inbox`, `recovery`, `offline-delegations`, `cola-plans` with its four
   files, the old `consent.json` OFF revision 15) plus `evidence/` with the moved
   `arena-reward-checkpoints`, `arena-reward-runs` and `arena-reward-snapshots` folders. The live
   folder held only the carried `consent.json` (OFF, revision 15). The profile file kept its size
   (153,176 bytes, still no reward block); the Tarkova Cola mod's own files were not touched.
3. `gpu-run start --host host2 --profile SPT-GPU-2` (16/16 maschine plugins identical, loaded =
   disk), ESCAPE FROM TARKOV → ARENA → NEXT: the PROFILE REWARDS card read `OFF`; one click on the
   switch → `ON` with "Activation pending — restart the normal SPT server",
   `/arenamode/rewards/consent` successful (11:29:33 local); `consent.json` → enabled, revision 16.
   BACK to the main menu, EFT quit gracefully.
4. `gpu-run server restart --host host2` (10:30:24 UTC): the pass logged
   `Startup reward: Deferred (rewards-enrolled-no-pending-result)` (Debug) for the profile; the
   live folder now holds `enrollment.json` (new installation id `77bb3c5f…`, new epoch `e34fafb2…`,
   runtime-managed, root `C:\SPT41\SPT_Runtime`), `enrollment-plan.json`, `journal.json` without
   entries, `handoff.json` and `delegations/`; the profile file carries the reward block again
   (153,404 bytes). No ArenaMode Warning or Error. SPT-GPU-2's rewards are ON from here.

Not run natively: a moved install (root change) and an incomplete Cola history (both xunit only), and
a payout into the fresh enrollment (the next Arena match with SPT-GPU-2 shows it). Also observed, not
part of this change: profile SPT-GPU-1 (`6ab7db11…`) logged the Debug status
`Startup reward: ClosedWithoutPayment (reward-closed-without-payment)` at both starts after its
Domination payout of 10:03:57 UTC; its folder shows no write at these starts besides the renewed
`handoff.json`. The status is the inbox summary (`RewardRuntimeInbox`: a closed claim and nothing
newer unpaid): the inbox claim `5ab6341f…` of 2026-10-28 has no import because a recovery closure
of that day closed it without payment, and the newest claim is paid. Pre-existing, read-only, not
a defect of this change. Evidence: server day log `spt-SPT-GPU-2-20261108.log` (lines 1568-1572 and 2255-2258),
BepInEx log `LogOutput-SPT-GPU-2-2026-11-08_11-28-15.log` (the consent request at 11:29:33 local).
