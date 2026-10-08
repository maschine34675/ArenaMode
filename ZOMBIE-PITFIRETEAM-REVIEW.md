# Protected Zombie survival with PitFireTeam — 2026-10-08

## Player contract

Optional squad variant of Zombies, selected through the existing native PFT
group flow: local PMC plus exactly four ordinary stored companions. Tactical
remains available; Deathmatch, Gun Game and independent Team DM remain unavailable
with this roster. Solo Zombies and its own-gear choice remain unchanged.

- Uses the player's and companions' own equipment. Both persistence stores remain
  protected by the existing exact-version PFT adapter and server guards.
- Wave 1 waits for four distinct active companions bound to the exact local PMC.
  Failure to prove the roster ends no contest; it never falls through to solo.
- Local death holds respawn. Optional killcam is followed by third-person
  observation of surviving companions. Escape remains owned by the shared native
  spectator menu bridge.
- A fully cleared wave rebuilds **all four companions**, including survivors,
  through the already established PFT teardown/prepare/activate/adopt lifecycle.
  Their equipment and health therefore refresh. This is shown in the menu.
- A surviving PMC keeps the same body, equipment and current condition; the solo
  Zombie refill/body replacement is intentionally bypassed. A dead PMC receives
  its captured entry loadout through the prepared one-use respawn transaction,
  before the fresh followers are bound to it.
- The next wave waits for the complete replacement squad and intermission.
  Five confirmed deaths end the match. Lost identities or failed reconstruction
  end no contest. Missing bodies are not accepted as proof of a normal defeat.
- Team diamonds, optional team tracers/lasers and friendly killfeed names use
  authenticated identities. No new friendly-fire rule is introduced; this keeps
  the existing PFT/Tactical behavior. Award and assist attribution distinguish
  infected from the protected team, including a dead follower's delayed grenade.

## Implementation boundaries

`PitFireTeamSelectionPolicy`, `ArenaModeCatalog`, `LocalRaidStartedPatch` and
server `PitFireTeamRaidStartPolicy` now admit `zombie` as well as `tactical`.
All original roster, version, special-companion, Fika/co-op and external-store
protection checks remain mandatory. The client freezes own gear for a protected
Zombie roster without changing the user's solo preference. The native preview
also reflects own gear even before the arm is frozen.

`ZombieTeamRuntime` owns the team lifecycle; `ZombieMode` continues owning infected
counts and the wave director. There is no opponent PMC batch. Living infected are
reconciled against the squad only when a nonempty enemy set exists: the Tactical
hostility helper intentionally rejects an empty list. Expensive live roster and
hostility observation is throttled; explicit total-death checks remain prompt.
Neither the wave clock nor generic loadout refill runs through protected rebuild.

The dead local body cannot enter the solo own-gear respawn branch. The match service
grants its replacement only from a live Zombie team coordinator with matching
game, corpse, epoch, prepared loadout, PFT transition and one-use authorization.
This preserves the original Tactical authorization path. Damage guards cover
assembly, reconstruction and terminal aftermath. Administrative follower retirement
uses the existing unscored lifecycle; persistence remains suppressed through exit.

Spectator requests explicitly select either the cooperative four-person roster or
the Tactical nine-person roster. Both identity admission and target-provider checks
use that same exact count; the service rejects a request shape for the wrong mode.
No arbitrary-size spectator request was opened.

Infected arrivals and idle pursuit target a live authenticated team member. The
spawn ring switches to a live companion after PMC death. Follower bodies do not
count toward infected wave quotas. Native alive-cap headroom includes four extra
companions; the server's Zombie location floor is 64 (maximum 56 infected plus
four followers and activation reserve). No ordinary location is changed.

The scoreboard synchronizes four stable companion account identities and no
permanent opposing roster. Its existing history compaction remains active.
The frozen friendly and admitted infected tracer identities cannot change sides.

## Verification and remaining native acceptance

Offline checks execute the production Zombie mode/coordinator with bounded native
test doubles, admission and menu policies, spectator policy, assists, markers,
tracers, laser drawing and existing PFT round lifecycle. They cover assembly,
empty opening/tail, delayed arrivals, local/follower death, team wipe, exact leader
replacement, one-use authorization, failed reconstruction and solo refill.
Compiled fault injection checks the new runtime gates. Independent source review
resolved empty-hostility, spectator-count, missing-body wipe and delayed-assist
findings. Detailed final counts and installed build identity belong in HANDOFF.

This is **not native acceptance** of Zombie+PFT. GPU2's `SPT-GPU-2` profile currently
has no stored PFT companions. Existing stored companions belong to `spt41dev`;
no companion data was fabricated, copied between profiles or modified for testing.

For the native run use a test profile with four properly configured PFT companions:

1. Restart both SPT server and EFT after deployment. Select four ordinary PFT
   companions, then Arena → Zombies. Verify own-gear avatar, squad label, next-wave
   respawn and locked equipment choice. No enemy PMC squad should appear.
2. Let one companion die and clear the wave with the PMC alive. Verify four fresh
   companions and a second wave, without changing the living PMC body.
3. Die in the following wave. Verify optional killcam → companion spectator, Escape
   access, active infected pursuit and no immediate respawn. Clear the wave through
   the survivors; verify the restored PMC and exactly four fresh followers.
4. Eliminate the whole team: one protected exit, no further wave/respawn. Repeat with
   quit during observation, and exercise two consecutive reconstruction cycles.
5. Compare the complete player profile and PFT teammate directory before/after,
   including the 30-second delayed-finalization window. Inventory/progression and
   companion stores must remain unchanged; classify ordinary bookkeeping timestamps.
6. Control raids: solo Zombies with kits/own gear, Tactical 5v5 and ordinary PFT.
   Also exercise maximum infected cap, closed/failed loading and a map transition.

Native animation, actual follower wrapper activation, live team hostility and
in-game profile neutrality remain unverified until this run is completed.
