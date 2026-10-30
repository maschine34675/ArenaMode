# Protected Zombie survival with PitFireTeam — 2026-10-08

## Opening countdown and PMC refill — 2026-10-15

[ZOMBIE-WAVE-REFILL-20261015.md](ZOMBIE-WAVE-REFILL-20261015.md) supersedes
the historical surviving-PMC exception below. A cleared wave also restores a
living PMC's entry gear through the protected body-replacement path. The first
wave waits for team readiness and then the configured intermission countdown.
The original implementation and test record below is retained as history.

## Current roster extension — 2026-10-13

[PFT-SQUAD-SIZE-REVIEW.md](PFT-SQUAD-SIZE-REVIEW.md) supersedes the historical
exact-four cardinality below: one to four invited companions are supported, with
that exact count and account/profile mapping frozen before arming. Tactical still
has five opponents. Wipe, reconstruction and spectator checks require every
frozen member; a missing member never reduces the expected roster. Solo defaults
are 32 alive / 50 first wave / growth 20 / own gear, preserving saved preferences.
At that milestone the surviving-PMC refill limitation remained unchanged; the
October 15 extension above replaces it.

## Map admission correction — 2026-10-09

The area-menu proposal retained a Tactical-only PFT guard after the first
delivery. A valid Zombie squad could select the mode but every map click was
replaced with a null location. Vanilla's SelectedLocation setter then hid Ready
and Next and closed the time panel. Use the existing ArenaModeCatalog.CanSelect
contract at this second admission seam as well; roster authentication and map
restrictions are unchanged. The Zombie area summary now says `5-player squad`
instead of implying two opposing teams.

The native-hook regression failed before this two-line production fix and passes
after it. Parent reruns: native area hooks 169, rendered area view 29, real BepInEx
area configuration 221, mode selection 157 assertions. The native hook fixture
also checks Factory day/night, Labs/Labyrinth, group and periodic refresh,
Tactical/Zombie switches and rejection of other PFT modes/unavailable maps.
Independent adversarial review found no new P0-P2 issue. GPU follow-up results
and final deployed identity are recorded separately in HANDOFF.md.

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
  authenticated identities. The Friendly Fire mutator defaults **off** for this
  squad, independently of Team DM and PFT Tactical; the preference freezes on
  arming. It protects authenticated teammates without altering AI hostility or
  self/enemy/environment damage. See `FRIENDLY-FIRE-REVIEW.md` for damage surfaces
  and native effect limitations. Award and assist attribution distinguish
  infected from the protected team, including a dead follower's delayed grenade.

Equator is excluded from PFT Zombies following the user's playtest: its narrow
space causes the five-person squad to obstruct itself. Earlier startup proof
does not override this gameplay restriction. Solo Zombies remains available
there; Bay5 and the three bounded Customs areas remain available to the squad.

## Implementation boundaries

`PitFireTeamSelectionPolicy`, `ArenaModeCatalog`, `ArenaLocalRaidStartedPatch` and
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

The initial 2026-10-08 delivery had **no native acceptance**: GPU2's dedicated
profile had no stored PFT companions. The user subsequently added four, and the
2026-10-09 bounded native run passed initial activation, local death/spectator,
one wave rebuild and protected exit. See `GPU-ZOMBIE-PFT-MENU-RESULTS-20261009.md`;
its explicit limits supersede the initial lack of test prerequisites. No
companion data was fabricated or copied between profiles for testing.

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

The full protocol above remains broader than the successful 2026-10-09 smoke
run: team wipe, surviving-PMC transitions, repeated rebuilds and control raids
still require native acceptance. Do not infer those from the bounded result.
