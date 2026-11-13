# Pre-release polish, native runs - 2026-11-13

Host: SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`), test profile `SPT-GPU-2`
(`6ab7db1c...`), 1920x1080, SPT 4.1.6, EFT 0.16.9.5. Driven from this machine
through UiBridge `0.9.9+92b998a` and gpu-run of UiBridge `main` (`adb65bd`). Branch
under test: `feature/pre-release-polish`. Contract for the Domination maps:
[DOMINATION-VANILLA-MAPS-20261113.md](../../reviews/DOMINATION-VANILLA-MAPS-20261113.md).
Raw evidence (frame captures of the kit window, stills, crops) is under the ignored
`artifacts/prerelease-20261113/` of the primary checkout. All times are host local
time (UTC+01:00); the runs on the branch builds lasted from 19:07 to 19:56, the
capture of the installed `master` build was taken at 16:52 and the backup at 19:03.

The five requests of the user this run answers: the KITS window's BACK button
reads DONE; the buttons of a selected kit no longer flash; Domination on every
vanilla map (the Labyrinth with one point); the bots' spawn protection in
Domination as in Team Deathmatch; the point letter more visible on vanilla maps
and gone on the ported maps.

Installed beside the builds and unchanged by this work: Redeploy.Core
`1.0.23+f092f98` (`c8fe330d...`, hash compared after the builds), Session helper
and Gate `0.5.0+c64846fb`, ArenaMapPort `0.5.0+160523d`, PitFireTeam `1.1.1`,
UiBridge as above.

## Builds

Both were built from the branch with `tools/AirPitChecks/NoDeploy.targets`, the
three stamps were checked, client, BigBrainCompat and server DLL were copied into
`D:\SPT41` and rolled out with `gpu-run deploy-wait --host host2 --server`
(identical on the host after 48 s and 50 s). The files of the `master` build
`0.5.0+c64846fb` installed before are in
`D:\SPT41-Backups\prerelease-test\20261113-190307`.

| Build | Commit | Client SHA-256 | Used for |
|---|---|---|---|
| base | `c64846fb` (the deployed `master`) | `d7a9380b5991852e62e880d885bef0f79ce7eff292576f0b7a9ffcbd37028724` | the kit-window capture before the fix (16:52) |
| A | `c5b1ed96` | `b9ff7fb1b3c88b24a532da3214a9164049fd084ff211215c312171b96e9c6864` | kit window, Domination on Factory, Chop Shop, Equator, The Lab, The Labyrinth and the three Customs areas |
| B | `a7965541` (A plus the letter's face dilate, `DominationPointView.cs` only) | `5cafa874ab427396fdba1ecf57f16524854e55e117d05daed801bb63f190de5e` | the letter again in Industrial Center, Team DM in Customs Yard, Domination on Chop Shop |

| Build | BigBrainCompat SHA-256 | Server SHA-256 |
|---|---|---|
| A | `387c691e601d1415fd76a3fd4c5539ae9cbc1d6a383f528e5e347f5d8dc2e503` | `ad613da4bc0d71861327094cb269712bd7a6cf5d0714aaa276570ce0781a9bae` |
| B | `50364ec12f12b08d6de1a5d9941621b762dda9d489526481e1049c380eb7da02` | `2e3894024dd25d393b76056166dc38b8d818b944aaaf6344aaa67eacadc6c989` |

`uibridge_status` in the last raid on build B: 16 of 16 `maschine-*` plugins and
both libraries identical (VM build = host disk = loaded), 40 plugins loaded. The
BigBrainCompat and server sources are the same in A and B; their hashes differ
through the commit stamp in `ProductVersion` alone. The commits after `a7965541`
change harnesses and documents only.

## Kit window

- The top-right button of the KITS window reads **DONE** (snapshot, build A); the
  editor keeps its own DONE, which returns to the cards.
- The flash, before: on the installed `master` build a click on the "Drifter"
  card rebuilt the detail pane's COPY TO MY KITS button light grey at the click
  frame, and it darkened over the next five frames (capture
  `20261113-165204-kit-flash-before`, frames 16 to 21, mean brightness of the pane
  falling frame by frame).
- After (build A, same click, capture `20261113-190805-kit-flash-after`, 258
  frames): the pane changes in one frame (frame 16) and every later frame is
  pixel-identical to it; the button is dark from its first frame.
- A custom kit (three buttons: COPY TO MY KITS, EDIT, DELETE): one change at the
  click frame and one in the following frame confined to the item icon row
  (x 1261 to 1534, y 682 to 742, icons arriving), nothing in the button row.
  Opening the editor: the press colour of EDIT, the editor in one frame, then four
  frames of changes confined to the slot icons on the left (x 226 to 329); no
  button fades in.
- Not seen natively: a card or the Tactical NO PURCHASE button built unusable
  (the profile can afford every kit, and no Tactical raid was part of this run).
  Those two sites use the same helper as the buttons above and have no offline pin.

## Bot spawn protection in Domination (ported box maps)

- Chop Shop, build A: while the banner read ASSEMBLING TEAMS the team bots had no
  outline; at 00:01 of the live match the bot next to the player carried the
  white shield outline (stills `dom-chopshop-shield-1`, `-2`). Same picture on
  build B at 00:02 (`dom2-chopshop-shield-live`). Before the fix the shield view
  could not appear in Domination at all: its phase read was the Team Deathmatch
  kill policy, which is null there.
- Equator, build A, friendly fire ON, weapon HK 416A5 full auto, aim tracked on
  the head through UiBridge:
  - A replacement USEC bot arrived at 19:21:08.78 in the team box 6.5 m from the
    player (`Equator team arrival: ..., USEC, arena-equator-tdm-v1-north-01`).
    Tracking started at 19:21:10.74, fifteen rounds were fired between about
    19:21:11.6 and 19:21:12.6, that is 2.8 to 3.8 s after the arrival and inside
    the four seconds of `Respawn / SpawnProtectionSeconds`. The bot was alive and
    moving afterwards; the still taken 0.2 s after the burst shows its outline.
  - The same burst on a team bot that had been alive for a minute killed it
    (4.5 m), and so did a burst on an opposing bot standing in the box (2.9 m).
  - At the match start a first burst of fifteen rounds 3.2 to 4.2 s after
    `Countdown -> Live` left the tracked team bot alive (12 m); that one alone
    would not have been conclusive, because the last rounds fell on the edge of
    the lease.
- Vanilla maps have no bot spawn shield in either mode; that rule is unchanged
  and was not tested here.

## The point letter

- Ported maps: on Chop Shop the point is the ring (and the map's own marker)
  only (`dom-chopshop-point-noletter-5m`, two bots capturing inside the red
  ring). On build B the scene holds `Arena Domination point X` and no object
  named `Control point label` or `Control point chip`.
- Vanilla maps, build A: the chip in the owner's colour and the constant size
  work as designed (Factory at 10 m and 20 m, The Lab at 6 m, The Labyrinth at
  8 m, Industrial Center at 46 m and 86 m in one picture; neutral grey, USEC blue,
  BEAR red). The glyph did not: it rendered as a black letter with a white
  hairline. TMP centres the outline band on the glyph edge, and the font the HUD
  uses there is thin (`Arial - Normal SDF`, `boldStyle` 0.5, shader
  `TextMeshPro/Distance Field`, read from the live label with the inspector), so
  an outline of 0.2 ate the face from inside.
- Build B (`_FaceDilate` 0.3 on the label's own material copy, outline 0.2,
  padding recomputed): a bold near-white letter with a black edge, on a neutral
  chip at 46 m and 86 m (`dom2-customs-center-B-46m`) and on a USEC-blue chip at
  11 m with its blue ring (`dom2-customs-center-A-11m`).
- The letter keeps scene depth: a bush and several walls hid it in the survey
  positions, as intended. The marker is not shown before the match is live.

## Domination on the new maps (build A unless noted)

Every start logged its proof line before the countdown ended, no alternate was
needed anywhere, no `Arena match start failed`, no ArenaMode warning or error
in any raid, pickups 6 of 6 in every round 1.

| Map / area | `Domination points verified` | Observed |
|---|---|---|
| The Lab (8v8) | `A=(-224.879, 0.071, -279.867); B=(-171.540, 0.011, -312.180); C=(-213.124, 0.026, -378.755)` | HUD row A/B/C; USEC took A at 17 s, C at 23 s, B at 54 s, BEAR took A at 92 s; three pickup candidates refused for the control point clearance; no route refusal, no deferred spawn |
| The Labyrinth (4v4) | `X=(-9.920, 0.042, 23.920)` | one point; BEAR took X 11 s after the start; about a minute and a half in, a USEC bot on the objective layer was 4 m from the point; pickups 6/6 (one candidate refused for the control point clearance) |
| Industrial Center (4v4) | `A=(355.000, 1.109, -80.000); B=(395.000, 1.082, -60.000); C=(435.000, 1.084, -75.000)` | `Arena area armed: customs-factory-center-polygon-v2; mode=domination, verified start capacity 4 per faction`, READY, `Domination objective routes from both area team starts verified.`; A and B USEC, C BEAR after 26 s. Again on build B: same three points, same lines, A USEC and C BEAR after 12 s |
| Industrial Side (4v4) | `A=(588.000, 1.402, -64.400); B=(590.000, -0.602, -100.000); C=(600.000, 1.153, -125.000)` | armed, READY, routes verified; C USEC at 17 s, A BEAR at 35 s; B (the lower strip, its primary) still neutral after about 75 s, with bots of both sides on the objective layer |
| Customs Yard (4v4) | `A=(-204.300, 0.856, -77.900); B=(-185.000, 0.870, -75.000); C=(-165.000, 0.784, -75.000)` | armed, READY, routes verified; A BEAR 9 s after the start |

Factory (random three of eight) and the ported maps start as before: Factory
`A=(-32.087, -2.669, 29.973); B=(-5.890, 0.031, -27.460); C=(-6.770, 0.031, 10.010)`,
Chop Shop `X=(-369.290, -3.805, 499.490)`, Equator with
`Domination objective routes from both opened team boxes verified.`

Menu, build A: with Domination selected all ten map tiles are offered (The Lab,
The Labyrinth and Customs among them); Customs shows exactly the three area rows
and the 2v2 to 5v5 row, no whole-map row; the description reads
`USEC versus BEAR.` and, on its second line, `Hold 3 points to earn team score.`
on The Lab and in the Customs areas and `Hold 1 point to earn team score.` on The
Labyrinth; the title of the area map preview reads `CUSTOMS — Industrial Center`.

Team Deathmatch in Customs Yard on build B (3v3), as the regression of the
widened mode checks: `Arena area armed: customs-yard-polygon-v1; mode=teamdeathmatch,
verified start capacity 3 per faction`, READY, `Team Deathmatch ready: 3v3`, live
after the countdown, no warning.

## Acceptance bookkeeping

- Whole-profile comparison of `SPT-GPU-2` before the first raid and after the
  server stop: 5400 leaves, two differences, `Health/UpdateTime` and
  `Hideout/sptUpdateLastRunTimestamp`. The other two profiles are byte-identical.
- The 61 files of `user/mods/pitFireTeam-ServerMod` are byte-identical.
- `com.maschine.ArenaMode.cfg`: the menu clicks of the run had changed
  `RandomKit` (true to false) and `AreaPlayersPerTeam` (5 to 3); the area and team
  size of the Customs raids were set with `persist=false` and never reached the
  file. The copy taken before the run was put back with the game closed and is
  byte-identical on the VM and on GPU-2. The newer build had also rewritten two
  setting descriptions (they now name Domination); the restored file carries the
  old text until that build writes the file again.
- EFT was closed and the GPU-2 server stopped at the end.

## Not run, and what the tools could not do

- Build B repeated Industrial Center, Chop Shop and the Team DM regression only;
  The Lab, The Labyrinth, Industrial Side, Customs Yard, Factory and Equator ran
  on build A, which differs from B in the letter's material alone.
- No match was played to its score or time limit on the new maps, nothing with
  PitFireTeam (refused there by rule), no rewards payment, nothing on GPU-1.
- Bot behaviour on The Lab's other floors and through its doors, and the 160 m
  route limit from the far starts of the Customs areas, were not measured beyond
  "no refusal line in the first one to two minutes".
- The material count over several raids (the freed font material clone) was not
  measured.
- gpu-run starts Domination on The Lab and The Labyrinth with `--map-from-menu`.
  For Customs it stops at its own readback (`map info shows "nothing", expected
  "CUSTOMS"`), and its mode table still refuses Domination there; the three
  area raids were started with single clicks (`CUSTOMS`, the area row, the size,
  NEXT, READY). The table and the readback belong to UiBridge.
