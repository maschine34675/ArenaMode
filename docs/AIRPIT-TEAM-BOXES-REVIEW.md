# AirPit original team boxes — 2026-10-17

Scope: independent Team Deathmatch only, original north USEC / south BEAR
enclosures, ten authored places per faction. Explicit even totals 4–20;
default eight. A saved 22/24-player team preference falls back to eight on read
without rewriting it. Solo modes retain their own 24-player ceiling; Tactical,
PFT, Zombies, normal raids and common MapPort markers are unchanged.

## Map contract

Requires ArenaMapPort 0.2.2 (extended north navigation and passport held gates).
The version is a documented asset prerequisite, not permission to bypass real
geometry validation. Older or changed map assets fail closed when their native
navigation cannot support the formation. No map-wide fallback is introduced.

Source: original AirPit TeamFight DesignStuff scene, north markers
6–10/16–20 and south 1–5/11–15. Coordinates, authored heights and headings are
retained. Versioned private IDs are inserted only into the protected raid clone;
the donor is unchanged `arena_airpit-bot-center-east` at (-3.21,.1,-12.09).
The external path destination (-15.51,.1,-37.64) is independently navigable;
it is not the donor. Native box bounds and anchors (indices 1/17) constrain
the existing closed-gate path exception.

MapPort 0.2.2's initially closed passport doors do not mean a foreign hold:
`IsHeld` remains false until a caller holds the gates. Arena's existing exact
world/game/arm ownership can therefore hold both passport and bus doors.
Its countdown release opens both; all twenty complete outside paths must pass
before combat. Respawns use the same boxes and never re-close the gates.
AirPit exits are outside the boxes and are never suppressed.
The user explicitly accepts jumping over low turnstiles; no extra barrier or
jump restriction has been added.

## Geometry review and validation

The v9 TeamFight scene is byte-identical to v8. Current U8 navigation has SHA256
`2A72A71BA0C959A816C26885C58EA50412075625AE62E4CFDB420B54D185D275`.
All twenty points and the destination lie on its main component, offline.
Smallest marker edge clearance is 1.1503 m; anchor clearance 6.3892/3.1502 m;
destination clearance 4.4519 m. These facts do not replace live floor, headroom,
occupancy or complete-path checks.

The frozen 0.2.2 release ZIP contains U7; current installed v9/U8 also restores
three distant crates. Native acceptance must identify the actual tested assets,
not claim that the frozen ZIP and current installation are identical.

Automated validation: full suite 1,151 passed / one installed-APBS probe skipped;
three-map runtime 318 assertions and 12 rejected mutants; gate suite 197 and 21
rejected mutants; installed map admission 801; participant menu 4,245; native
menu 725. Independent static review found no outstanding defect.
Acceptance ledger:
[AIRPIT-TEAM-BOXES-GATES.md](AIRPIT-TEAM-BOXES-GATES.md).

## Build and deployment

Implementation `341999f`; clean delivery source `132549d`, including committed
Zombie performance changes through `cd2ede5` and the canonical deployment layout.
The latter delivery commit accepts equivalent URL-encoded license links in the
staging verifier without relaxing license prose or embedded-file checks. A
deliberate license-text mutation was rejected.

The final manifest is under the implementation worktree's ignored
`artifacts/area-map-delivery/7a5bdfe518554030928f37ee18aacacc/manifest.json`.
Its source change list is empty. Staging preserved all 186 observed installed
files; deployment then replaced only Arena-owned artifacts. The old duplicate
flat client was archived by the canonical deployer outside plugin discovery.

| Artifact | SHA256 |
|---|---|
| Arena client | `2235B473511C0794A2D0AB339F9DEBC57915E769F90AFB6B06A3D111BC88BC13` |
| Arena server | `BA468562C633445CC668D709748861456102F1DAFEF5DA61457B732C3348845A` |
| AirPit scenes | `0D4E6FB3F95190154159C6DF6BB284B40B01C3351EF8CCF117FEE0A7CDCE3D1A` |

GPU1 was restarted after synchronization. Loaded client MVID
`70db91b5-3816-43fe-a76d-0bb6a5d92b14`, UiBridge instance `755a2aff`,
43 plugins loaded, all 19 maschine plugins and shared Core identical across
development disk, host disk and process. MapPort code was `0.2.2+b680f7e`
with the installed v9/U8 assets described above. No MapPort file was changed.

## Native acceptance on SPT-GPU-1

Both tests used the actual protected profile, real native spawns/combat and
UiBridge walking, not teleportation across the gates. Same fresh process,
successive worlds. Times below are local on 2026-10-17.

| Check | 3v3 | 10v10 |
|---|---|---|
| Safe capacity | Both sides 10/10, three required | Both sides 10/10, ten required |
| Initial human start | north-03 | north-10 |
| Initial active bots | 2 USEC + 3 BEAR | 9 USEC + 10 BEAR |
| Countdown / Live | 20:43:06 / 20:43:10 | 20:46:58 / 20:47:02 |
| Owned hold/release | Once each; 42 parts, 40 carvers | Once each; 42 parts, 40 carvers |
| Open navigation | All twenty complete outside paths | All twenty complete outside paths |
| Player respawn | north-08, successful walking departure | north-04, successful transit into south box |
| Bot departures/replacements | Correct factions and native departures | Both factions departed and replaced in their own boxes |
| Protected exit | Native disconnect, main menu | Native disconnect, main menu |

No gate was re-closed for player or bot respawns. At maximum size, the log
contains 48 bot arrivals including replacements, all with correct faction starts.
There was no Error, NRE, failed admission, no-contest or outstanding spawn lease
at teardown. Both complete profiles match their **actual server entry backups**
after ignoring only `characters.pmc.Hideout.sptUpdateLastRunTimestamp`;
424 inventory items before and after each raid.

One navigation caveat: walking from the south box back to the common outside
destination produced a 253.6 m NavMesh detour for a roughly 35 m straight-line
distance. The 35-second walk timed out after the player had already exited the
box. A following 25.3 m path reached the destination within 0.7 m. This is an
observed navigation inefficiency, not proof of a closed gate; it remains a map
navigation follow-up. No geometry was changed to hide it.

The second raid also logged 15 Unity MissingScript warnings in five groups,
including `extractor_smoke`, during bot preparation/activation. The log does not
identify their exact origin; no effect on gate lifecycle or teardown was seen.

Evidence is retained locally in the implementation worktree under ignored
`artifacts/airpit-native-20261017/`: full BepInEx log, raw bridge logs, walk
results, inspected screenshots, actual entry/exit profiles, deployment baselines
and preservation result. Profiles and other private raw evidence are not committed.
Final preservation checks passed for 51 other plugin DLLs, all five captured
AirPit map/data files, 33 PFT persistence files, server config and MapPort config.
The two test-owned preferences were restored (mode Zombies, AirPit team total
eight), godmode was disabled, and the host was left in the main menu with its
control lease free. Independently changing Zombie settings in the synchronized
config were preserved, not overwritten with an old whole-file snapshot.
