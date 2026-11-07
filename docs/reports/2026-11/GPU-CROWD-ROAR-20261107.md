# Crowd reactions with ArenaMapPort's roar layer: native run — 2026-11-07

Host: SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`), test profile `SPT-GPU-2`, 1920×1080, SPT 4.1.6.
Driven from this machine with UiBridge 0.9.9 and gpu-run; the scenario file is
`tools/CrowdReactionChecks/scenarios/crowd-tdm-bowl.json` (with the `[ReactionBeds]` expectations
added that day). Contract, the roar layer and the two rules it brought:
[CROWD-REACTIONS-20261102.md](../../reviews/CROWD-REACTIONS-20261102.md), section "ArenaMapPort's
roar layer as implemented". Raw evidence (scenario report, log exports, stills) is under the
ignored `artifacts/crowd-roar-20261107/gpu/`. The first native run of the crowd (phase A, voices
only) is [GPU-CROWD-REACTIONS-20261102.md](GPU-CROWD-REACTIONS-20261102.md).

## Build and delivery

No-deploy build of `feature/crowd-reactions` at `67bbc215` in a separate worktree
(`dotnet build ArenaMode.sln -c Release` with `NoDeploy.targets` and a sentinel `SPTUserMods`),
with the Redeploy checkout detached at `3c41bd6` for the build so the client stamps the installed
Core 1.0.19 (Redeploy `main` carries another session's unreleased Core 1.0.20); the checkout was
put back on `main` right after. Only the client DLL was copied into `D:\SPT41` by hand: the
installed server DLL (`0.4.1+3b0dc30e`, the reward start-order fix of another session) and the
Core bytes were left alone and checked unchanged. Then `gpu-run deploy-wait --host host2 --game
--files BepInEx/plugins/maschine-ArenaMode` (69 files identical on the host after 2.9 s, game
restarted, "build 16/16 identical").

| Artifact | Version | SHA-256 |
|---|---|---|
| ArenaMode client | `0.4.1+67bbc215` | `04BE71F53AF964F28273F39D7134D5C0E3946A155E54A0274CB0A28E1CF0D815` |
| BigBrain companion (unchanged, 2026-11-02 build) | `0.4.1+8d21e362` | as on 2026-11-02 |
| ArenaMode server (unchanged, another session's) | `0.4.1+3b0dc30e` | unchanged |
| Redeploy.Core (unchanged) | `1.0.19+3c41bd6` | `692243668D565FA89ADAFCFAB8F2E472A002C66522CA34E9105D1F2E3B073619` |
| ArenaMapPort client (roar layer, installed by the MapPort session on 2026-11-07 14:58) | stamped `0.4.0+d70acd2` (built from `d70acd2` plus the roar-layer work, committed as `e3e4290`) | `89DF2A03497B7F3AB17F038C84020D5EF62E21DB36B7E58F94392D35B3842457` |

ArenaMapPort's developer mode was on, so its `[SpectatorAudio] <map>: reaction <Kind> strength=…,
N voice(s) [...], roar=cheer|angry|none` line confirms each played reaction and its roar, and
`[ReactionBeds] <map>: roar cheer|angry amp=… hold=… s clip=… stands=…` each roar start;
ArenaMode's own lines are `Arena crowd …` at Debug (the new `Arena crowd reaction dropped: …`
among them).

## Results

| Run | Result |
|---|---|
| Team DM, Bowl, 4v4, 25 team kills, 17:18–17:26 (left after 8 min without a result, as on 2026-11-02) | MapPort built `6 roar beds (3 cheer, 3 angry), 6 stands, volume 1.00` at load; attach `excitement=bound, reactions=bound`, claim on the first attempt, `kinds=0x3E`. **Every reaction ArenaMode sent roared from all six stands with the right bed:** Start 1.0 → `roar cheer amp=1.00 hold=4.7 s clip=cheer3 stands=6` and 3 voices; TakenTheLead 0.25 (halved, announcer speaking) → `roar cheer amp=0.51 hold=2.8 s`, 1 voice (twice in the match); the test-hook death, Disapprove 0.5 → `roar angry amp=0.68 hold=5.0 s clip=angry3`, 2 voices; LostTheLead 0.15 (halved) → `roar angry amp=0.45`, hold 1.5–3.3 s, 1 voice, six times over eight minutes. 10 reactions sent, 10 played, 10 roars, nothing dropped by MapPort's cooldowns and nothing by ArenaMode's new rules (no two moods fell within a second; the test-hook fire did not score an own kill this time, so the trade case did not occur in the game). Release on leaving (`reason=native stop, sent=10`), `excitementPeak=0` (no end in sight). |

No ArenaMode and no ArenaMapPort warning in the whole process apart from ArenaMode's pre-existing
startup note about the unreviewed PitFireTeam 1.1.0 (present on every start since that mod's
update, unrelated to the crowd). The scenario's own two failed steps are the known traps of the
runner, not of the mod: `wait logContains` for the Start line counts from the wait's start and the
line was already written (the `expect ... sinceStep` for the roar found it), and 25 team kills do
not happen in 7 minutes of 4v4 (the result is covered by the Finish run below).

Amplitudes as computed by MapPort match the table in the review (`0.35 + 0.65 * strength`): 1.00,
0.51, 0.68, 0.45. The hold follows the voices' length (a single 0.15 lament 1.5 to 3.3 s, the
2-voice death 5.0 s, the 3-voice Start 4.7 s).

**Listening note (not changed):** in a 4v4 the lead changes hands often, and every LostTheLead
now boos from all six stands (0.45 for 1.5 to 3 s, plus 2 s release), six times in eight minutes
here, against two cheers for a lead taken (both halved as well, 0.51). Whether a lost lead should roar at all, or
only the own death should, is a tuning question for the ear; MapPort decides the roar by kind, so
ArenaMode could only withhold the reaction (voice and roar together).

### Finish run (scenario `crowd-finish.json`, 17:28–17:42)

| Run | Result |
|---|---|
| Team DM, Bowl, time limit 5 min, 17:28–17:34 | Start → `roar cheer amp=1.00 hold=3.1 s`; two TakenTheLead (0.25 → cheer 0.51) and two LostTheLead (0.15 → angry 0.45); the clock's `Draw` → **Finish 1.0 → `roar cheer amp=1.00 hold=3.2 s clip=cheer3 stands=6`**, 3 voices; detached after the result hold (`reason=match or raid no longer current, reactions=6`), `excitementPeak=0.6` (the last minute). |
| Deathmatch, Equator, 3 rounds of 2 min, 17:35–17:42 | `6 roar beds, 4 stands` on Equator. Start per round (three times, `source=deathmatch round start`) → `roar cheer amp=1.00`, hold 2.7 to 4.9 s; a Finish for each of the two non-terminal round ends and one for the match result (`Draw`) → `roar cheer amp=1.00` each; one LostTheLead per round (angry 0.45). 9 reactions sent, 9 played, 9 roars; `excitementPeak=0.85` (final round and its clock). MapPort relaxed its repetition rules for two of the Starts (`rules …` in its line: the thin Start pool, as noted on 2026-11-02). |

Across both scenarios: **25 reactions sent, 25 played by MapPort, 25 roars from every stand, the
bed always the right one for the kind (cheer for Start, Finish and Approve, angry for
Disapprove), no drop by MapPort's cooldowns, no drop by ArenaMode's one-mood rule or mood
window** (no two opposite moods fell within a second in these matches; the rules were exercised
offline only, 1,340 assertions and 61 mutants). No ArenaMode warning in the whole process; one
ArenaMapPort warning unrelated to the crowd (`[Physics] impact sound skipped for
SoundType=CardBoard ('Pizza_4_Physic')`, a prop without an impact bank, known from the earlier run
with `Books`). The scenario's `no-mapport-warning` step failed on that line and its cleanup leave
failed because the match had already ended (the result screens were walked with `gpu-run menu`).

## Settings put back

The scenarios change ArenaMode match settings through the menu. Put back afterwards:
`Match/GameMode` domination, `Team Deathmatch/KillLimit` 50, `TimeLimitSeconds` 600,
`Deathmatch/KillLimit` 0, `Rounds` 0, `TimeLimitSeconds` 0 (`uibridge_config_set`), and the hidden
Equator solo participants 8 → 6 through a short Deathmatch raid with `--participants 6` and a leave,
as on 2026-11-02. ArenaMapPort's keys were not touched (its developer mode is on on that host by
its own session's choice).

## Not covered

- Nothing listened to: the roar level, the mood window and the question above are for the ear.
- The trade case (own kill and own death within a second) did not occur in the game; the rules
  that handle it are proven offline only.
- Gun Game, Tactical, Domination and the spectator camera with the crowd remain unrun, as before.
