# Equator PFT restriction and Friendly Fire — 2026-10-09

**Superseded on 2026-10-11:** the user released GPU2. Deployment, native
menu/freeze/damage checks and preservation results are recorded in
`GPU-FRIENDLY-FIRE-RESULTS-20261011.md`, including the limitation of the ON
sampled-HP comparison. The later deployed `96ed83b` also includes Equator Team DM
box spawns; see `GPU-EQUATOR-TEAM-SPAWNS-RESULTS-20261011.md`. The waiting state
below is the historical October 9 record, not an active host restriction.

Implementation complete; deployment and native acceptance are **pending**.
At 21:07 the deployment guard found another session had started GPU2. It refused
before copying any DLL. The user explicitly requested waiting for that session.
Do not take over, restart or deploy to GPU2 until it is released.

## Source and staged artifacts

- `d265b4a`: exclude Equator from protected PFT Zombies at selection, arm and
  native server start, including stale armed sessions. Solo Zombies stays allowed;
  Tactical's existing Equator restriction remains.
- `100c003`: independent Friendly Fire mutator for Team DM/PFT Tactical/PFT Zombies,
  defaults ON/ON/OFF. Solo modes display N/A. Freeze on arming; mid-raid F12 edits
  apply next raid. AI hostility and self/enemy/environment damage remain native.
- Client/server staged together from `100c00362c0c2d47992868c4e66bad0dfe0f00f3`.
  Manifest: `artifacts/area-map-delivery/11909aa24ade491d83a283972ee40a0f/manifest.json`.
  Client SHA256 `854B538C4277BC7D3D293D805ED11C1F1C1D9CC391CE4EAC3BCF90F14A367F26`;
  server SHA256 `DEBDE6F54D874062BF8CEE27ED67B63AEEF3D94AA9970F13B758FF1BC4D6972E`.
  Zero errors, four existing client warnings/two existing server warnings;
  84 installed files unchanged during staging, preview package check passed.
- No installation was changed by this task. GPU2 still loaded the previous
  Arena build when the guard refused. Core/PFT/SAIN/UiBridge were not rebuilt.
  Unrelated untracked Molotov assets remain untouched. No push performed.

## Offline verification

- Unit suite: 940 passed, two existing skips.
- Map admission: 530 assertions, nine compiled negative controls. Native menu
  model 631, native area hook model 195, Customs integration model 392.
- Mutator/menu model: 1461 assertions, 27 compiled negative controls. Seventeen
  switches retain the panel footprint in nine/eight rows; this is layout arithmetic,
  not proof of native readability.
- Friendly Fire model: 154 assertions, 34 compiled negative controls. Parent
  repeated the baseline. Both PFT mode partials and grenade context/effect filters
  are real production sources; native engine and the frozen session input are doubles.
- Existing TDM runtime: 332 assertions. Zombie team: 62 and eleven compiled
  negative controls; solo: 85; pending wave: 49. Parent repeated all three.
  Real initial/rebuilt Zombie Live transitions bind the physical protection ledger.
- Bleeding integration: 130 assertions/six controls; duration difficulty: 77.
- Independent adversarial review: no outstanding P0–P2. Native compile passed.

Evidence directories: `artifacts/pft-friendly-fire-20261009`,
`artifacts/equator-pft-admission`,
`artifacts/tdm-match-menu-checks/01b8d521f5dc402fbda0548669c9c129`,
`artifacts/friendly-fire-checks/3ed50b058dca422d8f74c9d0c5c2fd3f`,
`artifacts/zombie-team-checks/e95ac8ce4f824dbcb264a136e29ccf2a`.

## Native continuation once GPU2 is released

1. Recheck source state, staged hashes, running processes and session ownership.
   Capture fresh profile/PFT/config/artifact baselines: the earlier snapshot in
   `artifacts/pft-friendly-fire-20261009` predates the other session's work and must
   not be used to attribute its changes to this test. Never restore profile JSON
   over the user's current state.
2. With EFT and server stopped, deploy only the two staged Arena DLLs locally and
   to GPU2. Restart server/EFT with profile `SPT-GPU-2`; verify VM/disk/loaded identities.
3. Native menu: Equator offered for solo Zombies and absent for PFT Zombies;
   inspect FF defaults, independent preferences, solo N/A and all seventeen rows.
   Invite the existing four stored companions through native PFT UI.
4. Two Bay5 protected Zombie raids, OFF then ON, with the same temporary native
   settings: WaveStartSize 30, MaxAlive 1, SpawnIntervalSeconds 10, WaveGrowth 0,
   ShooterPercent 0. Record/restore exact prior settings. This holds a live wave
   with low simultaneous pressure; assembly/intermission is independently immune.
5. The test profile currently has no weapon. Only after confirmed protected
   InRaid, UiBridge may grant one ER Bayonet (`54491bb74bdc2d09088b4567`) using its
   bundle-loading item hook. Examine/equip it normally; use native SelectKnife and
   ToggleShooting/EndShooting near one exact companion. No bot HP setter or forced
   bot kill, and no persistent menu inventory grant.
6. Require actual Live phase and frozen FF value. OFF proof: local damage-hook
   veto names the exact source/victim and HP stays unchanged. ON proof: an actual
   native hit reduces HP with matching attribution. A missed attack or staging
   immunity is not a passing control. Bot attacks/effect-filter candidates do not
   consume the one diagnostic for the local player's first blocked hit.
7. Leave through protected native exit, clear hooks, restore test preferences,
   compare full profile and all twelve PFT files with the fresh baseline, then
   close EFT gracefully and restore the pre-test server state. Inspect live versus
   post-Stop errors separately. Record native limits honestly.

See `FRIENDLY-FIRE-REVIEW.md` for ownership/damage contracts. Attributed native
grenade effects are protected; standalone Zvezda/flare blinding remains native.
Grenade visuals, all weapons and long matches are not certified by the planned
knife comparison alone.
