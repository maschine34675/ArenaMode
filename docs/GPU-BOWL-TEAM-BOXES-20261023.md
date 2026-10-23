# Bowl original team boxes: GPU-2 acceptance

Date: 2026-10-23. Host: SPT-GPU-2. Test profile: SPT-GPU-2.
Implementation: `314c7f50bec105e1f74519c5a5177c684a3e32f6`.
Scope: independent Team Deathmatch only; other modes keep generic placement.
See [contract and offline checks](BOWL-TEAM-BOXES-20261023.md).

## Build and delivery

Client, BigBrain companion and server were built in Release after the commit,
then deployed to `D:/SPT41` and GPU-2's `C:/SPT41`. Game and server were stopped
before replacement, then started through gpu-run. Server log starts at
08:54:04 local time and records ArenaMode loading; the running client and
companion report `0.1.3+314c7f50bec105e1f74519c5a5177c684a3e32f6`.
Final UiBridge comparison: all 16 plugins and both libraries identical
across local installation, GPU disk and loaded game modules.

| Artifact | SHA-256 |
|---|---|
| Client | `BA05B7BBC4EAF054985E9C37CF8A7D0A7F1813236FBDBF3F8018C89C0E64ABD5` |
| BigBrainCompat | `C0FA7428F1DEE19C3B3E5201AAE35EB272AA70578958F173BBC3FA5A0000473A` |
| Server | `686B6C677E5CBF122D780595CFB65747A7954F529E57DA50299B362B342D7049` |

MapPort stayed at `0.3.0+0dd0b2a`, Redeploy.Core at `1.0.8+d1d7240`,
Killcam at `1.0.1+17e19cb`, UiBridge at `0.9.4+42bbfb3`.
The delivery verifier checked three matching built/deployed artifacts and
32 unchanged protected files, including Bowl map data/assets, MapPort config,
MapPort client, UiBridge and Core. No map assets were rebuilt.

## Native results

| Run | Evidence and result |
|---|---|
| TDM 4v4 | Local USEC east-05; 3 USEC and 4 BEAR admitted in their correct pockets. Closed countdown visible. All four MapPort animations finished at 09:02:44.400; all twenty paths revalidated and Live at 44.909. Both factions subsequently in the field. |
| Player respawn | UiBridge death hook after walking out; Redeploy death pipeline completed in 48.2 ms. Respawn at east-04, arrival confirmed 09:03:29.679. No new hold/re-close. |
| TDM 10v10 | Native menu shows 20 (10v10); 9 USEC + 10 BEAR simultaneously Active plus player. Hold inspection true with 28 hold carvers. All four animations finished 09:06:33.148; path validation and Live at 33.726. |
| Open-state inspection | After 10v10 release: `_opening=false`, zero hold carvers, zero pose fallbacks, four posed parts. Logs confirm 24 park carvers retained. All twenty complete paths checked by production code. Private fields were read only for test evidence, never by production integration. |
| Departures | Both factions appear well outside their pockets in later position snapshots. Player walks 67.7 m along the NavMesh to the verified field point; saved walk #2 result is `arrived`, 0.9 m short. Bot combat and replacement spawning continue. A later snapshot has 18 Active + 1 PreActive replacement; it is not evidence of 19 simultaneously Active. |
| Non-team DM control | No hold, zero hold carvers, `ArenaTeamBoxRuntime.IsRequired=false`; native startup uses 21 ordinary markers instead of 41. Original gates park open at startup. Player walk #3 reaches field point, 1.0 m short. No private TDM placement or confinement. |
| Exits | All three runs left through native Disconnect and post-raid Next buttons, returning to MainMenu (19/20/21 seconds). Server confirms protected snapshot restoration/persistence. |

The 8-player run used daytime and the 20-player screenshot records the darker
time selection. No lighting settings were changed. These are bounded functional
tests, not long-session or performance certification. A BEAR local profile,
in-game cancellation before release, and PFT modes were not rerun; cancellation
and ordinary-raid isolation are covered by production-linked offline checks.

## Logs and preservation

Merged BepInEx/server/EFT export includes 158 warning/error entries across the
whole process session, not 158 team-box failures. No new box/gate exception,
readiness timeout or Arena/Redeploy/Killcam failure was found in these runs.
Native warnings remain: JetMaterial null during weapon pool setup (also before
Bowl load), Benelli missing foldable, unknown custom item type, missing scripts
including extractor_smoke, weather/season helpers, and ordinary marker/backend
reconciliation. These families predate this integration or belong to ported
scene initialization; this test does not prove their complete independence from
all Arena activity. See the earlier model-pool investigation. Private Bowl
markers had no reconciliation error. One DM `GetClosePoints error:16` is native
`BotCoversData.GetClosePoints` catching a cover/voxel lookup exception; 16 is
the bot ID, not an error code (`Development/_decompiled/BotCoversData.cs:253`).
It occurred with box rules disabled. No stack identifies the underlying failure,
so retain it as a separate cover-AI diagnostic follow-up, not a proven harmless
message and not evidence of a gate regression.

Test settings were restored: DeveloperMode false, GameMode gungame, Bowl TDM
participants eight. Only the intended admission migration remains different
from pre-launch config: revision 3 -> 4, Bowl appended to the recognized default
map list, and new solo/team Bowl preferences at eight. No complete config file
or profile was restored over user data.

Canonical test-profile comparison: all 315 inventory items and the entire
Inventory object unchanged; Skills, Stats, Quests, Achievements, encyclopedia
and all other PMC sections identical except two normal timestamps:
Health.UpdateTime and Hideout.sptUpdateLastRunTimestamp. Actual health values
are unchanged. Test hooks ended with the raids. Final state is MainMenu.

## Evidence and test-tool limitations

Local ignored evidence root: `artifacts/bowl-team-boxes-20261023/native/`.
Contains delivery.json/final-status.json, three run reports/screenshots,
opening/runtime/warning log exports, bot/player/walk snapshots and preservation
checks. Raw profile/config copies remain ignored and are not committed.

The first 8-player automation stopped after entering the raid because its
inspector reference used `/handle` instead of `/value/handle`; the raid itself
continued and was inspected manually. The corrected 20-player script completed
all 19 steps. The DM script passed its scope inspections, then its optional
godmode action met the same operator's still-active MCP lease; the original
session enabled it successfully and completed the walk/exit. These are recorded
tool-orchestration failures, not hidden or reported as passing scenario scripts.
The delivery checker also needed a PowerShell multiline-condition syntax fix;
its final run passes. No production change followed the tested delivery.
