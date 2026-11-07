# Crowd reactions: first native run with ArenaMapPort phase A — 2026-11-02

Host: SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`), test profile `SPT-GPU-2`, 1920×1080, SPT 4.1.6.
Driven from this machine with UiBridge 0.9.7 and gpu-run (UiBridge `main` `1f0a7ae`); the
scenario files are kept in `tools/CrowdReactionChecks/scenarios/`. Contract and decisions:
[CROWD-REACTIONS-20261102.md](../../reviews/CROWD-REACTIONS-20261102.md). Raw evidence (scenario
reports, log exports, stills) is under the ignored `artifacts/crowd-reactions-20261102/gpu/`.

## Build and delivery

`dotnet build ArenaMode.sln -c Release` of `feature/crowd-reactions` at `8d21e362`, with the
Redeploy checkout detached at `3c41bd6` so the client stamps and ships the installed Core 1.0.19
(Redeploy `main` carries another session's unreleased Core 1.0.20); the installed Core bytes were
backed up before the build and copied back afterwards. Then `gpu-run deploy-wait --host host2
--server --game` (72 files identical on the host, server and game restarted, "build 16/16
identical": VM build = host disk = loaded process).

| Artifact | Version | SHA-256 |
|---|---|---|
| ArenaMode client | `0.4.1+8d21e362` | `D1154BB331C925F94A07E2E9EAE27E128D89883649621189CE0082511A77A74F` |
| BigBrain companion | `0.4.1+8d21e362` | `22E28849BA85EB0B04343318C91E429FB6A5944DE913EB49B6ADEEDA44524C51` |
| ArenaMode server | `0.4.1+8d21e362` | `D6FE8E6955F438EEADB4107FDD6A53A4742E51F6CC3FF7CD63084076F1326024` |
| Redeploy.Core (unchanged) | `1.0.19+3c41bd6` | `692243668D565FA89ADAFCFAB8F2E472A002C66522CA34E9105D1F2E3B073619` |
| ArenaMapPort client (phase A, installed by the MapPort session) | `0.4.0+08964c1` (phase A work before its commit `d70acd2`) | `B6DF4A1D1F64F2F245A7B37F60FFC274681F959F6DBEAC970C55A33777AE8521` |

ArenaMapPort's developer mode was on, so its `[SpectatorAudio] <map>: reaction <Kind> strength=…,
N voice(s) [...]` line confirms each reaction it actually played; ArenaMode's own lines are
`Arena crowd …` at Debug.

## Results

| Run | Result |
|---|---|
| Team DM, Bowl, 4v4, 25 team kills (two runs; left after 8.2 min, no result) | Attach with `excitement=bound, reactions=bound`; claim on the first attempt, 0.3–0.4 s after the attach, `kinds=0x3E`. Start at Live (`source=RoundStart`): MapPort played 3 voices. An own headshot kill (godmode, aim tracking, test-hook fire): Approve 0.3 (HEADSHOT 0.6 halved, announcer speaking), 1 voice. The test-hook death: Disapprove 0.5 (`source=death`), 2 voices. Lead changes: TakenTheLead Approve 0.25 and LostTheLead Disapprove 0.15 (both halved), 1 voice each; one LostTheLead 1 s after the death was dropped by MapPort's cooldown. Leaving released the claim (`reason=native stop`). `excitementPeak=0`: correct, the match never came within five kills or 60 s of its end. |
| Team DM, Bowl, 4v4, time limit 5 min | `Team Deathmatch result: Won; Time limit reached; BEAR 12:10 USEC` → Finish 1.0 (`source=MatchWon`), 3 voices; detached after the result hold (`reason=match or raid no longer current`), `excitementPeak=0.6` (the clock's last minute). |
| Deathmatch, Equator, 3 rounds of 2 min, 10 kills per round | Start per round from `OnRoundStarted` (rounds 1–3), a Finish for each non-terminal round end (`source=deathmatch round end`, rounds 1 and 2), and exactly one Finish for the match result (`source=Draw`), no second one for the terminal round end; 3 voices each; `excitementPeak=0.85` (final round and its clock). |
| Iceberg, Deathmatch | Attached with both parts bound; MapPort reports no kinds there, so no claim, no reaction, `excitementPeak=0`. |
| Bowl, Zombies | `Arena crowd off: Zombies has no crowd reactions or murmur intensity.`; no claim. |

Every reaction ArenaMode logged as sent appeared as played in MapPort's log (or, once, as dropped
by MapPort's cooldown); no reaction played that ArenaMode had not sent. No ArenaMode warning in any
run. One ArenaMapPort warning in the Deathmatch run is unrelated to the crowd (`[Physics] impact
sound skipped for SoundType=Books`).

## Observations for the listening check (not changed)

- **The announcer interlock halves nearly every announced event.** Lead changes, HEADSHOT and the
  award callouts all trigger an announcer line in the same frame, so `IsPmcVoiceBlocked` is true
  when the crowd flushes: lead reactions and kill reactions arrive at 0.15–0.3 (1 voice), and an
  award (0.9) would arrive at 0.45, below MapPort's 0.8 band for the strong-only shouts, so those
  shouts never play while the announcer is on. Only the own death (no announcer line) kept its
  full 0.5. Whether that is wanted (the crowd stays under the announcer) or the interlock should
  weaken less, delay, or spare awards is a listening decision.
- **Start at 1.0 exhausts MapPort's thin Start pool.** MapPort logged `rules relaxed to level 3–5`
  for consecutive Starts (8 clips, 4 voice families, 3 voices each); the plan already suggests
  considering Start 0.6 (2 voices).

## Configuration and limits

The scenarios changed ArenaMode match settings (game mode, Team DM and Deathmatch limits, the
Equator solo participant count); all were set back to their earlier values afterwards (game mode
Domination, Team DM 50 kills / 600 s, Deathmatch rules off, Equator solo 6). No whole-profile
comparison was made: the feature touches no profile state, and this run is evidence for the crowd
presentation only. Nothing was listened to; where the AudioListener sits in the death replay and
the team spectator camera was not examined (MapPort's host item). The installed ArenaMode on all
Syncthing hosts is now this development build (`0.4.1+8d21e362`) instead of the master build
`0.4.1+73e0c773`.
