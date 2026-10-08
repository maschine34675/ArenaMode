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
  instance `c0d3d8b275fe4ff1bf9fac5972f75393`; all21 compared plugins matched
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

Six trigger inputs produced3 right and3 left shots and consumed3 cartridges
(magazine8 to5, chamber still1). Sixteen screenshots while walking sideways
with bot tracking showed models attached to their hands in the inspected samples;
no separation comparable to the user's screenshot was visible. Separate fire
frames showed alternating left/right flash and recoil. Reload returned to idle;
Akimbo OFF restored all24 sampled native renderer flags, and ON rebound the view.
The attempted knife switch left the same firearm active (no knife equipped),
so it is not evidence of a knife transition. Manual arms, APB/APS and other pistols
remain outside this run; no continuous video or zero-latency measurement claimed.

### Pickups: native failure found

All six identities spawned and remained distinct. During more than five minutes,
including collection and cooldown of Jump Boots, no position changed. The initial
placement had repaired one authored point by1.5m; all six safe occupied positions
remained reserved. A saturated set of usable positions is the supported working
explanation, not a measured per-candidate rejection diagnosis of the old build.

Two local collections requested the louder clip successfully. Runtime inspection
showed the clip loaded, an enabled/unmuted nonspatial source at volume1 routed to
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

Log coverage seq1–4491 contains no Error/Fatal. Four genuine NRE reports at Warning
level occurred in the earlier Zombie melee/death flow (`BotMemory.GoalTargetBeingHitAction`);
they are distinct from seven intentional RespawnApiProbe exceptions. No subsequent
DM/Gun Game exception was reported. Several bot pickup objectives ended very quickly;
their generic release reason does not establish why. These observations were not
silently fixed or attributed to the five follow-ups.

Raw evidence (ignored): `artifacts/arena-followups-oct8-native/`, including screenshots,
state receipts, `before-fix-log.json`, full-profile diffs and independent audit reports.
This report will be completed with the rebuilt pickup rotation retest below.
