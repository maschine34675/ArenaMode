# GPU-2 follow-up test — 2026-10-08 evening

This supersedes the connection limitation in `FOLLOWUPS-20261008.md`. GPU-2
(`192.168.178.109`, profile `SPT-GPU-2`) was reachable through SMB and Windows-MCP;
EFT was not running. A missing UiBridge endpoint did not establish that the host
was unavailable. `gpu-run start` launched the game normally. Its short Windows-MCP
connection deadline is another possible source of a misleading cold-start failure.

## Initial native run

- Loaded source `90fbf3aae2fa32fd7c789283db33bb9e7b36f02a`, client SHA256
  `4C9F7C5961A91A8C8ED540DFC721BE05E6E1AE0F8F333DEDA88BF9BA72034B02`.
- UiBridge `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`, EFT40743,
  instance `c0d3d8b275fe4ff1bf9fac5972f75393`; all 21 compared plugins matched
  development output, GPU disk and loaded process. The louder WAV matched
  `E0585D79CD137AC9240A01C79048490EC87F0C2D07F9B84B9BA24F5B5D5CB171`.
- Factory: Zombie Own Gear, Deathmatch and Gun Game. UiBridge hooks temporarily
  enabled godmode for DM/Gun Game and used safe movement/teleport and fire inputs.
  Godmode ended on leaving the raid. No ordinary PMC or PFT raid was run.

### Preview

Confirmation and deployment both showed the native own-equipment avatar for
Zombie Own Gear. This USEC profile had no equipped weapon/armor: an unarmored,
empty-handed avatar was therefore correct. The provided-equipment Deathmatch
flow showed the Arena representative with rifle, vest and helmet on both screens.
This is a representative mannequin, not a promise of the later randomly chosen kit.
Screenshots and inspector receipts are `own-confirmation`, `own-loading`,
`provided-confirmation`, `provided-loading` under the evidence directory.

Limits: the own-loading inspector preceded arming; later frozen-arm refresh,
nonempty personal equipment, protected PFT5v5, teammate selection and reconnect
were not separately certified. A stale inspector handle during provided loading
did not affect the visible model, which was captured separately.

### Akimbo

Actual test weapon was **PM(t)**, Auto arms update. Requested fixed APB/Glock rung
IDs belonged to the static catalogue, not the server's current generated ladder;
the mode correctly warned and used its generated ladder. This is not APB/Glock
native acceptance.

Six trigger inputs produced 3 right and 3 left shots and consumed 3 cartridges
(magazine 8 to 5, chamber still 1). Sixteen screenshots while walking sideways
with bot tracking showed models attached to their hands in the inspected samples;
no separation comparable to the user's screenshot was visible. Separate fire
frames showed alternating left/right flash and recoil. Reload returned to idle;
Akimbo OFF restored all 24 sampled native renderer flags, and ON rebound the view.
The attempted knife switch left the same firearm active (no knife equipped),
so it is not evidence of a knife transition. Manual arms, APB/APS and other pistols
remain outside this run; no continuous video or zero-latency measurement claimed.

### Pickups: native failure found

All six identities spawned and remained distinct. During more than five minutes,
including collection and cooldown of Jump Boots, no position changed. The initial
placement had repaired one authored point by 1.5m; all six safe occupied positions
remained reserved. A saturated set of usable positions is the supported working
explanation, not a measured per-candidate rejection diagnosis of the old build.

Two local collections requested the louder clip successfully. Runtime inspection
showed the clip loaded, an enabled/unmuted nonspatial source at volume 1 routed to
Master. Polling missed the short playback window and one inspector handle expired;
this initial run does not certify perceived loudness or listening acceptance.

The follow-up fixes rotation with a validated exchange when no free candidate is
available. Both appearances must be eligible; reciprocal authored positions and
actual floors are revalidated. Both new views are prepared before committing the
exchange, and both appearance tokens/fresh-entry guards change. Preparing,
pending or uncertain weapon transfers remain excluded. See `PICKUPS-REVIEW.md`.

## Initial cleanup and log audit

All temporary settings restored without conflicts. Saved Arena config is byte
identical to the original (`0A44F6891C84CC24BEEE6A6DA92D631C214EB06513D938231DF7535F03F06609`).
Whole-profile comparison after DM and after Gun Game found only
`characters.pmc.Health.UpdateTime` and `characters.pmc.Hideout.sptUpdateLastRunTimestamp`
changed; every other value, object and array matched. These are bookkeeping times,
not an inventory/progression difference. EFT was closed normally before deployment.

Log coverage seq 1–4491 contains no Error/Fatal. Four genuine NRE reports at Warning
level occurred in the earlier Zombie melee/death flow (`BotMemory.GoalTargetBeingHitAction`);
they are distinct from seven intentional RespawnApiProbe exceptions. No subsequent
DM/Gun Game exception was reported. Several bot pickup objectives ended very quickly;
their generic release reason does not establish why. These observations were not
silently fixed or attributed to the five follow-ups.

Raw evidence (ignored): `artifacts/arena-followups-oct8-native/`, including screenshots,
state receipts, `before-fix-log.json`, full-profile diffs and independent audit reports.

## Final pickup retest — accepted with safe retention

Fix source `1b08b84bc3390235c1d4fbc5c66afee962bf07fa`; client ProductVersion
`0.1.0+1b08b84bc3390235c1d4fbc5c66afee962bf07fa`, SHA256
`E673D20666AD49FEA22A16A1F73B49C6C5F17A69B862B9918A19824FB6F17E77`.
Staged build: zero errors, four existing CS8632 warnings. Parent reran the current
410-assertion/33-fault pickup suite and 545-assertion/12-fault objective suite.
Two independent adversarial reviews found no remaining concrete blocker.
Local and GPU2 disk receipts matched; restarted instance
`e6074c4a2cbd4b9e877896ef9a30687d` reported 21/21 loaded files identical.
Server/Core/BigBrain and the louder WAV did not change.

Factory Deathmatch, same round, approximately seven minutes:

- Bot collection was temporarily disabled to keep available appearances observable.
  All six distinct IDs/types survived 34 logged moves from 18:29:45.512Z through
  18:35:45.748Z. Damage/shield/boots/ammo each moved six times, weapon/health five.
  Every stored snapshot retained six positions with minimum separation over 13m.
- Unreachable/obstructed destinations caused retention rather than forced placement.
  Newly added diagnostics identified those refusals; later checks resumed rotation.
- Collected the M60 **after multiple rotations**, then equipped it through the
  native second-primary command: M60E6 with 100 M993 rounds. The appearance cooled
  for 45 seconds. No safe free/eligible exchange existed at rearm, so it initially
  returned at the same position, then moved on its next eligible interval.
  This is not a promise of a new position on every cooldown under every obstruction.
- Audio probe captured four samples of the real source actively playing, enabled,
  unmuted, volume 1, routed to Master, following that collection. The deployed asset
  is the verified +12dB WAV. Perceived loudness through the user's audio setup was
  not auditioned. UiBridge godmode deliberately blocks pickup eligibility; it was
  disabled before the successful native collection.
- Final log seq 1–3491 includes raid exit. No Arena warnings/errors/NRE in this
  retest. Its unrelated startup warnings and intentional probe are not new pickup
  failures. See the independent final audit for the classified list.

All three temporary retest settings restored with zero conflicts; final config
is byte-identical to the original. Whole-profile comparison again differs only
at the two bookkeeping timestamps listed above, including a final comparison
after quitting EFT. The collected M60 did not survive the protected raid exit.
EFT was exited normally after the run.

Native acceptance is bounded to Factory DM, sampled PM(t) movement and the two
preview variants described above. PFT5v5, Factory-night spawn traversal, all pistol
models/Manual arms and perceptual audio balance remain separate user playtests.
No push performed. The unrelated untracked Molotov assets were not changed.

Final evidence: `final-log.json`, `rotation-before/cycle1/cycle2/rearmed.json`,
`audio-final-collect.json`, `m60-collected.png`, `profile-final.json`,
`config-final.cfg`, `status-final-build.json` and `independent-final-native-audit.md`
under `artifacts/arena-followups-oct8-native/`.
