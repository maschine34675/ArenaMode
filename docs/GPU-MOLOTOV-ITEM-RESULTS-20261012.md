# Dedicated Molotov native acceptance — 2026-10-12

**Completed bounded native test; combat acceptance remains partial.** Final
product build `4fd79815d6a8dcd15f4d06d4cf5e0d1543d0b998` supplied the dedicated
Molotov normally, selected it through native G after both supplied RGD-5s were
used, and produced ground fire from an actual throw. Settings were restored,
the protected profile audited and host-1 game/server stopped cleanly.

| Gate | Actual evidence / limit |
|---|---|
| Final server registration and normal kit supply | PASS on r3: registration before login, strict validator ready, 60 optional requests, actual Molotov in Spartan rig without insertion hook |
| Native G, bottle throw and ground fire | PASS on r3: supplied item selected and consumed through native grenade hands |
| Rendered inventory icon, bottle in hands, fire appearance | PASS as prototype on r2; unchanged client presentation code in r3. Final r3 inventory capture still has an icon-loading spinner and does not independently certify the rendered icon |
| Self-damage / respawn, normal RGD regression, disabled Molotov | PASS on r2's actual throws; only server registration/supply code changed for r3 |
| Bot escape | Actual enemy contact, route and return to SAIN observed on r2; no forced bot motion |
| Audio | Technical playback and Master routing observed on r2; acoustic quality/audition still OPEN |
| Enemy kill attribution, assist settlement, TDM FF OFF/ON, PFT | NOT ACCEPTED natively in this run; bounded throws did not establish the required isolated contacts/kills |
| Ordinary PMC control, staging/missing-companion native fallback | Not executed here; offline checks are not native acceptance |
| Persistence and cleanup | PASS: complete post-login profile differs only in Hideout clock; 422 inventory IDs and 33 PFT files preserved; no unexpected setting remains |

This testing leaf controlled GPU host 1 (`192.168.178.114`) and test profile
`SPT-GPU-1` only. The parent separately restarted idle host 2 to make its
bidirectionally synchronized installation consistent; no host-2 raid tests were
performed. PFT team tests were unavailable without creating persistent
companions for this profile; no such setup was performed. No profile JSON was
edited/restored, and no bot HP, bot position, synthetic hit or kill was injected.

Unless explicitly prefixed with `artifacts/`, relative evidence paths below
refer to the ignored local directory `artifacts/molotov-native/`. Its private
profile snapshots are intentionally not
committed. The sections below retain r1/r2 as historical evidence, followed by
the final r3 record.

## First candidate: native registration failure, no combat acceptance

Commit `7d97b85bd2919837b5efdb3c5c91312893d9f3dc` was started through UiBridge's
official gpu-run workflow. Its client and BigBrain compatibility assemblies were
verified both on disk and in the loaded process, not inferred from the filename.

| Assembly | SHA-256 | Loaded MVID |
|---|---|---|
| Client | `FF02E75A6DF1BB1B5A3FCA8C5898B130C9A10C203E4C9EC974D295DFD8E0708F` | `0cea5417-8e16-4813-912d-970e27373e6e` |
| BigBrain compatibility | `8DB884354D289CA432E11001EA77087B1960E504B3AAF6E82FF2271E1B877A44` | `ffc88972-25e2-4d98-a547-6f309f146681` |
| Server | `EFC3BD7172428153C72FA2E00C6442E23662EBC3855ABF05C99D25CE527DABF0` | Not exposed by UiBridge; exact disk assembly plus fresh server startup verified |

Process instance `e86955adcf80416a804a5ab577520b8d` started at 10:37:04 CEST.
UiBridge reported `0.8.0+1f62c6375543487c8c518f40369664029712fd4e`,
EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`. Build proof is in ignored
`artifacts/molotov-native/build-proof/report.json`; manifest and expected MVIDs
are retained beside it.

The first protected Factory Deathmatch used the fixed Spartan kit and two total
participants. Its inventory contained the two ordinary RGD-5 grenades but no
Molotov. A single protected-raid `giveItem` attempt using the exact dedicated
template `a12e00000000000000000001` was rejected as an unknown template; no item
was inserted. No native fire throw or damage gate was attempted on this candidate.

The server log at **08:36:29.361 UTC** identifies the failure:

> Molotov supply unavailable: items must be added before profiles load; lower
> the registering service's TypePriority to OnLoadOrder.Preload.

The actual installed SPT rejected the late registration. The server leaf owns
the load-order correction. The test immediately left through the protected native
exit flow with `--no-heal`, then restored settings and stopped both game and server.
Complete logs are preserved in ignored
`artifacts/molotov-native/server-first-complete.log` and `LogOutput-first.log`.

## Profile and settings audit of the first candidate

The complete test profile, 63 configuration files, 33 existing PFT companion
files and 84 installed DLL hashes were captured at 10:33:59 CEST before login.
No profile JSON was edited or restored. The test profile has no PFT companions;
the 33 files belonging to existing profiles were only hashed/copied for comparison.

The full pre-login → post-login diff had four paths:

- Chest HP: `72.51942857142858` → `85`.
- Health update timestamp: `1791737210` → `1791794247`.
- Hideout timestamp: `1791737398` → `1791794250`.
- A WTT-ContentBackport `2.0.5` mod-list entry, timestamp `1791794247`.

These changes were already present in the captured MainMenu profile before
entering Arena. The server recorded WTT loading at 08:36:17.407 UTC and
`/client/game/start` at 08:37:27.864 UTC. No healing purchase was made. The HP
change is consistent with native out-of-raid regeneration; the decisive boundary
evidence is that it predates Arena, rather than an assumption that all HP changes
are harmless.

Both complete comparisons **post-login → protected exit** and **post-login →
logout** contain exactly one difference: the Hideout timestamp advanced from
`1791794250` to `1791794550`. All remaining profile fields, including complete
inventory data, were unchanged. All **422 inventory IDs** remained present and
all **33 PFT files** remained byte-identical. Evidence:
`artifacts/molotov-native/first-post-login-diff.json` and checkpoint subfolders.

The config ledger restored five changed values, found one already equal and
reported zero conflicts or missing entries. The sole residual configuration
assignment is the new default `Arena incendiary / Enabled = true`. Godmode ended
with the raid; no walk, aim tracking or capture was left active. Game and server
were stopped at the end of this attempt.

Deployment was initiated on host 1, but bidirectional Syncthing also copied the
new Arena DLLs to the local installation and the client/compatibility DLLs to
host 2. Host 2's existing processes were not controlled or interrupted by this
test. This was not an isolated host-1-only disk deployment; see the parent's
corrected deployment receipt for that distinction.

## Historical r1 checkpoint: remaining native gates

Await the corrected pinned build and fresh processes. Confirm successful server
registration before entering another raid. The dedicated inventory icon, bottle
in hands, native quick-G selection, real fire throw, self/enemy/team damage,
attribution, bot escape, ordinary RGD-5 regression, disabled-item harmlessness,
ordinary PMC control and visual quality remain unaccepted at this checkpoint.
Offline tests or the earlier RGD-5 prototype do not substitute for those gates.

## Second candidate: native fire and remaining supply incompatibility

Commit `394e920ec2d3e8187865b66267fbcca756a7be21` was deployed by the parent and
started fresh. Server PID 4596 registered the private item at 08:52:21.094 UTC,
before profile loading at 08:52:24.285. The game process instance
`49294547fe6143599133ff6f0734cae2` started at 10:53:23 CEST. Exact loaded MVIDs:
client `34d192d7-ec4a-41da-bd59-d494050eb179`, compatibility
`e89b7efb-0aed-4e34-945b-97a4cb97843c`. Hashes and full three-way proof are in
`expected-build-r2.json`, `pinned-manifest-r2.json`, and `build-proof-r2/` under
the ignored artifact directory. All 19 reported plugins and Redeploy.Core
matched disk/loaded/local at the start.

The standard Spartan kit still supplied only two RGD-5s. A protected-raid
`giveItem` now successfully inserted the exact dedicated item, permitting real
native presentation and combat tests. This insertion is not proof of normal kit
supply. Read-only inspection of the held Molotov's actual template confirmed
`CanSellOnRagfair = true`, although `Strength = 0`. The server investigation
identified SVM's disabled BSG flea-list setting and SPT's subsequent bulk
sellability pass; the strict harmless-item validator therefore correctly refused
the optional kit request. The private native flea blacklist fix is a separate
server correction requiring another fresh server process.

Actual native observations from this candidate:

- Inventory: dedicated orange-label bottle icon, label Molotov, distinct from
  the two RGD-5s. Original captured frame
  `molotov-inventory-r2/frame-0000-f7237.png`.
- Hands: dedicated brown bottle and wick, using native grenade hands and native
  slot 4 selection. Original captured frame
  `molotov-hands-r2/frame-0000-f10105.png`. Native G still selected an RGD-5 in
  this mixed inventory; explicit Molotov selection through G was not accepted
  at this r2 checkpoint. It is established separately in r3 below.
- Actual underhand throw: native `TryLowThrow` then `FinishLowThrow`; first fire
  ignited at 10:57:15.468 with 24 floor cells. Captured native frames 12517–12766
  show the bottle throw and lit ground fire. `molotov-fire-r2.mp4` preserves
  their recorded timing, 250 frames / 2.967 seconds, without audio. This capture
  reached its configured frame cap; its `complete=false, reason=frame_limit`
  metadata is retained and it does not cover the full six-second lifetime.
- Ordinary RGD-5 after Molotov/pool use: native G selected RGD-5. Its real
  underhand throw at 10:57:50.623 caused native grenade self-death at
  10:57:54.558 followed by protected respawn. No new incendiary conversion or
  ignition occurred. No kill or health hook was used.
- Fire self-damage: another real Molotov ignited at 11:00:07.860 with godmode
  off. Before the throw, HP was 440. During contact, native readout showed
  Head 3/35, Chest 8/85, Stomach 6/70, arms 6/60 each, legs 6/65 each, followed
  by native death at 11:00:12.188 and protected respawn. Score and kills remained
  zero; deaths advanced to two. The 4.33-second ignition-to-death interval is
  consistent with the configured 100 HP/s. No synthetic damage/death event was
  injected. Additional real rendered frames are in `molotov-self-r2/`.
- Bot escape: three bounded attempts produced two actual enemy contacts, no
  enemy kill. At 11:02:06.313 the physical USEC bot `6acca089ad46c511f4710f16`
  entered fire. BigBrain recorded escape routes at 11:02:06.348 and
  11:02:09.647; subsequent native position moved from (5.10, 0.00, -21.33)
  through (6.98, 0.03, -19.30) to (9.6, 0.1, -16.6). Its current layer then
  returned to SAIN Combat Layer. No bot freeze/position/health mutation was used.
- Disabled fire: Enabled=false, godmode off, native Molotov throw at the same
  position used for the successful fire self-kill. The item was consumed, no
  incendiary throw/ignition occurred, and HP remained 440 with deaths unchanged
  after more than eight seconds. Enabled was restored to true immediately.
- Live audio: during a separate real TDM fire at 11:13:00.426, read-only
  inspection found AudioSource "Arena ground fire", `isPlaying=true`,
  `loop=true`, clip "Arena original burn", `samples=87649`, and output mixer
  group "Master". After expiry, the runtime Fires collection was empty. This
  is technical playback/lifecycle evidence, not an audition or loopback capture.
- TDM FF OFF: native log froze teammate damage blocked. Two bounded attempts
  did not establish ally contact: the ally moved away from a narrow stair fire,
  then a destination was correctly refused as unsafe and the throw occurred at
  the previous position. Ally head HP stayed 35, but without a contact this is
  **not evidence of immunity**. No FF pass is claimed from these attempts.

The first procedural bottle is visibly dark and the fire strands are thin;
both parent and model reviewer considered these acceptable prototype visuals,
with further presentation polish still open. No acoustic quality conclusion is
drawn from screenshots or from a lack of log errors.

The TDM menu label is now "TEAM DM"; gpu-run's canned recipe waited for the
older "TEAM DEATHMATCH" and stopped before launch. An artifact-only scenario
used the actual observed TEAM DM/FACTORY/NEXT/READY controls successfully. No
UiBridge source was edited.

Both protected exits completed with `--no-heal`. Settings ledgers restored four
TDM and five general test values, with the unchanged sixth general value left
alone. The mode changed by native menu selection was explicitly restored from
teamdeathmatch to the original deathmatch. Post-restoration audit reports zero
unexpected configuration values, all 422 inventory IDs present, all 33 PFT files
unchanged. Complete post-login comparison is `r2-post-login-diff.json`; complete
logs are `client-r2.json`, `LogOutput-r2.log`, and `server-r2-complete.log`.
Unlike r1, the on-disk post-login snapshot of r2 still held the earlier health
timestamp: later checkpoints added the native health update time `1791795224`
(08:53:44 UTC, during login and before entering the raid) as well as Hideout
clock advances. No HP, inventory or other substantive profile field changed
between these r2 checkpoints. These timestamp differences remain recorded in
the complete diff rather than silently discarded.
Both game and server were then stopped cleanly for the final server correction.

The parent additionally restarted the previously idle host-2 game and server to
finish synchronizing a locked older server DLL and avoid a mixed installation.
It checked idle MainMenu/no raid/free control first; no host-2 raid or gameplay
test was performed. The final host-2 restart followed a MainMenu state with last
key input about four minutes earlier, not an assertion of zero recent input.
This leaf did not control host 2.

## Final candidate r3: normal supply and native G verified

Final product commit `4fd79815d6a8dcd15f4d06d4cf5e0d1543d0b998` was deployed by
the parent with game/server stopped, then started fresh. Later documentation
commits do not change this native build identity. Pinned manifest:
`artifacts/double-jump-build/c6e49b3807244d04bcd5a0257af3c64d/manifest.json`.
The local copies `pinned-manifest-r3.json`, `expected-build-r3.json` and
`build-proof-r3/report.json` retain exact binding evidence.

| Assembly | SHA-256 | MVID |
|---|---|---|
| Client | `2AABDA1A0EE282F7600BBCDD7577B6068ADFE52BFA7E1CC98E599E6B5C9440FE` | `01ca1713-c3b3-49d0-9699-855f4491c194` |
| BigBrain compatibility | `0C4AC3763CCB170CBD69733AE6765C342136FA9C2CCE8124A2EF4722D95F7AE1` | `f37c870c-128e-485a-b0f6-1de59a731efe` |
| Server | `BFDD201F1BF6B8DC2BE868E23780234AA79B8E0DDA970A46B52D560C58676A60` | `511f9139-12d7-44a1-9c4c-6ba07dd49c92` (disk, followed by fresh process; not a UiBridge loaded-server claim) |

Game instance `1f8981d4b2a34b88b2d0ab256cc4ace1` started at 11:21:09 CEST.
All 19 reported client plugins and Redeploy.Core matched loaded/disk/local.
Server log `server-r3-complete.log` proves private registration at
**09:20:35.563 UTC**, `Molotov ready` at **09:20:42.190 UTC**, before client
login `/client/game/start` at **09:21:30.131 UTC**. The Arena request at
**09:22:53.516 UTC** contained **60 optional grenade requests**, rather than
r2's 40. This final correction only changed server registration/supply code and
its tests/documentation; the r2 client/fire/audio implementation was unchanged.

Factory Team DM used native TEAM DM / FACTORY / NEXT / READY controls, Spartan,
2v2, time limit 600 s and frozen FF ON. Native inventory before any insertion
contained the two RGD-5s (`6acca7062b0c3c8f1a005bb6`,
`6acca7062b0c3c8f1a005bb7`) and Molotov (`6acca7062b0c3c8f1a005bb8`, exact
template `a12e00000000000000000001`), all in TacticalVest. `final-kit-r3/`
contains eight native rendered frames; its Molotov tile is still loading, so
the already rendered r2 icon remains the presentation evidence.

Both supplied RGD-5s were selected with `PressThrowGrenade`, their native hands
checked, then actually thrown with `TryLowThrow` / `FinishLowThrow`. With only
the supplied Molotov remaining, the native G command selected Molotov at
11:25:04.200. Native player/inventory readback confirmed the exact item; no
slot binding, inventory relocation, template mutation or insertion was used for
this selection gate. Its actual underhand throw was recorded at
**11:25:47.147** and ignition at **11:25:47.337**, with **35 floor cells**, six
seconds and configured 100 base HP/s. Local godmode was on for this selection
test, so this particular throw is not self-damage evidence.

Two bounded FF ON attempts remained inconclusive. The first targeted live USEC
teammate Straxus (`6acca7067722091408f17ede`), who moved off the intended fire
position; there was no teammate contact log. A single extra protected-raid
Molotov (`6acca7d92b0c3c8f1a005bf1`) was then inserted for one additional attempt,
only after the normal supply/G/throw gates had passed. The old observed
teammate body's head HP read **0 before the second throw**, invalidating that
body as a live-damage baseline. The actual second throw at **11:27:53.389**
ignited eight cells at **11:27:53.584** and logged only local-player contact.
Neither unchanged teammate HP nor fire next to an already invalid baseline is
credited as FF proof. No further attempts were made.

`final-ffon-r3/` contains 120 native frames 23205–23324 around that second real
throw, with retained `complete=false, reason=frame_limit` metadata. Its close
camera is partly obscured by the nearby body; it supplements actual throw
evidence, not a clean fire overview or damage proof. The separate prematurely
started `final-fire-r3` capture closed before the throw and is not used as fire
evidence. Original r2 frames/video remain the useful visual review material.

Final complete client log is `LogOutput-r3.log`; `client-r3.json` is the bridge
export and has one pre-existing 2000-character-truncated entry, so it is not
the full-log substitute. No incendiary failure or damage exception appeared in
this run. The native microphone-output initialization error occurred before the
raid, and the installed RespawnApiProbe intentionally threw its documented
Replaced-subscriber test exception; neither is claimed as a clean zero-error log.

## Final restoration and persistence boundary

Protected native exit with `--no-heal` completed in 37 seconds through
DISCONNECT → ExitStatus → KillList → SessionStatistics → MainMenu. The r3
ledger restored four changed values, found two already equal, and reported zero
conflicts/missing values. Native menu selection's GameMode was separately
restored from teamdeathmatch to the original deathmatch. UiBridge confirmed
godmode off outside the raid. No walk or aim tracking was used. Capture had
finished encoding before shutdown. EFT quit gracefully; the server stopped
normally through gpu-run/Ctrl-C.

Full post-login → protected exit and post-login → logout recursive comparisons
both have exactly **one** change:
`/characters/pmc/Hideout/sptUpdateLastRunTimestamp`,
`1791796902` → `1791797557`. Every other profile field, not merely the inventory
IDs, is unchanged from the r3 post-login snapshot. The parent independently
reproduced the same full comparison. Evidence: `r3-post-login-diff.json`,
`parent-final-profile-diff.json`, `tdm-on-protected-exit-r3/audit.json`,
`logout-r3/audit.json`. Both checkpoints retain all 422 inventory IDs and all
33 PFT files byte-identically, with zero unexpected config assignments. The
sole new config assignment is the shipped default `Arena incendiary/Enabled=true`.
Exactly the three intended Arena DLLs differ from the original 84-DLL baseline.
The original pre-login differences described above remain explicit; the full
baseline-to-final profile is not falsely called byte-identical.

The remaining targeted native follow-up is enemy kill attribution and assist
settlement, a confirmed live ally contact with FF OFF and ON, PFT teammate
behavior if a disposable test squad becomes available, and an ordinary PMC
control. Add audio audition and the missing-companion/staging native cases when
practical. Current offline coverage and real self-kill do not stand in for that
matrix; failure to land these bounded target throws is not itself evidence of a
product FF/attribution defect.
