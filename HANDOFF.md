# ArenaMode working handoff

Snapshot: 2026-10-11. Read `AGENTS.md` first. This is a compact handoff, not the
conversation transcript or a live synchronization channel. Verify current files,
commit IDs, installed artifacts and logs before treating a recorded result as
current. User-facing communication is normally German.

## Baseline and responsibilities

- **Dependencies / default full-auto / vest HUD (2026-10-11, deployed/native passed):** `DEPENDENCIES.md` records
  actual hard/conditional/optional dependencies, absorbed own mods and release
  boundaries. DefaultAutoFireMode was previously separate only. Arena now owns
  native full-auto selection for its exact living local player at each completed
  draw, plus once per published initial/replacement controller. Manual switching
  while held stays intact; unsupported modes, ordinary raids and bots are not
  altered. Standalone remains independent for ordinary raids and retains its
  broader fallback rules. See `DEFAULT-FIRE-MODE-REVIEW.md` and the production-
  linked harness. Implementation `436c51c`, Skoll CC BY 3.0 vest icon `9eaa904`,
  replacing only armor_body.png with unchanged HUD layout/tint/helmet. Attribution
  ships in ArenaMode/HUD-ICON-LICENSES.md. Client SHA256
  `6BCA5C490564240F004E8B1C2176375DDC95A642D0C0907F4139B633927249A3`,
  deployed locally/GPU2 and loaded in a fresh process; 43 plugins/five libraries
  matched. Offline 46/11 faults and built sprite verification passed, no new
  build warnings. Native Factory DM: M4 initial/manual-single/redraw/respawn,
  automatic held-trigger fire, and SVDS single-only/redraw/held-trigger passed;
  vest icon visually checked. Standalone disabled during test, then restored.
  Protected exit, zero Client Error/Fatal; settings restored, twelve PFT files unchanged.
  Whole-profile audit: only two bookkeeping timestamps plus eight exact missing
  stack counts that SPT had repaired BEFORE Arena snapshot, proven in server log;
  no inventory loss/gain. Raw audit and separate cause adjudication retained.
  No server/Core/standalone source or unrelated Molotov asset changes. Ordinary
  PMC/own-gear/PFT paths not separately native-tested here. GPU2 at MainMenu,
  server running, no active hooks or push. Full evidence/limits in
  `GPU-AUTO-FIRE-VEST-RESULTS-20261011.md`.

- **Equator Team DM team boxes (2026-10-11, deployed and GPU-tested):** `381f784`
  and nullable-annotation follow-up `96ed83b`; the exact
  per-raid clone adds twenty private original TeamFight markers (USEC north,
  BEAR south) without changing common MapPort points. The client restores only
  those records after MapPort's conservative booth repair; initial placement,
  bot activations and player respawns use the corresponding box, with real
  floor/clearance/path/occupancy checks and persistent native reservations.
  Total participants are 4–20, default four. Tactical/PFT Zombies remain blocked;
  other modes and ordinary raids retain their general starts. Delayed respawn
  continuations now also require the exact current native game/world pair.
  Offline: runtime 100/11 faults, burst93/17, TDM247/20, native menu640,
  participant UI3730 + native195 + area29/6 faults, map admission540/9,
  unit959/one existing APBS skip; independent source review clear. Installed
  files unchanged during checks. Final client/server staged from `96ed83b`,
  zero errors/four old client and two old server warnings; deployed locally and
  to GPU2, restarted with all 43 plugins/five libraries identical to disk.
  Native 3v3: both sides 10/10 safe connected points, local USEC starts north,
  17 bot arrivals in their own boxes, one natural player death/box respawn,
  both teams leave their boxes and fight; no Error/Fatal or Arena warnings.
  Same-process Deathmatch control starts outside the boxes with three bots and
  no team-spawn hooks; both raids exited protected. Full profile only two
  bookkeeping timestamps changed; twelve PFT files and all pre-existing config
  values unchanged, no foreign DLL changes. Actual 10v10, local BEAR entry and
  crowded-box failure remain offline-covered only. See
  `GPU-EQUATOR-TEAM-SPAWNS-RESULTS-20261011.md` for hashes and evidence,
  `EQUATOR-TEAM-SPAWNS-REVIEW.md` for the contract. No MapPort/Core or Molotov
  changes. GPU2 at MainMenu, server running; no push.

- **Equator PFT Zombies and Friendly Fire (2026-10-11, deployed/native follow-up):**
  `d265b4a` excludes Equator only from PFT Zombies in addition to its existing
  Tactical block; solo Zombies remains available. `100c003` adds independent
  raid-frozen Friendly Fire preferences: Team DM/PFT Tactical ON, PFT Zombies OFF,
  solo N/A. Health/armor and attributed native grenade effects use exact physical
  team admission; self/enemy/environment damage and AI hostility remain native.
  Unit 940/two skips, FF154/34 compiled faults, menu1461/27, admission530/9,
  TDM332, Zombie62/11 plus solo85/pending49 passed; independent review clear.
  User released GPU2 October 11. Fresh baseline, staged 100c003 deployment and
  restart preceded native menu/default/per-mode/freeze checks. Equator solo
  Zombies stays offered, PFT Zombies/Tactical excluded. OFF real knife hit was
  vetoed with all seven HP values unchanged. ON hit produced exact local
  attribution and native DidBodyDamage 21.0599976 (actual synchronous HP loss);
  a later sampled HP read was already full, so a paired sampled-HP comparison
  is not certified. PFT's post-combat full heal after >=12 seconds is a concrete
  possible explanation, not observed execution. No damage/healing code was
  changed for that observation. Protected exits preserved profile/PFT/config;
  prior post-Stop physical-roster diagnostics only, no live Error/Fatal.
  `GPU-FRIENDLY-FIRE-RESULTS-20261011.md` contains the exact evidence and limits.
  That build was then superseded by the Equator build above, which includes the
  unchanged FF implementation. Historical staging status remains in
  `FRIENDLY-FIRE-TEST-STATUS-20261009.md`; contract in `FRIENDLY-FIRE-REVIEW.md`.

- **PFT ported maps and unified Customs areas (2026-10-09, latest):** `54d9f7e`
  fixes the installed PFT empty-cover startup dereference using an existing real
  core only in exact protected Bay5/Equator raids. Customs now requires one of
  its three surveyed areas in every Arena mode; ordinary PMC is unchanged.
  Native integration follow-ups: `27fdf80` accepts the exact PFT Coop entry,
  `9aad58c` preserves native NavMesh behavior without absent cover-graph fallback,
  `91db119` defers failed-start Stop and revokes pending ambient/managed spawns,
  `e55d6a7` defers initial roster discovery until bounded mode leader binding,
  `21b6af6` scopes duplicate/late spawn ownership per native raid creator, and
  `f462bfb` retains five frozen Tactical opponent bodies until roster retirement.
  Client `0.1.0+f462bfba9c4420c290674b9e0f86241528f9cf06`, SHA256
  `F43AA3C179C371310F48882A27DF0473EDBD3CD72719A33F7D67E307B50A70E8`,
  deployed locally/GPU2; restarted 21/21 plugins/Core identical. Server
  `0.1.0+54d9f7edf38154cc31e3fa48be0de123c1dedb2e`, SHA256
  `DD7738CE012E64CDE81C4B08D9F02F7723D110FF413E707F217D897E5DD19E7B`.
  Core/PFT/SAIN/UiBridge unchanged. Staged builds zero errors (client four old
  warnings, server two); unit 932/two existing skips. Final ported/bounded
  harness234/24 compiled faults, cover150/25, area459/22, failed-start65/13,
  managed-solo141/12+17 native IL contracts; parent menu/admission/PFT/Zombie
  checks passed and independent review was clear. Tactical corpse68/16 faults,
  cleanup309 and DynamicMaps61 passed. Native menu proof covers all
  five Customs modes and only three areas; solo Factory Side starts nine bots
  inside the contour. Final f462bfb GPU2: Bay5 Tactical natural round-1 victory,
  exact round-2 rebuild and actual corpse trim/release pressure passed; then
  Center Tactical exact 5v5 and Yard Zombies exact four companions passed in
  the same process. Yard combat continued through PMC death/spectator and ended
  normally on team defeat. Sampled Center/Yard bot positions stayed inside.
  Bay5/Equator Zombie combat passed on e55d6a7 with the unchanged ported combat
  adapter. No live errors in the audited final windows; known after-Stop roster
  and occasional melee diagnostics remain. Full profile differs only in two
  bookkeeping timestamps; all twelve PFT files unchanged, test settings restored.
  GPU2 EFT closed gracefully, server running, no push. Unrelated Molotov assets
  preserved. Forced failed-start retry and ordinary-PMC exclusion retain
  source/harness coverage; no claim of every area/mode or long-match acceptance.
  Equator Tactical remains deliberately blocked: five opponent points at the
  existing minimum distance are not available from every possible surviving
  player position. Do not remove that guard to claim the map fully accepted.
  Details: `GPU-PFT-PORTED-CUSTOMS-RESULTS-20261009.md`,
  `PFT-PORTED-MAPS-REVIEW.md`, `PORTED-COVER-REVIEW.md`,
  `CUSTOMS-ALL-MODES-REVIEW.md`, `MAP-MODE-COMPATIBILITY.md`.

- **Zombie voice budget and PFT fast path (2026-10-09, latest):** `ae6031a`
  skips only PFT's redundant hostile-layer prefix for confirmed native infected;
  `4220c05` handles native duplicate wait-layer entries without changing their
  contents. Standby wake/custom brains/followers retain PFT. `66ab250` limits
  repetitive zombie playback to six voices/four distant voices; nearby attack
  and critical phrases, native/SAIN signals and other event subscribers pass.
  Default-on immediate comparison switches: Zombie/PftFastPathEnabled and
  Zombie/VoiceBudgetEnabled. Client `0.1.0+4220c0501c69d362dd868e2bec73a3b388bfe01f`,
  SHA256 `96E93E67E1CAD501D6B7F6C335C9912143C0C626FECBB3A3F94CB2A111800D19`,
  deployed locally/GPU2; restarted 21/21 plugins/Core identical. Server remains
  `9c67ed7`, Core/SAIN/PFT/UiBridge unchanged. Unit 913/two skips, PFT 190/19 faults,
  voices 325/19 faults and independent installed-IL/source review pass; staged
  client zero errors/four existing warnings. Final solo 56-melee comparison has
  >1 million PFT calls, only 223 standby fallbacks, zero errors; native attacks
  and protected exit preserved. Voice admission confirms actual playing-source
  observations and bounded suppression; subjective listening remains separate.
  Same-raid settled PFT cost: off 0.33815–0.37805 ms/frame, return-on 0.04413
  including the nested guard; no FPS/population-cap claim. Four companions were
  invited natively, formed the exact squad, killed infected and kept fighting
  after natural PMC death/spectator. Protected exit passed; the known roster
  observer error immediately after native Stop(Left) recurred and remains a
  separate teardown-diagnostic follow-up. Full profile changes only two
  bookkeeping timestamps; all 12 PFT files unchanged, previous config values
  restored, only two new default-on keys added. GPU2 EFT closed, server running;
  no push, unrelated untracked Molotov assets preserved.
  SAIN global pairs deliberately unchanged: required hearing/companion consumers
  and lifetime prevent a verified safe Arena-owned skip. See
  `GPU-ZOMBIE-VOICE-PFT-RESULTS-20261009.md`, `ZOMBIE-PFT-PERFORMANCE-REVIEW.md`,
  `ZOMBIE-VOICE-BUDGET-REVIEW.md` and `ZOMBIE-SAIN-GLOBAL-REVIEW.md`.

- **Zombie pending-wave fix and retreat measurement (2026-10-09, latest):**
  `5637ce7` shares the pending-arrival clearance barrier across solo/PFT and
  watchdog retirement; pending requests no longer expire into free capacity
  after 20 s. Known preparation failure retries exactly once; unresolved work
  ends protected after separate 300 s preparation / 60 s native arrival limits.
  Client `0.1.0+5637ce7a75d30d966b6debe8ecd34b5bbd6a64ef`, SHA256
  `8B2E3DF3E57C76136F6642DE8445928DD05B7472E5C0719C0203569E5D65CB57`,
  deployed locally/GPU2, restarted with 21/21 matching plugins. Server remains
  `9c67ed7` unchanged. Unit 913/two skips; pending49/service33/12 faults,
  Burst66/13 faults, solo85 and PFT59 pass; independent review clear, staged
  client zero errors/four existing warnings. Native one-zombie kit waves remain
  at wave1 before the kill; two actual kills each trigger exactly one refill,
  reach wave3, and player death ends protected without respawn. Long native
  delays/timeouts and final-build PFT rebind remain controlled-harness coverage.
  Temporary probe `f860fc6` measured native retreat at about 0.0036 ms/frame
  with 56 mixed infected, including close shooting checks: no production
  retreat optimization justified; all native planning and population limits
  stay unchanged. Probe removed, configs byte-identical, profile only two
  bookkeeping timestamps changed, 12 PFT files unchanged. EFT closed, server
  running, no push, unrelated Molotov assets preserved. See
  `GPU-ZOMBIE-WAVE-RETREAT-RESULTS-20261009.md`, `ZOMBIE-WAVE-PENDING-REVIEW.md`
  and `ZOMBIE-RETREAT-REVIEW.md` for exact evidence and remaining limits.

- **Bounded Zombie performance work (2026-10-09, latest):** `bc21205` removes
  unused optional catalogue prewarming; `415c5aa` skips confirmed friendly
  infected delayed footsteps and peaceful social searches. Native combat,
  navigation, global pair registration and all population limits remain.
  Final client `0.1.0+415c5aa6aadbd339b3dda646f4e44b76ef533332`, SHA256
  `425A517EC295F90CE8BBCE5CD36D40942D9D0FA7A655AF959848D2E04CE15240`,
  deployed locally/GPU2 and loaded 21/21 matched. Server remains exact `9c67ed7`
  bytes below. Unit 909/two skips; hearing 192/17 compiled faults, warmup 90/10,
  TDM 37, solo 85 and PFT 59 pass; staged build zero errors/four existing warnings;
  independent review clear. GPU2: 56 approach/melee/solo-death end pass; first
  minute 461 delayed steps +3097 social searches avoided; own gear no longer
  preloads 20 unused kits/387 templates. Observed FPS modestly better but differing
  convergence/AI work prevents a causal percentage or higher-cap claim.
  Separate Enforcer smoke confirms one kit preload/use and refill execution,
  but exposes an existing solo tiny-wave bug: intermission can begin while the
  final dispatched infected is pending (the guard currently exists only for PFT).
  Fix this before treating one-zombie wave transitions as accepted; director
  source unchanged here. See `GPU-ZOMBIE-PERFORMANCE-RESULTS-20261009.md` for exact
  limits, artifacts and timings. Configs restored byte-identically, profile only
  two bookkeeping timestamps changed, all 12 PFT files unchanged. EFT closed,
  server running, no push; unrelated untracked Molotov assets preserved.

- **Unified rules and expanded maps (2026-10-09, latest):** `1649ccf` gives all
  five modes the same visible description/objective/respawn/equipment schema;
  `9c67ed7` opens Bay5 and whole Customs for all modes, Equator for all except
  Tactical (only four sufficiently distant opponent points near the atrium).
  The three bounded Customs areas remain TDM-only until other modes have a
  containment/spawn adapter. Whole map is now the fourth Customs TDM choice.
  Final client/server ProductVersion `0.1.0+9c67ed74ea032c239656599536ee645b53e9fc7a`,
  client SHA256 `77F72EC9E932B05B3E2758C7EF15058125D8B0B7BEC35EB6F54CBC4B5FF8982D`,
  server `C0871C34B4F1AFAAE990777E39827EDCC1BAF7A6C74401A1A0A47AD73DE49307`.
  Deployed locally/GPU2; loaded client 21/21 matched. Native solo-mode menus,
  Equator TDM 2v2 live/refill, Bay5 Tactical five active opponents and whole
  Customs Zombie spawn/approach/melee pass. New PFT pairings/later rounds remain
  untested. Whole Customs has long travel distances. One existing melee callback
  NRE occurred 244 ms after protected Stop, not during play; exact IL/timing
  strongly support a disposed Melee receiver, an open teardown follow-up. Profile
  comparison changed only two Health/Hideout timestamps; all twelve PFT files
  unchanged, config restored byte-identically. EFT closed, server running.
  Unit suite 909 passed/two existing skips; integrated offline gates and
  independent review pass. No push. See
  `GPU-MODE-COPY-MAP-ACCESS-RESULTS-20261009.md` for evidence and test boundaries.

- **Solo Zombie one-life rule and Factory courtyard (2026-10-09, later):**
  `e4ba7c3` excludes the four additional courtyard points beside the known
  `c671337f` marker on Factory day/night; ten exact exclusions including the five
  existing underground records, with 135/145 original points retained. Protected
  PFT leaders now receive this filter in Zombies as well as Tactical; ordinary
  and solo player starts are unchanged. GPU scene inventory confirmed all five
  courtyard markers, not final-build PFT pathfinding acceptance.
  `92f3f4b` makes solo Zombies end on first death with either loadout, retaining
  exact-owner terminal hold through optional replay; PFT cleared-wave restoration
  remains separate. `2db0f4f` prevents the result panel covering the killcam.
  Final client ProductVersion `0.1.0+2db0f4fe087509de475ae6416db95a3eddadf733`,
  SHA256 `AD4AC28B9F29FD5E2268D183E09988AB463D71F2857EF876EE5C342FBE46D883`;
  server `0.1.0+e4ba7c3cbf8adb7fe36d60bee18094d6b92ff09f`, SHA256
  `FEF1AD1DDBB40DCD68F3389E0B796053472134EB0934F0CC361839A49B8AB93A`.
  Both deployed locally/GPU2, server restarted and final client 21/21 loaded/disk
  matches verified. GPU2 own-gear/replay-off death and kit/replay-on melee death
  exit without respawn; final replay retest visibly clear. Full profile unchanged
  except Health/Hideout bookkeeping timestamps; all twelve PFT files unchanged.
  Config restored byte-identically, EFT closed, server running, no push. See
  `GPU-SOLO-ZOMBIE-FACTORY-RESULTS-20261009.md` for exact native/offline boundaries,
  hashes and known benign teardown warnings. Unit suite 885 passed/two skips;
  Factory 1105/14 compiled faults; final Solo 85/13, PFT 59/11, burst 34/9;
  related menu, killcam, TDM, Give up and announcer regressions pass. Independent
  reviews clear. Final client build zero errors/four existing warnings, server
  zero errors/two existing warnings; Core, BigBrain, PFT and assets unchanged.

- **Zombie PFT map correction and native smoke (2026-10-09):** `ecc22d5` removes
  the leftover Tactical-only area-menu guard and describes the Zombie squad as
  five allies. Locally and on GPU2 deployed client ProductVersion
  `0.1.0+ecc22d532124fd20b5dbd8c769dc7716f95428ac`, SHA256
  `C2A295B57BB5A9DF86732B1F03FCD02015A23EB72E0F6C5FF42599D5A4B83CA1`;
  server remains the `4ece234` build below. GPU2's newly added four companions
  were invited through My Squad. Old-build missing controls reproduced; new
  Factory day/night selection, confirmation and raid start pass. Local death,
  ally spectator/Escape and wave 1 -> 2 with restored PMC/four fresh followers
  pass. Protected exit preserved all 12 companion files byte-for-byte; player
  profile changed only Health/Hideout bookkeeping timestamps. See
  `GPU-ZOMBIE-PFT-MENU-RESULTS-20261009.md` for precise evidence and limitations.
  Shared PFT teardown roster-observer false alarm remains a separate follow-up;
  no gameplay no-contest or persistence loss. Offline checks 169/29/221/157 pass,
  adversarial review clear, staged client zero errors/four existing warnings.
  Config restored byte-identically; EFT closed, GPU2 server running. No push.

- **Zombie + PitFireTeam survival (2026-10-08):** implemented optional four-companion
  team mode using own gear; see `ZOMBIE-PITFIRETEAM-REVIEW.md` for the exact lifecycle
  and native test steps. Death holds until a cleared wave, with ally-only spectator;
  a team wipe ends protected. All four followers refresh between waves; a living PMC
  keeps its body/current loadout and condition, while a dead PMC restores entry gear.
  Admission/version/profile guards remain strict; solo Zombie and Tactical retain
  their paths. Parent checks: 885 unit tests passed, 2 existing skips; Zombie runtime
  59 assertions + 11 compiled mutants; PFT 309, lifecycle 389, mode menu 157,
  rendered menu 1180, tracer 243, friendly markers 150, lasers 1295, assist capture
  137, team expansion/spectator 856; preview lifecycle 676 + 16 compiled mutants.
  Client staged build passes with the four existing CS8632 warnings; server build
  passes. Independent reviews resolved four concrete findings; no remaining new
  P0-P2 issue identified. Native Zombie-PFT acceptance remains pending: GPU2's
  SPT-GPU-2 test profile has no stored PFT companions (existing files belong to
  spt41dev); no profile/team data was modified to fabricate that precondition.
  Source commit `4ece234ef08b9b38eb6b22e04a3d417519a059f9`; client and server both
  report ProductVersion `0.1.0+4ece234ef08b9b38eb6b22e04a3d417519a059f9`.
  Both DLLs installed locally and on GPU2 with matching SHA256:
  client `0F45AC2A8742CBF01B73E2F44C0A3B874E92FB24BB83003602739067F543A0EE`;
  server `1DEBA5FC55E382DDB5F0BD987B1C8C4FD57BB7F40D28E9B1F5DF19ECA45EC048`.
  Delivery manifest/backups: `artifacts/zombie-pft-delivery/`. GPU2 EFT was closed;
  its server was stopped gracefully and restarted, ready at 19:40:28 UTC. The new
  startup confirms Arena patches and the PFT 0.10.3 persistence guard enabled.
  No native client run followed this deployment. Server build has two existing
  warnings (CS8620/MUD0002); Core/BigBrain/PFT/assets/config unchanged. No push.

- **GPU2 follow-up acceptance (2026-10-08, later evening):** host reachable;
  EFT started/restarted with 21/21 compared plugins matching disk and loaded code.
  `GPU-FOLLOWUPS-RESULTS-20261008.md` supersedes the earlier connection limitation.
  Representative own/provided avatars and PM(t) Akimbo movement/alternation pass;
  APB/Manual arms/PFT and listening balance are explicitly not certified.
  Native testing found pickup rotation starvation; fix `1b08b84` adds validated
  two-appearance exchanges without relaxing pending weapon or placement guards.
  Parent checks 410/33 compiled faults and 545/12 pass, independent reviews clear.
  Final native Factory DM: 34 moves across all six IDs; rotated M60 collected and
  equipped, cooldown rearmed safely then later moved; real audio playback observed.
  Final client `0.1.0+1b08b84bc3390235c1d4fbc5c66afee962bf07fa`, SHA256
  `E673D20666AD49FEA22A16A1F73B49C6C5F17A69B862B9918A19824FB6F17E77`,
  installed locally and on GPU2. Server/Core/BigBrain/WAV unchanged from below.
  Config byte-identical after restore; entire profile unchanged except Health and
  Hideout bookkeeping timestamps. EFT closed normally. No push; Molotov intake
  untouched. First Zombie preview raid logged four native BotMemory NRE warnings;
  later DM/Gun Game and final retest had no new exception, details in report.

- **Initial Akimbo/Factory/preview/pickup delivery (2026-10-08 evening):** source
  `90fbf3a`, five separate scoped commits, details and native follow-up steps in
  `FOLLOWUPS-20261008.md`. Akimbo models now register to solved hands after IK;
  own-gear variants use native confirmation/deployment equipment; pickups move
  after cooldown and after 60 available gameplay seconds; cue WAV +12 dB without
  clipping. Factory night already shared all six exact exclusions, verified
  against installed day/night databases (no production change). Relevant suites
  rerun by parent and independent reviews resolved. Local client deployed:
  `4C9F7C5961A91A8C8ED540DFC721BE05E6E1AE0F8F333DEDA88BF9BA72034B02`,
  ProductVersion `0.1.0+90fbf3aae2fa32fd7c789283db33bb9e7b36f02a`;
  pickup WAV `E0585D79CD137AC9240A01C79048490EC87F0C2D07F9B84B9BA24F5B5D5CB171`.
  Server/Core/BigBrain DLLs unchanged. **At that initial attempt, no native acceptance or confirmed GPU
  mirror receipt:** GPU-2 UiBridge and GPU-1 control connection unavailable during
  attempts. No game run/profile/config changes; untracked Molotov intake left
  untouched. Not pushed. A game restart and the bounded checks in the report
  were still required; the later GPU2 run above records what is now verified.

- **Menü-Politur (2026-10-08, Branch feature/menu-polish, Worktree ArenaMode-zombie):**
  Sechs Nutzerwünsche aus der GPU-Abnahme von v2 in fünf Commits in dieser
  Reihenfolge (Punkt 3 und 4 teilen sich einen; `git log feature/menu-polish`),
  danach Review-Korrekturen an Tests und Doku (strengere Pins, Zustandsautomat
  der Beschriftung offline ausgeführt, keine Verhaltensänderung im Spiel). GPU-2-Abnahme
  08.10. 11:03–11:12 mit dem Merge-Stand `7308eb1` (Client `0A0FE617…`) und
  Kopfzeilen-Nachprüfung 13:3x mit dem Review-Stand `972e4de` (Client
  `209625EB0E8D5BA5737C4DF11F7FF7ABDD7D8AA2AB605EB88992759A9578C794`, lokal + Host 2
  deployt, 21/21 identisch): alle sechs Punkte im Spiel bestätigt, Protokoll in
  `GPU-MENU-POLISH-RESULTS-20261008.md`; nicht gepusht. Details je Punkt in
  `MUTATORS-MENU-REVIEW.md` (Nachtrag 2026-10-08), `NATIVE-MENU-REVIEW.md` und
  `ZOMBIE-MODE-REVIEW.md`.
  (1) **ANNOUNCER blendet die Einblendungen aus:** `ArenaHud` zeigt die
  Kill-/Award-Durchsagen ("DOUBLE KILL", ...) nur noch, wenn
  `AnnouncerService.IsEnabledForLiveMatch()` (jetzt `internal`) wahr ist; die
  HUD-Textdurchsagen ("WAVE n", Eigen-Granaten-Stichelei) laufen über einen
  eigenen ungegateten Handler. Folge für Zombie (Announcer dort standardmäßig
  AUS): eine frische cfg zeigt im Zombie-Raid keine "DOUBLE KILL"-Einblendungen
  mehr, bis ANNOUNCER für Zombie eingeschaltet wird; "WAVE n" bleibt. Das ist
  die erwartete Folge, keine Regression (bei der GPU-Abnahme so lesen).
  Zeilenbeschreibung "Stingers, cues and kill
  callouts."; Pins + dreizehn Mutanten in `tools/AnnouncerChecks` (der Award-Handler
  ist als Ganzes gepinnt: negiertes Gate, `return;`, dann `Show`; ein fehlendes
  `!`, ein `&& false` oder ein fehlendes `return` fällt auf; die Verdrahtung ist
  seit Runde 2 ohne Kommentare und als exakte Liste gepinnt: eine auskommentierte
  oder zweite, ungegatete `OnCallout`-Subscription, auch als Lambda, und ein
  direktes `_callout?.Show` außerhalb der beiden Handler fallen auf).
  (2) **Mutatoren-Panel ohne Legende und Fußzeile:** beide Texte sind weg, die
  16 Zeilen rücken auf Top 52 und nutzen den Platz bis 8 px über dem Kastenboden
  (Abstand 51,5 statt 47,5); Rechteck 600 × 472 bei Y=508 unverändert. Neue
  Geometrie-Pins und drei Mutanten in `tools/TdmMatchMenuChecks`.
  (3+4) **Ein Schema für die Modus-Texte, Zombie passt in den Kasten:** RULES =
  Ziel (höchstens drei Wörter/18 Zeichen), RESPAWN genau `Instant` oder `Next
  round`, EQUIPMENT = Ausrüstungsquelle (drei Wörter/18 Zeichen), Beschreibung
  ein Satz bis 80 Zeichen mit Punkt; Zombie jetzt `Survive the waves` /
  `Instant` / `Arena kits`. Der Tactical-RULES-Override (`1v5 / Solo`) ist weg,
  der Kader steht weiter im Zeilenlabel. Die Beschreibung bleibt ungenutzt:
  `HoverTooltipArea.SetUnlockStatus(true)` schaltet die Komponente ab, eine
  erlaubte Zeile zeigt also nie einen Tooltip (Dekompilat
  `EFT.UI/HoverTooltipArea.cs:131-135`). Schema-Test + fünf Mutanten in
  `tools/NativeModeSelectionChecks` (`Verify-CatalogSchema.ps1`), gerenderte
  Fakten in `tools/TdmMatchMenuChecks` (plus Mutant für den Roster-Override).
  (5) **Gefärbte Tracer nur in Team-Modi, wie die Laserfarben:**
  `DeathmatchColoredTracers`, `GunGameColoredTracers`, `TacticalSoloColoredTracers`
  und `ZombieColoredTracers` sind entfernt; `Settings.TracerEntry` liefert
  `Effects/TeamTracers` nur für Team DM und geschütztes Tactical 5v5, sonst null
  (Renderer nativ, Mutator-Zeile N/A mit dem Laser-Grund, Beschreibung "Team colors
  for every bullet."). Die Laserfarben waren schon team-only, unverändert. Die Killfeed-Namen
  nutzen weiter `ArenaTracerTeams.Classify` in jedem Modus (die Freund-Markierungen
  klassifizieren Ã¼ber den Modus selbst und sind nicht berÃ¼hrt). Die
  gespeicherte cfg behält die vier Schlüssel als verwaiste Einträge (harmlos, kein
  Migrationsschritt). Harness: `tools/TdmMatchMenuChecks` (Assertions ersetzt, zwei
  Mutanten) und `tools/ArenaTracerChecks` (Renderer pro Modus, Quelltext-Pins, zwei
  Mutanten).
  (6) **"PVE ZONE" -> "ARENA ZONE":** neues `ArenaSessionModeCaption` (reine Regel)
  und `ArenaSessionModeCaptionPatch` (Postfixe auf
  `MatchMakerSelectionLocationScreen.ShowInternal`,
  `MatchMakerAcceptScreen.Show(controller)` und
  `MatchmakerTimeHasCome.Show(controller)`, je auf den Arena-Ablauf begrenzt,
  Reconnect ausgenommen) plus
  die ARENA-Umschaltung im Seitenbildschirm (`ArenaSideSelectionPatch`, an und
  zurück auf den nativen Text). Nur im Arena-Ablauf, nie werfend; normale Raids
  unberührt. Dekompilat-Zeilen und Gründe in `NATIVE-MENU-REVIEW.md` (Abschnitt
  2026-10-08). Der Setz-/Rücksetz-Automat (`ArenaSessionModeCaptionLedger`) wird
  im Harness mit einem Platzhalter-Label ausgeführt, die Gates der Postfixe sind
  als je eine vollständige `if (...) Apply(...)`-Anweisung gepinnt (alle Pins lesen
  die Quellen ohne Kommentare; `Apply` muss try/`catch (Exception)` ohne `throw`
  behalten; der Seiten-Sync ist als ungeschützte Anweisung gepinnt; der Lade-
  Bildschirm vertraut `ArmedForNextRaid` nicht, das erst NACH dem Bildschirm
  gesetzt wird und dort nur veraltet sein kann); 35 Mutanten in
  `tools/NativeMenuChecks` (`Verify-SessionModeCaption.ps1`). Bekannte Lücke: mit
  `SkipPreRaidScreens` aus zeigen Game Settings und Insurance im Arena-Ablauf
  weiter "PVE ZONE" (siehe `NATIVE-MENU-REVIEW.md`); der Annahme-Bildschirm
  gehört mit PitFireTeam zu jedem Arena-Ablauf. Harmony-Bindung und Darstellung auf
  GPU-2 bestätigt: „ARENA ZONE“ auf Seiten- (nach dem ARENA-Toggle), Karten-, Annahme-
  und Ladebildschirm, nach Wechsel auf PMC wieder „PVE ZONE“, keine Warnung. Offene Altlasten, nicht von dieser Änderung:
  `tools/NativeAreaHookChecks` kompiliert auf diesem Stand nicht (`ArenaCompactMapInfo`
  und `ArenaPortedMapPresentation` werden von `ArenaLocationSelectionPatch.cs`
  verwendet, fehlen aber im Harness) und `tools/PitFireTeamChecks` scheitert an
  der Assertion zum Team-Arena-Körperersatz, die nur `ArenaSession.cs` liest
  (Datei auf diesem Branch unverändert).
  GPU-2-Lauf erledigt (08.10.): Mutator-Panel ohne Legende/Fußzeile (16 Zeilen ab
  y=560, Abstand 51,5), Zombie-/Gun-Game-/Tactical-Fakten einzeilig, ANNOUNCER aus =
  Kill ohne Callout (`_shownAt` -10; Gegenprobe an: 424,7 mit "double_kill"),
  COLORED TRACERS N/A in Zombie/DM/Gun Game/Tactical 1v5 und ON in Team DM, "ARENA
  ZONE" auf allen vier Bildschirmen. Offen: Tactical 5v5/PFT-Weg, Zombie-Raid mit
  "WAVE n", eine gefärbte Team-DM-Salve (Renderpfad unverändert).
- **Getrennte Akimbo-Animation + doppelte Kadenz (2026-10-08):** `5bf58d7`
  trennt beide sichtbaren Pistolen in eigene Mesh-Kopien und beschleunigt nur
  native Schusstimer + exakten Hands-Animator gemeinsam. `5327906` übernimmt
  Schlittenposen erst nach nativen FireEvents (Auto-VisualPass läuft davor).
  Native Renderer werden besitzgebunden ausgeblendet/zurückgesetzt; dynamische
  Patronenmodelle ausgeschlossen. Munition weiterhin zwei Schüsse pro Patrone,
  ein Schuss je Abzug bei Einzelfeuer. Keine neuen Einstellungen.
  Lokal + GPU-2 deployter/geladener Client `0.1.0+5327906…`, SHA256
  `DE9C48C3FCBC282C8BCA67E0137BB7C42FF6E1D50A46F599DDB5A6912A40CC93`.
  GPU-2 APS: 1 s Dauerfeuer ON 26 / OFF 13 Schüsse; getrennte Rechts-/Links-
  Darstellung in acht Screenshots; schnelles Einzelfeuer im Mittel 68,17 ms,
  Halten weiterhin nur ein Schuss. Acht Patronen ergaben 16 Schüsse, auch über
  OFF/ON. Reload fertig; OFF während Auto-Feuern gab alle 17 Renderer frei und
  stellte Animator 2 -> 1 zurück. Profil in sieben Schutzbereichen identisch.
  Keine Errors/Arena-Warnungen. Config restauriert, Spiel geschlossen, Server
  unverändert an. 29 Fehlerkontrollen, vier produktionsgebundene Harnesse,
  unabhängige Reviews und gestagter Build bestanden. Revolver/FastAnimator
  weiterhin nicht nativ nachgetestet; Messerwahl blieb in dieser Gun-Game-
  Fixture ohne verifizierten Wechsel. Fingergriff bleibt optischer Prototyp.
  Details: `GPU-AKIMBO-ANIMATION-RESULTS-20261008.md`. Nicht gepusht.

- **Abwechselndes Akimbo + Pickup-Sound (2026-10-07):** `65b8115` setzt je
  Abzug einen nativen Schuss abwechselnd rechts/links um; zwei Schüsse pro
  geladener Patrone. Kein verzögertes Doppelgeschoss. Der kostenlose Teil wird
  vorab reserviert; OFF/ON, Wegstecken und Nachladen erneuern ihn nicht.
  `9fd5558` ergänzt IENBA/CC0 nach bestätigtem lokalem Pickup, eigener Master-
  Ausgang, `Arena pickups / SoundVolume=0.7`. `8e42a31` hält Lizenz-/Manifest-
  Bytes stabil; `147fcad` repariert nur eine bestehende doppelte Testklasse.
  Client und genau diese Audioassets lokal + GPU-2 deployt; Server/Core/
  Companion unverändert, unversionierte Molotov-Audios unberührt. Geladener
  Client `0.1.0+65b81153911bb14f686431f8c1074c17cc4b255a`, SHA256
  `63361A5BDC19ADC7EBCCE36AB153880C63D4ECB89E8E52C564E6BC15DFCA9F46`.
  GPU-2: PB sieben Patronen -> 14 Schüsse (7 rechts/7 links), leerer 15. Abzug
  ohne Zusatzschuss, Reload und Paar-Erhalt über OFF/ON/Messerwechsel bestätigt.
  Pickup-Audioaufruf/Master-Bindung und Cleanup bestätigt, keine Geräte-Hörprobe.
  Revolver-/Dauerfeuer-Nachtest offen; zufällige Gun-Game-Rung-IDs taugen nicht
  als feste Waffentest-Fixture. Tests, unabhängige Review und Build grün.
  Profil: erster Vergleich enthält nur 18 explizit ergänzte StackObjectsCount=1
  plus Energie/Wasser auf 100 (Ursache nicht isoliert); nicht als byte-identisch
  gewertet. Der nachfolgende DM-Vergleich ist in allen sieben Schutzbereichen
  identisch.
  Eigene Konfigurationsänderungen zurückgesetzt, Spiel beendet, Server blieb
  an. Keine Error-Zeilen im exportierten Testlog. Details und Grenzen:
  `GPU-AKIMBO-ALTERNATING-RESULTS-20261007.md`. Nicht gepusht.

- **Brandgranaten-Balancing (2026-10-07):** Nutzer bestätigt Funktion und sofortiges
  Bot-Ausweichen. Grundschaden 80 → 100 HP/s (+25%, 25 pro Viertelsekundentakt).
  Dauer, Fläche, Fluchtlogik und sonstige Regeln unverändert; kein Nachbrennen.
  F12-Hilfetext und Log lesen den Wert aus derselben Konstante. Änderung auf dem
  aktuellen Hauptstand, der den Molotov-Prototyp inzwischen bereits enthält.
  Neu abgelegte unversionierte Molotov-Audios nicht angefasst. Nächster Vorschlag:
  Feuerdarstellung und eigener Arena-Gegenstand; Details im Prototype-Review.
  Höherer Schaden noch nicht nativ nachgetestet; keine Auslieferung aus dem alten
  September-Prototyp-Worktree, die neuere Modusänderungen zurücksetzen könnte.
  57 Assertions/acht Fehlerkontrollen, gestagter Client-/Companion-Build und
  unabhängige Review grün. Inzwischen im oben genannten Akimbo-Client lokal und
  auf GPU-2 mitdeployt; weiterhin kein eigener Nachtest des höheren Schadens.

- **Zombie-Modus-Prototyp (2026-10-06, Branch feature/zombie-prototype, Worktree ArenaMode-zombie):**
  21 Commits auf `29f670c`: neun Ursprungs-Commits (`7275289` Population/
  Hostility/LocationMutator, `2ac377d` Server-Tests, `604a76d` Katalog/
  Settings/Menü, `5392e8e` reine Wellen-/Rollen-/Profilplan-Regeln, `3df7977`
  ZombieMode + nativer Spawn-Naht, `a3dc746` BurstSpawnService.SpawnInfected,
  `f16ed39` ZombieSpawnService verdrahtet, `6ed6ecf` ZombieNativeEventBridge,
  `f45bae0` Rollen-/Schwierigkeits-Mapping) plus sechs Review-Fix-Commits
  (`cb2f76e` Flag-Leck geschlossen + Teleport unterdrückt, `c038b1a`
  Stuck-Wave-Watchdog + Pending-Ledger per Identität, `cc008d2` Watchdog
  verdrahtet + Tick gedrosselt + Opening-Snapshot eingefroren, `4b89c52`
  Scoreboard trimmt wieder, `d00cd17` Corpse-Cleanup-Ausnahme auf 0 für
  Zombie, `f35dfb5` optimistischer Wellen-Slot erst nach echtem Dispatch).
  Fünfter Arena-Modus **ZOMBIES**: solo, endlose Wellen aus EFTs eigenem
  Halloween-2024-Infizierten-Event gegen den lokalen Spieler, Instant-Respawn,
  Arena-Kits, kein PitFireTeam, kein Loot. Der Server reaktiviert das native
  Event nur für den exakt bewaffneten Raid (`ZombieLocationSettings.Apply`
  auf dem Clone); der Client treibt Wellen/Rolle/Schwierigkeit selbst
  (`ZombieMode`/`ZombieWavePolicy`/`ZombieRoleMix`/`ZombieProfilePlan`/
  `ZombieSpawnService`) und reaktiviert den nativen Spawn-/Verfolgungscode
  über fünf eigenständige Harmony-Hooks (`ZombieNativeEventBridge`),
  einschließlich eines Watchdogs gegen eine feststeckende Welle und
  drei unabhängiger Wiederherstellungspfade für das native Flag nach jedem
  Raid. Vertrag und vollständige native Belege: `ZOMBIE-MODE-REVIEW.md`.
  Offline grün in diesem Worktree: `dotnet test ArenaMode.Tests` mit dem
  No-Deploy-Sentinel (`artifacts/zombie-nodeploy/must-not-exist`, vor und
  nach dem Lauf als abwesend bestätigt) meldet 828 bestanden, 2 übersprungen
  (vorbestehend, umgebungsabhängig, nicht Zombie-bezogen), 0 fehlgeschlagen,
  830 gesamt; `dotnet build ArenaMode.Server`/`ArenaMode.Client -c Release`
  mit derselben No-Deploy-Property je 0 Fehler.
  **Deployt und auf SPT-GPU-2 getestet (2026-10-06):** Client-DLL aus `1e76582`
  (SHA256 `1ee4c51ffeeb8cc997bdd38fd8793f091f57e326ef08ab670a9242205d38eb18`)
  und Server-DLL (SHA256
  `0a826e8b3016cbce6a9abe910c46281040d4f4d4e77ebbcb464b7b3c1aea850b`;
  Server-Quellen seit `1e76582` unverändert, gilt auch für Raid 2) in die
  Spiegel-Installation `D:\SPT41` kopiert, Syncthing lieferte sie in 15-20 s auf
  Host 2. Raid 1 (12:52-13:11, `1e76582`): alles außer dem Nahkampf bestanden,
  kein einziger Treffer. Befund: SAINs globaler `RunToEnemyUpdate`-Prefix
  unterdrückt den nativen Nahkampf (Fix `445269c`..`0c6fab3`, siehe unten).
  Raid 2 (14:11-14:16, Client-DLL aus `0c6fab3`, SHA256
  `231f3be155298aa04ed1531b2efeead24a5d229c065472978437cda30770f4bd`, EFT per
  `gpu-run restart` neu gestartet, 24/24 geladene maschine-Plugins identisch):
  erster nativer Schwung 14:12:25, erster Treffer 14:12:25.8, ein Spielertod mit
  Instant-Respawn (0,20 s), drei Kopfschuss-Kills per Testhook (Score 450),
  Raidende „2618 native melee swing(s) attempted, 178 landed hit(s)“, keine
  Error-Zeilen. Kontrollraid (normaler PMC, Factory, 14:16:40): keine
  Zombie-Zeilen, Flag `false`, normale Heilung. Offen: Wachhund-Semantik (100 s
  räumen eine laufende Welle mitten im Kampf ab), zweites `Activate()` ohne
  bekannten Aufrufer, Performance bei MaxAlive 40-56, Schützenanteil. Details,
  Grenzen und nächste Schritte: `GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`.
  Offene Spieltest-Fragen (Reihenfolge und Details in
  `ZOMBIE-MODE-REVIEW.md`, Abschnitt "Known limitations and GPU playtest
  checklist"): Spawnen zeigt das Zombie-Modell und landet im 25-70-m-Ring;
  keine `InfectedLayersStrategy`-Exception und keine NRE aus
  `AddBackupProfiles`; kein sichtbarer Teleport-Sprung direkt nach dem Spawn;
  natives Nahkampf-/Schussverhalten je Schwierigkeit greift; das Wellen-HUD
  zählt korrekt runter, Intermission folgt, die Welle endet; Spielertod/
  Respawn hält die Wellenprogression nicht an; das native Flag ist nach dem
  Raid zurückgesetzt (ein folgender gewöhnlicher Raid zeigt keine
  Zombie-Logzeilen und keine Infizierten); Performance über die Zeit
  (Frame-Zeit, Bot-Zahl, Leichenzahl); zusätzlich der Stuck-Wave-Watchdog
  selbst unter echten Bedingungen (ein absichtlich gestrandeter Zombie nach
  `StuckWaveTimeoutSeconds`).
  **Erster GPU-Raid (SPT-GPU-2, 2026-10-06 12:52-13:11, Build `1e76582`):**
  alles außer dem Nahkampf bestanden — Spawns, Zombie-Modelle, Messer in der
  Hand, Verfolgung, HUD, Stuck-Wave-Watchdog, keine Error-Zeilen — aber KEIN
  Zombie hat dem Spieler in 19 Minuten Schaden zugefügt. Ursache
  (Live-Inspektion + Decompile, Details in `ZOMBIE-MODE-REVIEW.md`,
  Abschnitt "Native melee is suppressed by SAIN's global prefix"): SAINs
  eigener `RunToEnemyUpdate`-Prefix endet mit einem bedingungslosen
  `return false;` für jeden Bot, den SAIN nicht führt — alle vier
  Infizierten-Rollen eingeschlossen — wodurch der native Nahkampf-Body nie
  läuft. Fix (dieser Worktree, Commits `445269c`/`f4b0569`/`c7b4dee` auf
  `29f670c`): ein neuer Prefix auf `OneMeleeAttackNode.UpdateNodeByBrain`
  (`ZombieMeleePatch`) treibt den echten nativen `RunToEnemyUpdate`-Body
  über einen HarmonyX-Reverse-Patch (`Harmony.CreateReversePatcher`,
  bestätigt im installierten `0Harmony.dll` 2.9.0.0) — SAINs Prefix wird
  dadurch nie konsultiert; eine handgeschriebene, zeilenzitierte
  Fallback-Reimplementierung greift, falls der Reverse-Patch zur Laufzeit
  nicht vorbereitet werden kann. Die reine Gate-Entscheidung
  (`ZombieMeleeGate.ShouldDrive`) ist wie `ZombieWavePolicy` EFT/Unity-frei
  und getestet (5 neue Tests, Fault-Controls pro Bedingung).
  `ZombieMeleeDiagnostics` loggt die ersten 3 nativen Schwünge/Treffer pro
  Raid an Info sowie eine Summe am Raidende (`ZombieMode.OnMatchEnd`).
  Nebenbefund behoben: `Activate()` feuerte 77ms nach der ersten
  "armed"-Zeile ein zweites Mal INNERHALB desselben Raids
  (`BotsEventsController.Activate` — Decompile-Verfolgung fand genau eine
  Aufrufstelle je Kettenglied in Assembly-CSharp, die Ursache liegt also
  außerhalb dieser Kette, vermutlich ein SPT- oder Fremdmod-Patch; nicht
  abschließend lokalisiert). Harmlos, aber bisher als Warning geloggt;
  `ActivatePatch` unterscheidet jetzt per `GameWorld`-Identität zwischen
  demselben Raid (jetzt Info) und einem echten, anderen gestecktgebliebenen
  Flag (weiterhin Warning) — die Wiederherstellungs-Semantik selbst bleibt
  unverändert. Offline nach allen drei Commits: 833 bestanden, 2
  übersprungen, 0 fehlgeschlagen, 835 gesamt; Client-Build 0 Fehler;
  No-Deploy-Sentinel vor/nach jedem Schritt abwesend bestätigt. **Dieser Fix
  war zum Zeitpunkt dieses Absatzes noch NICHT im Spiel getestet (in Raid 2
  bestätigt, siehe oben)** — der nächste GPU-Raid sollte zeigen:
  Spieler-HP sinkt im Nahkampf, ein Zombie-Nahkampf-Kill erscheint im
  Killfeed, keine neuen Error-Zeilen, die `LogPathChoiceOnce`-Zeile
  bestätigt, welcher Pfad (Reverse-Patch oder Fallback) tatsächlich lief.
  **v1 (2026-10-06 abends, elf weitere Commits auf `b235068`: `452d87a`
  `7588923` `7bdd868` `66fb9aa` `a54201a` `40503d1` `3a16528` `a6879df`
  `1203438` `3c0a528` `c088b5d`):** Umsetzung der vier Nutzerwünsche nach dem
  Playtest ("funktioniert gut"). (1) Wachhund-Semantik geändert: der Timer
  läuft jetzt nur noch, solange kein Infizierter stirbt (Fortschritt statt
  Dauer je Welle), Einstellungsname/Standard (100 s) unverändert, nur die
  Beschreibung korrigiert; neuer monotoner Zähler
  `ZombieSpawnService.ConfirmedArrivals` füttert `ZombieWavePolicy.Evaluate`.
  (2) Deutlich größere, pausenlos nachfüllende Wellen: `WaveStartSize` 8→30,
  `WaveGrowth` 4→10, `SpawnIntervalSeconds` 1,0→0,75 (`MaxAlive` bleibt bei 20,
  Bereich 4-56); dabei den einen echten Stau-Risikofaktor gefunden und
  behoben — `ZombieProfilePlan.BackupCountForWaveStartSize` skaliert den
  nativen Backup-Cache mit der Wellengröße, sonst hätte eine 30er-Welle den
  festen Cache von 4 fast sofort leergezogen und wäre in
  `BotProfileClient`s synchronen Fallback gelaufen. (3) Munition/Verbrauchs-
  güter werden zwischen den Wellen aufgefüllt, ohne neue Inventar-Mutations-
  code zu schreiben: `ZombieRefillPolicy` (reine WANN-Entscheidung, genau
  einmal je Wellenende, nur solange der Spieler lebt und nicht mitten im
  Respawn) plus `ZombieLoadoutRefill` (WIE), das den bestehenden Live-Kit-
  Pfad `LiveKitRespawn.AtRoundBreak` wiederverwendet (denselben, den Gun
  Game/Tactical schon zwischen Runden nutzen) — akzeptierte Nebenwirkung:
  das stellt auch Rüstungs-/Waffenhaltbarkeit wieder her. (4) Neue Option
  **OWN GEAR** (`Settings.ZombieOwnGear`, Standard aus, F12 und neue
  Mutator-Zeile im Kartenmenü): eigenes, geschütztes Gear statt Zufalls-Kits,
  exakt über Tacticals vorhandenen `TeamArenaLoadoutSnapshot`-Mechanismus
  (Einfrieren beim Armen, Erfassen der Eintritts-Ausrüstung, Überspringen von
  Kit-Tausch/Ladebild-Preload/Containerleerung, Wiederherstellung bei jedem
  Tod und bei der Wellen-Pause-Auffüllung). Review-Fund und Fix noch am
  selben Abend (`1203438`): `ZombieMode._local` wurde nie erneuert, wenn der
  Kit-Tausch oder die eigene Pausen-Auffüllung selbst den Live-Körper
  tauschte (nicht nur ein echter Tod) — Unitys `MonoBehaviour`-Gleichheit
  ließ die zwischengespeicherte Referenz auf den zerstörten Körper fortan
  für immer `!= null` lesen wie `null`, lautlos, ohne Logzeile. Dadurch
  füllte die Pausen-Auffüllung höchstens einmal pro Raid statt einmal je
  Welle auf. Fix: `_local` pollt jetzt `_world.MainPlayer` und heilt sich
  selbst, nach demselben Muster wie `TeamDeathmatchMode.Tick`. Offline danach
  852 bestanden, 2 vorbestehende Skips, 0 fehlgeschlagen, 854 gesamt;
  No-Deploy-Sentinel vor/nach jedem Lauf abwesend bestätigt; Client-/
  Server-Build je 0 Fehler.
  **v1 im Spiel getestet (SPT-GPU-2, 2026-10-06 abends, drei Raids):** Raid A
  (`cf100ab`, Zufallskit, noch Wellenwerte 8/4/1): vier verteilte Kopfschuss-
  Kills, der Wachhund blieb still und feuerte erst 100 s nach dem letzten Kill,
  danach Nachfüllen nach Welle 1 (Magazin voll, Kills erhalten, keine Tode,
  Spieler an einen Spawnpunkt versetzt). Raid B (`cf100ab`, OwnGear, 30/+10/0,75):
  Own Gear hält die Ausrüstung über vier Tode (Respawn 0,07-0,19 s), Profil
  vor/nach identisch (nur Bajonett-Testloadout), 119,6 fps bei 20 Zombies — aber
  der Wachhund feuerte nie: er wurde nur bei vollständig gespawnter Welle scharf,
  die 30er-Welle blieb bei MaxAlive 20 gesättigt (20 lebend, 10 offen). Fix
  `39e193d`/`acd57ff` (scharf bei Sättigung oder komplett gespawnt; bei Rest
  bleibt die Welle in Phase Wave und spawnt sofort weiter), in Raid C
  (`acd57ff`, OwnGear) bestätigt: Wachhund 18:29:50 und 18:31:38, Own-Gear-
  Nachfüllen nach Welle 1, Welle 2 = 40. Ausgeliefert zuletzt die Client-DLL aus
  `acd57ff`, SHA256 `fdff66da1ce6f4be4573b25580e39bd2006bb30af815254f324cc61f562765c5`
  (Server unverändert). Host-2-Einstellungen danach: OwnGear `false`, die drei
  Wellenwerte bleiben auf den neuen Standards 30/10/0,75 (BepInEx behält
  gespeicherte Werte: auf einer bestehenden Installation greifen neue Standards
  erst nach Zurücksetzen in F12 oder Löschen der cfg-Einträge). Weiter offen:
  Wellenende durch eigene Kills, Nachfüllen nach Welle 2, Own Gear mit echtem
  Loadout, In-Place-Nachfüllen statt Versetzen. Details:
  `GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`, Abschnitt "v1 (06.10.2026
  abends)"; Checkliste mit Stand in `ZOMBIE-MODE-REVIEW.md`, Abschnitt "v1
  (2026-10-06 evening)".
  **v2 (2026-10-06 nachts, Equipment-Choice ins linke Panel + ANNOUNCER-
  Mutator, neun weitere Commits auf `091c5f9`: `f79efc4` `3080683` `11c97e0`
  `ab2ed91` `2f1df78` `f82c187` `a32dda3` `2124f17` `091c5f9`):** Nutzerwunsch
  nach dem v1-Playtest, OWN GEAR aus dem rechten Mutatoren-Panel ins LINKE
  Modus-Panel zu verschieben (als Equipment-Wahl, die später für die meisten
  Modi gelten soll), plus ein neuer Mutator ANNOUNCER. (1) `Modes.
  ArenaEquipmentChoice` ist eine reine Pro-Modus-Verfügbarkeitstabelle (heute
  bietet nur Zombie eine Wahl an); die neue Zeile sitzt jetzt im
  EQUIPMENT-Fakt-Slot des linken Panels (`ArenaEquipmentChoiceView`/`-State`)
  und ersetzt dort den reinen Fakttext, sobald ein Modus eine Wahl anbietet —
  alle anderen Modi (inklusive Tactical, dessen eigener automatischer
  Own-Gear-Pfad unverändert bleibt) zeigen weiter nur den Fakt.
  `ArenaMutator.OwnGear` ist entfernt (wieder 15 Mutatoren); derselbe
  `Settings.ZombieOwnGear`-ConfigEntry, dieselbe Arm-Sperre und derselbe
  eingefrorene `ArenaSession.ZombieOwnGearSelection`-Capture/Reset-Vertrag
  bleiben unverändert — nur der Schreibpfad wechselt zu `Settings.
  TrySelectEquipmentChoice`. (2) Neuer Mutator **ANNOUNCER**
  (`ArenaMutator.Announcer`, wieder 16 Mutatoren): schaltet den
  Arena-Announcer PRO MODUS wie Killcam — Standard AUS bei Zombies, AN bei
  den anderen vier Modi, je ein neues `ConfigEntry<bool>` pro Modus-Sektion
  über `Settings.BindAnnouncer`; der bestehende globale Audio/Announcer-
  Hauptschalter bleibt unverändert als Mastergate, Lautstärke bleibt global.
  HUD-Textdurchsagen ("WAVE n") sind kein Announcer und bleiben unverändert.
  Review-Fund noch am selben Tag (ANN-1, `091c5f9`): der Pro-Modus-Wert wurde
  live statt eingefroren gelesen, anders als bei jedem Geschwister-Setting
  (Killcam, GunGameLadderSelection, ZombieOwnGearSelection) — ein F12-Eingriff
  mitten im laufenden Raid hätte sofort gewirkt. Fix: neues `ArenaSession.
  AnnouncerEnabledSelection`, beim Armen eingefroren und an denselben sechs
  Stellen zurückgesetzt wie `ZombieOwnGearSelection`; alle vier Lesestellen in
  `AnnouncerService` laufen jetzt über einen gemeinsamen
  `IsEnabledForLiveMatch()`-Helfer. Offline danach 870 bestanden, 2
  vorbestehende Skips, 0 fehlgeschlagen, 872 gesamt; No-Deploy-Sentinel
  vor/nach jedem Lauf abwesend bestätigt; Client-/Server-Build je 0 Fehler.
  Vier weitere Commits auf demselben Branch (`e73351b`, `c4ba796`, `2739263`,
  `0e05208`) stammen aus einer anderen, parallel laufenden Sitzung im selben
  Worktree und beheben vorbestehende, von diesem Nachtrag unabhängige
  Offline-Tool-Build-Brüche (AreaTeamMenuChecks-Panelgrenzen, RoleLabelChecks-
  Stand-in, ParticipantMenuChecks-Ceiling/Theme-Link) — per `git diff --stat`
  bestätigt, dass dieser Nachtrag keine der betroffenen Dateien berührt.
  **v2 wurde zum Zeitpunkt dieses Absatzes noch NICHT im Spiel getestet** —
  offen für den nächsten GPU-Raid: die neue Equipment-Choice-Zeile im linken
  Panel sichtbar und für Zombie umschaltbar, N/A/versteckt für alle anderen
  Modi; das Mutatoren-Panel zeigt 16 Zeilen inklusive ANNOUNCER; ein
  Zombie-Raid bleibt beim Announcer still, ein Deathmatch-Raid bleibt
  hörbar; Killcam unverändert. Details: `ZOMBIE-MODE-REVIEW.md`, Abschnitt
  "v2 (2026-10-06 night)".
  **Performance-Messung mit 56 gleichzeitigen Zombies (SPT-GPU-2, 2026-10-06
  20:07-20:17, Build `acd57ff`, v1-Client):** gemessen, der Punkt "Performance bei
  MaxAlive 40-56" oben ist damit beantwortet. Auf dem Host liefen die eigenen
  Nutzer-Einstellungen (MaxAlive 56, Spawnintervall 0,2 s, Schützenanteil 20 %,
  OwnGear an, Godmode). 56 Zombies am Spieler kosten 17,2 ms (58 fps, Profiler
  offen) gegenüber 8,4 ms (119,7 fps) bei 20, rund 0,25 ms je zusätzlichem Zombie.
  Instrumentiert sind ≈ 7,3 ms: ArenaMode nur 0,48, dazu BigBrain 0,85, SAIN 0,67,
  PitFireTeam 0,43, Waypoints 0,13 und EFTs eigene Hooks 4,6; die übrigen ~10 ms
  sind Rendering, Animation, Physik und Audio von 56 Körpern plus Leichen und mit
  dem heutigen Werkzeug nicht aufzulösen (die Summe ist wegen verschachtelter
  Zeilen eine Obergrenze, siehe Vorbehalt im Ergebnisdokument). Zahlen und
  Tabellen: `GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`, Abschnitt
  "Performance-Messung mit 56 gleichzeitigen Zombies"; Kandidatenliste mit Nutzen
  und Risiko: `ZOMBIE-MODE-REVIEW.md`, Abschnitt "Performance at 56 concurrent
  infected (measured 2026-10-06)". Oberster Kandidat ist der Leichenabbau im
  Zombie-Modus: Der Wachhund räumt bis zu 56 Körper auf einmal ab,
  `CorpseCleanupService` gibt aber nur 8 je 5-s-Sweep frei, sodass bis zu 60 tote
  Körper ~35 s lang als vollständige, aktive Spieler-Hierarchien in der Szene
  bleiben (Ragdoll, deaktivierte aber weiter registrierte Renderer, `LootList`,
  jede Szenensuche — korrigiert 2026-10-07: NICHT `Player.ComplexUpdate`, das
  eine Leiche schon einen Frame nach dem Tod verlässt, siehe
  `ZOMBIE-MODE-REVIEW.md`, Abschnitt "v3 (2026-10-07)"); ein höheres
  Freigabelimit oder eine frühere Freigabe nur für Infizierte verkürzt diese
  Verweildauer, aber die Freigabereihenfolge ist tragend
  (`CORPSE-CLEANUP-REVIEW.md`), also braucht es ein eigenes Review und einen
  Messraid. **v2** (Equipment-Wahl im linken Panel, Announcer-Mutator,
  Commits `f79efc4`..`d302fd7`) ist gebaut und als Client-DLL mit SHA256
  `0B765EA6878B256CB32E580BF111488911118A246086503D9DC510AA50E4125B` lokal und
  per Syncthing auf Host 2 ausgeliefert (EFT neu gestartet, 24/24 identisch) und am
  06.10. um 22:07-22:11 auf GPU-2 nativ geprüft: linke Zeile „EQUIPMENT: Own gear"
  mit den Knöpfen „Arena kits | Own gear" unter den Fakten der ZOMBIES-Zeile,
  Klick schreibt `Zombie / OwnGear` (true → false → true belegt); bei DEATHMATCH
  ist die Zeile ausgeblendet (dort stehen wie bisher die Deathmatch-Regeln);
  Mutator-Panel mit 16 Zeilen, **ANNOUNCER** zeigt bei Zombie OFF, bei Deathmatch
  ON, kein OWN GEAR mehr rechts; keine Überlappungen (Screenshot gesichtet). Kurzer
  Zombie-Raid: Log „Arena announcer attached: mode=zombie, enabled=False", keine
  Error-Zeilen. Nicht geprüft: hörbarer Announcer in einem Deathmatch-Raid
  (Standard ON, nur per Mutator-Anzeige und Offline-Tabelle belegt). Kleiner
  Nachtrag (erledigt 2026-10-08, Branch feature/menu-polish): die Fußzeile des
  Mutator-Panels nannte „Killcam/tracers: per mode", der Announcer fehlte dort —
  Fußzeile und Legende sind inzwischen ganz entfernt. Der Satz weiter oben, v2 sei
  noch nicht im Spiel getestet, ist damit überholt.
  **v3** (07.10.2026, schnellere Leichenfreigabe im Zombie-Modus, setzt den
  oben genannten Top-Kandidaten um): `dde0370` reparierte zuerst den offline
  Harness (fehlende Stubs für `ArenaModeCatalog.ZombieId`/`ArenaSession.
  ActiveMode` nach `d00cd17`, plus einen unabhängig kaputten
  Shot-in-Flight-Mutanten seit `79eb2ab`); `6bebae4` liefert die eigentliche
  Änderung: Zombie sweept jetzt alle 1 s statt 5 s (`ZombieCadenceSeconds`)
  und gibt zusätzlich zwischen den Sweeps frei (`DrainBetweenSweeps`), je
  höchstens zwei Körper pro Frame (`ZombieMaxReleasesPerFrame`) statt acht
  pro Sweep — über denselben `ReleaseTrimmed`/`MayRelease`-Pfad wie der
  Sweep selbst, ohne eigenen Reassert- oder Trim-Durchlauf und ohne den
  Sweep-Zähler zu erhöhen. Jeder andere Modus bleibt exakt beim bisherigen
  5-s/8er-Verhalten; der Ragdoll-Gate (Körper fällt sichtbar zu Boden) ist
  unverändert. Offline grün in diesem Worktree:
  `Verify-Regression.ps1` 309 Assertions/35 Mutanten,
  `DynamicMapCorpseChecks/Verify.ps1` dieselbe Suite plus 61/8,
  `dotnet test ArenaMode.Tests` (No-Deploy-Sentinel) 870 bestanden/2
  übersprungen/0 fehlgeschlagen, `dotnet build ArenaMode.Client -c Release`
  (No-Deploy-Sentinel) 0 Fehler. GPU-2-Raids 07.10. 20:21–20:36 (Client
  `736A11CE89618A6A9EFE2435D43DBD48A5ED8FFA6ECEF3A02D024F448DC47E72` =
  `43cc7c3`, lokal + Host 2 deployt, 24/24 identisch; Server unverändert
  `0A826E8B…`): Wachhund-Abräumung 36 Körper in 5,1 s und 47 Körper in 4,6 s
  vollständig freigegeben (06.10. laut Host-Log: 46 Körper in 33,5 s), Sweeps
  im 1-s-Takt, Freigabe 2 s nach dem Trim, 56 lebende 53,7 fps → nach der
  Abräumung 113,6 fps, 0 Error-Zeilen, Flag danach `false`. Zwei
  vorbestehende Warnungen (auch im 06.10.-Log): `Arena damage exception`
  (NRE in `BotMemory.GoalTargetBeingHitAction` beim Zombie-Treffer auf den
  sterbenden Spieler) und `no profiles for infectedAssault` in Welle 2.
  Details: `GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md`, Abschnitt
  „v3-Messraids", und `ZOMBIE-MODE-REVIEW.md`, „v3 (2026-10-07)" → „GPU
  result". Die Korrektur der ComplexUpdate-Lesart (`facf269`, `1e69028`)
  steht in denselben Abschnitten.

- **Akimbo-Pistolenmutator (2026-10-05, aktueller Client):** `97deb6b`,
  `f11f998`, final `fadec49`. Kartenmenü **AKIMBO PISTOLS** / F12
  `Combat / AkimboEnabled`, Standard AUS; dauerhaft gespeicherter Schalter,
  automatisch auf die gehaltene Pistole einschließlich Automatik/Handrevolver.
  Gleichzeitige Geschosse, gemeinsames Magazin/Reload/Audio/Rückstoß, kostenloses
  Bonusgeschoss; kein echtes abwechselndes Schießen und kein Pickup. Bindet nach
  Waffen-/Magazinwechsel und Respawn neu, beendet sich außerhalb einer gültigen
  lokalen Arena. Echte Waffenstörung sperrt, natives Controller-Flag allein nicht.
  Grobe linke Stützhand bleibt akzeptierte Grenze. 15 Mutatoren passen sichtbar
  neben Factory-Thumbnail/Navigation. 2086 Akimbo-/946 Menü-Assertions,
  11+14 Fehlerkontrollen, unabhängige Reviews und isolierter Build bestanden.
  GPU-2 prüft Glock 17, automatische Glock 18C, Magazinmodellwechsel, Lifecycle;
  finalen Revolverleerstand/Reload und gewöhnlichen PMC mit aktiviertem Schalter.
  Einzelne Tests laufen auf dokumentierten Vorstufen, nicht jede Waffe/Animation
  ist zertifiziert. Client lokal/GPU-2 SHA256
  `28947B729CEE2F8DDA5FF61D6F1B97823561C21BAFB81070C25170777563D9E2`.
  Finale Instanz `4201422c`, 24/24 Identitäten; Server/Core/BigBrain unverändert.
  Arena-Profilkern vor normalem Kontrollraid unverändert belegt; dessen separate
  native Kosten danach durch explizite Testfixture-Wiederherstellung zurückgesetzt.
  Arena-Konfiguration restauriert, Akimbo AUS, Spiel/Server beendet. Final kein
  Error/Fatal; zwei frühere BotFirstAid-Exit-NRE separat dokumentiert, nicht gelöst.
  Vertrag `AKIMBO-REVIEW.md`, Details `GPU-AKIMBO-MUTATOR-RESULTS-20261005.md`.

- **Akimbo-PM-Prototyp (2026-10-05, Vorgänger):** `3c16748`, ausdrücklich
  standardmäßig AUS, noch kein Pickup. F12 `Arena akimbo prototype / Active`
  während einer laufenden Arena mit gehaltener PM: 20 Spielsekunden, sichtbare
  Kopie und ein natives Bonusgeschoss pro Schuss; gemeinsamer Abzug/Magazin/
  Reload, Bonusmunition kostenlos, ADS gesperrt. Kopie pausiert bei Interaktionen;
  Tod, Waffenwechsel, Runden-/Raidende oder AUS räumen auf. Keine zweite native
  HandsController-Instanz oder persistente Waffe. Client lokal/GPU-2 SHA256
  `656FBCEA0B75E88A7889359F223CD6672E28C594836609F3B33F97DD2E4BD6A7`.
  66 Assertions, fünf negative Kontrollen, isolierter Build und unabhängiges
  Review bestanden. GPU-2 `5e8928ac` bestätigt Darstellung, Bonus-Emission,
  Reload/Inventar/Sprint/ADS und Cleanup einschließlich Respawn. Linke Finger
  bleiben sichtbar eine grobe Stützhandpose; Zusatzschaden nicht separat vom
  Originaltreffer isoliert, kein Anspruch auf fertiges Dual-Wield. Profilkern
  unverändert, Testkonfiguration restauriert, Spiel/Server beendet. Server/Core/
  BigBrain unverändert. Vor Pickup/weiteren Waffen erst PM-Griff und gewünschtes
  Schuss-/Munitionsverhalten ausarbeiten. Vertrag `AKIMBO-REVIEW.md`, Abnahme und
  explizite Grenzen `GPU-AKIMBO-RESULTS-20261005.md`.

- **Movement-Master im Menü (2026-10-05, Vorgänger):** `f2e5a88`
  setzt Bunnyhop standardmäßig AN; `d30e3b0` ergänzt FAST MOVEMENT als direkten
  Schalter für `Movement / Enabled`. AUS deaktiviert alle lokalen Movement-Boni
  und -Fähigkeiten gemeinsam, ohne Einzelwerte/Multiplikatoren zu ändern;
  Bot-Movement bleibt unabhängig. Vierzehn Mutatoren, `91a90ef` kürzt nach
  GPU-Sichtprüfung die Hilfetexte. Client lokal/GPU-2 SHA256
  `DF5B3A97F044E96CE54E27464A8DB26463CC475B2B2C459010A0BEA5A2EEE9C0`.
  846 Menü-Assertions, 14 Fehlerkontrollen, Review und isolierter Build grün.
  Finales GPU-2-Menü (`afffd8bb`) mit 24/24 Identitäten geprüft: OFF/N/A/ON,
  lesbare Texte, Factory-Preview und freie Navigation. Kurzer Factory-Raid auf
  `d30e3b0` bestätigt bestehenden Master-Guard; finale Änderung nur Texte.
  Profilkern/Konfiguration unverändert, Bunnyhop AN, Spiel/Server beendet.
  Server/Core/BigBrain unverändert. Bericht und explizite Prüfgrenzen:
  `GPU-MOVEMENT-MASTER-RESULTS-20261005.md`.

- **Bunnyhop (2026-10-05, Vorgänger):** `93b8ba6` + native Korrekturen
  `2d3f8f7`, `5a6b90b`, `6356bef`. Neuer Mutator BUNNYHOP, Standard AUS;
  150-ms-Eingabepuffer, native Landung, +6 % Carry pro Hop bis +25 %/12 m/s.
  SimpleCC-Kontakt wird über Substeps erhalten und mit DoubleJump geteilt;
  keine Fake-Grounded-Flags oder erzwungenen Animator-Übergänge. Finaler Client
  lokal/GPU-2 SHA256 `5674CAF6746E453246B9DB76D773526AD3A1D649E894E8B8404ED118344034FF`.
  GPU-2 Prozess `e34bb467`: Sprint-Kette 2, Geh-Kette 3 Folgesprünge; OFF,
  Master-OFF und erneut ein Hop nach Respawn bestätigt. 76 Assertions/13
  Fehlerkontrollen plus Integrationssuiten, unabhängiger Review und Build grün.
  Profilkern und bestehende Konfiguration unverändert, Bunnyhop wieder AUS,
  Spiel/Server beendet. Server/Core/BigBrain unverändert. Bericht und explizite
  native Grenzen: `GPU-BUNNYHOP-RESULTS-20261005.md`; Vertrag:
  `BUNNYHOP-REVIEW.md`. Separat offen: Factory-Pickups im finalen Test vom
  unterirdischen Startanker sämtlich übersprungen; Kill-Hitmarker-PCM-Warnung
  weiterhin vorhanden. Keine Error/Fatal-Zeilen im finalen Log.

- **Solo-Tracer / Surgical Walk (2026-10-03, Vorgänger):** `4ae504a`
  ergänzt getrennte Tracer-Opt-ins mit Standard AUS für Gun Game und Tactical
  1v5; TDM/PFT behalten Effects/TeamTracers. `1f538d4` erlaubt CMS/Surv12-Gehen
  für den aktuellen lokalen Arena-Spieler, höchstens 1 m/s nach Bewegungsboni.
  [Tracer-Teil überholt 2026-10-08: Gun Game/Tactical-1v5-Opt-ins entfernt, gefärbte
  Tracer sind nur noch in TDM und geschütztem Tactical 5v5 verfügbar.]
  Native Med-Flags und Behandlungsoperationen bleiben unverändert. Client
  lokal/GPU-2 SHA256 `56F7DAA61B5C99A9630B274937868A3E6AF9937B53A9398060560FE6CEC3652B`.
  GPU-2-Menü und Factory-Raid geprüft, Prozess `b68a888e`: Surgery 1 m/s,
  Master-Off 0 m/s, nach Abschluss wieder 3,7 m/s; Abbruch gibt Waffe frei.
  CMS-Abschluss ebenfalls bestätigt, seine Geschwindigkeit nicht separat
  quantifiziert. Profil- und Config-Prüfung nach Raid sauber. EFT beendet,
  bereits vorher laufender Server unverändert an. Keine Server/Core/BigBrain-
  Änderung. Testbericht `GPU-SURGICAL-WALK-RESULTS-20261003.md`, Vertrag
  `SURGICAL-WALK-REVIEW.md`. Separater Loghinweis zur Kill-Hitmarker-Audiodatei
  dokumentiert, keine Error/Fatal-Zeilen.

- **Mutatoren / Kartenzeiten (2026-10-03, Vorgänger):** `5fabaf6` ergänzt
  Air vault, Slide, Infinite stamina und No fall damage (zwölf Schalter insgesamt)
  sowie Deathmatch/ColoredTracers=false als eigene gespeicherte Wahl. Die
  bisherige Effects/TeamTracers-Einstellung für andere Modi bleibt erhalten.
  [Überholt 2026-10-08: Deathmatch/ColoredTracers ist entfernt, siehe Menü-Politur oben.]
  GPU-2 zeigte einen READY-Overlap; `5a63fa9` behebt ihn ohne kleinere Schrift
  oder Verschieben der Kartenvorschau. Client lokal/GPU-2 SHA256
  `6EB0A8BC6A59803ADC88C4B0ECDC1D8CA9A5F1531741B544ACCACC2E876077DE`.
  ArenaMapPort separat `aa0a8c1`/`1cb4572`: Bay5 native 12:00/00:00 inklusive
  echter Raidzeit/Servergear, Equator-Nachtregenicon mit nativer Rückstellung.
  Beide MapPort-DLLs über den neuen sicheren -DllOnly-Weg lokal/GPU-2 eingespielt.
  Keine Bundles und keine Modusfreigaben geändert. Native 1080p-Menüabnahme auf
  GPU-2 bestanden, Prozess `52a36096`, 24/24 geladene Identitäten korrekt.
  Testwerte restauriert, Spiel/Server beendet. Kein Raid in diesem Lauf; Bay5s
  Nachtbeleuchtung noch nicht neu visuell geprüft. Bericht, Hashes und Grenzen:
  `GPU-MUTATORS-MAP-OPTIONS-RESULTS-20261003.md`; Ausbau aller Karten/Modi:
  `MAP-MODE-COMPATIBILITY.md` (`f800b08`). GPU-1 war nicht erreichbar und wurde
  nicht direkt beschrieben; Nutzer hatte GPU-2 ausdrücklich freigegeben.

- **Kartenbuttons / eigene Tode (2026-10-01, Vorgänger):** `a387581`
  erhält die native TMP-Größenberechnung unter den goldenen Bay5-/Equator-Labels;
  `8fd3e03` ergänzt reine Killfeed-Anzeigen für eigene Tode ohne fremden Angreifer
  und erfolgreiches Aufgeben. Keine neuen Kill-Punkte/Assists. Client lokal/Host 1
  eingespielt, SHA256 `39CA280EF8EC44D41A9281BEAC12982446D6694FADE722E1FD0F372F013173B3`.
  Server/Core/BigBrain unverändert. Offline-Prüfungen, unabhängiger Review und
  Build bestanden. Auf SPT-GPU-1 nativ bestätigt: komplette Klickflächen und
  Auswahlhintergründe beider Maps, PMC-Restaurierung, eigene Todeszeile nach
  erstem/zweitem Körper sowie GAVE UP nach Backspace. Kein Zusatzscore.
  Zusatztest Selbstgranate wegen aktivem Brand-Prototyp und Bot-Schaden nicht
  aussagekräftig; kein Explosion-Pass behauptet. Profilkern und Arena-Konfiguration
  unverändert; Spiel/Server wieder aus. Bericht und Grenzen:
  `GPU-HITAREAS-SELF-DEATHS-RESULTS-20261001.md`; Auslieferungsbeleg
  `HITAREAS-SELF-DEATHS-DEPLOYMENT-20261001.json`.

- **Thumbnail-/Marker-Revision (2026-09-30, Vorgänger):** `a8b8dd1`.
  Keine Kartenbeschreibung mehr, dafür natives Thumbnail; Customs behält seine
  Arealvorschau. Oberes Feld immer 320 Referenzpixel hoch, Mutatoren fest bei
  Y=508. BAY 5/EQUATOR im Arena-Kartenscreen ohne Präfix, goldene eigene Labels
  mit dunklem Auswahl-Gold. Bay 5 oben im Hafen, Equator südöstlich Interchange;
  native Daten/IDs/Events unverändert, Präsentation beim Verlassen restauriert.
  191 Menü-Assertions +14 Fehlerkontrollen, 4419 Karten-Assertions +5
  Fehlerkontrollen, unabhängiges Review und isolierter Build bestanden.
  Client lokal/Host 1 eingespielt, SHA256
  `1E7A0EE9D8584B15038961B60FBCFCB1093312646A09EAA50AE70E5D3E5E036D`;
  Server/Core/BigBrain unverändert. **Noch keine native Abnahme dieser Revision:**
  Windows-MCP auf Host 1 war nicht erreichbar; Disk-Zugriff funktionierte.
  Keine Prozesse/Testwerte verändert. Nächste Prüffolge und Grenze:
  `GPU-MAP-PRESENTATION-TEST-20260930.md`; Auslieferungsbeleg
  `MAP-PRESENTATION-DEPLOYMENT-20260930.json`.

- **Inline-Mutatoren (2026-09-30, ersetzt die Popup-Darstellung unten):**
  `d0a602e`: alle acht Schalter dauerhaft rechts, ON mit grünem Chip/Streifen,
  OFF/N/A grau. Karteninfos kurz oben rechts; bei Customs kompakte Arealkarte
  darüber mit weiterhin bedienbarem AREA/OVERVIEW. Native Zeit-/Wetterwahl und
  Navigation bleiben frei, gewöhnliche PMC-Karteninfos werden wiederhergestellt.
  188 Menü-Assertions + 14 Fehlerkontrollen, 4393 Karten-Assertions + 5
  Fehlerkontrollen und isolierter Build bestanden; unabhängiges Review ohne
  offene konkrete P0–P2. Client lokal und Host 1 eingespielt, SHA256
  `3388B737E8129A7F735C8CA4C8E8A5D6847A0695385F18844993E10B5C7004FD`.
  Server/Core/BigBrain unverändert. Native 1080p-Menüprüfung erfolgreich,
  inklusive Farbwechsel/F12-Wert, Factory-Zeitwahl, Customs und PMC-Restaurierung.
  Kein Raid; Testwerte restauriert, Spiel/Server wieder aus.
  `GPU-INLINE-MUTATORS-RESULTS-20260930.md` und
  `INLINE-MUTATORS-DEPLOYMENT-20260930.json` enthalten Belege/Grenzen.

- **Mutatoren-Menü (2026-09-30):** MUTATORS ersetzt den einzelnen Killcam-Button
  im Kartenmenü. Acht bestehende F12-Optionen in kompakter Ansicht: Double jump,
  Air control, Airborne hip fire, Backpack items, Killcam, Pickups, Colored tracers,
  Team laser colors. Gleiche gespeicherte Werte, Killcam weiter pro Modus;
  keine neuen Defaults oder Gameplayänderungen. N/A erklärt Abhängigkeiten;
  Arm-/Raid-Sperre und Callback-Bereinigung bleiben erhalten.
  Details und Prüffolge: `MUTATORS-MENU-REVIEW.md`.
  Umsetzung `2ff4722`; 179 Menü-Assertions und 14 kompilierte Fehlerkontrollen
  bestanden, unabhängiges Review ohne konkrete offene P0-P2. Isolierter Build
  ohne Fehler, lokal und auf GPU-Host 1 eingespielt; Server/Core/BigBrain unberührt.
  SHA256 `A2CF4757A74E5BF679FE075ECFEB2054183B1FF3105BAAA730774CB32480A9E7`;
  Beleg `MUTATORS-DEPLOYMENT-20260930.json`. Native 1080p-Menüprüfung auf Host 1
  erfolgreich: Schalter/F12-Werte, Modusverfügbarkeit, Abschirmung, Close,
  Außenklick, Escape/Wiederöffnen und normaler PMC-Kontrollschirm.
  Kein Raid; Testwerte zurückgesetzt, Spiel/Server wieder beendet.
  Bericht/Grenzen: `GPU-MUTATORS-RESULTS-20260930.md`.

- **Neue Standardwerte (2026-09-30):** Auf Nutzerwunsch HitmarkerVolume und
  AnnouncerVolume jeweils 0,7; BotWalkSpeedMult und BotSprintSpeedMult jeweils
  1,5; VisibleLaserBrightness und VisibleLaserBeamBrightness jeweils 3;
  RagdollImpulseScale 5; KillfeedOtherKills true; AirControlDegreesPerSecond 80;
  SpawnProtectionSeconds 4. Nur Bind-Defaults geändert, keine Migration oder
  Überschreibung gespeicherter Einstellungen. Neue Konfigurationen und das
  Zurücksetzen auf Standard verwenden diese Werte. Bereiche, Mechanik und
  gespeicherte Nutzer-/Testkonfigurationen bleiben unverändert.
  Umsetzung `0312f78`; Menü-/Konfigurationsharness 121 Assertions bestanden,
  unabhängiges Review ohne Befund und isolierter Release-Build erfolgreich.
  Client lokal eingespielt, SHA256
  `8B1D65D8B075DA78DED7C9A336C0392173A52A8A47C57921D572A022DA600CB7`.
  Server/Core/BigBrain unverändert. Keine direkten GPU-Schreibzugriffe;
  externe Synchronisierung übernimmt, deren Abschluss ist hier nicht geprüft.
  Kein neuer Ingame-Test. Beleg: `DEFAULTS-DEPLOYMENT-20260930.json`.

- **Salven-Schadenszahlen und Killcam pro Modus (2026-09-30):**
  `99a91f2`: Option `HUD / CombineDamageNumbers` standardmäßig aus, über F12
  und das bestehende HUD-Panel schaltbar. Treffer desselben physischen Körpers
  innerhalb 0,25 s ergeben eine stabil platzierte Summe; HP/Rüstung getrennt,
  Rohereignisse, Marker/Sounds, Punkte und Assists unverändert. Siehe
  `BURST-DAMAGE-REVIEW.md`.
  `b6fb823`: Kartenmenü `KILLCAM: ON/OFF` für jeden Modus separat
  (Einschalten = 3 s, F12 weiterhin 0–10 s). Benötigt aktive KillcamReplay-Mod;
  ohne sie N/A. Wahl beim Arm eingefroren. Respawn wartet auf Vorlauf, Playback
  und tatsächliche Kamerarückgabe, ohne zusätzliche feste Pause. Tactical/PFT
  behält seine Rundenregeln; lange Replays verbrauchen nicht vorzeitig das
  geschützte Respawnfenster. Tote Körper verlieren nur ihre störenden visuellen
  Gesundheitseffekte. Details: `KILLCAM-MODES-REVIEW.md`.
  `57a45f9` repariert veraltete Harness-Annahmen ohne Gameplayänderung;
  siehe `TEST-HARNESS-MAINTENANCE-20260930.md`.
  Gezielte Prüfungen: Burst 225/11, Replay 170/8, Menü 121/12, visuelle Scope
  80/9 (Assertions/kompilierte Fehlerkontrollen), alle grün. Breite Regression:
  Deathmatch 163, TDM-Resultat/Exit 227, TeamGiveUp 618/16, TDM-Runtime 332/28
  plus BurstIntegration 21/8, PFT 276, TeamArenaExpansion 849 bestanden.
  Beleg `artifacts/arena-killcam-integration/cfd0c0a2048547c1819c192050504a86/`.
  Unabhängiges Review fand Layoutkollision, tote Gesundheitsdarstellung und
  langes PFT-Replay; behoben und nachgeprüft, keine offenen P0–P2.
  **Gemeinsamer Auslieferungsstand `de25153`:** Der parallel schon eingespielte
  Molotov-Quellstand `f3183a2` wurde konfliktfrei übernommen, damit sein Prototyp
  erhalten bleibt. Erneutes unabhängiges Integrationsreview sowie Burst 225/11,
  Molotov 57/8 und AssistCapture 130/5 mit 39 nativen IL-Verträgen grün.
  Sauberer Client-Build (vier bestehende Nullable-Warnungen, keine Fehler),
  lokal und auf GPU-Host 1 (.114) unter exklusiven Dateisperren eingespielt.
  SHA256 `1E4BD4313D820CC1C401424225DB22DB6BFC47419C000A93CE4CA544D2AFCBBF`;
  `BURST-KILLCAM-DEPLOYMENT-20260930.json`. Server/Core und vorhandener
  Molotov-BigBrain-Companion unverändert.
  **Host 2 (.109) beim Deployment nicht angefasst:** dort lief eine andere
  Sitzung mit Client `f3183a2`. Die bestehende externe Synchronisierung und ein
  anschließender Neustart dieser Sitzung haben danach den neuen Client geladen:
  Disk-SHA identisch zu oben, UiBridge-Instanz `c692bb08`, Start 11:27:33,
  meldet alle 24 maschine-Plugins als VM = Host-Disk = geladen identisch.
  Nur lesend geprüft; kein Eingriff in deren Steuerung und keine daraus
  abgeleitete Gameplay-/Killcam-Abnahme.
  Kein Spielstart oder Raid für diese Änderung; sichtbare Salven, native
  Menüposition, Replay/Skip/Kamerarückgabe und PFT-Rundenwechsel noch im Spiel
  prüfen. EFT vor dem Test neu starten. Fremde Worktrees/Branches und die
  laufende zweite GPU-Sitzung bleiben erhalten.

- **Brandgranaten-Prototyp (2026-09-30, eigener Branch):**
  `feature/molotov-prototype`, isoliert in `../ArenaMode-molotov`.
  F12 `Arena incendiary prototype / Enabled` standardmäßig aus; wandelt nur
  eigene RGD-5 während Live-Arena mit passendem BigBrain-Companion um.
  Sechs Sekunden begrenztes Bodenfeuer, 80 Basis-HP/s ohne Stapeln,
  Werfer-/Assist-Zuordnung und eigener Bot-Fluchtlayer. Vorläufig vorhandenes
  Granatenmodell, prozedurale Flammen/Knistergeräusch; kein Server-Update.
  Offlinetests/Review, Grenzen und Prüffolge: `MOLOTOV-PROTOTYPE-REVIEW.md`.
  Auslieferung und native Abnahme werden getrennt protokolliert; ein erfolgreicher
  Build allein bestätigt weder das Feuerbild noch das Botverhalten.
  Prototyp `79eb2ab`, Testbuild `bfdf327` enthält aktuellen Hauptbranch `50b7bc9`.
  Drei echte Equator-Würfe auf Host 1: Feuer/Eigenschaden/Respawn sowie Botkontakt,
  beobachteter Fluchtlayer mit Bewegung und Rückgabe an SAIN bestätigt. F12 wieder
  aus, Godmode aus, native Ressourcen leer, Profilvergleich mit dokumentierten
  normalen Hideout-/Serialisierungsdifferenzen. Grafik noch grober Platzhalter;
  kein gegnerischer Feuerkill und kein PFT-/TDM-Test. Bericht:
  `GPU-MOLOTOV-RESULTS-20260930.md`. Finale zusätzliche Reset-Bereinigung besitzt
  57 Assertions/acht Fehlerkontrollen und unabhängige Review.

- **Stacheldraht im Killfeed (2026-09-30):** Direkte native `Barbed`-Tode
  ohne Angreifer bekommen einen eigenen HUD-Eintrag mit Opfer/Teamfarbe,
  rotem Arena-Stacheldraht-Icon und `BARBED WIRE`. EFT löscht hier den letzten
  Angreifer; der bisherige Kill-Callback läuft deshalb nicht. Separater,
  Arena-begrenzter `Player.OnDead`-Postfix mit Matchidentität und schwacher
  Körper-Deduplizierung; kein künstlicher Kill, keine Punkte/Assists/Ansage
  oder Totenkopf. Killfeed-Schalter bleiben wirksam. Ein späterer tatsächlicher
  Blutungstod bleibt ein Blutstropfen, kein rückwirkend vermuteter Drahttod.
  Neues Originalasset mit Hash/Provenienz, weiterhin keine freie
  Weiterverteilungslizenz festgestellt. Details und Testgrenzen:
  `HUD-BLEED-POLISH-REVIEW.md`, Abschnitt Stacheldraht.
  Implementierung `8ff8551`; 103 Capture-/237 UI-/108 Text-Assertions plus
  zugehörige Fehlerkontrollen grün, unabhängiges Produktions-/Testreview ohne
  offene P0–P2. Sauberer Client-Release-Build lokal und auf beiden GPU-Hosts
  (.114/.109) unter exklusiven Dateisperren eingespielt. SHA256
  `3CAE4334A0052D2CE2BB38E68604B7ED742C0435096DAEE0A61DAEF19D8D56D7`.
  Server/Core/BigBrain unverändert; Beleg `BARBED-FEED-DEPLOYMENT-20260930.json`.
  Kein Spielstart/Raid durch diese Änderung: nativer Drahttod und sichtbare
  Anzeige im Labyrinth noch offen. Die zusätzliche Desktop-HarmonyX-Probe
  scheitert schon vor Hook-Bindung an einer CLR-Initialisierung; keine native
  Bindungsabnahme behauptet. Beim nächsten EFT-Start wird der neue Client geladen.

- **HUD-/Avatar-Nachbesserung (2026-09-29):** `2dfc1e7`. Nutzer hat die bisherige
  Totenkopf-Darstellung akzeptiert; jetzt +42 statt -42 HUD-Einheiten, also über
  dem Fadenkreuz. Arena-Kartenauswahl ohne Avatar unten rechts, nachfolgende
  Bestätigungs-/Ladebildschirme behalten ihren Modellbesitzer. Vorhandenes
  Blutstropfen-Icon bei tatsächlichem Light-/HeavyBleeding-Tod nun rot und ohne
  geerbtes Headshot-Badge; Punkte/Kill-Zuordnung unverändert. Keine neuen Assets.
  193 UI-Assertions/12 Fehlerkontrollen, Preview-Suite mit 144 Host-Lifecycle-
  Assertions/7 Fehlerkontrollen, native Hook-Kompilierung und Release-Build grün;
  zwei unabhängige Reviews ohne offene P0-P2. Lokal und auf GPU-Host 1 direkt
  eingespielt; Host 2 während fremder Sitzung nicht angefasst, anschließend neuen
  Disk- und geladenen ArenaMode-Build dort lesend bestätigt. Client SHA256
  `05A017E181049D48EC0332D19EFB88FE08727630C2EFA7CCA5DE2B552B5DE303`.
  UiBridge-0.7.3-Menütest auf Host 1 visuell bestanden: Karte ohne Modell,
  Bestätigung mit Modell, Rückweg erneut ohne Modell; Charakterauswahl sowie
  PMC-/Scav-Navigation funktionieren. Kein Raid, kein erneuter nativer Blutungstod.
  Keine Preview-Fehler; ein Mikrofonfehler schon beim Spielstart. ArenaMapPort-
  Build parallel abweichend und überlappende Zusatzkartenlabels in PMC/Scav als
  separater Nebenbefund dokumentiert. Host 1 im Hauptmenü, Steuerung frei.
  Details: `HUD-BLEED-POLISH-REVIEW.md` und
  `HUD-BLEED-POLISH-DEPLOYMENT-20260929.json`.

- **Kill-Totenkopf und entfernter HS-Text (2026-09-29):**
  Implementierung `838e909`, finaler sauberer Client-Build `e1d8975` (letzter
  Commit präzisiert nur eine Fehlerkontrolle). Bestätigte, wertbare eigene Kills
  zeigen das vorhandene Arena-Totenkopf-Asset zusätzlich zum Trefferkreuz:
  0,15 s halten, 0,65 s ausblenden, unterhalb des Fadenkreuzes. Eigene Lebensdauer,
  bestehender Hitmarker-Schalter, keine neue Einstellung. Killfeed ohne erzeugten
  `HS`-Text; das separate Kopftreffer-Icon bleibt. Keine Spiel-/Profiländerung.
  Details: `KILL-SKULL-REVIEW.md`. 160 Asset-/UI-Assertions, zehn kompilierte
  Fehlerkontrollen und Killfeed-Prüfungen bestanden, unabhängige Review ohne
  offene P0-P2. Client lokal und auf beiden GPU-Hosts (.114 und .109) eingespielt;
  Server/Core/BigBrainCompat unverändert. Client SHA256
  `7BFADE51FFB2ABF1DE5FBEEA8EA927A10DD28C1A7939BB14D5C8A2E8BDAAA3BA`.
  Beleg: `KILL-SKULL-DEPLOYMENT-20260929.json`.
  Mit UiBridge 0.7.3 auf Host 2 autonom einen echten Equator-Deathmatch gespielt:
  3 eigene Kills (2 Kopf, 1 Brust), native Skull-Ereignisbindung/Asset/Ausblenden
  und Killfeed-Text/Icon-Zustand geprüft. 24/24 geladene Builds abgeglichen.
  Keine Error-Level-Zeilen; vollständiger Profilvergleich unterscheidet nur
  zwei Verwaltungs-Zeitstempel. Testhilfen aus, Handles freigegeben, Hauptmenü.
  **Keine Pixel-Abnahme des 0,8-s-Totenkopfs:** Screenshot kam zu spät;
  Größe/Position/Lesbarkeit noch kurz visuell bestätigen. Bericht und Grenzen:
  `GPU-KILL-SKULL-RESULTS-20260929.md`. Die ältere UI-Gesamtabnahme unten wird
  dadurch nicht ersetzt; F9/Scrollen/Tactical wurden hier nicht getestet.

- **EFT-Arena-Grafiken für Killfeed/F9-Scoreboard (2026-09-29):**
  Quellstand `b65bf86`. 17 Originalgrafiken aus dem vom Nutzer bereitgestellten
  Arena-Export eingebettet. Killfeed mit Waffen-/Todesursachensymbol und separatem
  Kopftreffer-Icon; vollständige Namen, Rollen, Entfernungen, Teamfarben und
  vorhandene Positionsregler bleiben erhalten. F9-Tabelle mit Teamleisten,
  abwechselnden Zeilenflächen, K/D/A/HS-Icons, eigener Zeilenmarkierung und
  dynamischer Höhe für lange Namen. Kompaktes oberes Score-Panel unverändert.
  Keine Änderungen an Ranking, Assists, Punkten, Profil oder Eingabehoheit.
  Originaldateien mit SHA256/Provenienz unter `ArenaMode.Client/Assets/Ui`;
  keine freie Weiterverteilungslizenz festgestellt, für private Installation.
  Vor öffentlicher Veröffentlichung Rechte prüfen; nicht der Codelizenz zuordnen.
  Details und native Testfälle: `ARENA-UI-ASSETS-REVIEW.md`.
  143 Produktions-/Ressourcenprüfungen plus sieben kompilierte Fehlerkontrollen,
  bisherige Killfeed-Checks einschließlich Fehlerkontrollen, breite HUD-/Menü-/
  Tactical-Regressionsprüfungen und Server-Suite grün (789 bestanden, 1 separater
  APBS-Labortest übersprungen, 0 Fehler). Isolierter Client-Release-Build erfolgreich;
  vier bestehende CS8632-Warnungen. Eingebettete PNGs/Herkunftsdokumente auch in
  der echten Client-DLL per Hash geprüft. Unabhängige Review ohne offene P0–P2.
  Client lokal und auf GPU-Host 192.168.178.114 eingespielt; beide Zieldateien vor
  dem ersten Schreibzugriff exklusiv geöffnet, Backups und Hashvergleich.
  Server, Redeploy.Core und BigBrainCompat auf beiden Rechnern unverändert.
  Client SHA256 `ABBDA5A958640218E9F4EB3BCAD1689822BCEBE1D299B76B783A670C1B0C2F5E`.
  Beleg: `ARENA-UI-ASSETS-DEPLOYMENT-20260929.json`.
  **EFT vor dem Test neu starten.** Keine native Raid-/Pixel-Abnahme: lange Namen,
  Teamfarben, sechs wiederverwendete Killfeed-Zeilen, F9/Scrollen/Esc und Tactical
  über einen Rundenwechsel samt Kaufmenü prüfen. Die Offline-Vorschauen unter
  `artifacts` verwenden angenäherte Schriftmetriken und sind keine EFT-Screenshots.

- **Customs-Areale vereinheitlicht und Konturen erneuert (2026-09-28):**
  Quellstand `b3aae91`. Die drei unveränderten Exporte von heute sind als
  Factory Center (40 Eckpunkte), Factory Side (32) und Customs Yard (28)
  eingebunden. Pro Areal nur ein Menüeintrag, jeweils unabhängiges TDM 2v2–5v5;
  keine getrennte Fest-/Teamvariante mehr, weiterhin keine Höhenbegrenzung.
  Beide bisherigen Varianten waren TDM; andere Modi wurden nicht freigeschaltet.
  Neue Geometrie-IDs verhindern eine Mischung alter/neuer Begrenzungen zwischen
  Client und Server. Bekannte gespeicherte Center-/Side-Auswahlen werden gezielt
  migriert; alte IDs bleiben im Netzwerk ungültig.
  Center behält seine Teamstarts. Bei Side entfällt ein jetzt zu wandnaher
  Botpunkt. Yard erhält wie vom Nutzer freigegeben 20 zusätzliche Startkandidaten:
  je Fraktion 2 Spieler- und 8 Bot-Alternativen um native Bodenanker, mit neuen IDs
  und korrekter Zone/Core-Zuordnung im privaten Raid-Klon. Originalmarker und
  globale Daten bleiben unverändert. Native Navigation, vollständige Wege innerhalb
  der Kontur, Belegung und erforderliche Teamgröße entscheiden weiterhin über
  die tatsächliche Freigabe; keine unsicheren Ersatzspawns bei Fehlschlägen.
  Einzelheiten, Provenienz und Testfälle: `CUSTOMS-AREAS-REVIEW.md`.
  Alle vier ausführbaren Prüfgruppen unter `tools/CustomsAreaRefreshChecks` grün:
  exakte Konturen/Spawns mit Fehlerkontrollen, komplette Server-Suite
  (789 bestanden, 1 separater APBS-Labortest übersprungen, 0 Fehler),
  Konfigurationsmigration/Menü/Vorschau/Team-Laufzeit und isolierter gepaarter
  Release-Build. Unabhängige adversariale Review ohne offene P0–P2.
  Client und Server lokal sowie auf GPU-Host 192.168.178.114 gemeinsam eingespielt;
  vier exklusive Zieldateisperren vor dem ersten Schreibzugriff, Backups und
  anschließend Hashvergleich. Redeploy.Core/BigBrainCompat unverändert.
  Client SHA256 `1E119FC58D8508556D75F6C56E72312C78C48CA3BBF484EB8F07D7AB0B741C31`,
  Server SHA256 `53747DEED12904B28B1CC7262731E33FBA23EB0630597087CF689C4C484931CB`.
  Beleg: `CUSTOMS-AREAS-DEPLOYMENT-20260928.json`.
  **EFT und SPT-Server vor dem Test neu starten.** Keine native Raid-Abnahme in
  dieser Sitzung: alle drei Vorschauen, 2v2/5v5 möglichst mit beiden Fraktionen,
  Respawns und geschütztes Verlassen prüfen. Insbesondere Yard-Boden/Hindernisse
  und Erreichbarkeit der neuen Kandidaten müssen im Spiel bestätigt werden.
  Die native Error-Level-Meldung über 20 ergänzte SpawnPointMarker ist bei Yard
  erwartet; konkrete Readiness-/Pfadfehler dagegen vollständig festhalten.

- **Bot-Anlaufen, Richtungswechsel und Gewichtsträgheit (2026-09-28):**
  `22f3b3d` auf master. Nutzer hat das vorherige Bot-Such-/Kampftempo positiv
  getestet; diese zusätzlichen Änderungen benötigen noch eine eigene Raid-Abnahme.
  Nur exakt zugeordnete, aktive Arena-Bots am Boden: native Beschleunigungswerte
  x1,5, Walk-Trägheit nach Gewichtskorrektur x0,6, normalisierter Trägheitswert
  x0,5. Verbleibende positive Gewichtsnachteile werden zur Hälfte zur nativen
  Nullgewichts-Basis zurückgeführt, aber nur wenn diese die Bewegung verbessert.
  Native Nullwerte bleiben erhalten; insbesondere wird eine bereits auf null
  gefallene Sprintbeschleunigung nicht wiederhergestellt. Inventargewicht,
  Ausdauer, Skills, globale Kurven, Spieler und normale Raids bleiben unverändert.
  Keine weiteren Einstellungen: `BotMovementEnabled` schaltet mit. Vorhandene
  Kollisions-/Türbedingungen, Bewegungsgrenzen und SAIN-Aktionshoheit bleiben.
  Details und Testfälle: `BOT-AGILITY-REVIEW.md`.
  Offline: 309 Agility-, 131 Motion-, 43 SAIN-Bindungs- und 149 Tempo-Prüfungen
  grün (632 insgesamt), 22 kompilierte Fehlerkontrollen. Zehn native Lesestellen
  in vier Methoden gegen die installierte EFT-DLL geprüft; zwei unabhängige
  Reviews ohne offene P0-P2. Client-Build: 0 Fehler, vier bekannte CS8632.
  Nur Client lokal und auf GPU-Host 192.168.178.114 eingespielt, SHA256
  `FD8FD7A039E313596CDC01715799343475B733FD8E87E1FDA7E7BC24FB53AF6B`.
  Server, Redeploy.Core und BigBrainCompat auf beiden Rechnern unverändert;
  Beleg `BOT-AGILITY-DEPLOYMENT-20260928.json`. Vor dem Test EFT neu starten.
  Native Animationen, Verhalten an engen Ecken/Türen, tatsächlicher Gewichtseffekt
  und Leistung bei voller Besetzung sind noch nicht im Spiel abgenommen.

- **Bot-Such- und Kampftempo (2026-09-28):** `ba22ab4` auf master, separater
  Laufzeitadapter für die installierte SAIN 4.5.1.0. Die drei freigegebenen
  Punkte: unnötigen Such-Wartetail nach beendetem Nachladen auf 0,5s sichere
  Beobachtung verkürzen; Sprint auf vollständigen vorhandenen Suchrouten mit
  >=4m bis zur nächsten Ecke und >=8m Restweg bevorzugen; positive native
  `DogFightMove`-Strafewerte unter 0,7 auf 0,7 anheben. Native Nachlade-/Heil- und
  Gefahrhandlungen, Türen, Navigation und echte Sprintbedingungen bleiben
  bestehen. Kein SAIN-Preset verändert, keine zusätzlichen F12-Regler;
  `BotMovementEnabled` schaltet mit. Fremde PFT-/Arena-Pursuit-Aktionen bleiben
  unberührt. Displacement-Maximum 1,5 und Schrittgrenze 0,3m unverändert.
  Minute-Diagnosen unterscheiden Tempo-Eingaben von tatsächlich geboosteten,
  eckenbegrenzten und schrittbegrenzten Bewegungsaufrufen. Sie messen kein
  tatsächlich erreichtes Lauftempo. Details: `BOT-TEMPO-REVIEW.md`.
  Offline: 149 Tempo-, 131 Motion-, 43 SAIN-Bindungs- und 104 TDM-Suchprüfungen
  grün (427 insgesamt), dazu 19 kompilierte Fehlerkontrollen; installierte
  SAIN-/EFT-Verträge geprüft. Unabhängige adversariale Review ohne offene P0-P2;
  gefundenen mutativen Flash-Getter durch reines Lesen von `_isFlashed` ersetzt.
  Client-Build: 0 Fehler, vier bekannte CS8632. Nur Client lokal und auf
  GPU-Host 192.168.178.114 eingespielt, SHA256
  `2C35467D13242387B28FDAE413BB37C0DF51DC5F432AA2608DCB8A124B85DA2D`.
  Server, Redeploy.Core und BigBrainCompat auf beiden Rechnern unverändert;
  Beleg `BOT-TEMPO-DEPLOYMENT-20260928.json`. Kein EFT-Neustart/Raidtest durch
  diese Sitzung: vor Abnahme EFT neu starten. Native Wirkung, SAIN-Aktionsabdeckung
  und Leistung bei voller Besetzung bleiben offen; besonders lange Suchwege,
  Nachladen, enge Türen/Ecken und Tactical/PFT nach dem Spielertod vergleichen.

- **Score-Vergleich und 150-HP-Pickup (2026-09-26):** Screenshot
  `../Screenshot-2026-09-23-170642.png` zeigte eine allein umgebrochene
  Rivalenpunktzahl. `c90c7a4` trennt den Vergleich vom Matchstatus und reserviert
  in einer gemeinsamen unteren Zeile die gemessene Zahlenbreite; nur der Name
  kann gekürzt werden. Panelhöhe 84 und alle Offsets unverändert. Ohne Vergleich
  (TDM) bleiben zwei Statuszeilen möglich. `f7b446f` verdoppelt Health von 75 auf
  150 HP; Anzeige, Hilfetext, Log und Botbewertung lesen denselben Wert. Native
  Maxima, zerstörte Teile, Wunden und 20s-Cooldown unverändert.
  Factory hat unverändert acht gemeinsame Ausgangspositionen für Tag/Nacht und
  sechs Pickups pro Runde; kein Custom-Pickups.json auf lokal/GPU vorhanden.
  Health/Bot-Objective-Harness: 375 Prüfungen, 11 kompilierte Fehlerkontrollen;
  Pickup-Harness: 255 Prüfungen, 30 Quelltextverträge, 18 Fehlerkontrollen grün.
  Unabhängige adversariale Review ohne offene P0-P2, TMP-Breitenmessung gegen
  installierte Assembly geprüft. Client-Build 0 Fehler/vier bekannte CS8632.
  Beide Commits auf master; nur Client lokal und GPU 192.168.178.114 eingespielt,
  SHA256 `9767F43EACC007C0414597E05C82E322020D427F96BDA89582122091A66B33BC`.
  Server, Redeploy.Core und BigBrainCompat unverändert. Beleg:
  `SCORE-HEALTH-DEPLOYMENT-20260926.json`. Kein EFT-Neustart oder Raidtest in
  dieser Änderung: nach Neustart lange Namen/große Punktzahlen, Tactical/TDM
  sowie tatsächlichen Heilgewinn prüfen. Siehe UNIFIED-UI-REVIEW und
  PICKUP-OBJECTIVES-REVIEW für Darstellung bzw. unveränderte Heilungsgrenzen.

- **Pickup-Platzierung wiederholt fehlende Punkte begrenzt (2026-09-26):**
  Reaktion auf Factory-Log 25.09. 18:54:41 mit 0/6 nach einmaliger Prüfung.
  Insgesamt 15 Spielsekunden ab Live-Phase, höchstens ein vollständiger Versuch
  pro Sekunde mit aktuellem geerdetem Spieleranker; nach Fristende kein Versuch.
  Bereits platzierte IDs/Marker, Waffenladevorgänge und Cooldowns bleiben bestehen.
  Respawn/Waffenwechsel/Pause warten ab; vorhandene Phasen-/Kontext-Guards gelten
  weiterhin. NavMesh, Boden, Freiraum, Areal und Abstände werden nicht gelockert.
  Symmetrische 3m-Abstände bei mindestens einem rotierenden Punkt verhindern
  Kollisionen mit später erholten festen Custom-Punkten. Pro Versuch Info mit
  Spieler/Anker, Alternativen, Geometrieproben und allen Fehlerhäufigkeiten;
  endgültige Warnungen erst bei Ablauf. Details/Native-Testgrenze im PICKUPS-REVIEW.
  Nächster GPU-Test: Factory neu starten, bei anfangs 0/6 innerhalb des Fensters
  einige Meter bewegen; vorhandene Pickups dürfen nicht wandern oder duplizieren.
  Umsetzung `2326bbe`, auf master vorgespult. Pickup-Harness 255 Prüfungen,
  30 Quelltextverträge und 18 kompilierte Fehlerkontrollen; erweiterter
  Health/Bot-Objective-Harness 374 Prüfungen und 10 Fehlerkontrollen grün.
  Unabhängige Review ohne offene P0-P2-Befunde; Abschlussdiagnose unterscheidet
  ausdrücklich einen noch nicht bereiten Körper vom eigentlichen Pfadfehler.
  Client-Build 0 Fehler, vier bekannte CS8632-Warnungen. Nur Client lokal und
  GPU-Host 192.168.178.114 eingespielt, SHA256
  `F34C153665F9C750D3FD842321D2B441D14408164337CE0D7B8AC0532692059B`;
  Server, Redeploy.Core und BigBrainCompat auf beiden Rechnern unverändert.
  Beleg: `PICKUP-PLACEMENT-DEPLOYMENT-20260926.json`. GPU-Spiel war im Hauptmenü
  und wurde nicht neu gestartet; vor dem Test EFT neu starten. Dies ist noch
  keine native Abnahme der Factory-Platzierung oder ihrer Wiederholung.

- **HUD reduziert, Reserven nur noch nutzbar gezählt (2026-09-25):** Nutzerwunsch
  nach dem Spielen: Rüstungsanzeige minimal, keine eigenen Rucksackzähler für
  Magazine/Granaten, Magazine und Granaten in einer Zeile. Umsetzung `fbfa647`
  (Branch `feature/hud-minimal-reserve`, auf master vorgespult). Statuspanel
  250x94 statt 202: HP/Gewicht, darunter Weste % und Helm % (Summe je
  ArmorComponent, Warnfarbe bei `ArmorWarningPercent`), dann Blutungen/Brüche.
  Magazine zählen genau die Nachladekandidaten (Rig/Taschen direkt plus
  Rucksack direkt, wenn `ArenaBackpackReload` sie anhängt); verschachtelte
  Taschen zählen nicht mehr. Granaten: eine Zahl je Art über
  `IsAtReachablePlace` (inkl. Arena-Reichweite); Symbol gedimmt, wenn G selbst
  keine dieser Art erreicht. Lose Patronen unverändert (Rucksack-Patronen sind
  nicht ladbar). Keine zweite Zeile mehr; zu volle Zeile skaliert auf Faktor
  >= 0,6. Details und Prüfumfang: `RAID-INFO-PANELS-REVIEW.md`, letzter
  Abschnitt. Offline 326 Prüfungen/34 Fehlerkontrollen, fünf Review-Linsen plus
  zweite Runde ohne offenes P0-P2, Build 0 Fehler/4 bekannte CS8632, Build-Verify
  grün (9 importierte + 5 eigene Icons, 17 stillgelegte fehlen). Nur Client
  eingespielt: SHA256
  `C3C6AC9A2398AE8E40BE96AAB8875AD6A0C7E17EFDD5EBDEDDDC5FF70FE0A051`, vorher
  `0DE9A41D...` gesichert; 110 geschützte Dateien (andere Plugins, Arena-Assets,
  Servermod) unverändert. Beleg
  `artifacts/hud-minimal-delivery/adbfb187d05d4c69bba0c240e6556396/receipt.json`.
  GPU-Sichtprüfung durch diese Session (Nutzer gab den Host frei), EFT per
  `gpu-run restart` neu gestartet, 22/22 identisch geladen; Equator-Deathmatch,
  HK-416A5-Kit: Status 440/440, 17,9 kg, Weste 100 %, Helm 100 % (Inventar:
  Zhuk 225/225, 6B47 45/45); fünf volle Magazine und zwei RGD-5 in einer Zeile.
  Ein Magazin und eine Granate in den Rucksack gezogen: weiter 5 und 2. Zweite
  Granate ebenfalls in den Rucksack: 2 gedimmt, nativer G-Schnellslot leer.
  Keine ArenaMode-Warnung im Log. Nicht geprüft: Skalierung einer übervollen
  Zeile, Schrotflinte mit Rucksack-Patronen, Warnfarbe nach Treffern,
  verschachtelte Taschen, UseItemsAnywhere aus, R aus dem Rucksack in diesem
  Build (am 21.09. für den Nachladepfad abgenommen). Server unverändert.

- **M60-Aufnahme: Ursache bestätigt, temporärer Untersuchungsstatus (2026-09-23):**
  Umsetzung `5d31353` auf master; nur Client lokal/GPU unter exklusiven
  Dateisperren eingespielt, identische SHA256
  `0DE9A41D06B58B3C55A7E47EDC4A2314BFDC5B6036261D0552A42DDEC0CEEDC1`.
  Lieferbeleg `WEAPON-PICKUP-EXAMINATION-DEPLOYMENT-20260923.json`.
  155 Adapterprüfungen, elf erkannte Fehlerkontrollen, 54 installierte native
  Vertragsprüfungen und 59 M60-Definitionstests grün; Client-Build erfolgreich
  (vier vorhandene CS8632-Warnungen). Server/Core/BigBrainCompat unverändert.
  Gegenprüfung: keine Profil-/Template-Schreibzugriffe, Referenzidentitäten und
  Phasengrenzen erhalten, schwache Referenzen halten keine alten Welten fest.
  UiBridge nicht erreichbar; keine native Aufnahme oder Profilerhaltung getestet.
  GPU-Log 20:09:00.334/20:09:05.717 nennt `InstallNotExaminedError` für die M60
  selbst bei freiem zweitem Slot. Arena erkennt nun nur die konkreten erzeugten
  Pickup-Instanzen inklusive ursprünglicher Anbauteile für den aktuellen lokalen
  Spieler im laufenden DM/TDM als bekannt. Keine Profil-/Enzyklopädie-Änderung,
  keine XP und kein Ändern gemeinsamer Templates. Fremde Beute bleibt unverändert;
  Körper-/Inventar-/Welt-/Rundenwechsel und beendete Arenaphase entziehen die
  Ausnahme. Übergabe, freie inaktive Slots und spätere Waffensteuerung bleiben
  nativ. Siehe PICKUPS-REVIEW.md. Nach Client-Neustart Aufnahme, Ziehen, Schießen,
  Nachladen und unverändertes Profil nach Rückkehr prüfen; native Abnahme offen.

- **Waffen-Pickup: Ablehnung genauer protokolliert (2026-09-23, Codex):**
  Diagnose-Commit `a5a6fa2` auf master, Client lokal/GPU eingespielt und identisch:
  SHA256 `52E873A85DD010D3150E97656E3EF362FF4FAFF58F84F63397085E765E08178A`.
  Lieferbeleg `WEAPON-PICKUP-DIAGNOSTICS-DEPLOYMENT-20260923.json`; Server,
  Redeploy.Core und BigBrainCompat unverändert. Build grün (vier bestehende
  CS8632-Warnungen); Waffenadapter 120 Prüfungen, sechs absichtlich erkannte
  Fehlerkontrollen, 46 installierte native Verträge und 59 M60-Definitionstests.
  Gegenprüfung des Diffs: nur Diagnose/Anzeige verändert, keine neue Transaktion,
  keine Lockerung der Eigentums-/Runden-/Hands-Prüfungen; Formatter-Ausnahmen
  abgefangen, Ausgaben begrenzt. Kein unabhängiger Laufzeittest behauptet.
  Aktuelles GPU-Log 17:02–17:19: neun Ablehnungen bereits in der nativen
  Transferplanung, auch bei drei freien inaktiven Waffenplätzen. Der bisherige
  Zähler `incompatible` unterschied keine Fehlerursachen; eine konkrete Ursache
  ist aus diesem Lauf daher nicht bewiesen. Neue Diagnose nennt Fehlerklasse und
  Beschreibung pro Zielslot (`reasons` / `planRejected`); keine Inventarprüfung
  gelockert, kein Profil-/Untersuchungszustand verändert. Siehe PICKUPS-REVIEW.md.
  Dies ist noch kein bestätigter Aufnahme-Fix. Nächster Test nach Client-Neustart:
  freien zweiten Waffenplatz lassen und M60-Pickup erneut betreten; bei Ablehnung
  die neue `reasons`-Zeile auswerten. Native Laufzeitprüfung hier nicht möglich
  (UiBridge nicht erreichbar).

- **Heilungs-Pickups und gezielte Bot-Pickup-Suche EINGESPIELT (2026-09-23, Codex):**
  Fahrplan `9a9bf82`, Umsetzung/Tests `aac5822`. Client-SHA256
  `0208A9E0FFAE708936B7603720F2B4CF202EBD9627FBF4D626CA16D6D9B1D11F`,
  MVID `bc4ca929-9b8f-4a3c-87b1-dda64625fbc0`; BigBrainCompat-SHA256
  `72E6BC7B6983FFD53FD906651CD9D3434F23614E5BBE2679A664EE57D804C8D1`,
  MVID `bef884ae-e69e-4bf2-b68e-e80b0c63f621`. Beide DLLs lokal/GPU identisch
  unter exklusiven Dateisperren eingespielt, Sicherungen im Lieferbeleg.
  **Noch kein In-Raid-Test und kein Nachweis geladener Module**: UiBridge war
  nicht erreichbar. EFT für den Test neu starten; nichts am Server/Core geändert.
  Factory Tag/Nacht hat nun sechs Pickups aus denselben acht Positionskandidaten:
  weißes Heilungs-Kreuz, bis 75 HP, 20s Wiederkehr, keine Wundheilung oder Reparatur
  ausgeschalteter Körperteile. Eigene `Pickups.json` bleiben unverändert und
  benötigen einen selbst ergänzten `Health`-Punkt. Nur DM/unabhängiges TDM.
  Optionale bestehende BigBrain-1.5.0-Erweiterung steuert Bots zu nützlicher
  Heilung/DD/Schild; Kampf/Gefahr haben Vorrang, Wege/Versuche sind begrenzt.
  Keine neue Einstellungsflut, kein PFT-Pursuit-Umbau, keine Waffenaufnahme für Bots.
  Source-linked: 345 Prüfungen inkl. Pickup-Basis und zehn gezielt abgewiesene
  Fehlerkontrollen; bestehende Pickup-Suite 226 + 30 Quellverträge/14 Kontrollen.
  Integration Assist/HUD/Movement/DM/TDM grün, PFT-Pursuit 42, TDM-Suche 104;
  Unit-Suite 764 bestanden/2 vorhandene Überspringer (Factory-Installationsfall,
  APBS-Integration), vorhandene doppelte Test-ID gemeldet. Native Client-/Compat-
  Builds ohne Einspiele-Nebenwirkung grün. Unabhängiger Review ohne offene
  konkrete P0–P2-Befunde nach Fixes für Callback-Rundenwechsel, Tür-Rückkehr und
  endgültige Claim-Invalidierung. Verhalten/Abnahmefälle und Grenzen stehen in
  `PICKUP-OBJECTIVES-REVIEW.md`; Lieferung `HEALTH-PICKUPS-DEPLOYMENT-20260923.json`.

- **Einheitlicher Arena-Look EINGESPIELT (2026-09-22, Codex):** Umsetzung `fa8acae`,
  native Nachbesserung `d9d408c`; Client-SHA256
  `2AB51B569ED9D339DB6FD84F3961B2A2D1976EBD94DDAEC08112AE66B4766551`,
  lokal/GPU identisch und in frischem EFT-Prozess verifiziert. Dezenter gemeinsamer
  Stil für HUD, Scoreboard, Tactical-Shop, Zuschauer und Arena-Menüflächen.
  Keine neuen Regler; Offsets/Spielregeln unverändert. Native Sichtproben auf
  Factory in DM und Tactical: Panels/Killfeed/Scoreboard/Shop/Bereitschaft und
  Eingabe sichtbar/bedienbar, F9-Checkbox aus/an geprüft. Vollständige Offline-
  Prüfungen und unabhängiger Review grün. Noch offene visuelle Fälle (PFT-Zuschauer,
  Arealkarte, DLSS etc.) ausdrücklich in `GPU-UNIFIED-UI-RESULTS-2026-09-22.md`;
  keine pauschale Vollabnahme. Zweiter Profilvergleich nur nativer Hideout-
  Zeitstempel, Konfiguration zurückgestellt, EFT beendet; Server unverändert.
  Erster Vorherstand war anderer vorhandener Syncthing/Server-Profilzweig, kein
  belastbarer Vorher/Nachher-Nachweis; nicht blind Profile zurückschreiben.
  Details: `UNIFIED-UI-REVIEW.md`, `UNIFIED-UI-DEPLOYMENT-20260922.json`.

- **UseItemsAnywhere / Rucksack-Nachladen REPARIERT UND GPU-GEPRÜFT
  (2026-09-21, Codex):** Fix `0d9ceea`; Client-SHA256
  `C14FBCEC806AE884876CACEAEB67B0E41A18B81A0CC7D95C629A7CC0A8FC14B8`,
  lokal und auf GPU eingespielt, in frischem EFT-Prozess identisch geladen.
  Ursache der fehlenden Waffenpanels und kaputten R-/G-Abfragen: Monos gemeinsam
  genutzte generische Inventarmethode lief für Munition/Granaten durch den
  Magazine-Harmony-Wrapper (`ArrayTypeMismatchException` im nativen Original).
  Ersetzt durch drei nichtgenerische Magazin-Call-Sites; native Ammo/ThrowWeap-
  Abfragen bleiben unangetastet. GPU: M4, MP-153 und Mosin mit sichtbarem Panel
  und erfolgreichem R; Rucksack-only mit Aus/An-Kontrolle, G-Ziehen und Wurf,
  Granatentod/Respawns ohne Wiederkehr des Fehlers. Teilnehmer- und Kitsettings
  vollständig zurückgestellt; Profil nur mit zwei nativen Zeitstempeländerungen.
  EFT danach regulär beendet. Server/Core/UiBridge unverändert. Unabhängiger
  Source- und Belegaudit bestanden; Offlineprüfungen und Grenzen stehen in
  `GPU-BACKPACK-RELOAD-RESULTS-2026-09-21.md`, Lieferbeleg in
  `BACKPACK-RELOAD-DEPLOYMENT-20260921.json`. Teilnehmerauswahl vom Nutzer bestätigt;
  die Vorgaben ausdrücklich noch nicht angepasst.

- **Redeploy RespawnApi-Probe im Spiel (2026-09-21):** Kein Arena-Commit. GPU-Lauf der
  offenen Core-Szenarien 3–6 auf Factory Tag, Arena Deathmatch Endless, ArenaMode.Client
  `be7c49d` / SHA256 `2263FBDDEEF9DFD516B04B04792B764171B4B5B8FD9EDB6AE3970D86C3DC5C6A`
  als Todes-Treiber (Prio 100). Arena hat den Tod nach dem Raidstart-Swap beansprucht
  (Nachzug 15:13 bei echten **0,50 s**, Schalter über UiBridge 0.6.0
  `uibridge_config_set`); Reentranz und Stop-Ablehnung halten. Beleg:
  `../Redeploy/docs/RESPAWN-API-PROBE-RESULTS-2026-09-21.md`.

- **Teilnehmerzahl pro Karte EINGESPIELT (2026-09-21, Codex):** Implementierung
  `f84c915`, sauberer gemeinsamer Master-Build
  `be7c49d7334135fbf6f7f8e0b0c1c279960271b2`. Client-SHA256
  `2263FBDDEEF9DFD516B04B04792B764171B4B5B8FD9EDB6AE3970D86C3DC5C6A`, Server-SHA256
  `42246A592933D18D0D01DD6848ABAFA2EEC0B22B6483451D2111DAB76651C660`.
  Minus/Wert/Plus im nativen Kartenbildschirm, Gesamtzahl inklusive Spieler;
  pro Karte gespeicherte Solo-/TDM-Werte. Vorgaben: Equator 4 (keine harte Grenze),
  Bay5 8, Factory Tag/Nacht je 10, Labs 16, Labyrinth 12. DM/GG erlauben 2–24,
  volle Team-DM-Karten gerade 4–24; optionale MapPort-Karten weiter nur DM/GG,
  Customs-Teamstarts und Tactical/PFT unverändert. Explizite Zahlen werden exakt
  ausgehandelt; Serverklon und finale Startfreigabe halten fremde Wellen fern.
  Solo reserviert Botplätze über Profilanfragen, native Aktivierung und lokale
  Respawns hinweg; unklare native Arbeit wird nicht nach Timeout überbucht.
  Bei aktivierten Labyrinth-Bossen ein mitgezählter Bossplatz, Tagilla/Killa im
  Wechsel nach Tod, keine zusätzlichen Helper-/Escort-Wellen.
  Nur Client- und Server-DLL ersetzt; Core, Configs, Audiodateien und Drafts
  unverändert. Beleg: `PARTICIPANTS-DEPLOYMENT-20260921.json`.
  Offline: neue Solo-Prüfung 131/11 Fehlerkontrollen, 17 native IL-Verträge;
  Menü 3227 plus Hooks 129/Bereichsansicht 23, sechs Fehlerkontrollen; Server 126,
  vier Fehlerkontrollen; Integration 474 Client-/332 TDM-Prüfungen, drei
  Fehlerkontrollen. Vollständige Unit-Suite 765 bestanden/1 übersprungen
  (gesonderter APBS-Integrationstest), bestehende doppelte Test-ID gemeldet.
  Spawnfilter 415, Give-up 618, FFA-Gruppen 83/zwei Fehlerkontrollen und
  Deathmatch-/TDM-Menü-/Ende-Regressionsprüfungen grün. Unabhängiger Review fand
  fehlende native Boss-Initialisierung und falsche Freigabe nach einem zweiten
  fehlgeschlagenen Spawn-Callback; beide behoben und erneut geprüft.
  Native Builds: keine Fehler, vier bestehende Client-/zwei Serverwarnungen.
  **EFT UND SPT-Server neu starten; GPU-Abnahme und Spiegelung nicht verifiziert.**
  Kurze Spieltestanweisung steht in `PARTICIPANTS-REVIEW.md`; gesonderte Details in
  `PARTICIPANT-MENU-REVIEW.md`, `PARTICIPANT-SERVER-REVIEW.md` und
  `SOLO-POPULATION-REVIEW.md`. Eigener Feature-Branch nach Merge entfernt,
  andere Worktrees/Branches unberührt, nicht gepusht.

- **Pickup-Rotation, Infinite Ammo und Rucksack-Nachladen EINGESPIELT
  (2026-09-21, Codex):** Commits `1bac37a` (Pickups) und `4384855`
  (Rucksack-Nachladen), gemeinsamer sauberer Master-Build
  `2a1e7f132b670ff49cf8d4b0d94d149efa67efbe`. Client-SHA256
  `DCF4FA9A80F34C4AB18211C1DE4F8FBFAE534641F40D9F34EE6F6703EB328E7A`.
  Factory Tag/Nacht wählt pro Runde fünf Pickups (je eines pro Art) aus acht
  möglichen Stellen; die vier vom Nutzer bestätigten Stellen bleiben enthalten.
  Sichere Positionen werden pro Runde gewählt und bleiben während der Cooldowns
  stehen. Feste eigene Layouts behalten ihre Abstände, optionale `Alternatives`
  erlauben Rotation. TDM ohne Rundenwechsel wählt erst beim nächsten Match neu.
  Neues oranges Infinite-Ammo-Pickup: 10s, 45s Wiederkehr, nur lokaler Spieler,
  nur live Deathmatch/independent TDM. Geladene Magazinwaffen verbrauchen dabei
  keine Patronen; keine Inventarauffüllung. Leere Waffen, Werfer, Revolver und
  Kipplaufwaffen bleiben ausgenommen; Störungen und Repetieren bleiben nativ.
  Bereits vorbereitete Schüsse dürfen nach Ablauf/Tod nativ abschließen.
  Mit `UseItemsAnywhere` findet R zusätzlich passende Magazine direkt im Rucksack,
  unter Beibehaltung der nativen Such-/Untersucht-Filter, Animation und Transaktion.
  Der Ablageort des alten Magazins bleibt nativ; volle Rig/Pockets können weiter
  zum Fallenlassen führen. Verschachtelte Behälter sind nicht zusätzlich freigegeben.
  Keine Profil-, Server-, Config- oder Draft-Änderung; nur Clientbytes geändert,
  drei Paketdokumente unverändert kopiert, 130 andere Dateien unverändert.
  Build: 0 Fehler/4 bestehende CS8632-Warnungen. Offline: Pickup 226/14 Fehlerkontrollen,
  Effekte 366/10, Ammo 2839/4, Rucksack 30/4; native Verträge und unabhängige Reviews
  ohne verbleibenden konkreten P0–P2. Nachladen 779/11, Granaten 123/14, Double Jump
  293/24, M60/Config 59, Modus/Assist/Movement/HUD-Regressionsprüfungen grün;
  Unit-Suite 690 bestanden/2 übersprungen (bekannte doppelte Test-ID ebenfalls gemeldet).
  Neue Prüfsuiten und Modus/Assist/Movement/HUD/Units nach dem Merge erneut geprüft.
  Beleg: `PICKUP-AMMO-BACKPACK-DEPLOYMENT-20260921.json`.
  **EFT-Neustart und GPU-Abnahme noch ausstehend; GPU-Spiegelung nicht verifiziert.**
  Prüfen: neue Positionen/geometrische Freigabe und Wechsel über mehrere Runden,
  10s Schießen mit M60/Automatik/Halbautomatik/Repetierer, Ablauf/Tod/Waffenwechsel,
  R mit Magazin nur im Rucksack sowie normaler PMC-Kontrollraid. Native Mono-Hooks
  und Darstellung sind durch Offline-Checks nicht bewiesen. Siehe
  `PICKUPS-REVIEW.md` und `BACKPACK-RELOAD-REVIEW.md`. Eigener Feature-Branch entfernt;
  andere Branches/Worktrees unberührt, nicht gepusht. Die bereits getesteten
  Equator/Bay5-Commits sind im gemeinsamen Stand enthalten.

- **Optional Bay5 integration DEPLOYED AND HOST-TESTED (2026-09-21, Codex):** branch
  `feature/bay5-map-integration`, based on `8c05327`. `arena_bay5` now joins
  Equator in the native Arena menu for Deathmatch/Gun Game only, with independent
  per-map server availability and owned menu rows. Defaults use `BotZone_Main`,
  eight bots/four-alive target; the two-bot burst and 25m gate are unchanged.
  Revision 2 upgrades the exact existing Equator defaults while preserving
  customized lists, disabled values and prior/subsequent map removals. Real
  BepInEx and server-file reload checks cover the deployed revision-1 boundary.
  `tools/EquatorMapChecks/Verify.ps1` now covers both optional maps, five native
  source-linked harnesses and five fault controls, with isolated builds and no
  direct installation writes. Clean implementation `75a7430` is deployed and
  passed native ARENA Deathmatch/Gun Game admission, combat respawn and protected
  return on the SPT 4.1.6 host, UiBridge instance `aed4e141`, 07:38-07:45 CEST.
  Both imported maps appeared for DM/Gun Game and were absent for Tactical/Team
  DM. Deathmatch replenished two PMCs; respawns completed in 0.63/0.62 s with full
  health. Whole persisted profiles before, after DM and after the final GG flush
  match except the background hideout timestamp (canonical SHA256 `eda7e20a…55ac3a7`).
  Gun Game remained tier 1/24; coordinate-assisted movement does not establish
  a complete walk-through of every map area. See `BAY5-MAP-REVIEW.md` for hashes,
  exact times, source evidence and the early unflushed GG snapshot caveat.
  That Arena lifecycle pass used MapPort client `DD2F77DE…20601C`. The final
  companion MapPort retest subsequently passed on `d896214`, client
  `99578972…F92D34`, fresh instance `c2a37398`, 08:11–08:32 CEST. Its rebuilt
  Bay5 NavMesh (14,754 vertices / 6,816 triangles) passed all authored spawn,
  adjusted bot, old ghost-box, depot/warehouse and exit probes; both ordinary
  exits completed extraction. Equator automatically classified 607 indoor
  renderers / 1,103 clones in both raids around Hideout, without F8. The unchanged
  ArenaMode code was not retested through another DM/GG cycle during these
  ordinary map checks. The first-pass profile hash remains the ArenaMode evidence;
  no full Gun Game ladder or exhaustive passage walk-through is claimed.

- **Optional Equator integration DEPLOYED AND HOST-TESTED (2026-09-20, Codex):** branch
  `feature/equator-map-integration`, based on `649a773`. Native Arena menu and
  server admission now support installed ArenaMapPort `arena_equator` for
  Deathmatch/Gun Game only. Exact `BotZone_Main`, eight-bot cap/four-alive
  replenishment target; unchanged two-bot burst and 25m gate. Optional dependency
  absence fails closed, customized/disabled map settings are retained with
  one-time default migration guards. See `EQUATOR-MAP-REVIEW.md` and
  `tools/EquatorMapChecks/Verify.ps1`. Clean code build `22ff216` was independently
  reviewed, deployed with MapPort `9547b0b` (ten bot/four player markers), and
  verified loaded after restarting server and EFT on the SPT 4.1.6 GPU host.
  Actual native ARENA Deathmatch and Gun Game starts, atrium replenishment,
  combat deaths/respawns and return passed at 23:05-23:17 CEST. Whole persisted
  profiles match before/after except the background hideout timestamp. Gun Game
  was a tier-1 smoke test, not full ladder advancement. Tactical/Team DM hiding
  was tested in the native menu; PitFireTeam rejection remains offline-tested.
  Full hashes and evidence are in `EQUATOR-MAP-REVIEW.md`. Existing two-second
  countdown and ExitStatus flow remain; EQUATOR DEBUG is a separate ordinary
  raid shortcut. Other Arena maps are not admitted by this change.

- **User retest follow-up DEPLOYED (2026-09-19, Codex):** supersedes the
  announcer/pickup runtime details below. Separate commits: Jump Boots `454fec1`,
  persisted M60 setting `e6367a2`, Deathmatch preparation `42a9782`; combined clean
  master/client build `42a97824b7f791fb6d628bd6cbb3258fa9b3efd6`, SHA256
  `1C9F19563B463B8EEE428845CF758E90D4AF07562F0705906D770D867E7086C0`.
  The user's supplied `../LogOutput.log` proves Prepare was canceled after
  723 ms when Deathmatch's real rules advanced from round 0 to 1. Preparation
  and intermission now announce the upcoming round's epoch, preserving normal
  speech advancement without weakening real cancellation. A preceding round's
  Draw does not consume the following terminal Draw; existing result priority
  remains intact. See `ANNOUNCER-PREPARE-REVIEW.md` for evidence and limits.
  Jump Boots now give **+100% relative height** for 30 gameplay seconds, including
  double jump, without stacking or extra jump credit. The old persisted
  `PrototypeWeaponKit=spartan` is migrated on plugin startup once to the M60;
  other configured kits and later intentional choices survive. Custom
  `Pickups.json` files are untouched; layout logs now identify actual weapon kits.
  Build: zero errors/four existing CS8632 warnings. Checks: audio 309/4 faults,
  progress 139/22, actual mode integration 54/7, jump 293/24, powerups 348 plus
  42 native contracts/9 faults, pickup runtime 195 plus 28 contracts/13 faults,
  native M60 definition and real BepInEx configuration persistence 59 assertions.
  Faulty prior behavior was reproduced before the fixes; independent reviews
  found no remaining concrete blocker. Only client bytes changed; three package
  docs recopied unchanged, 129 other files including Core/server/UiBridge unchanged.
  Config was not manually edited during deployment. Receipt:
  `ANNOUNCER-PICKUP-FOLLOWUP-DEPLOYMENT-20260919.json`.
  Read-only UiBridge confirmed the matching DLL on the GPU disk at
  `192.168.178.114:C:\SPT41`; its running process still loaded `4bca31c`, so the
  next **EFT restart** is required. No remote actions interrupted the other test,
  no native hearing or pickup acceptance is claimed. Next user check: full
  Prepare at Deathmatch start, M60 pickup after startup migration, and +100%
  ground/double-jump height during Boots followed by normal expiry.

- **Preparation announcer repair DEPLOYED (2026-09-19, Codex):** source fix
  `64f0d7f`, asset-verifier follow-up/client build
  `4bca31cddf0d47e716f824031089aea76e1c0ea6`, now on master. Installed client SHA256
  `99C7F9DB982D1DCBF8727D096363620087FD958C57231558C1A8AD36CE888C56`.
  `ANNOUNCER-PREPARE-DEPLOYMENT-20260919.json` binds the clean staged build and
  verified local deployment. Only client bytes changed; three package docs were
  recopied unchanged, 129 other files (including Core/server/UiBridge) unchanged.
  Native build: zero errors/four existing CS8632 warnings. Audio checks: 309
  assertions/four new fault controls, full historical regression green; progress
  139 assertions/22 faults; mode integration 34; all 52 OGGs validated, seven
  metadata plus eleven inventory faults rejected. Independent review found no
  concrete remaining blocker. GPU mirror and native hearing remain unverified.
  The existing 1.468 s
  PREPARE TO FIGHT clip is complete. Normal preparation-to-countdown/live
  progress used to stop it early; direct RoundStart could replace it as well.
  Preserve its spoken duration and defer only the start voice, never gameplay.
  Real cancellation/epoch/result/mute/exit still discard pending speech. See
  `ANNOUNCER-PREPARE-REVIEW.md` and `tools/AnnouncerChecks/Verify-Preparation.ps1`.
  Claude's six-cue integration reviewed without a concrete runtime blocker;
  corrected outdated AIRBORNE documentation. No audio assets or settings changed.
  Native hearing after restart remains the user's test; this is offline proof.

- **Jump Boots and M60 pickup DEPLOYED (2026-09-19, Codex):** implementation
  `ed669cf`, merged/client build `48549ef15ce40cc690e1042cf17e214aec4a3c73` on
  master; retains the current corpse/pool/announcer work. Jump Boots are the fourth
  Factory day/night pickup: green marker/beacon, +50% relative jump height for
  30 gameplay seconds, also for the existing double jump, no extra jump credit.
  Local player only, Arena movement must be enabled; bots do not consume them.
  Refresh never stacks; expiry/death/scope reset remove the bonus. Native gravity,
  collision, stamina, ordinary raids and existing air-vault limits stay intact.
  Gold weapon pickup now uses the copied M60E6 FDE `retzrestdfg` from the actual
  `spt41dev` profile, MRS/blue laser/RK-1/Fortis RED and 100 M993 (40+40+20).
  Fresh native IDs, no normal catalogue insertion or runtime profile access.
  Source profile SHA256 stayed byte-identical; native examination stays required.
  M60 requires a free compatible **inactive** weapon slot and never auto-draws.
  Only the local `PrototypeWeaponKit` value was migrated from `spartan` to
  `pickup-m60-retzrestdfg`; other config bytes were preserved and backed up.
  Installed client SHA256
  `4AF74D51F36493F644776CACB4714934E87B3A115D36C72F3EB0B7C396707250`.
  `JUMP-BOOTS-M60-DEPLOYMENT-20260919.json` binds client/config, source hashes,
  original preset, backups and checks. Only client/config bytes changed; three
  package docs recopied unchanged, 129 other inventoried files including
  Core/server/UiBridge unchanged. Restart EFT only; GPU mirror receipt unverified,
  so check the saved pickup setting on that host before testing.
  Two independent adversarial reviews: no open concrete blocker. Final merged
  checks: powerups 348 + 42 native/9 faults; double jump 293/24 faults; runtime
  195 + 28 source/13 faults; M60 definition 41; weapon 108 + 46 native/5 faults;
  selected movement/combat/HUD/TDM regressions green, units 633 passed/2 skipped.
  Native build 0 errors/4 existing CS8632 warnings. No native GPU raid proof here.
  Next GPU test: full Factory DM/TDM, inspect four placement markers and logs;
  compare ground/double jumps before collection, during 30s and after expiry;
  confirm one extra jump only, death/reset cleanup and green beacon/timer.
  Collect M60 with empty compatible inactive slot, inspect attachments/ammo and
  free hands, repeat after 45s cooldown; inspect all markers during round/raid exit.
  Existing custom `Pickups.json` replaces built-ins; add `JumpBoots` explicitly
  there if used. See `PICKUPS-REVIEW.md` for placement/authoring and native limits.

- **Corpse release: the damage-exception finding FIXED and GPU-ACCEPTED
  (2026-09-18, Claude):** `d265353` on `master` (fast-forwarded from the since
  deleted `feature/announcer-six-cues`, pushed to `dev`). The
  `ApplyDamageInfo`/`ShotDelegate` NREs since the corpse release were the dead
  Labyrinth boss `bossTagillaAgro`'s own TRIPWIRES going off after Arena had
  released his body (21 of 21 on 2026-09-17, each 3.5-4.2 s after SAIN's
  "Player Null from ID" for that planter). `CorpseDamageSources` keeps a body
  hidden while an unfinished tripwire, a reaching grenade, a flare or a shot in
  flight names it, then 2 s quiet (SAIN's deferred `GetHit`). Offline:
  `tools/DynamicMapCorpseChecks/Verify.ps1` green (61 + 180 assertions, 19 source
  mutants), `dotnet test` 633/2 skipped. GPU raid 06:34-06:48 (client SHA256
  `81DBDD363F5060BCF81E30043E66A99390510386B87DEA9156A7ACEAC76F157C`, plugin
  table + finalizers checked in the running game): boss tripwires fired 11 times
  after his death, guard held and let go (both log lines present), 200 bodies
  released, zero damage reports, zero NREs in-session. Details:
  `CORPSE-CLEANUP-REVIEW.md`, "What held that body" and "GPU acceptance".
  Expect SAIN's "Player Null from ID" lines to remain (native, a dead planter's
  tripwire), and two `TriggerZone` NREs at raid teardown (pre-existing).

- **Pickup refinements DEPLOYED (2026-09-15, Codex):** shield `3140360`, weapon
  transfer `f06381b`, safe placement/beacons `66901a9`; main merge/build
  `bc84accc8ee2e3bc9104c18bcefb917f9e18c459`. Supersedes the builds below and
  retains their post-raid/UI features. Shield now refills 220 impact points for
  30 gameplay seconds; Double Damage stays 20 seconds. Native transfer refusal
  was caused by treating zero-destroy inventory bookkeeping as item loss; only
  positive losses are now rejected. Empty compatible inactive slots work in the
  adapter tests without changing hands or weakening queued ownership safeguards.
  Refusal messages/logs distinguish capacity/compatibility, native veto and loss.
  The playtest log skipped the obstructed Factory damage point (2/3 placed).
  Built-in prototypes now try at most 32 fully checked alternatives within 2m,
  with final floor height bounded to 0.5m. Custom layouts stay exact and frozen.
  Available pickups have same-color glow/core beacons up to 4m, ceiling-clipped,
  hidden while unavailable; marker-owned cleanup never takes weapon ownership.
  Independent adversarial reviews: no remaining concrete blockers. Final merged
  checks: powerups 329 assertions + 42 native contracts/8 rejected faults;
  weapons 108 assertions + 46 native contracts/5 rejected faults; runtime/geometry
  189 assertions + 25 source contracts/13 rejected faults. Native build: 0 errors,
  4 existing CS8632 warnings. Source checks are not native rendering/raid proof.
  Installed client `0.1.0+bc84accc8ee2e3bc9104c18bcefb917f9e18c459`, SHA256
  `1002C05D76F4CC3657A70AD91F279833B88C62F03CF6149BB88437B3B9DFB8B0`.
  `PICKUP-REFINEMENTS-DEPLOYMENT-20260915.json` binds final sources, checks, stage,
  backups and installation. Only client bytes changed; three existing package
  docs recopied unchanged; 118 unrelated inventoried files including Core/server
  unchanged. Restart EFT only; GPU mirror receipt remains unverified.
  Next user test: Factory day/night DM/TDM, find all three points and inspect any
  `relocated`/`skipped` log; cross shield for 220/30; take weapon with a free inactive
  slot (also empty hands), then again after cooldown; inspect colored beacons,
  ceiling/wall occlusion and their disappearance during cooldown/round/raid exit.
  Actual Factory alternative positions, native transfer and Unity rendering still
  require that GPU acceptance. See `PICKUPS-REVIEW.md` for unchanged scope/safety.

- **Arena post-raid pages DEPLOYED (2026-09-15, Codex):** code `a9f3711`,
  main merge/build `ac704d39099ad97e89a6a18071afdbeb3d5c5f94`. Supersedes the
  previous UI/pickup builds below and retains their features. The native Raid
  Ended page remains with duration/status/navigation, but the false red item-loss
  warning is hidden only for its exactly bound Arena result. The XP page is
  skipped both after statistics and in the Main Menu button's queued treatment
  history, so treatment Back cannot expose it. Ordinary results, treatment,
  cancellation prompts, profile refresh and native menu completion are unchanged.
  `POSTRAID-PAGES-REVIEW.md` explains result identity capture before native unload,
  post-raid latch exception and the four GPU checks. Independent review found no
  concrete blocker. Final merged offline checks: 55 assertions/native contracts,
  6 compiled defects rejected; existing direct-exit regression 65 assertions green.
  Native staged client: 0 errors, 4 existing CS8632 warnings. No native UI test here.
  Local client `0.1.0+ac704d39099ad97e89a6a18071afdbeb3d5c5f94`, SHA256
  `8286A57FA9A3D874EF34F3618435EFE6800667B412FFB2DDC3039E6017870CD0`.
  `POSTRAID-PAGES-DEPLOYMENT-20260915.json` binds sources, checks, stage and verified
  local installation. Only client bytes changed; three existing package docs
  recopied unchanged; 118 unrelated inventoried files including Core/server
  unchanged. Restart EFT only. GPU mirror receipt/rendering/navigation remain
  unverified. Test Arena Leave -> results -> Next and Main Menu/Back; ordinary
  raids must retain vanilla loss warning and XP page.

- **Arena model click and direct raid exit DEPLOYED (2026-09-15, Codex):**
  character-click `5ef5fb4`, direct-exit `d21b19a`; main merge/build
  `ee6bf1f4af3201f1c55ca06885cfbf25cce47df6`. This supersedes the pickup build
  below and includes its unchanged features. Clicking the Arena figure now
  changes the existing native Arena toggle, retaining all eligibility checks.
  The layout owns/removes its separate input target before preview transfer;
  later menu screens remain input-transparent. Independent review corrected
  early-preview-failure and optional-button-failure cleanup paths.
  In an exact active Arena raid, Esc -> Disconnect now skips the entire red
  item-loss confirmation and invokes the native Stop(Left) continuation. A
  running respawn/missing owner or paused game defers exit until safe; it never
  forces an owner rebuild or bypasses protected profile restoration. Normal
  raids retain their confirmation. Notification faults cannot discard an exit.
  `CHARACTER-MODEL-CLICK-REVIEW.md` and `ARENA-RAID-EXIT-REVIEW.md` explain scope,
  native seams, failure handling and the short user GPU acceptance steps.
  Offline: 65 exit assertions, 6 rejected compiled defects, installed native IL
  contract; native menu/preview/host-lifecycle checks green. TDM exit regression
  227 assertions and 15 rejected defects green. Both independent final reviews
  had no remaining concrete blocker; staged native build had 0 errors and the
  4 existing CS8632 warnings. These do not prove native rendered clicks/raid exit.
  Local client `0.1.0+ee6bf1f4af3201f1c55ca06885cfbf25cce47df6`, SHA256
  `69BC4E4A19C4E88A04D7DF16B4601B937EFD811377545F5AB83B1568F21751A7`.
  `UI-INTERACTIONS-DEPLOYMENT-20260915.json` binds sources/build/local deployment;
  only client bytes changed, 118 unrelated installed files unchanged (the three
  package docs were recopied unchanged). Core/server were not rebuilt or replaced.
  Restart EFT only. GPU mirror receipt, native model hitbox and live exit/profile
  return are not tested here. User tests: figure click/forward/back; Arena exit
  alive and after death/PFT; ordinary raid still offers cancellation.

- **Arena pickups DEPLOYED (2026-09-15, Codex):** feature `14cbeba`, main
  merge/build `b0511045020bc6d9fa3271aa5273dcae4272d0ad`. Live Deathmatch and
  independent TDM gain fixed collectibles; other modes/ordinary loot unchanged.
  Full Factory day/night each have three candidate points: Double Damage (20s,
  non-launcher firearm health damage), shield (100 impact points/max20s) and the
  loaded primary weapon from `spartan` (free compatible inactive slot only, no
  swap/draw). Powerups return after30s, weapon45s. Both sides' bots can collect
  powerups by crossing; weapon auto-take local only, no added AI routing.
  F12 **Arena pickups** defaults on; disabling clears the current match, enabling
  again applies next match. Effects reset per physical death/round/end, do not
  stack and never change worn armor or DamageCoeff. Buff timers/balance appear
  below the score panel. Placement rejects obstructed/disconnected/wrong-floor
  candidates and logs reasons; all three Factory points remain GPU candidates.
  `PICKUPS-REVIEW.md` documents exact positions, damage/inventory limits and the
  five native test steps. Optional `ArenaMode/Pickups.json` replaces built-ins
  per exact map/area; **Development - Pickups** captures aim/feet with AltF6/F10,
  undo AltF7, export AltF8 to PickupDrafts. Normal PMC survey additionally needs
  **Development - Area outline / AllowInNormalPmc**, and remains read-only.
  Installed client `0.1.0+b0511045020bc6d9fa3271aa5273dcae4272d0ad`, SHA256
  `146648D056A5792845825BE4756C6F1D2A4E38FFC58938E4B6049C5C6EA5B420`.
  `PICKUPS-DEPLOYMENT-20260915.json` binds staged build, source/test hashes, scoped
  delivery, external compiler inputs and backups. Only client bytes changed;
  three package documents recopied unchanged, 118 other inventoried files
  unchanged including Core/server/audio. No external project build/deployment.
  Clean native build: zero errors/four existing warnings. Final merged checks:
  effects327 +42 native/source contracts +8 compiled faults; weapon101 +34 native
  contracts +4 faults; actual runtime/layout/geometry155 +22 source contracts
  +11 faults. Existing regressions1564 (431/126/163/126/155/44/292/227), TDM15
  fault controls, units621 passed/two skipped. Independent reviews clear after
  fixing projected-contact wall admission, outward reentry, anchor readiness and
  queued native ownership. Restart **EFT only**. Native Factory placement, rendering,
  collection/hands/collision and C: mirror receipt remain unverified. Existing
  GPU instruction build IDs were not repointed. Later documentation commits do
  not change the DLL stamp; no Git push performed for this feature.

- **Bot movement speed DEPLOYED (2026-09-15, Codex):** code `96f18cf`, main
  merge/build `3e78736032f9dfbc651a92d5aa4ad48ba5f040d1`. F12 **Bot movement**
  adds an independent enabled switch, walk/strafe x1.25 and sprint x1.30, both
  adjustable live from 1 to 1.5. Both teams and PFT followers qualify only as
  exact live registered Arena AI. Local movement and ordinary raids are unchanged.
  Native collision remains active; the bonus is suppressed at doors, outside
  grounded Run/Sprint, near native path corners, or on already-large steps.
  The SAIN 4.5.1 read-only door guard rejects unproven ownership/unsupported
  contracts. This changes travel speed, not sprint decisions, stamina, weapon
  animations or idle/search behavior. Read `BOT-MOVEMENT-REVIEW.md` for limits
  and the four user-run GPU checks, including corners/doors and PFT round rebind.
  Installed client `0.1.0+3e78736032f9dfbc651a92d5aa4ad48ba5f040d1`, SHA256
  `4EB97AA6EA8B4E2827BD6D0BC55B6520FB16D10DE0B190C03FC3ED5F37093C84`.
  `BOT-MOVEMENT-DEPLOYMENT-20260915.json` binds the clean staged build, delivery,
  source/harness evidence, external compiler inputs and backups. Only client
  bytes changed; three existing package documents were recopied unchanged.
  All 117 other inventoried files stayed unchanged, including Core/server/audio.
  Clean native build: zero errors/four existing nullable warnings. Final main
  verification: 126 motion and 43 SAIN assertions, four compiled faults,
  installed native/SAIN contracts and six existing harnesses with **832**
  assertions (155/161/278/92/104/42). The earlier implementation commit message
  incorrectly summed those six counts as 932; the individual logs and this
  delivery record carry the correct total. Independent reviews found no remaining
  concrete blocker. Feature merged and removed; restart **EFT only**. Native
  traversal speed, crowded navigation, animation/performance and C: mirror
  receipt remain unverified. Existing GPU test IDs were not repointed. Later
  documentation commits do not change the DLL stamp.

- **Damage assists DEPLOYED (2026-09-14, Codex):** code `a69e9c1`, main
  merge/build `f8f21c47a9465df26124f07d8bc8d2994536b253`. The user confirmed
  additional assist rewards while preserving the killer's existing full points:
  `floor(victim base points * contributed actual HP loss / all actual HP loss)`.
  A 30% contribution against a 200-point PMC earns 60 points. No shared bonuses,
  extra kills, Gun Game progress or Tactical buy money. HUD adds an assist count,
  brief `ASSIST +N`, and scoreboard `A`; no new speech or assets.
  Native HealthChanged observation captures the final clamped HP delta before
  kill scoring. Hostile source/final-hit admission and physical-life/round cleanup
  prevent friendly/stale payouts. Logical-key buckets preserve PFT/local identity
  and archived/recreated ordinary rows without duplicate or self-assists.
  Read `ASSIST-SCORING-REVIEW.md` for the full rules and pending GPU checks.
  Installed client `0.1.0+f8f21c47a9465df26124f07d8bc8d2994536b253`, SHA256
  `8A764AD3B80E269148C25A3F833A0D550B6F35DA85D04CA9F680AD171064003B`.
  Exact delivery/build/test bindings and backups: `ASSIST-DEPLOYMENT-20260914.json`.
  Only client bytes changed; the scoped helper recopied three unchanged package
  documents, and all 117 other inventoried files remained unchanged, including
  Core/server/audio. No Redeploy build/deployment; external compiler inputs bound.
  Clean native build: zero errors/four existing nullable warnings. Final main
  re-verification: capture 126 assertions/39 installed-IL+source contracts/five
  compiled faults, scoring 431/three faults; unit suite 621 passed/two skipped.
  Existing announcer 236, combat 194, progress 139, integration 34, shot history
  113, PFT 248, expansion 830, give-up 618, roles 174, Tactical 74 and TDM 227
  checks pass with their existing negative controls. Independent adversarial
  review and follow-up fixes reviewed without remaining concrete blockers.
  Feature branch merged and removed. Restart **EFT only**. Fresh native raid/HUD
  acceptance and the C: mirror's received file hash remain unverified; older GPU
  test IDs were not repointed. Later handoff commits do not change the DLL stamp.

- **Trickshot flight threshold DEPLOYED (2026-09-14, Codex):** code `abc85c9`,
  main merge/build `2641c4f5d41d6c241bd3dd64e4d343c6627144bb` requires at least
  0.5 seconds of observed continuous free flight before the shot, still without
  ADS. Short drops/early shots no longer qualify; a later impact cannot promote
  them. Double-jump without landing continues the clock. No Scope and priorities
  are unchanged. HUD/key remains TRICKSHOT/`trickshot`, with Godlike fallback;
  AIRBORNE! is a naming/recording suggestion, not a supplied replacement clip.
  Installed client SHA256 `D3094E40A4CA8C10A2E14B17E11F882A117250CBE0519C7B0F0252AE3C56E421`.
  Exact deployed files, Core/server hashes, external build inputs and backups:
  `TRICKSHOT-AIRTIME-DEPLOYMENT-20260914.json`. Only the client bytes changed;
  the three package documents were recopied unchanged by the scoped delivery tool,
  with 117 other inventory files unchanged. No Core/server/audio update.
  Clean native build: zero errors/four existing warnings. Targeted checks: 177
  adapter assertions, 55 native/source contracts, six compiled faults; audio
  236, full combat 194 and shot history 113 pass. Independent adversarial review
  found no blocking issue. Full unit suite was not rerun for this client-only
  predicate; preceding run below is historical. Restart EFT only. User raid test
  and C: mirror receipt remain unverified; see `SHOT-AWARDS-REVIEW.md` for limits.

- **Complete announcer update DEPLOYED (2026-09-14, Codex):** user paused parallel
  respawn work and authorized delivery. Main merge/build `3054375` includes the
  19 additional cues, multi-kill priority fix, TRICKSHOT/NO SCOPE (both temporarily
  using Godlike), and text-only `NICE THROW, GENIUS!` for a proven direct death from
  the current body's own thrown grenade (`660d249`). Their isolated NOT DEPLOYED
  build records below are historical and superseded by this combined delivery.
  Installed client ProductVersion `0.1.0+3054375c1aecfafff1354642da6acce42c14559c`,
  SHA256 `586C2D1D539B49EE7BABE2071AE782354E194E411FC24FD3D9885075D2F93668`.
  `ANNOUNCER-DEPLOYMENT-20260914.json` records exact source, targets, installed
  hashes, external compiler inputs and backups. Four files copied: client and
  ANNOUNCER-PACK.md, ANNOUNCER-SOURCES.json, AUDIO-LICENSES.md. All 118 other
  inventoried files unchanged, including Core 1.0.1 (`EE09B6B5...`), server
  (`70FDD6CB...`) and all 46 OGGs. User drafts were excluded from the operation.
  No Redeploy build/deployment; its two linked/imported source hashes were frozen.
  Native client build passed with four existing nullable warnings, zero errors.
  Combined-source regression checks: 621 unit tests passed/two skipped; audio
  236, combat 194, progress 139, real mode adapters 34, shot history 113,
  native shot adapter 128/5 faults, self-grenade 74 plus 35 native/source contracts
  and three faults; PFT 248, team expansion 830, give-up 618, Tactical 74 and
  TDM 227/15 faults passed. Independent adversarial review and installed inventory
  verification passed. Read `SELF-GRENADE-TAUNT-REVIEW.md` and shot/announcer reviews.
  Restart **EFT only**. No fresh native HUD/audibility/raid acceptance or C: mirror
  hash proof is claimed. Existing GPU test IDs were not repointed; use this new
  client binding for later tests. Later documentation commits do not change it.

- **Redeploy.Core 1.0.1 installed (2026-09-14, Claude, Redeploy `d532a3c`):** a respawn near a
  corpse now has to be walkable from it, outside sniper and mine zones and with room to stand
  (fix for a user's repeated out-of-map respawns). Arena reaches that code only through the
  open-arena fallback (`ArenaSpawnSelector` -> `InfilSpawnSelector` -> `NearPositionSpawnSelector`,
  25 m) when both native spawn lookups fail; bounded areas never do. No ArenaMode change and no
  rebuild: the installed client `54C2D7B7…` is stamped 1.0.0, which the Awake gate accepts against
  Core 1.0.1 (hash `EE09B6B5…`). Its new log lines start with `Respawn position on`.

- **Shot-time awards (2026-09-14, Codex; NOT DEPLOYED):** the isolated announcer
  branch also implements TRICKSHOT (airborne hip fire, including falling) and
  NO SCOPE (bolt-action hip fire). Direct lethal bullets use the exact local
  body's firing snapshot, not ADS/grounded/held-weapon state at death. Pooled
  projectile, parent/fragment, accepted native hit and round/body identities are
  checked. Both logical labels provisionally play the existing Godlike recording;
  healthy future root `trickshot.ogg`/`no_scope.ogg` recordings override it.
  First Blood/Payback/Shutdown/multi-kills precede these awards; sprees and
  Headshot follow. No score, gameplay, asset bytes or profile changes. Read
  `SHOT-AWARDS-REVIEW.md` for boundaries and user-run tests. Code commit `0f25d5c`;
  clean staged client `0.1.0+0f25d5c8e03d29078dca9eb20d62d60d232ca3ae`, SHA256
  `3F7D1C5CE3D830945F9FA4EBC85FB095771477FA325BEDC0989BCC0CACB747A5`.
  Exact path, installed hashes, external Core source hashes and checks are in
  `SHOT-AWARDS-BUILD.json`. Four existing native-build warnings, no errors.
  Parent checks: history 113/6 faults, native adapter 128/5 faults plus 46
  metadata/wiring assertions, audio 236, combat 194 and six new audio/combat
  faults; retained historical controls and all 46 clip hashes/decodes pass.
  Unit suite 621 passed/two skipped; PFT, team expansion, give-up, Tactical and
  TDM regression suites remain green. Independent adversarial review passed.
  No native audibility/raid proof; main and installed client/server/Core unchanged.
  The preceding 19-cue build below is historical. The parallel checkout remains
  responsible for respawn integration; merge combined source and rebuild before
  deploying, never copy this isolated DLL over a newer respawn build.
  During this work, main independently advanced to `fa43fa5` documenting installed
  Redeploy.Core 1.0.1 (`EE09B6B5...`); client/server were unchanged. New staging
  uses the actually installed Core, without building or deploying Redeploy.

- **Additional announcer lines (2026-09-14, Codex; NOT DEPLOYED):** implemented in
  isolated `D:\SPT41\Development\ArenaMode-VoiceLines` on
  `feature/additional-announcer-lines`, code commit `ce53948` (base `5593ea7`).
  Adds 19 explicitly loaded Creator reserve phrases for real preparation/countdown,
  lead/limit/series/Gun Game progress, actual Draw, physical-life Shutdown and living
  protected-team Clutch. Multi-kills now precede sprees in selection and queue;
  First Blood/Payback/Shutdown remain higher. Original win events/recordings and
  all score/profile/respawn rules are retained. See `ANNOUNCER-EXPANSION-REVIEW.md`
  for triggers, priority and the legacy shared-GUISounds cancellation limitation.
  Parent re-verification and independent adversarial review passed. Unit suite:
  621 passed, two skipped; audio 203, progress 139, combat 151 and real-adapter 34
  assertions plus deliberate compiled faults; existing PFT 248, Team Arena 830,
  give-up 618, Tactical polish 74 and TDM match/exit 227 checks pass. All 46 audio
  hashes/decodes verified; no OGG bytes or provenance changed.
  Clean staged client: `0.1.0+ce5394888a01d11f4a8aa0231b7824f030c05e0e`, SHA256
  `8B83449BC7D1095380D7A1D880B285E0B9ABBA305C02C72BD825082E9F80B143`.
  Exact path, installed-file snapshots and checks are in
  `ANNOUNCER-EXPANSION-BUILD.json`. Four existing nullable warnings, no build errors.
  Main checkout and installed client/server/Core remain unchanged; the respawn
  session owns its own integration/deployment. Merge this branch into the final
  combined source and rebuild before deploying; do not install this older staged
  DLL over newer respawn changes. No server update is needed for this feature.
  Native audibility/pacing remains untested. After integration, restart EFT and
  test countdown/readiness loss, coincident Triple Kill/Spree, time/kill/lead
  warnings, Gun Game wrap and a living Clutch versus a dead-player team victory.
  Existing GPU test/build IDs have deliberately not been rewritten.

- **Redeploy.Core 1.0.0, Core skew check and raid-end log level (2026-09-13 evening,
  Claude):** `6a72ddb` logs the designed first raid-end answer
  `{restored:false, deferred:true, state:InRaid}` as Info (user request); every other
  unacknowledged answer stays a warning, covered by new `tools/NativeMenuChecks` cases that
  fail against both mutants. `b5011cd` stamps the Core version this client is built against
  (`Redeploy.Core/CoreVersion.props`) and warns in Awake, plus `Chainloader.DependencyErrors`,
  when the installed Core is older (Redeploy `9f9932d`, design 3.9/5.6). Deployed client
  `54C2D7B72EF96535813454B5EF07B9B2919EBABFCBFF84AD8F8EAD94C3BE0DEE`
  (`0.1.0+b5011cde01a508a6c3f0d5ac707d038eb179f589`) with Core
  `C3DC2F125389F17FC3A171F48A973429CE859628F7375F7F63CD2AACAF2D44E3` (`1.0.0+33c79a7`),
  Redeploy.Client `FC2F1A40…`, KillcamReplay 0.9.8 `9D3245A8…` and the dev plugin
  `maschine-RespawnApiProbe.dll` (all scenario switches off; remove it for clean logs). The
  server was not rebuilt and stays `70FDD6CB…` (`699df57`); `ArenaMode.Tests` was not run
  because it redeploys the server and links none of the changed files. Consequence: the
  installed client and Core no longer match `GPU-ARENA-POLISH-r1-BUILD.json` (its Core hash
  was already stale since the stage-1 Core `82A7C33A…`), so `arena-polish-r1` needs a new
  binding before a GPU run. Known unrelated breakage: `tools/ArenaWeightChecks` does not
  compile (`MovementRuntime.cs` references `ArenaAirVault`, not linked in the tool).
  In-game not yet tested.
- **Area/Tactical/HUD polish and Git consolidation (2026-09-13):** user-requested
  changes are integrated on `master`. Implementation commits: HUD offsets
  `c6b78c6`, maps `7db7387`, Tactical `7d3a672`. Clean paired build source is
  `699df574a062705c8be3242953aba0599b63667f` (ancestry-only merge of a previously
  reapplied UI patch, with byte-identical tree before/after). Both new Arena DLL
  ProductVersions are `0.1.0+699df574a062705c8be3242953aba0599b63667f`.
  Client SHA256 `4513C3F7890517048CAD306C16728EE72ACFAC948B6D67803E8A9FB990E5F2F0`; server SHA256 `70FDD6CB46A5E7601AA3476703791D684A118DDFEBB545D6A395656DFA56A290`.
  Both were copied to the D: installation and hash-verified against staging;
  70 other inventoried installed files, including Redeploy and map/audio
  assets, remain unchanged. Backups: `artifacts/arena-polish-deploy-r1/babd40d8028942b7bf7a997522cdfe05` (ignored).
  Restart **both** game and server. The existing mirror transports these files;
  direct C: hashes and fresh loaded process identity still require the GPU check.
  Use test ID `arena-polish-r1`, `GPU-ARENA-POLISH-TEST-INSTRUCTIONS-r1.md` and its
  exact `GPU-ARENA-POLISH-r1-BUILD.json`. Later documentation commits do not change
  the binary build ID. GPU Codex may run only menu checks; raids remain user-run.
  No new native menu or raid acceptance is claimed for this build.
  - Removed only the two prototype rectangles: the exact old "Factory Center"
    row and "Factory South". Measured Center contour and Center team starts
    remain. Both retired IDs now fail shared/server admission. Actual BepInEx
    clamping changes obsolete saved IDs to full-map; native Customs subsequently
    offers/persists a valid contour before start, while Factory stays full-map.
  - Former "Factory East" is now **Factory Side (team starts)**, using native
    ZoneFactorySide naming rather than an unverified compass direction. Same
    stable ID `customs-factory-east-polygon-v1`, same eleven measured XZ vertices,
    unlimited height; explicit negotiated 2v2-5v5 on both client and server.
    Exact native starts: each faction has five alternative PMC markers plus
    eight USEC/six BEAR bot markers, opposing spacing at least 28.876 m, minimum
    simultaneous same-team spacing 1.885/1.710 m. Existing full-path, occupancy
    and area-containment checks are unchanged. Live connectivity, cover,
    fairness and respawn behavior still need the user's 2v2/5v5 tests.
  - Solo Tactical waits for Active opponents (recognizing early eliminations)
    before Round Start and combat timing. Existing partial-ready timeout remains
    45 seconds plus activation grace for late bodies. Zero-ready retries do not
    rebuy/swap or announce. Logical award reset happens before dispatch; delayed
    audio skips that reset, preserving early First Blood and kill chains.
  - Solo Tactical uses an explicitly free cheapest valid starter without a
    purchase. No random expensive opening kit; paid choices, survivor resupply
    and existing post-death affordable automatic purchase remain. Preloading
    uses the same starter. Initialization failures use protected abort, and PFT
    keeps its entry gear. Countdown Esc routes the exact local owner to native
    menus, closes the buy overlay and pauses countdown progress without dropping
    death/profile protection. See `TACTICAL-START-POLISH-REVIEW.md`.
  - F12/HUD `KillfeedOffsetX`/`KillfeedOffsetY` are live, zero by default, positive
    right/down in scaled 1920x1080 units. Moving the root preserves row contents
    and lifetimes; score-panel offsets remain independent.
  - Offline: 621 unit tests passed, two existing optional fixture tests skipped.
    Current production-linked checks passed: Customs survey 2430 assertions,
    Tactical polish 74, real BepInEx config 141, menu/hooks/view 451/74/23,
    preview 1982, PFT 248, team expansion 778, assembly 46, give-up 618,
    area runtime 369, solo spawn 57, and input release/drain 78. Historical
    regressions and compiled fault controls passed, including the independent
    review finding about award reset. Staged real client/server builds and map
    package validation passed. Native input detours, bots and profile exit still
    need the explicit user-run checks; builds/harnesses are not raid evidence.
  - Git: all 46 inventoried branch tips remain reachable from master. Deleted
    45 merged local branch refs and the obsolete remote feature branch without
    force; `dev/master` is the GitHub default. The three linked worktree
    directories (DoubleJump, HitmarkerAudio, NativeHud), their ignored artifacts
    and Git pointers are preserved, detached at their original exact commits.
    No abandoned working changes or new source remains outside master. Continue
    from master; do not resurrect an old mirrored feature ref. Old UI tip
    `3d75c6a` was already represented by `77305dd`; merge `699df57` only records
    that verified ancestry without overwriting the current evolved files.

- **Area-map r2 functional menu acceptance (2026-09-13):** GPU report `b418bd4`,
  `GPU-AREA-MAP-PREVIEW-TEST-RESULTS-r2.md`, passes the executed native menu cases.
  One click from Center selects East and retains it beyond 15 seconds; all four
  areas, AREA/OVERVIEW, camera-only config stability, the team-size offer and ten
  Next/Back cycles pass. Other maps and ordinary PMC/Scav menus retain native
  information. No new menu/runtime errors; nine known client startup warnings,
  zero later client warnings/errors and zero warnings/errors in the r2 server
  delta. GPU pre/post checks bind all nine files and four ProductVersions to
  client `2c0223f` / unchanged server `d7cde9d`; the client was freshly started,
  the same verified r1 server process was retained. Parent review verified all
  92 evidence hashes, the copied build binding and all nine installed D: files;
  representative original images agree with the report. No new build/deploy.
  Layout acceptance covers 1920x1080 Borderless and 1904x993 Windowed only;
  smaller resolutions/UI scaling and framewise video/flicker remain untested.
  **Zero raids:** Factory East still needs the user's independent TDM 2v2 test
  of initial team readiness, reachable spawns/respawns, boundary and normal
  match exit/profile restoration. Older respawn/HUD tests are not accepted by
  this menu report. Original r1/r2 evidence and delivery-snapshot status fields
  remain unchanged. See `AREA-MAP-PREVIEW-REVIEW.md` for the bounded acceptance.

- **Historical area-map r1 block and r2 release (2026-09-13; native menu status
  superseded by the acceptance above):** report
  `588090f` proved the nine-file/fresh-process binding and initial Center layout,
  but Factory East could not be selected. Settings omitted East in both its
  whitelist and BepInEx acceptable-value list; native selection returned false
  without an exception. Other camera/lifecycle/resolution cases and all raids
  were untested at r1. Fix `2c0223f` adds East and shares the BepInEx constraint with
  menu selection; no geometry, server or profile change. Real Settings + real
  BepInEx persistence harness: 165 assertions, both historical failures
  reproduced, fixed probes and clean rerun passed. Area/menu checks 406/73/24
  plus five faults; TDM Settings/menu checks 92 plus ten faults. Independent
  production/oracle review passed. Staged client build: zero errors, four existing
  nullable warnings. Only the client was deployed: ProductVersion
  `0.1.0+2c0223f643e070d392a13a492411985b743c9966`, SHA256
  `5D2443A237DB64A539EC2E5B0B2BDA7B0B583FF5925E32878E60EF31EDDB2C62`.
  Server remains `0.1.0+d7cde9d115a0f775f8c1b52cc324ee8b0c319e62` / `A6949113…`;
  the other eight bound DLL/assets are unchanged. Read
  `GPU-AREA-MAP-PREVIEW-TEST-INSTRUCTIONS-r2.md`, test ID `area-map-preview-r2`,
  and its exact `GPU-AREA-MAP-PREVIEW-r2-BUILD.json`. Mixed client/server IDs are
  intentional. At delivery, GPU success was not yet claimed; the r1 report and
  evidence stay immutable. Old client backed up under
  ignored `artifacts/area-map-deploy-r2/backup`.

- **Historical r1 Customs preview + HUD deployment (2026-09-13; client and test
  binding superseded by r2 above):** user lifted the
  no-deploy hold. Promoted the existing clean `d7cde9d` client/server candidate
  without rebuilding: both ProductVersions are
  `0.1.0+d7cde9d115a0f775f8c1b52cc324ee8b0c319e62`. Installed client SHA256
  `AB53689653C666F8B842DA30C0F4FF9C7C86B801950A8418B47B2FAA37323A17`, server
  `A694911344BE7AC9058211EAC856B6944AA1CB16EB3AB1282ECB46403391C7B7`.
  Both DLLs and five map/source/license files verified on D:, with the other
  65 previously inventoried dependencies/assets unchanged. Old DLLs retained
  under ignored `artifacts/area-map-deploy-r1/backup`. This also delivers the
  previously prepared score offsets and left killfeed. No new native GPU result.
  `GPU-AREA-MAP-PREVIEW-TEST-INSTRUCTIONS-r1.md` is now released, test ID
  `area-map-preview-r1`; `GPU-AREA-MAP-PREVIEW-r1-BUILD.json` binds all nine
  required files with portable relative paths. Verify those hashes on C: after
  the existing mirror and restart BOTH client and server. Documentation commits
  do not change this binary ID. Earlier respawn r4 evidence/instructions retain
  their original build binding; do not mix them with this menu test.
  Existing Syncthing reports SPT-GPU connected, folder idle, 100% completion,
  zero pending items/bytes after delivery. This is mirror status, not a direct
  GPU disk-hash check or proof that a running process loaded the new DLLs.

- **Historical preparation: native Customs area map (2026-09-13; deployment
  status superseded by the release above):**
  `feature/area-map-preview` replaces native right-hand map information only for
  offered Customs Arena areas. AREA frames the actual catalog contour;
  OVERVIEW shows it on the whole ground map with other outlines in grey.
  Image and polygon share one calibrated projection; no runtime DynamicMaps
  dependency, live tracking, draft loading or new setting/arm side effects.
  Normal menus and other maps retain native information. Owned roots/textures,
  exact CanvasGroup restoration and retired callbacks are covered by the new
  production-linked preview harness: 1986 assertions plus five source faults.
  Menu checks now pass 406 rules, 73 hooks and 24 view assertions plus five faults;
  asset verification includes full PNG decode, hashes and six tamper controls.
  Unit suite: 617 passed, two existing optional-fixture checks skipped. Independent
  review passed; staged client/server builds and real embedded/loose asset checks
  pass without deployment. Map artwork is CC BY-NC-SA 4.0 with original source,
  license and provenance packaged separately from code terms. See
  `AREA-MAP-PREVIEW-REVIEW.md` and the subsequently released
  `GPU-AREA-MAP-PREVIEW-TEST-INSTRUCTIONS-r1.md`. A clean paired candidate manifest
  is generated by `tools/AreaMapDelivery/Build-Staged.ps1`; it is not the currently
  installed GPU build by itself. Both DLLs have since been promoted as above.

- **Historical preparation: second Customs contour (2026-09-13; deployment
  status superseded by the release above):**
  `feature/area-map-preview` adds `customs-factory-east-polygon-v1`, labelled
  Factory East (contour), from the user's `20260913-090513` draft. All eleven
  XZ vertices are exact; no floor/ceiling or spawn-height band. This first
  release is fixed independent TDM 2v2; the existing center/team-start IDs and
  rules are unchanged. Database survey finds 20 PMC candidates per faction and
  15 Bot/BotPmc candidates; native navigation/obstacles are not proven offline.
  Existing live enclosure and complete in-area path checks remain mandatory.
  Four rows fit the owned area panel, above native navigation. The new standalone
  area harness passes 2367 assertions and four source-fault controls; existing
  area-menu checks pass 406 rules, 71 hook and 24 view assertions plus four fault
  controls. Independent integration review passed. Original draft and byte-hash
  fixture are retained under `tools/CustomsEastAreaChecks/`. Staged client build
  succeeds; deployment was held during the user's bound GPU tests. Area commit:
  `c7de967`; paired native map preview is prepared above on this branch.

- **Historical preparation: HUD positioning (2026-09-13; deployed in the
  release above):** branch
  `fix/hud-panel-position` from `1f3f9f0`. F12 HUD `ScorePanelOffsetX` and
  `ScorePanelOffsetY` add offsets to the existing top-center position; defaults
  0/0 preserve it. Positive X moves right, positive Y down, in scaled 1920x1080
  HUD units. Position updates independently of text refresh; the start/wait/result
  banner follows with its existing gap. Client compiled with the existing staged
  no-deploy helper; assembly-notice checks pass 44 assertions and five fault
  controls. The fixture gained the position boundary, not a rendering oracle.
  User's respawn r3 GPU tests were bound to the then-installed build, so these
  HUD changes were held until the release above. Killfeed anchors and aligns top-left (24 units
  from the left, 96 from the top), leaving DynamicMaps' usual minimap corner free.
  Width, wrapping, names, distance, team colors and fading are unchanged.
  Killfeed checks pass 104 assertions plus five fault controls; info-panel
  production checks pass 292 (their fault suite was not rerun). Both layout
  changes independently reviewed; staged client builds pass with zero errors.
  All 68 inventoried installed DLL/asset files still match their pre-build hashes.
  Later visual checks: adjust both offsets during a raid and a visible countdown,
  reset to 0/0, and check long killfeed entries with DynamicMaps at the actual resolution.

- **User playtest closes the stage-0 list (2026-09-13, 13:39–14:40, client
  `0.1.0+2c0223f…` which carries `1e1ca6d`/`12f70cb`/`a2fcced`):** two
  sessions (`BepInEx\logs\LogOutput-2026-09-13_13-59-07.log`, current
  `LogOutput.log`). Ordinary Factory raids: `Death intercepted 0/3` →
  respawn → `1/3` → respawn (10 s protection), once a third death answered
  with give-up → `Player gave up — ending raid as Killed` (expected), once a
  regular raid end. Arena FFA: 6 deaths (3 natural), respawns 0.36–0.47 s;
  Tactical: two `Arena round respawn complete`, 2 deaths; Customs TDM 5v5:
  4 deaths, respawns 0.68 s, `left 8 item(s) out of the secure container`.
  **0 `THROW`/`DROPPER` in every raid**, no kit on the ground (user). All
  forbidden markers 0, no SAIN/MenuScreen errors. Two things worth knowing:
  (1) the raid-end NRE family showed a variant — `BotFirstAid+CG_ApplyToSelf
  .method_1 ← MedsInHandsOperation.GoNextBodyPart ← Start ← MedsController
  .Spawn ← FirearmController.Remove.HideWeaponComplete`, 0.4 s after
  KillcamReplay re-invoked the deferred `LocalGame.Stop` (bot switching to
  meds as Stop hit; same disposed-owner race); (2) **ContinuousHealing's
  flag leak is now observed**: 14:36:20 `MEDS-FINISH bot=Belette … queue=1
  … CH.CancelRequested=True stack=Finish ← ContinuousHealing.Patches
  .CH_EndHeal_Patch.Prefix ← OnEffectRemoved` — a bot's IFAK operation
  ended by CH's static flag with a body part still queued. Pre-existing
  and NOT from today's changes, per the user attributable to outline mods: a
  Unity error flood `SkinnedMeshRenderer: Rendering stopped because the data
  for mesh '<body mesh>' … does not match the expected mesh data size and
  vertex stride` — 678 lines over 7 s at 13:45:01 (`Top_wild_Hoody_victory
  _base_lod0`, `Top_wild_UnderArmor_base_lod1`, FFA, session with the user's
  LootOutline loaded and outlining 3 bodies; it stopped when bodies in range
  dropped to 1) and 384 over 5 s at 08:28:55 in the user's morning session on
  the old build (`Bear_Head_1_lod0/lod1_base`, TDM; the user had briefly tried
  `com.harmonyzt.breakoutoutlines` around then). Absent in Codex's r3 session
  and in the 14:18 session without outline mods; both outline mods are removed.

- **r3 result classified, r4 issued (2026-09-13):** `aaaf510` (Codex, on the
  same bound build `a2fcced`). **The gesture gate is proven:** E (Customs TDM
  5v5) had 0 `THROW`/`DROPPER` with `OnStartPeacefulMove` over the whole raid
  (the user's session before the fix: four in five minutes), a natural death
  with Arena respawn in 1.69 s, profile equal except the pre-declared hideout
  timestamp. A and C reached no natural death (tester stayed put); C's
  give-up respawn passed. Codex stopped on two SAIN error lines in C: SPT
  gives 10 % of scavs a player name (`bot.json
  chanceAssaultScavHasPlayerScavName`), its `CustomAiPatch` then rolls the
  brain from `playerScavBrainType` (Factory: assault 1, bossKilla 1,
  pmcBEAR 2, pmcUSEC 2), and SAIN 4.5.1 refuses `bossKilla` for a PlayerScav
  (`IsAssignedBrainAllowed` → `Init SAIN ERROR, Disposing`), leaving that
  bot on the vanilla Killa brain. SPT default vs SAIN, any raid, not ours;
  the user may zero `bossKilla` in `playerScavBrainType`. Side finding in A
  (ordinary raid): a PMC bot called `BotSurgicalKit.ApplyToCurrentPart` every
  frame for 22 s (861 lines, `using=False`, part `Chest`, Surv12) —
  `FindDamagedPart` had found the chest destroyed on a living bot and the
  start was refused each frame; cause open, no Arena involvement. **Next GPU
  job: `GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r4.md`** (same binding, no
  rebuild; A' and C' with an explicit "seek death" rule — no armour, walk to
  the bots, six-minute cap — C carries the missing E2 ground photos, D is
  mandatory; the SAIN pair is pre-declared as counted, not stopping). The
  working tree of `feature/area-map-preview` carries another session's
  uncommitted map-preview work: stage only your own files.

- **Gesture gate, corpse-looting gate registered, r2 classified, r3 issued
  (2026-09-13):** three commits on `fix/offline-harness-contracts` after
  Codex's r2 report `7cc2391`. `1e1ca6d` `NoBotGesturesPatch`: prefix on
  `BotPeacefulActions.SetActiveAction` (the only raiser of
  `OnStartPeacefulMove`), gated on `IsActive || ArmedForNextRaid` (bots tick
  during the countdown), F12 Bots/`NoBotGestures` default on — no friendly
  hand signal in an arena raid, hence no secure-container item exchange;
  StartDo's pair cooldown and bark stay vanilla. `12f70cb`: the
  `NoCorpseLooting` setting had been a no-op since `c8bb849` (patch never
  registered); the gate moved from `StartLook` to `BotDeadBodyWork.UpdateCheck`
  (the step that chooses a body; a skipped `StartLook` would have left
  `_isStay` and the loot cursor behind) and is registered now; the TDM
  runtime harness models `UpdateCheck` (327 checks). `a2fcced`: a burst spawn
  cancelled by raid teardown logs `cancelled with the raid` at Info instead of
  `failed:`. Three adversarial review rounds (10 agents) drove the
  SetActiveAction target, the armed gate and the harness assertion for a
  hostile bot. Installed: Client `E4CD2ACB…` and Server `71C0AEC1…` (both
  `0.1.0+a2fcced…`; server source unchanged since `f257a78`, rebuilt because
  `dotnet test` had already redeployed it at `7cc2391` — `dotnet test` deploys
  the server DLL, mind the hash binding), Core/Redeploy unchanged.
  **r2 (`7cc2391`, Codex) classified:** the C stop
  (`BotFirstAid+CG_ApplyToSelf.method_1`, 364 ms after Confirm Leave) is the
  same vanilla race family as r1 — `BotWeaponManager.Dispose` nulls
  `Selector`, the bot's meds operation finishes on the next health tick,
  caught by `BaseHealthController.ManualUpdate`; no Arena/Redeploy frame.
  B (cooldown race) is not natively triggerable (no exit while dead) and is
  dropped; the `sptUpdateLastRunTimestamp` delta is SPT's
  `HideoutHelper.UpdatePlayerHideout`; the `burst spawn … failed: The
  operation was canceled` line was teardown noise (fixed above); the
  BotDropDiag hash change during r2 was a rebuild on the dev machine at
  08:42, all four r2 raids ran on the old build. Open: the single Unity
  `MenuScreen` coroutine error in A (no Arena/Redeploy/PitFireTeam frame
  found). **Next GPU job: `GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r3.md`**
  (new client/server binding; E = TDM without kits on the ground, A' open
  sub-cases, C' natural death, D optional; the meds-callback family and the
  hideout timestamp are pre-declared as counted, not stopping).

- **Bot Surv12 drop diagnosis (2026-09-12):** the user sees PMC bots start a
  Surv12, stop, and leave it on the ground (Arena). Two verified mechanisms,
  neither in Arena/Redeploy code: (1) ContinuousHealing 1.6.3
  (`com.lacyway.ch`, installed 5./6.9.) keeps a static `CancelRequested` set
  by the LOCAL player's heal cancel and applies it in its
  `MedsInHandsOperation.OnEffectRemoved` prefix to every player before its
  `IsYourPlayer` check, so bot surgeries end at their next effect removal
  until the player starts a new heal; (2) SPT `pmc.json
  forceHealingItemsIntoSecure = true` (honoured by APBS) puts PMC healing
  items into the secure container, and EFT's `BotRandomPlanItemDropper`
  (bear/usec `DO_RANDOM_DROP_ITEM`) throws a random secure-container item on
  every peaceful-move start — i.e. PMC bots throw their own meds to each
  other. Log-only plugin `maschine-BotDropDiag` 0.1.0 (repo
  `../BotDropDiag`, now `f0bafe9`, SHA256 `0D885B3F…`) is deployed to
  attribute the next occurrence; r2 lists it as permitted. No Arena change.
  **Log result (2026-09-13, Arena TDM 5v5 Customs):** four Surv12 throws,
  all from the secure container, all via `OnStartPeacefulMove`, 1.9–11.7 s
  after the thrower's own spawn; no `SURGERY-START` at all and every
  `MEDS-FINISH` with `CH.CancelRequested=False`, so mechanism (1) did not
  occur that session and no bot used a kit. Verified against `_decompiled`:
  `BotRandomPlanItemDropper._activateTime` is never assigned (the 60 s grace
  period is dead once the process has run a minute), `_nextTimePossible`
  starts at 0, `IsTrue100(160)` is always true, and the gesture partner is
  the nearest bot of any side (`BotsConnections` is raid-wide). Fix options
  were assessed; the user chose the Arena-only gate (`NoBotGesturesPatch`,
  see the entry above) since normal raids are not bothered by it. The
  standalone item-level fix for normal raids (client postfix on
  `FindItemToDropToBot` returning null for `Meds` plus setting
  `_activateTime` in `Activate`) stays an option, not built.
  Do NOT set `Patrol.DO_RANDOM_DROP_ITEM` on an arena bot's
  `Settings.FileSettings`: `BotSettingsComponents.Copy()` is shallow and
  `Patrol` is a process-wide cached instance per (difficulty, role), so the
  change would leak into normal raids.

- **Respawn stage 0 GPU r1 stopped on a vanilla race; r2 issued (2026-09-12):**
  `c42fdec` (Codex) confirms on the bound build: startup markers present, no
  fail-closed marker, an ordinary Redeploy respawn with the expected order
  (`Death intercepted` → `Spawn protection active` → `Respawn complete` →
  `Spawn protection ended`), KillcamReplay/AutoIFF/Freecam rebinding without
  exception; 8/8 bound hashes matched at four reads. The remaining A checks,
  B, C and D stay OPEN: the naked replacement died 0.5 s after the 10-s
  protection with `MaxRespawnsPerRaid = 1`, and the run then stopped on a
  `NullReferenceException` in `BotSurgicalKit+CG_ApplyToCurrentPart.method_1`
  130 ms after KillcamReplay re-invoked the deferred `LocalGame.Stop`.
  Classified from the decompiled game as a **vanilla race, not a regression**:
  `LocalGame.Stop` → `BotsController.StopGettingInfo` → `BotsList.Stop` →
  `BotOwner.Dispose` (`Medecine = null`, `WeaponManager` disposed); a bot still
  mid-surgery finishes its `MedsInHandsOperation` on the next tick
  (`MedEffect.Residue`) and its completion callback dereferences the disposed
  owner; `ItemController.SafeRemoveItemEvent` catches it. No Redeploy, Core or
  Arena frame; stage 0 touches neither Stop nor bot disposal; KillcamReplay's
  19-s deferral only widens the window. Also corrected: `Blocked raid-end black
  screen while respawn is pending.` cannot appear on the normal Redeploy path
  (CG_Spawn is intercepted before any Stop, and only `BaseLocalGame.Stop` and
  `TarkovApplication` start that fade) — r1 expected it wrongly.
  **Next GPU job: `GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r2.md`** (same bound
  files, no rebuild; test values `MaxRespawnsPerRaid = 3`,
  `SpawnProtectionSeconds = 30`; that exact vanilla signature is pre-declared
  as counted, not stopping). Committed on `fix/offline-harness-contracts`,
  where `c42fdec` landed.

- **Offline harness contracts repaired (2026-09-12):** test-only branch
  `fix/offline-harness-contracts` from `b5ff258`. Reproduced all three reported
  early failures; no production regression found. `14d7d56`: PFT now checks the
  frozen three-argument arm and canonical Deathmatch/TDM policies (**248**).
  `f82f980`: team expansion checks current announcer provenance records, cue
  scope and hashes instead of superseded WARLORD CC wording (**774**, including
  13 in-memory negative controls and later installed PFT/SAIN checks).
  `42d2400`: Give-up fault binds only to the TDM branch, updates both shared
  terminal guards and preflights all targets (**618 / 16 behavior faults / 618**).
  Independent adversarial reviews passed. Default unit suite **617 passed,
  2 skipped, 0 failed**; enabling existing database/APBS fixtures yielded
  **619/619 passed, no skips**. Full details and safe command:
  `OFFLINE-HARNESS-REPAIR-2026-09-12.md`. Installed files were not rebuilt or
  deployed: **73 files unchanged at the first receipt**; later the Arena cfg
  differed, with a recorded modification time before the final unit run
  (likely parallel GPU synchronization; local write time/author not captured),
  while all other **72 original hashes still match**.
  Current settings were preserved and this drift recorded, not rolled back.
  Unit builds redirected SPTUserPath to a scratch
  destination; use this instead of bare dotnet test during bound GPU tests.
  No restart required. The r6/r4 preflight blockers below are still in force;
  these harness fixes do not rebind or pass those native acceptance jobs.

- **Redeploy.Core stage 0 integrated (2026-09-12):** Redeploy `c938f5c43cb311a8a9ba1f81b2eb10573b99b936`
  (main, deployed) hardens the shared engine: `RunRespawn` refuses before any
  teardown (null request/game/selector; `DeathInterception.IsAvailable == false`
  = keep-alive patch set incomplete, no driver, or Fika; raid already ending;
  re-entrant call from a death/respawn raise; pipeline still running); every
  Core event is raised per subscriber; `DeathInterception.EnsurePatched`
  installs each patch on its own and fails closed; raid start/end postfixes are
  paired in `RaidLifecycle` (NarrateGameWorld start and HideoutGame end
  filtered, engine state reset at both, drivers' `OnRaidEnded` still before
  `ArenaSession`'s handler); the owner-cleanup guard is bound to the retired
  owner instance; Core enables the Freecam patches at raid start for every
  consumer. Design, evidence and GPU scenarios: `../Redeploy/docs/RESPAWN-API.md`.
  Arena side: `668619f` (LiveKitRespawn tolerates a refusal — `IsStillWired`
  leaves an untouched body alone, the rewire steps are idempotent, and the swap
  stands down while a pipeline runs), `bb82ce8` (death path publishes only the
  verified body; `OnKitApplied` after success instead of inside the engine's
  equipment strategy), `3a5123c` (`ArenaEvents` per-subscriber isolation).
  Regression: unit suite **617/619 (2 skipped)**, BleedingCleanup, KitPolish
  Supplies **1605**, TdmMatch **227** pass. **Pre-existing failures, verified
  identical at `67d27ca` in a temporary worktree:** PitFireTeamChecks stops
  after 98 assertions ("client freezes the server-armed mode…"),
  TeamArenaExpansionChecks after 277 ("VoiceBosch WARLORD cues" — the announcer
  replacement removed the CC fragment the harness requires), TeamGiveUpChecks
  mutant `kill-first-local-death` (`if (kill.LocalIsVictim) ReportLocalDeath();`
  now occurs twice in `ArenaMatch.cs`). Not GPU-verified: a refused live swap,
  the cooldown race, the fail-closed patch set and re-entrancy (design doc,
  section 10, scenarios 8–11). Source/build commit `b5ff258` (clean; code stand `f257a78`, follow-ups below).
  **Installed client SHA256 `A92164BC306762ABD954CF3F2A5AA012FA49E8471626966C5A459A1FEE02843D`**, Core
  `FA452E5A50570D0CDFBDE9CB4B6289BED33A4773A438058EF4F8DB0288E9639C` (rebuilt with
  Redeploy `287c2d9`, a docs-only commit; code stand `c938f5c`), Redeploy.Client `7EC406A1AB98FB3348CD59B3C6803DAE3897827BA4F55ADB385FBAE879319E46`,
  server `5A9201177DC3E4404995EA314FE48A231712435D5E5EB9E1A081C67734D6C04E`
  (rebuilt by the harnesses' `dotnet test` from unchanged sources; the server
  hash drifts with every commit because ProductVersion embeds the commit). The r6/r4 GPU
  instructions were bound to client `2042C558…`; rebind them to this build (or
  restore that client) before their evidence is comparable; `bc7f541` (Codex,
  20:39) already records the r6/r4 preflight blockers. Adversarial review of
  the three commits (3 reviewers, 2 refuters per finding): one medium and three
  low findings, all addressed in `f59f367` (a mid-wire replacement is wired
  before a bounded area aborts; `IsStillWired` requires a Started owner) and
  `f257a78` (allocation-free damage feed; dead KitPolish stand-in removed).
  BleedingCleanup and KitPolish Supplies re-run green on `f257a78`.
  **GPU test order for this build: `GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r1.md`**
  (ordinary Redeploy respawn, cooldown race, Arena kit swap/death/give-up,
  optional round break; binds every hash above plus SAIN, KillcamReplay and
  DynamicMaps). Run it before re-binding r6/r4 to this client.

- **Creator announcer pack delivered (2026-09-12):** User supplied the newly
  Creator-generated `alle_ansagen.ogg`; all **46 phrases** were split in the
  supplied order. Replaced all **18 active** announcer OGGs directly in tracked
  assets; **28 reserved** phrases/alternatives are under `sounds/reserve/`,
  packaged but not loaded and without new gameplay triggers. ULTRA stays mapped
  to `multi_kill`; current ROUND WINNER/MISSION FAILED/MISSION ACCOMPLISHED/GAME
  OVER results retain their keys. Original stereo/44.1 kHz, Vorbis q7, uniform
  -1 dB headroom and 5/10 ms edge fades. Source hash, cuts, output hashes and
  user-stated Creator provenance: `ArenaMode.Client/Assets/ArenaMode/ANNOUNCER-SOURCES.json`;
  full list/reproduction: adjacent `ANNOUNCER-PACK.md`. Former free-plan and CC
  announcer records are explicitly historical; these new files are not CC.
  Offline decoder verification and independent waveform/transcript/cut/metadata/
  license review passed; wrong-source, corrupt-OGG and wrong-hash controls fail
  as intended. All **61 asset files** match both isolated staging and the local
  install through the normal `DeployArenaAudioAssets` target. The three Arena
  DLL hashes and existing CC0 hitmarkers are unchanged. No full build, public
  release or push. Backups/evidence: `artifacts/creator-announcer/` (ignored).
  **Restart the game**, since healthy announcer PCM remains cached between
  matches; no server restart is needed. Native listening remains for the user.

- **r5/refinements r3 triaged; safety follow-up and air control delivered (2026-09-12):**
  `0a49898` confirms four isolated F9 Close clicks without firing, subsequent
  normal controls, three-round autoexit and FFA autoexit. Terminal toggles/score
  freeze, timely result-button exit and remaining budget cases stayed open.
  `bd95b72` stopped the separate first Customs TDM raid after a new
  BotWeaponSelector.OnWeaponTaken NRE and an ambiguous one-round ammunition loss
  across six native graphics/resume clicks. None of the four refinements gained
  acceptance. Historical reports and evidence remain unchanged.
  Native source/IL identifies two actionable gaps, without proving the historical
  null receiver or which click fired. `12df136` extends the released-pointer
  drain to exact native menu return, preserving native navigation; `6694a84`
  prevents only already-retired exact Arena bot selectors from reviving disposed
  weapon services, preserving IsChanging completion and ordinary/temporary
  NonActive behavior. No blanket error suppression. Reviews:
  `NATIVE-MENU-INPUT-REVIEW.md`, `BOT-WEAPON-CALLBACK-REVIEW.md`.
  `0b7f38d` adds local Arena WASD air control: default 60 degrees/second,
  live F12 Movement `AirControlEnabled` and `AirControlDegreesPerSecond` (0–180).
  Preserves native XZ magnitude/Y and collision, heading without input, exact
  body/raid scope, double-jump and conservative mantle limits. Menu ownership
  denies a new turn. Review and limits: `AIR-CONTROL-REVIEW.md`.
  Independent production and harness reviews passed. Root verification:
  input **78 assertions/18 compiled faults**; combined exit **118 assertions**,
  existing pickup/SAIN **9 faults**, new weapon guard **8 faults**; air control
  **155 assertions/21 faults**, plus installed native contracts. These checks
  are not native Unity/Harmony/physics or GPU acceptance.
  Source/build commit `0b7f38d594b09644803f263a510543293b11ed63`, branch
  `feature/arena-air-control`, clean tree at build. Full native client:
  **0 errors, 4 existing nullable warnings**.
  **Installed client SHA256:**
  `2042C558340877C85DAF0687A8AEBB4E7D54FD720AD467E770F5AEE7F0914ECD`.
  ProductVersion `0.1.0+0b7f38d594b09644803f263a510543293b11ed63`.
  Build/manifest: `artifacts/double-jump-build/f4f38f3505ba4d9ca1976ebccf2a4a5a/`.
  Backup/receipt: `artifacts/air-control-delivery/2d2989b50ded4bcd93571978283a7754/`.
  Fresh inventory verified all **43 non-client files unchanged**, including
  server, Redeploy, SAIN, DynamicMaps, audio and current post-test settings.
  No old cfg/settings files or profiles restored; no server build ran. Server
  remains `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
  Next bounded job: **`GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r6.md`**, first
  isolated native Resume/settings clicks and natural TDM replacement, then
  remaining result/continuous cases. After safe A and no later stop, separate
  **`GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r4.md`** prioritizes air control,
  then the still-open four features. Both bind the client above and preserve
  prior evidence. Every new unsafe state stops the entire test again.
  **Restart the game after GPU synchronization; no server restart needed.**
  Only D:\SPT41 delivery is verified here; GPU mirror/runtime remain unverified.
  User deployment authorization persists; the mirror skips files in use, so
  do not ask repeatedly whether the game/server is stopped before delivery.

## Historical snapshots

The entries below preserve earlier states. Their current-build and pending-test
claims are superseded by the entry above; old accepted evidence remains valid
for its recorded client only.

- **r4 triaged; panel-close correction r5 delivered locally (2026-09-12):**
  `228d892` confirms the second F9 Close click fired one MP-153 round. Its test
  correctly stopped; the later manual disconnect triggered the SAIN guard once
  without a new exception. Pickup cancellation was unobserved. Three-round
  autoexit, terminal F9/banner/score freeze and B/C remain open. Original reports
  and evidence are unchanged. See `ARENA-PANEL-INPUT-REVIEW.md`.
  Branch `fix/arena-panel-close-input`, source/build commit
  `f64df5be499d06d29cc4e6b717dd52ea0931725a`, clean tree at build. F9 and Tactical
  buy-panel close now keep command/axis filtering until one fresh actual input
  batch with released mouse buttons finishes delivery. Existing owner votes,
  Escape/release whitelist, exact Arena game/world, and normal-raid behavior
  are preserved; stale/reentrant/smooth dispatch cannot release a newer close.
  Input-manager replacement and failed patch cleanup cannot strand the latch.
  Independent adversarial implementation/test review passed. Parent checks:
  **60 assertions and 14 compiled faults**, native installed contracts, existing
  banner baseline **44 assertions**. Full real client: **0 errors, 4 existing
  nullable warnings**. No native GPU proof from these offline checks.
  **Installed client SHA256:**
  `7DCAA94CFDF5F0C7E6736796901BE4C3DCBADCEC6F74C76E1668C22B81201F11`.
  ProductVersion `0.1.0+f64df5be499d06d29cc4e6b717dd52ea0931725a`.
  Build/manifest: `artifacts/double-jump-build/54da549e8e144e76beabdac7d9b6b080/`.
  Backup/receipt: `artifacts/deathmatch-r5-delivery/1c5a3a84724f483c8aa3a79f1402eb10/`.
  Fresh before/after hashes verified all **42 inventoried non-client files**
  unchanged, including server, Redeploy, SAIN, audio and current settings. The
  two settings files were hashed in their current post-r4 state; no older files
  or metadata were restored. No server build or profile edit ran.
  Server remains `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
  New job **`GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r5.md`**, test ID
  `deathmatch-menu-exit-r5`: first controlled Close/ammunition comparisons,
  then the three-round autoexit and remaining bounded cases. Ready and capped
  pending/released input diagnostics must be captured. This corrected-build
  retest releases the r4 test stop only for its stated scope; any new unsafe
  state stops the entire run again. Four refinements remain separate in
  **`GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r3.md`**, same client, only after
  r5 A passes. Earlier instructions bind superseded builds and stay historical.
  **Restart the game after GPU synchronization; no server restart needed for
  this client change.** Delivery under D:\SPT41 is verified; GPU mirror and
  native gameplay remain unverified here. Do not request repeated permission
  to deploy: the user authorized it and the mirror skips files in use.

- **r3 triage completed; combined client r4 delivered locally (2026-09-12):**
  branch `fix/deathmatch-r3-exit`; source/build commit
  `39bd7882713da14afcc2432cad092a6c9e1919f6`, clean tree at build. Contains all
  four refinements below plus `40b859b` (F9 owns terminal result display) and
  `39bd788` (exact-owner pending pickup cancellation and optional null-only SAIN
  teardown guard). Native Stop disposes bot services before the animator fade
  finishes; SAIN can disable an attached but uninitialized component. The guards
  preserve native disposal/registries, viable activation cleanup and profiles.
  No blanket exception suppression. Full diagnosis and limits:
  `DEATHMATCH-R3-REVIEW.md`. Same-GameObject BotOwner lookup during pool destruction
  is not guaranteed; if unavailable, SAIN's original still runs. Thus no claim
  that the two historical NREs are already runtime-fixed. The separate possible
  F9 close-click shot remains unconfirmed and is explicitly in the retest.
  Independent implementation/test review passed. Parent checks: exit 69
  assertions/9 compiled faults, banner 44/5, installed native/SAIN contracts.
  Final native client build: **0 errors, 4 existing nullable warnings**.
  **Installed client SHA256:**
  `9D27F9A0EA9F76805E33F3F9FF5CBA2AF8821A4C210CFB6822EDA8EDD6090BD9`.
  ProductVersion `0.1.0+39bd7882713da14afcc2432cad092a6c9e1919f6`.
  Build/manifest: `artifacts/double-jump-build/f51871e16b864fdeb72337aab1d0c367/`.
  Backup/receipt: `artifacts/deathmatch-r4-delivery/529c55a9d656423692b4cdf1e1131d74/`.
  All 41 inventoried non-client files stayed byte-identical during both client
  deliveries, including server, Redeploy, audio and current settings. No server
  build ran. Server remains `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
  **Restart the game after GPU synchronization; server restart not required by
  this client change.** GPU mirror and native behavior remain unverified here.
  New test job `GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r4.md`, ID
  `deathmatch-menu-exit-r4`: first repeat C2 exit, then remaining bounded cases.
  This supersedes the r3 stop only for the focused corrected-build retest;
  any new exception/unsafe state stops it again. Four-feature acceptance remains
  separate in `GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r2.md`, same build, only
  after r4 case A passes. The old r1 feature checklist binds a superseded DLL.
  Immutable r3 report/evidence preserved; no new GPU test or profile edit here.

- **Prepared refinements delivered locally (2026-09-12):** user authorized
  deployment and r3 triage. The exact staged client below is now installed at
  `D:\SPT41\BepInEx\plugins\maschine-ArenaMode.Client.dll`, SHA256
  `3FFF60D7EA67B7CF40046421362F93BED3A87C984449F0C62B518026344F3FF6`.
  Backup and receipt: `artifacts/refinements-delivery/31fea656258a464698bbc56bd350376d/`.
  All 41 inventoried non-client files stayed byte-identical during delivery,
  including the installed server, Redeploy, audio and current settings. Only
  the game needs restarting; GPU mirror and live behavior are not verified here.
  This supersedes the staged-only delivery status below, not its test limits.
  Work continues on `fix/deathmatch-r3-exit`; r3 exceptions remain under triage.

- **Four client refinements staged, not delivered (2026-09-12):** source branch
  `feature/arena-visibility-movement`; source/build commit
  `c151aa683a7bcb83eec8d0183f36fc19ab8ce18a`, clean tree at build. Feature commits:
  `5ec04f9` (optional DynamicMaps 1.2.1 trimmed-corpse skull removal), `ef53c2f`
  (per-side contour grid fade, original opacity within 6 m and invisible beyond
  20 m, physical/navigation blockers unchanged), `0be7f7a` (friendly diamonds
  projected through applied native SSAA state into final DLSS output), and
  `b1148a6` (one deliberate air jump after a ledge fall, including early tiny
  departures before FallDown). No mantle-limit, server, audio or default change.
  DynamicMaps removal uses only its own marker UI methods and exact successful
  trim evidence; native corpses/Players/loot registries remain intact. Ground
  contact, native jump signal ownership, one action debit, reset/body lifetime
  and stable lift across rapid landings are preserved by the movement revision.
  Independent adversarial reviews passed all four implementations. Parent
  rechecks passed: corpse 57 assertions + 7 compiled faults and existing 44/4
  cleanup regression; marker 148 assertions + 3 projection faults (leaf also ran
  the complete 14-fault regression); movement 278 assertions + 22 faults; area
  77 assertions + 4 faults and existing 69,023/13 enclosure checks. These helpers
  do not run native rendering, Unity contact or the EFT animator graph.
  First integrated build caught an EFT/System Version name collision; explicit
  `System.Version` correction `c151aa6` passed independent review and compilation.
  Final full client: **0 errors, 4 existing nullable warnings**.
  **Staged client SHA256:**
  `3FFF60D7EA67B7CF40046421362F93BED3A87C984449F0C62B518026344F3FF6`.
  ProductVersion `0.1.0+c151aa683a7bcb83eec8d0183f36fc19ab8ce18a`.
  Output/manifest/receipt:
  `artifacts/double-jump-build/146ebfa928204a548a5c314c528e431c/`.
  **Not installed or GPU-tested.** Installed r3 client/server/Redeploy, audio,
  other assets and r3 instructions still match all 40 non-setting baseline files.
  ArenaMode.cfg and server client-settings.json changed concurrently during r3
  tests without writes by this task and were left intact. No server build ran.
  New separate test job: `GPU-ARENA-REFINEMENTS-TEST-INSTRUCTIONS-r1.md`, ID
  `arena-refinements-r1`; deliver its exact staged client only for a later test
  stand and restart the game. No old r3 report is acceptance of these changes.
  Concurrent GPU report `ee0a421` arrived as a sibling of movement `b1148a6`;
  normal merge `3f9052b` preserves both histories. The report's C2 exit failures
  still need separate triage before any further acceptance raid; this staged
  checklist does not release the r3 stop. This feature task preserves the report
  without claiming to diagnose those old-client errors.
  Reviews: `DYNAMICMAPS-CORPSE-REVIEW.md`, `AREA-GRID-VISIBILITY-REVIEW.md`,
  `FRIENDLY-MARKERS-REVIEW.md`, `DOUBLE-JUMP-REVIEW.md`.

- **r3 build identities refreshed after parallel work finished (2026-09-12):**
  the user confirmed completion; a fresh installed-file check supersedes the
  original r3 delivery hashes in the historical entry below. No build or DLL
  delivery was performed for this refresh. Client SHA256:
  `4F33B337108987C5471E42FDD706F57D61421CB05D9AAD25186A592366130473`.
  Server SHA256: `294635440FD1E788B06B708DFFC4912222AD7539E79EEB6524C1E2E8F56BF7E7`.
  Both DLLs embed `f887090f5b04569a6f35fd19bf8a3ac63c25e0e2`; checkout at the
  comparison was `939d56454e7479a7d49f2ac9a03f475c02d59a29`. Client/Shared sources
  remain unchanged since preview implementation `3010346`. The installed server
  matches the current Debug artifact, not the separate Release artifact; do not
  substitute a Release hash for the actual installed file. The later server
  commit changes the weapon ladder draw, with builds predating that commit.
  Independent read-only review matched the installed server's CodeView identity
  to its portable PDB and its LadderPicker document hash to current `939d564`
  source: the last production change is included despite the older embedded id.
  `GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r3.md` now binds these actual identities;
  test ID and scope remain unchanged. Restart both processes and verify GPU-side
  hashes before testing. This is a local file check, not GPU/runtime acceptance.

- **GPU Deathmatch r2 triaged; preview lifecycle client r3 delivered (2026-09-12):**
  report `c5c8945` confirms bounded native menu behavior and the same visible
  Arena equipment on map/offline/confirmation. All 18 original screenshot hashes
  were independently verified. Its four Unity errors are rejected SetParent calls
  from inline host OnDisable cleanup, not a proven crash or profile corruption.
  No raid started: loading, repeated navigation, actual match/round rules and
  raidwise protection remain open. The user's jitter acceptance remains intact.
  Implementation `3010346e23f802ae40d58669d12b4327fa86b1fe` detaches from known
  active screens before native Close; fallback hide/destroy callbacks defer
  structural work to the independent guard. New attachments drain old cleanup
  before snapshotting native flags and preserve their already-admitted widget
  request. Destroyed Unity hosts fail open. Camera/posing/gameplay/audio unchanged.
  Adversarial review caught and fixed a fast-reopen owner-loss case; final review
  found no further established defect. Parent reran 51 state assertions plus
  121 production-flow lifecycle assertions, ten compiled fault controls, native
  hook compilation and the synthetic frame-helper check. These do not execute
  native rendering. Actual staged client: zero errors, four existing warnings.
  **Client SHA256:** `804EB3ED5362F5D269E5C5B8080CDD3941BCA8C6E6F66435EECAB49294454C2B`.
  Build manifest: `artifacts/double-jump-build/280c414cc795412aac925496526fb300/manifest.json`.
  Source tree was clean for that build. Local installed client matches; all 40
  freshly inventoried other files stayed unchanged during this client delivery.
  Receipt: `artifacts/preview-r3-delivery/6921ac83f4fb4bffa82b334e91a2f8d0/receipt.json`.
  Parallel work committed `5c08a55` (weapon placeholder filtering) before the
  preview commit and subsequently exchanged server DLLs during tests. Its final
  review correction is `6f40314bd70f861c1cd1ffd3d2db5bddc1354ccc`.
  **Server SHA256:** `47C21F048E097FA0DDBFA7A142BB8DD3C1259F835D10B1C2BB13B387AEDA463F`.
  Installed server matches that work's Release artifact and embedded source id;
  this preview correction did not revert or deploy it. The receipt's intermediate
  server snapshot predates the parallel final delivery; use the final build-pair
  record `final-build-pair.json` alongside the receipt and these r3 hashes.
  The other 39 inventoried files also remained unchanged through that final check.
  **Restart game and server**
  for the combined test stand; do not reuse r2's server hash or native acceptance.
  GPU mirror/native tests are not verified here. New test job:
  `GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r3.md`, build ID `deathmatch-menu-preview-r3`.
  Start with two native forward/back cycles, then the still-open bounded FFA and
  all-vs-player raids. Keep r1/r2 reports immutable. No mandatory jitter recording.

- **GPU Deathmatch r1 triaged; client r2 delivered (2026-09-12):** report
  `05e88d0` stopped solely at the stale "Five modes" description under the too-broad
  stop-on-any-error rule. Four original screenshot hashes were independently
  verified; Arena selection/model visibility is observed, but no map menu or raid
  ran. This is not a match crash or failure of the new Deathmatch lifecycle.
  Commit `02a35ca` replaces the count with "Solo and team arena combat.", retaining
  two explicit lines and the unchanged PFT branch. The user separately confirmed
  that mannequin jitter is gone; accept that observation without claiming r1
  video/frame evidence or downstream model continuity. Preview code is unchanged.
  A bounded legacy-id audit also found and fixed the missed canonical Deathmatch
  admission for point/beam laser brightness (`2066950`). Exact raid/body ownership,
  unknown-mode refusal and absence of solo team tint remain. New production-linked
  test failed before the fix, then 1293 assertions and 14 compiled fault controls
  passed. Independent implementation/test/instruction review found no remaining
  established defect. Actual client build succeeded with 0 errors and 4 existing
  nullable warnings. No server build or audio/dependency delivery was needed.
  Test build **deathmatch-menu-preview-r2**, implementation `20669505459d90c212dc90806f24c12b3082870b`.
  **Client SHA256:** `60C2685951C4ECD17E29C80192EE26AA7CF7735C723091C89E8162BFAF4C6551`.
  **Unchanged server SHA256:** `8F9A56F574835FEAB73AB78C2EF4D5CF5734F12ABE63F384E2148CF319890B73`.
  Local client delivery hash matches. All 40 inventoried protected files, including
  server and Redeploy DLLs, remained unchanged across delivery. Receipt:
  `artifacts/deathmatch-r2-delivery/314aa230db0f46128a262474d7f3d3f4/receipt.json`.
  **Game restart required.** GPU mirror/new native acceptance still pending.
  Use `GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r2.md`; keep r1 files immutable.
  Cosmetics are recorded as failures but do not stop independent tests. Safety,
  exceptions, uncertain mode/rules and hash drift still require stopping. Missing
  recording tools leave that optional subcase open. Prioritize untouched menu,
  downstream-preview and bounded raid cases; jitter video only if it recurs.

- **Unified Deathmatch and continuous Arena preview ready for GPU r1 (2026-09-12):**
  branch `feature/deathmatch-menu-preview`; implementation commits `6e6d469`
  (Deathmatch) and `c8acf08` (preview). Native mode picker now has Deathmatch,
  Gun Game, Tactical and Team DM. Deathmatch merges old Endless/Rounds with
  finite/unlimited round count, per-round kill/time limits and FFA/all-vs-player.
  User confirmed individual FFA winners and local-player targets in all-vs-player.
  Both limits off means continuous play; finite rounds require an end condition.
  Legacy settings migrate once. Rules freeze before the exact protected arm,
  including server-acknowledged hostility. Result ownership preserves native
  death/respawn/exit and profile boundaries. Tactical labels show actual solo
  1vN (default 1v5) or 5v5 with four accepted PitFireTeam companions.
  FFA needs individual native groups as well as server role hostility: confirmed
  same-map loading admission, exact spawner ownership, delayed PreActive-safe
  cleanup and missing reciprocal enemy repair are documented and tested. Do not
  simplify these to an IsActive-only hook or dispose an empty newly created group.
  The detached Arena character is transferred to subsequent native pre-raid
  screens with exact widget/generation ownership. Its smaller render-origin
  offset is a jitter hypothesis, not a proven visual fix; no animation speed
  changes were made. Menu area placement and stale row callbacks were corrected
  after independent reviews. See DEATHMATCH-MENU-REVIEW.md, DEATHMATCH-MODE-REVIEW.md,
  DEATHMATCH-HOSTILITY-REVIEW.md, DEATHMATCH-GROUPS-REVIEW.md and ARENA-PREVIEW-REVIEW.md.
  Offline verification: full server suite 593 passed / 2 environment-dependent
  skipped; runtime163 assertions +9 compiled defect controls; preview51 +5;
  server-hostility55 tests +2 defect controls; group83 +2. Native menu/integration
  suite passed including existing result-exit and menu fault controls. Additional
  native-area71 and Labyrinth-access50 assertions passed after fixing their
  missing test dependency and legacy-mode expectation. Peer reviews found no
  remaining established defect. These checks do not execute native AI or rendering.
  Final staged build used implementation `c8acf084b3eaf4e98455a70eacaa90ba56349207`,
  with zero compilation errors. Only later test/handoff documentation changes
  differ from that implementation; do not rebuild merely to make its recorded
  source commit equal the documentation HEAD.
  **Client SHA256:** `FBBF82C6993E16D3299B3081B8A53BD7770F3712CA3AB3E0A4B5F2D052A92D90`.
  **Server SHA256:** `8F9A56F574835FEAB73AB78C2EF4D5CF5734F12ABE63F384E2148CF319890B73`.
  Both DLLs delivered locally with matching hashes. All39 protected Arena audio,
  config and other asset files plus Redeploy stayed unchanged. Delivery receipt:
  `artifacts/deathmatch-delivery/2314cb145a9540cdb1d69bdbbf875f1f/receipt.json`;
  build: `artifacts/duration-balance/build/4dd14e613d4f4982b1f994996484510e/manifest.json`;
  parent gate run: `artifacts/deathmatch-final-gates.log`.
  **Restart both game and SPT server.** GPU mirror and live acceptance are pending.
  Next job: `GPU-DEATHMATCH-MENU-TEST-INSTRUCTIONS-r1.md`, build ID
  `deathmatch-menu-preview-r1`. Bounded menu/navigation checks, original short
  clips/consecutive-frame inspection and at most three limited Deathmatch raids;
  capture actual process/build identities and stop on errors. No r6 result carries
  over to these DLLs. Existing audio rights and protected-5v5 follow-ups below remain.

- **ElevenLabs clips now persist across builds (2026-09-12):** the user explicitly
  requested moving the five audition clips into the repo after deployment
  restored three original voices. Copied the five reviewed OGGs unchanged into
  `ArenaMode.Client/Assets/ArenaMode/sounds/`; DOUBLE KILL, MULTI KILL (spoken
  ULTRA KILL), MONSTER KILL, UNSTOPPABLE and GODLIKE now come from that source.
  Normal build/publish/deploy includes all five. Packaged attribution/source
  hashes and the root/packaged LOCAL-ANNOUNCER-TRIAL.md now reflect repo inclusion;
  previous local-only/exclusion/reapply notes below are historical. These are
  ElevenLabs free-plan, non-commercial development recordings, not CC assets;
  review voice rights/terms/attribution before public release. No publication
  authorized. Existing hitmarker and other voices are unchanged.
  Verified five source hashes, Vorbis/stereo/44.1 kHz metadata, attribution,
  decoded signal and durations. Executed only the existing audio-copy MSBuild
  target, first in isolation then live; all 30 packaged source assets matched
  both destinations. No gameplay compilation or DLL delivery by this task.
  Evidence/backups: `artifacts/announcer-repo-promotion/receipt.json`.
  Twenty-five other installed files stayed unchanged. Client and server DLLs
  changed during the window of concurrent work; receipt records both hashes and
  write times rather than claiming unchanged binaries. They were not reverted.
  Independent audio/source/deploy review passed. **Restart the game** to clear
  announcer PCM caches. GPU mirror and in-game audibility remain unverified.

- **Quick-slot ledger survives the protected 5v5 rebuild (2026-09-12):**
  pre-existing defect from an adversarial review. `AutoQuickSlots` remembered
  its own bindings as `Item` references and counted a key as player-cleared
  only while that object was still on the body. `TeamArenaLoadoutSnapshot`
  reissues rig/pocket/backpack items under fresh ids between rounds and
  carries the FastAccess bindings over to the new instances, so a key the
  player cleared after a round rebuild read as "item gone" and refilled
  within a second; a key the player had filled themselves from a protected
  special pocket slot was never the service's and stayed cleared, hence the
  visible asymmetry. New pure `Kits/AutoQuickSlotLedger<TSlot>` (linked into
  the tests like `BotLoadBoostPolicy`): ids instead of objects, an occupied
  key adopts its current occupant, a key that empties is a clear iff the
  remembered id is still on the body. `ArenaRespawnDriver.RespawnProtectedTeamPlayer`
  settles the ledger against the dead body before `TryCommit` (the poll stops
  at the death, so its last second is unjudged) and remaps ids with
  `NextRuntimeByPreviousRuntimeId` only after a settled commit. The first
  review found that remapping an unsettled ledger turned a medkit used up in
  the last second before the death into a permanent player clear, because
  the map names every entry-loadout item; the settle closes that. Solo modes
  unchanged except one edge: an item the player bound over an owned key and
  later used up no longer counts as a clear. 17 ledger tests; mutants verified
  (adoption off: 2 red; emptied keys kept owned: 8 red). Two independent
  adversarial reviews, the second with no P0-P2 and its P3 comment/test
  findings applied. Unit suite 553 passed / 2 skipped; Team Arena loadout
  checks 142 assertions; Release build zero errors, pre-existing warnings
  only. Client-only, game restart required; server source unchanged.
  Build and installed SHA256 match: client
  `68C1952411D592DF520ECB406427205D4B13EBA9B064DB3356F61FFE122B833E`, server
  `77C2719B9E7A12746EDCFC1449D96768FF74369A580EF48585656C536C62334E`. No GPU
  result: in a protected 5v5 raid, clear a bound medical key after a round
  rebuild and confirm it stays empty; use a medkit up right before dying and
  confirm its key refills next round.

- **r6 bounded GPU evidence reviewed (2026-09-12):** report `98211b9`,
  `GPU-PANELS-R6-RESULTS-2026-09-11.md`, records one short Endless Factory raid
  and one independent BEAR TDM 5v5 raid at 1920x1080. The pre-start client hash
  matched `D9BB7FA0...1461626C` / source `41bb6e3`. Screenshot/roster/config
  review confirms numeric other-kill distances, five complete separate rows,
  solo opposing/opposing names, both TDM kill directions using the correct
  relative teams, and new Green/Magenta entries after a native palette change.
  TDM names remained colored with both TeamTracers and TeamLasers disabled.
  Standard magazine/grenade row and menu hide/return checks are accepted only
  within the report's observed scope. Each raid's recorded protected-field
  comparison has 16 equal and zero unequal results, not whole-profile equality.
  Independent raw-log audit: no client/server Error/Fatal/Critical entries or
  thrown HUD/TMP exceptions. The two deferred raid-end warnings are followed
  by server snapshot restoration and protected-end success, not failed exits.
  Local kill/natural-death identity, extra text wrapping/verified pool reuse,
  six concurrent rows, bright-scene coverage, precise timing, unknown distance,
  old-entry palette retention, grenade overflow and PFT remain open. Accidental
  solo Give up/respawn is not evidence for a natural killfeed death.
  No killfeed correction is indicated by these observations; do not rebuild or
  overwrite parallel gameplay changes just to repeat already observed cases.
  The final unchanged-delivery check FAILED: client `7965FEB2...` and server
  `4F8453B4...` replaced the initial files. Their recorded 07:10:49/07:10:58 UTC
  write times are after both raid ends and the 07:06:06 main-menu image. This
  supports attribution to the process started on the verified baseline but is
  not a measurement of loaded assemblies; the later DLLs receive no GPU
  acceptance here. `cbfc582` concerns protected-container handling and leaves
  the killfeed/HUD/team-registry source unchanged. Preserve the report's drift
  caveat and separate any later test rather than retroactively changing its hash.
  The three changed audio files (double_kill, godlike, unstoppable) match the
  committed release clips, including their current local installed hashes;
  the pre-test hashes match the local ElevenLabs overrides. A normal deploying
  build's DeployArenaAudioAssets is consistent with this replacement, but its
  exact actor/time is not established. The audition is therefore mixed until
  explicitly reapplied or fully rolled back; no audio files were changed here.
  Raw evidence: `artifacts/gpu-panels/arena-panels-native-ui-20260911-r6/2026-09-12T06-43-02-230Z/`.

- **Killfeed team colors and extended r6 (2026-09-11):** source `41bb6e3`
  replaces role/local-death name colors with the existing match-owned tracer
  registry for both attacker and victim. Solo: local friendly, other verified
  actors opposing; TDM: admitted relative teams; PFT: exact frozen rosters and
  retained same-side historical IDs. Missing/invalid/unbound teams stay gray.
  The synchronous HUD event formats each row once, before mode.OnKill; no body
  lookup, scoring-field writes, roster changes or normal-raid hooks were added.
  FriendlyTracerColor/OpponentTracerColor also tint the names, normalized from
  the shared HDR palette like friendly markers. Effects switches do not gate
  the name colors. Settings changes apply to new entries, not existing rows.
  Default names are cyan/red; roles, distance, layout and lifetime are unchanged.
  Independent review: no P0-P2 findings. Actual formatter/registry checks passed
  104 assertions and rejected five compiled source faults; they do not execute
  Unity/TMP, F12 or the Harmony hook. Clean client build: zero errors, four
  existing CS8632 warnings, all 29 embedded panel icons verified.
  Client SHA256
  `D9BB7FA07D05FF4BB6909E9C3ED885D0D41C96F8BD385FBB33B7C7281461626C`;
  manifest `artifacts/double-jump-build/355b96dcc0624dc1b7ee95d05ab42f22/manifest.json`.
  Delivered client-only; receipt/previous DLL:
  `artifacts/killfeed-team-colors-delivery/a7e0de238de1406db8a5b04621ea51da/receipt.json`.
  All 67 protected files unchanged, including server/Redeploy.Core, configs,
  release audio and installed local ElevenLabs trial clips. Preserves the merged
  grenade magazine row. Supersedes the `DD25D2AE...` client below. Restart the
  game; server unchanged. GPU mirror and live rendering not verified here.
  `GPU-PANELS-TEST-INSTRUCTIONS.md` binds r6 to this exact source/hash: retain the
  short Endless distance/solo check and add one short independent TDM team-color
  check (up to five active minutes each). Distances and team colors are separate
  main goals; palette changes/old-row retention and other unobserved cases remain
  bounded optional checks. Do not blend earlier r6 hashes/results into this run.
  PFT runtime, rare gray actors and other previously open cases are not accepted
  by the offline checks or by the independent TDM test alone.

- **Grenade counts in the magazine row (2026-09-11):** user follow-up to the
  separate grenade row. Magazines/loose rounds pack from the left of the weapon
  panel's reserve row, grenade kinds from the right (frag outermost); the panel
  is back to 280x132 and grows to the r4/r5-tested 160 only for a crowded row,
  keeping that wrap until the next body. Counting is unchanged. See
  `RAID-INFO-PANELS-REVIEW.md` "Grenades in the magazine row". Source `6450e2d`
  on `feature/grenade-mag-row` (based on `5b58e64`, fast-forwarded into
  `fix/arena-panel-native-ui`). Offline: 292 assertions, 27 rejected source
  faults; three-lens review, no P0/P1, its findings fixed before commit. Clean
  staged build: zero errors, four existing CS8632 warnings, all 29 icons
  verified. Client SHA256
  `DD25D2AEDF65854818CCDC7D2E37D20E31168412A10E48BF797137E6D7E0E13F`; manifest
  `artifacts/double-jump-build/0cba72db0a4c45858233aa9aad27238e/manifest.json`;
  receipt `artifacts/reserve-row-delivery/204162f01df148b0ba1d75f40e6e3466/receipt.json`.
  Previous r6 client `41F26B5D...` backed up there; 67 protected files
  unchanged, including server/Redeploy.Core DLLs, config, release audio and
  the local announcer trial clips. Client-only: game restart required, server
  unchanged. Killfeed sources are identical to r6's `a53a350`;
  `GPU-PANELS-TEST-INSTRUCTIONS.md` now binds r6 to this source/hash with one
  added visual check. GPU mirror and rendering are not verified here.

- **Local ElevenLabs announcer audition (2026-09-11):** user supplied
  `D:\SPT41\Development\ansagen2.wav`, generated with the free ElevenLabs plan.
  Split into five Ogg Vorbis clips in the user-provided order, with cuts in
  measured inter-phrase pauses. DOUBLE KILL, MONSTER KILL, UNSTOPPABLE and GODLIKE
  use their existing keys; ULTRA KILL uses `multi_kill.ogg` for the four-kill
  event (HUD remains MULTI KILL). No scoring or DLL changes. See
  `LOCAL-ANNOUNCER-TRIAL.md` for the exact scope, mappings and licensing notes.
  Input and generated audio are NOT committed or packaged: only the intake
  script/docs are tracked; outputs live in ignored
  `artifacts/local-announcer-trial/ansagen2/`. Release assets are unchanged.
  Delivered exactly five trial OGGs plus two identifying notes to the installed
  ArenaMode asset directory; all seven hashes verified. Three originals backed
  up; multi_kill and monster_kill did not exist before. Receipt/backups:
  `artifacts/local-announcer-trial/ansagen2/delivery/ec18d7e6c8fc48bea8dc79db4a194099/`.
  All 54 protected files, including release assets, other installed audio and
  client/server/Redeploy.Core DLLs, remained unchanged. Original rate/stereo/gain
  retained; five clips decode with expected durations and non-clipping signal.
  Independent review found no blocking issue. No build needed for this asset
  trial, and no speech-recognition or GPU listening claim. **Restart the game**
  to clear announcer PCM caches; server restart unnecessary. Deploying a later
  build can overwrite three trial clips but leaves the two added clips behind;
  reapply or fully roll back the trial as documented. Do not import these
  free-plan trial recordings into release assets without a separate decision.
  GPU mirror and in-game audibility remain unverified here.

- **r5 bounded acceptance and killfeed distance (2026-09-11):** report `487f38b`
  confirms complete names/metadata on the observed other-kill entries, two
  non-overlapping rows, common fade, menu hiding/return, and the optional
  uninterrupted two-to-one grenade comparison. It had zero local kills/deaths;
  local identities/distance, real wrapping/resizing, verified pool reuse and
  active-feed-at-menu-entry remain open. Independent screenshot/raw-client
  review corroborated these limits: three Error lines belong to SAIN, no
  Arena kill-capture/HUD failure. Use r5 `logs/final/server-current.log`, not its
  older sync-conflict server snapshots. Profile checks remain limited to the
  reported fields; no full-profile or normal-PMC proof is implied.
  Distance was still deliberately formatted only for local kills, explaining
  its absence from the observed bot-kill entries. Now all kill directions show
  measured meters, placed before weapon details. Capture keeps the original
  aggressor/victim body positions and existing Distance value; a separate
  DistanceKnown flag distinguishes a successful finite measurement from missing
  positions. Unknown/invalid distance displays `? m`, a real zero displays `0 m`.
  This is separation at death, not shot-origin distance or projectile path length.
  No scoring, bot or profile changes; wrapping/layout code is unchanged from
  the r5 build, whose actual wrapping cases remain untested.
  Source `a53a350` passed independent capture/formatter review (no P0-P2).
  Formatter checks: 52 assertions, three rejected source faults; the harness
  does not execute the Harmony hook or native renderer. Actual clean client
  build: zero errors, four existing CS8632 warnings, all 29 icons verified.
  Client SHA256 `41F26B5DA68D781BA533BB169B035BB78E8BFB881C920D909C4D7C9FD9FA1EFB`;
  manifest `artifacts/double-jump-build/56955a470f3c422e8ebf812ca12e4fe7/manifest.json`.
  Delivered client-only, prior r5 client backed up; all 40 protected server,
  dependency, config and asset files unchanged. Receipt:
  `artifacts/killfeed-distance-delivery/2041fdb4530a49d98d1ab575beebc454/receipt.json`.
  Game restart required, server unchanged; GPU mirror/new distance rendering not
  verified here. `GPU-PANELS-TEST-INSTRUCTIONS.md` now binds this source/hash for
  r6: one short Arena raid, primarily numeric distance on other-kill entries.
  Earlier reports stay unchanged; r5 missing local/wrap/transition cases remain
  open unless separately observed. New report: `GPU-PANELS-R6-RESULTS-2026-09-11.md`.

- **r4 accepted core UI; killfeed follow-up delivered (2026-09-11):** report
  `6ac779e` confirms observed menu hiding/panel return, ammo refresh, respawn
  values, two Arena starts without native list/header, normal PMC restoration
  with single-O, and quickslot/assembly spacing at 1920x1080. Independently
  inspected screenshots and final raw log: two each of panel/header/list markers,
  no Arena HUD/panel/extraction failure or duplicate-CanvasGroup spam. Keep r4's
  limits: grenade comparison interrupted by death, incomplete transition/time
  coverage, no reliable double-O or actual extract, no wider PFT/resolution proof.
  Reported partial-name killfeed remains rejected; the wrapped identity fix is
  now delivered, but is not yet GPU-tested. World-space green exit waymarkers
  temporarily overlapping the status panel are a separate unresolved UI issue.
  QuestTracker/startup RenderTexture, SAIN teardown and BotWeaponSelector errors
  do not establish UI causation; no speculative fix to them is included.
  The parallel audio task finished as `3ea1977` during delivery preparation.
  Built that clean combined commit to preserve its neutral config description;
  reviewed killfeed/HUD/Combat sources are unchanged from `4e49537`. Build: zero
  errors, four existing CS8632 warnings, all 29 embedded icons verified; formatter
  recheck: 31 assertions and one rejected missing-attacker source fault.
  Delivered client SHA256:
  `A9D9A0E5D60E2BC56D9E419F5284745857D910C16B8E51496B5CAFBE995028F9`.
  Manifest `artifacts/double-jump-build/8cb73ba9b89f46dba256fee636d43cfa/manifest.json`;
  receipt `artifacts/killfeed-identity-delivery/8cc7fedc98514757bb6478070cf7a3bb/receipt.json`.
  Previous r4 client backed up; all 40 protected server/dependency/config/asset
  files unchanged, including the separately delivered new hitmarker assets.
  Client-only delivery; game restart required, server unchanged. New GPU mirror
  and rendering unverified. `GPU-PANELS-TEST-INSTRUCTIONS.md` now binds source
  `3ea1977` and this hash for r5: one focused Arena raid, a separate r5 report.
  All four prior GPU reports remain unchanged. Older `0DA0F70F...`/`BC93977A...`
  staged clients are superseded; do not deploy them over this combined delivery.

- **User-selected hitmarker replacement (2026-09-11):** all three cues now use
  Freesound 570335 by User391915396 (CC0), supplied and auditioned by the user.
  Identical 187.506 ms mono PCM16/44.1 kHz WAVs; source gain preserved, no trimming
  or pitch change. WAV SHA256 for hit/headshot/kill:
  `DF59AAA66C8727F33EA6E5C6C3C80A5E0BD43418C4ABB3C24A931D9282F0FE54`.
  Source/license/conversion hashes and full CC0 text are packaged alongside.
  Offline asset checks and production parser/service tests passed (54 assertions);
  existing announcer checks passed (142 assertions plus rejected fault cases).
  Independent review found no remaining correctness issues. Staged client build
  passed with zero errors and the four existing CS8632 warnings, manifest:
  `artifacts/double-jump-build/66e0bf0ee4214d4883d80117e418fe29/manifest.json`.
  **Delivered assets only**, receipt/backups `artifacts/hitmarker-replacement/570335/`.
  Six installed asset/document hashes matched; 25 other files, including client,
  server and Redeploy.Core DLLs, remained unchanged. The r4 client remains
  `857206A4BF295607130B6F54A2A9CDECDB3F5A92B53784F9C644F7FC5199F1BB`.
  Old Kenney notices are absent from source but harmless copies remain installed;
  current AUDIO-LICENSES.md and SOURCES.json identify the replacement correctly.
  Settings description is neutral and included in the combined DLL delivery
  above; no playback or settings behavior changed. WAVs reload at the next
  Arena match; no game/server restart needed for this asset-only replacement.
  GPU mirror and in-game audibility remain unverified here.

- **Killfeed identity/overflow follow-up, initially staged (2026-09-11):** user reports
  clipping during the ongoing r4 job: other kills show attacker plus ellipsis;
  local kills omit the local name. The latter was the old format itself, while
  the recent fixed-height Ellipsis setting can truncate horizontally or vertically.
  New format always puts full attacker and victim names above roles/weapon/HS
  metadata; local-kill distance retained. No capture/scoring change. Killfeed rows
  wrap and use installed TMP preferred-height measurement at their fixed width;
  backgrounds and following rows resize accordingly, including shrink on reuse.
  No ellipsis, no smaller headline font, same six-row pool and 4.5s hold/1.5s fade.
  `tools/KillfeedTextChecks`: 31 formatter assertions and one rejected compiled
  missing-attacker fault. Independent source/native TMP review: no P0-P2. Native
  rendering still needs a GPU test: long names, six varying-height entries,
  tall-to-short reuse and pushes while the canvas is hidden by a menu. The stack
  grows with wrapped content; check clearance from other panels. Clean source
  `4e49537` built with zero errors and the four existing CS8632 warnings; all 29
  icons verified. Staged manifest:
  `artifacts/double-jump-build/71399aeeda8c44f9abac6875757236f3/manifest.json`.
  Staged client SHA256:
  `0DA0F70F165F6E3EB1ED000CAF7AB3DDB1A8F00ADA180AD6CECCC6BDEF9829D4`.
  Client/server/Redeploy.Core installed hashes verified unchanged after build.
  Delivery was held while r4 ran, preserving its installed client and instruction.
  r4 is now complete; the combined delivery and r5 instruction above supersede
  this staged artifact. Previous reports remain unchanged.

- **Killfeed readability and quickslot clearance (2026-09-11):** each pooled
  killfeed row now has a dark 88%-opacity background, padded 18px bold text and
  brighter death/other-kill colors. Entries stay fully visible for 4.5 seconds,
  then fade over 1.5 seconds; the six-row pool and six-second lifetime remain.
  Alpha is reset before reuse, and TMP is still constructed while active. Text
  stays within the existing 460px feed width (ellipsis for unusually long lines)
  to contain long lines at the 1920x1080 reference layout. No input targets added. ScorePanel
  moves from 18 to 110px below the top at the 1920x1080 reference resolution;
  the assembly notice follows below it at 190px. The r2 gameplay screenshot has
  quickslots ending around 96px, but other resolutions/modded positions still
  need visual confirmation. Native-menu hiding and extraction suppression from
  `d35d878` remain unchanged. Next GPU check: bright/dark scenery, full and reused
  killfeed, long names, quickslots plus assembly notice, and inventory return.
  Independent review of source `e37c3b6`: no P0-P2. Actual client build passed
  with zero errors and the four existing CS8632 warnings; all 29 embedded icons
  verified. Delivered client SHA256
  `857206A4BF295607130B6F54A2A9CDECDB3F5A92B53784F9C644F7FC5199F1BB`;
  previous client backed up and all 39 protected server/dependency/config/asset
  files unchanged. Receipt:
  `artifacts/hud-readability-delivery/69ce41e9c20d427fb0dfa6245e44a1c3/receipt.json`.
  Game restart required, server unchanged. GPU rendering and mirror not verified.
  Other aspect ratios may narrow the gap to the centered score; check those
  separately. No new synthetic tests for these cosmetic changes. Report `2d03da3`
  records the r3 preflight blocked by the old instruction hash, without a game
  start; that report remains unchanged. User requested rebinding the instruction
  to this delivery: `GPU-PANELS-TEST-INSTRUCTIONS.md` now specifies job
  `arena-panels-native-ui-20260911-r4`, source `e37c3b6` and the SHA256 above.
  It retains the pending panel/header checks and adds killfeed/quickslot coverage,
  with a new `GPU-PANELS-R4-RESULTS-2026-09-11.md` report. No new build or DLL swap.

- **r2 GPU acceptance and remaining UI polish (2026-09-11):** report `2a57e7d`
  confirms visible panels, direct-rig grenade counts/two successive G throws,
  armored-rig magazine reserves, F12 adjustments and fresh post-respawn data.
  Named exit lists stay hidden in two Arena starts and return in normal PMC.
  Independently read synchronized final r2 log: 2 panel markers, 2 list markers,
  0 CanvasGroup spam and 0 panel/extraction exceptions. Overall report remains
  bounded: 5 passed, 1 failed, 6 blocked, 2 not run. Screenshots confirm status
  and top-score overlap in inventory and the separate residual green header.
  New source hides Arena canvas drawing on non-BattleUI screens, without
  deactivating its GameObject or stopping ticks. Panels also hide on the early
  native inventory flag and refresh immediately on return. Existing positions
  remain unchanged. Header target independently resolved from installed level46:
  `_mainDescription` contains Image/Icon/State, numeric Clock is a sibling;
  runtime hierarchy guards preserve that separation. Only header activeSelf is
  borrowed/restored, existing successful list masking retained. The original
  two GPU reports stay unchanged. Separate BotWeaponSelector/SAIN/async-upload
  errors are recorded in r2; the available stacks do not establish UI causation,
  and no speculative gameplay fix is part of this UI change. Source `d35d878`
  passed independent native/context reviews without P0-P2 findings. Final checks:
  269 panel assertions / 18 rejected source faults; 44 extraction assertions /
  5 rejected source faults. Actual client build: zero errors, four existing
  CS8632 warnings, all 29 icons verified. Client-only delivery from that clean
  commit, SHA256
  `218F176B15D007DB451893368EC754DDA5669F45C8E96B4F0EE1E67E9BA58C7C`.
  Previous `ED74F678...` client backed up; all 39 protected server/dependency/
  config/asset files unchanged. Receipt:
  `artifacts/panel-r2-followup-delivery/cf79028df6284c57b4e199856d17a666/receipt.json`.
  `GPU-PANELS-TEST-INSTRUCTIONS.md` now pins this build for the focused
  `arena-panels-native-ui-20260911-r3` job and a separate r3 report. Neither
  GPU mirror nor native visual/input behavior of this follow-up is verified here.
  Game restart required; no server update/restart. The new screen-visibility
  harness covers the helper/context, not actual ArenaHud.Tick execution or native
  rendering; continued event ticks were source-reviewed separately.

- **GPU panel/native UI failures corrected (2026-09-11):** test report
  `734af45` and the user's follow-up log reject the original UI delivery.
  Independent native diagnosis found two separate regressions: inactive-parent
  TMP construction in ArenaInfoPanels, and rejected duplicate CanvasGroup
  additions on PointsPanel (1,826 lines in the captured user log). Build the
  panels synchronously while active, hide before returning; borrow and restore
  the existing list group's alpha/enabled values instead of adding a component.
  Missing/failing native groups now fail open and are quarantined per list/raid;
  once-only full exceptions and construction/masking markers aid diagnosis.
  Both regression harnesses now model the previously missed Unity contracts.
  Panel tests still substitute TMP views and do not certify rendering.
  Current source includes the completed `3f34f86` grenade-picker fix unchanged.
  Independent reviews found no P0-P2. Source commit `998cef5` on
  `fix/arena-panel-native-ui`; final checks 35 extraction assertions / 3 rejected
  source faults, 248 panel assertions / 15 rejected source faults. Actual client
  build: zero errors, four existing CS8632 warnings, all 29 icons verified.
  Delivered from that clean commit, client SHA256
  `ED74F678CB303D775035FA6A4192387C167214C32B3A6FE6DD4CEE251D5CBEE1`.
  Old `D927D5B9…` client backed up; all 39 protected server/dependency/config/asset
  files unchanged. Receipt:
  `artifacts/panel-native-ui-fix-delivery/07f8e2b9a54f4fe0a308780659959878/receipt.json`.
  Revised `GPU-PANELS-TEST-INSTRUCTIONS.md` pins that build as job
  `arena-panels-native-ui-20260911-r2` and requests a separate retest report.
  Previous GPU failure report unchanged. No new GPU execution or mirror
  verification here; client only, game restart required, server unchanged.

- **Grenade pick after widened reach (2026-09-11):** fixes the pre-existing
  defect handed over by the grenade-row review. A grenade bound and drawn from
  outside the tracked rig/pocket top level (the backpack without the installed
  UseItemsAnywhere mod; nested pouches with it) stayed
  `Equipment.TopPriorityGrenade` after it was thrown, so every tap of G fell back
  to the weapon until hold-G. `ArenaReachPatches.cs` adds a postfix on
  `FastAccessGrenadeItemView.OnItemRemoved` (the pick, or a container holding it,
  left and is no longer reachable: run the view's own `SetNewTopPriorityGrenade`)
  and a void prefix on `PlayerInputTranslator.vmethod_1` (clear an undrawable pick
  so the native no-pick branch runs). Both gate on `ArenaSession.IsActive` alone
  (argued at the code), are enabled through a contained helper, and log once per
  raid. Ordinary raids untouched. New `tools/ArenaGrenadePickChecks`: 123
  assertions, 14 rejected source faults, installed contracts plus 8 rejected
  contract faults. Three-lens adversarial review: no P0/P1; both P2s and the
  P3s fixed or documented. Implementation commit `3f34f86`; staged from it,
  clean (zero errors, the four existing CS8632 warnings), client SHA256
  `FF07FE124952C9D81A958284C8DEA8E83AB303B4243A0D6BCCA336E88A171EA0`, manifest
  `artifacts/double-jump-build/2307d55300984d3886b2406aecd9c2d3/manifest.json`
  (it also contains `18267c8`'s native UI work). **Initially staged only:** the
  installed `D927D5B9…` remained pinned during the first GPU panel job. That job
  has finished; the picker fix is now delivered in the combined `998cef5` client
  documented above. Do not deploy the older staged `FF07FE12…` build over it.
  Chain, residuals and exact GPU steps: GRENADE-PICK-REVIEW.md. Client-only;
  game restart.

- **Native Arena UI polish (initial delivery, 2026-09-11):** **GPU rejected this
  implementation; superseded by the correction above.** It built on grenade-row
  commits `2b86e3a` / `f178906`, which remain intact. The character-selection
  description now uses two short, non-wrapping lines for solo and PFT choices;
  PMC/Scav still replace the text natively. ArenaExtractionList blocks only
  exit-list requests in the exact active game/world/UI and adds its own
  transparent list-only CanvasGroup after synchronous Arena confirmation.
  This catches the startup animation which native SetTime starts before
  OnGameStarted. It does not depend on HudEnabled, touch Armed state, cancel
  native coroutines or suppress the separate extraction countdown/single-O
  clock. Mask removed at end, changed world/UI and shutdown; disabled or failed
  masking leaves native requests available. Independent review findings on
  failed unpatch cleanup and fail-open hooks were fixed and covered by the
  production-linked NativeArenaHudChecks (28 assertions and one rejected
  compiled fault). Existing panel/grenade checks: 247 assertions and 14
  rejected faults; actual client build and all 29 icon resources verified.
  Delivered client-only from clean commit `18267c8`, built in the separate
  `ArenaMode-NativeHud` worktree to exclude the concurrent uncommitted
  ArenaReachPatches/grenade-picker work in the main checkout. Installed SHA256:
  `D927D5B98D654BE63DDB25BD41727408FC86C1B488165C7666D283898F512ABE`.
  Previous client `ACE467B4…` backed up; all 39 protected server/dependency,
  configuration and asset files unchanged. Receipt:
  `artifacts/native-hud-delivery/df831be51ae4485893f69851ef384d2a/receipt.json`.
  **GPU job:** [GPU-PANELS-TEST-INSTRUCTIONS.md](GPU-PANELS-TEST-INSTRUCTIONS.md)
  specifies this exact build, a separate test profile, P01–P14 with screenshot/
  log evidence, and ordinary-raid restoration. Job prepared, not dispatched;
  GPU mirror and native visual/input acceptance remain unverified. Game restart
  required, no server update. Recheck installed hash if parallel delivery occurs.

- **Grenade row in the weapon panel (2026-09-11):** frag/flash/smoke counts
  split into "ready" (the native tap-G candidate list, which the installed
  UseItemsAnywhere widens to the backpack top level) and dimmed "+N" stored
  (nested pouches etc.). Stays visible with a grenade in hand; hidden without
  grenades. Three new icons with their own hash record `ARENA-ICONS.json`;
  `SOURCE-IMPORT.json` untouched, never rerun the imported generators. Offline
  suite 247 assertions / 14 rejected faults; staged build clean; adversarial
  review found no P0-P2, its P3s fixed. A pre-existing stale-TopPriorityGrenade
  defect in the reach patches (tap-G blocked after throwing a backpack grenade
  via a number key) was handed to a separate task (since fixed, see the
  grenade-pick entry above). GPU acceptance pending, see
  the grenade section of RAID-INFO-PANELS-REVIEW.md. Client-only; game restart.
  Delivered from clean commit `2b86e3a`: installed client SHA256
  `ACE467B448B942420A6A4AF00E95B0055FAE050D7E17DBB5FCBA86E5793CD6A5`
  (previous `6C660F3B…` backed up), server and 38 other protected files
  unchanged. Receipt:
  `artifacts/raid-panel-delivery/c97b2993a80b4e86a87d664bb94d4a9a/receipt.json`.
- **RaidInfoPanels absorbed (2026-09-11):** the main checkout now uses
  `feature/arena-raid-info-panels`, based on `0f9f1e9` and therefore includes
  the hitmarker implementation and completed parallel Solo Tactical changes.
  Both weapon/reserve and health/armor/status panels are now match-owned
  modules on Arena's canvas, using its font and outline style. Their complete
  source, 26 unchanged generated icons and four generators were imported from
  the user's RaidInfoPanels source `55ee600`. No standalone plugin/reference
  or additional gameplay patch. F12 settings live under `HUD Panels - ...`;
  all 17 current values migrate as defaults from the old CFG, with existing
  Arena settings taking precedence. **Keep the legacy CFG in place until at
  least the first new game start.** Exact local-body gates hide these panels
  in ordinary raids, on death/respawn and during spectating; match teardown
  releases their caches. Weight warnings use the body's actual Arena limits.
  Armored rigs already count through `TacticalVest`; the old "Armor-Vest-Grids"
  note referred only to the separate `ArmorVest` slot. Clarified and covered
  by the production-counter regression. Parent verification: 180 assertions,
  seven controlled source faults; real client build/resource checks pass.
  See RAID-INFO-PANELS-REVIEW.md and tools/RaidInfoPanelChecks.
  Verified complete source/Git/installed-artifact backup:
  `D:\SPT41\Development\_archive\RaidInfoPanels-2026-09-11-f8c969a4dffa4335987a02ade57e66fe`.
  Only the user-named local project and installed standalone DLL are retired;
  GitHub remote stays unchanged. **Delivered and retired:** clean source
  `1e6d350`, client SHA256
  `6C660F3B3F8B39ED6B54F3E9960605D4A437FA63CE277A29DC0D7DC7FB190B72`.
  Build: `artifacts/double-jump-build/b7ea9d9beaaa47ee9c22873495191521/manifest.json`.
  Delivery/previous-client backup:
  `artifacts/raid-panel-delivery/9b26d7afe2414856bc9d2e5c62b24440/receipt.json`.
  The local RaidInfoPanels checkout and old installed DLL are gone; retained
  legacy/Arena CFGs, server/dependencies and existing audio/drafts are unchanged.
  **Game restart only.** GPU layout/migration/playtest and
  mirror completion are not claimed; test both panels over kit changes,
  death/respawn, a second raid and an ordinary raid.

- **Example hitmarker audio (2026-09-11):** implemented in isolated worktree
  `D:\SPT41\Development\ArenaMode-HitmarkerAudio`, branch
  `feature/hitmarker-audio`, now based on `b7678ef` so the completed parallel
  Solo Tactical fix below is preserved. This feature is also included in the
  current main checkout through the RaidInfoPanels branch described above.
  Three adapted Kenney CC0 hit/headshot/kill WAVs, unchanged original pack
  notices and source/hash/conversion manifest are bundled. F12 Audio:
  `HitmarkerSounds=true`, `HitmarkerVolume=0.5`, independent of Interface,
  announcer and visual HUD. Exact local hostile direct damage only, 40 ms
  coalescing, kill priority, no queued stale audio or paused tails. Shared Master
  adapter has separate announcer/impact instances; existing announcer tests
  and fault controls pass. See HITMARKER-AUDIO-REVIEW.md and
  tools/HitmarkerAudioChecks for tests and pending user listening/GPU checks.
  **Delivered:** clean source commit `d9e7598`, client SHA256
  `0E7AD442F924314B47565AA5755C6766C74FD900ABAAC8AC0FD50ACFF08EDAD6`.
  Build manifest: `artifacts/double-jump-build/f63c503ccbe546b295e6e4c412c858e5/manifest.json`.
  Receipt/backups: `artifacts/hitmarker-delivery/992cd14563c8416e9404be81c958707d/`.
  Seven audio/license/source-record files plus client verified at destination;
  ten protected dependencies and all pre-existing audio/draft files unchanged.
  Offline: 54 hitmarker assertions, 142 existing announcer assertions and its
  17 controlled source faults; three WAVs and original notice hashes also
  verified from committed Git blobs. Client build: 0 errors, 4 existing CS8632
  warnings. Independent reviews complete. **Restart game only**; GPU listening
  and mirror completion unverified. Do not rebuild an older checkout lacking
  these changes over the current integrated client.

- **Solo Tactical: the buy phase waits for a body swap in flight (2026-09-11, 09:58):**
  this commit on `feature/arena-mantle-reload`, on top of the entry below.
  The round-1 buy phase (5 s minimum, or Ready) could end before the
  raid-start kit swap had landed; StartRound then charged the purchase and
  started a second swap on the same body, the base-kit swap landed first and
  the purchase stood down silently — paid, not received, one round late.
  `Tick` now asks the pure `SoloTacticalRoundStartPolicy` once the buy clock
  has run out: it holds while `LiveKitRespawn.IsInitialSwapPending` or (for a
  death in the buy phase, which Tactical never counts as down) while
  `ArenaRespawnDriver.IsRespawnInProgress` with `_playerDown` false, then
  starts; after 60 s it gives up with a warning and never waits again that
  match, so a swap that never exits cannot deadlock the mode. Toast "Round 1
  — waiting for your kit", HUD "BUY  waiting for your kit", buy panel
  "starting when your kit has landed". Five xunit tests on the policy (four
  go red against a policy that never holds, checked by mutation), a
  source-text gate assertion plus a fourth and fifth mutant in
  `tools/SoloTacticalSpawnChecks` (53 assertions, five mutants rejected),
  PitFireTeam / TeamArenaExpansion / RoundLifecycle / Tracer checkers green,
  two independent reviews without a new fault. Installed client SHA256:
  `A79D4F6F7B4E7FA747BF55EA71E8F521EB08498F74F1126CC00D75166B6A8638`.
  **Restart game only.** Playtest: solo Tactical, BuySeconds 5 and
  KitPreloadInLoadingScreen Off (cold kit), press Ready right after the raid
  starts: expect the waiting toast/HUD, then the base kit landing, then the
  round; the log shows "waiting for the body swap in flight" and "settled
  after N s". Then the same with a kit bought before Ready: it must land at
  round start and be charged once. Known and left alone: a non-buying
  round-1 survivor gets the in-place base-kit swap and, a tick later, the
  usual round-break re-issue at a spawn point with protection — the same
  sequence every warm-kit round 1 already had.

- **Solo Tactical: the raid-start kit now counts as worn (2026-09-11, 07:28):**
  this commit on `feature/arena-mantle-reload`. Pre-existing defect found by
  the adversarial review of `af3647d`, not caused by it: the solo kit provider
  fell through to `GetBaseKit` for the raid-start swap, but nothing recorded
  that kit in `_worn`, so a round-one survivor who bought nothing was handed a
  NEW random draw at every round break (RandomKit on; `PickRandomKit` never
  repeats the last draw, so a different kit was certain) under the "you keep
  your gear" toast. A configured kit hid it, which is why the first rounds
  played never showed it. The provider now records the base-kit draw as worn
  at hand-out time; the dead `_bought = Affordable()` store at match start is
  gone (EnterBuy cleared it before anything read it), so Tactical still opens
  on the base kit as `KitPreloadPlan.OpeningKit` and its test require.
  Clean Release build, tests 531 green / 2 skipped, two independent reviews
  without a new fault. Installed client SHA256:
  `DD27FDC4F58C4A8788A03811B93321CC8F8C8489C97E2D4E843E8CC03D66BA26`.
  **Restart game only.** Playtest: solo Tactical, RandomKit on, survive round
  1 without buying; round 2 must re-issue the same kit, and the client log
  must show one `Arena kit drawn:` line per match, not one per round.
  The round-1 buy-phase race both reviews named is fixed by the entry above.

- **Airborne mantle user acceptance (2026-09-10, 20:37 retest):** user confirms
  the tested lower climbs now work; higher places no longer trigger. Five logged
  native entries at 0.676-1.302m remaining height all have nativeError=false,
  restored 1.6m capsules and positive post-exit height. Four samples are grounded;
  the first is already jumping again. Late higher requests at 1.617-1.703m are
  rejected by the 1.31m cap. No Error-level lines in the current snapshot.
  Preserve the working `9a550a5` implementation and 0.5m actual approach gate;
  greater remaining height would be a separate physical/animation extension,
  not an unverified limit increase. See AIR-MOVEMENT-REVIEW.md for frozen log
  path/hash and scope of acceptance. This does not certify every ledge or the
  still-unrun normal-raid/respawn regressions. Documentation only: no new build,
  deployment or gameplay changes. Installed client hash remains the one below.

- **Actual mantle approach correction (2026-09-10, 20:13 retest):** user reports
  unchanged drops despite `b0a8664`. Nine native entries have valid remaining
  heights. Seven apply their requested motion in full; several start 0.83-1.47m
  from the selected surface while the clip travels only ~0.55m forward. The
  native velocity-shifted grid accounts for the difference. Near 0.4m attempts
  3/7 remain above entry; attempt 4 is also above after further movement, so
  distance is a supported mechanism, not a guaranteed outcome classifier.
  Snapshot has no Error-level lines. Frozen log/table/hash and limitations are
  in AIR-MOVEMENT-REVIEW.md.

  Implementation `9a550a5` on `feature/arena-mantle-reload` additionally requires
  actual planar body-to-selected-surface distance <=0.5m before extra airborne
  Climb. Held input retries, including final dispatch refresh; height remains
  separate. Native Vault preference, standing reach, physics/global grid and
  reload handling stay untouched. The selected sample need not be the nearest
  edge: fast approaches may wait for wall contact and a slower refreshed scan.
  No forced positioning or physical success claim.

  Full **374 assertions / 51 source faults**, independent review without P0-P2.
  Evidence: `artifacts/air-vault-checks/d0e7fd88bda647e78e6cf04ae4148f81/`.
  Clean build from `9a550a5`: zero errors/four existing nullable warnings;
  manifest `artifacts/double-jump-build/048d51fd92ca47f786239a08e5c33693/manifest.json`.
  Verified local client SHA256:
  `4018D0A24643C187B6DA713C2C20C9CCE452AAE4C62650FA72E56B42B957F7E8`.
  Receipt/verified old-client backup:
  `artifacts/mantle-distance/delivery-9a550a5-7299471308264575bc06d8ae5c60b59c/`.
  Same ten protected files as below stayed unchanged. **Restart game only** after
  existing mirroring; GPU mirror/result unverified, no push/merge or GPU launch.
  Test the same ledges with held vault during jump, slow/fast approach and a
  normal standing climb; inspect motion plus physical-approach rejection logs.

- **Mantle physical follow-up / reload accepted (2026-09-10, 19:40 retest):**
  implementation commit `b0a8664` on `feature/arena-mantle-reload`. User confirms
  reload speed is now correct and understands the UIFixes in-place swap pause;
  reload code stays unchanged. The log proves eight Unity Climb entries, but the
  user usually falls back down. Native clips top out at 1.30m vertical travel;
  Arena had admitted up to 1.70m. Additional airborne Climb now waits for at most
  1.31m remaining height, so held input can retry as the jump rises. Standing
  reach and Vault preference remain unchanged. This corrects a proven unsupported
  range, not a proven explanation for all eight drops. See AIR-MOVEMENT-REVIEW.md
  for native asset/grid-coordinate evidence and the frozen log hash.

  New read-only `Arena mantle motion #...` diagnostics capture accepted geometry,
  requested versus actual movement, capsule/rise and exit/0.75s post-exit state.
  Twelve attempts per world, eight-second observation timeout; no position,
  collider, gravity or native-state correction. Future logs can distinguish a
  blocked displacement from an over-distant ledge or post-animation drop. The
  separate optic-icon and SAIN unload NREs do not establish a mantle connection.

  Full checks passed **363 assertions / 47 source-fault controls**; independent
  source/production review found no P0-P2. Evidence:
  `artifacts/air-vault-checks/a17fde8e2deb4c0580e27b3ea4918584/`.
  Final clean staged build from `b0a8664`: zero errors/four existing nullable
  warnings, manifest `artifacts/double-jump-build/f203e355b8774806b8142ffa687514e8/manifest.json`.
  Verified client-only local delivery SHA256:
  `51DFE284308A34DD65877CB0FA40AC0E812455CA5086597DDE7DC9AAECC84827`.
  Receipt and verified previous-client backup:
  `artifacts/mantle-physical/delivery-b0a8664-7e2c5618056e45118e8d5b6c0e201b9a/`.
  Ten protected files unchanged: Arena server/BigBrain, three Redeploy DLLs,
  APBS DLL/config, both UIFixes DLLs/config. **Restart the game only** after
  existing mirroring. GPU mirror completion and actual physical mantle success
  remain unverified here; no game launch, push or merge. User test: the same ledge
  with held vault during a jump, plus lower/higher ledges; if it drops, inspect
  entry/exit/after-exit motion records. Ordinary-raid/respawn isolation remains
  a live regression check, not something offline collision doubles can prove.

- **19:09 retest fixes: in-place reload and stale falling flag (2026-09-10):**
  still on `feature/arena-mantle-reload`, with separate implementation commits
  `d968bda` (mantle) and `d71fa13` (native magazine swap handling).
  The new log contains 78 reload diagnostic lines for 13 regular reloads with
  verified Unity multipliers, below the 96-line budget. User confirmed normal R.
  Installed UIFixes routes R through native PullOutMag/InsertMag operations if
  there is no free magazine slot in the rig. Those two exact operation types were
  excluded from both speed and diagnostics; they are now included. Native swap
  inventory/callback behavior remains, with owned speed restored before all five
  completion/reset boundaries. No UIFixes dependency or setting change. This also
  accelerates the same explicit inventory magazine actions inside Arena. Direct
  chamber/cartridge work, unknown types, malfunctions, AI and normal raids remain
  excluded. Additional native swap transitions can still make it slower overall.

  All 47 detailed mantle dispatch refusals showed a valid Jump_Move/Sprint owner,
  no transition, IsJumping=true and Landing=false, but FallingDown=true. The native
  MovePlayerState can leave this bool set during a genuine jump; only FallDown.Exit
  clears it. Removed only that unreliable bool guard; exact managed Jump/animator
  ownership, landing, geometry, stamina and hands guards remain. Real FallDown
  stays excluded and the bool is not modified. Alternating queue/refusal reasons
  now have separate log throttles instead of consuming all 100 lines immediately.
  See both movement/reload review documents for native sources and evidence.

  Frozen client log:
  `artifacts/reload-mantle-retest/58b6d72db3324285838ec897c2104dfe/client.log`,
  SHA256 `17D6D61FC0BE42CB95C1ACF509ABCBE84C41AAD41B82323A76E0C56DB1E1355C`.
  The remaining SAIN OnDisable NRE was during world unloading at 19:09:32.723;
  a link to these features is not established. Server snapshot restore/persistence
  had already completed at 19:09:32.503/.541 local, resolving the earlier deferred
  client warning. No additional error-driven match stop was observed.

  Full offline checks: mantle **339 assertions / 41 source faults**, reload
  **779 / 11**, handling **161 / 12**, plus installed native IL contracts. Both
  independent source reviews found no P0-P2. Clean final build from `d71fa13`
  passed with zero errors and four existing shared nullable warnings. Verified
  client-only local delivery SHA256:
  `A15D2BDE379153D5C6C35A92D69EAE641AA0F1C733A444AAB998D223F99714ED`.
  Receipt and verified old-client backup:
  `artifacts/reload-inplace-mantle-falling/delivery-d71fa13-6430d21d42f146e5b6761efd546076cd/`.
  The old client was `97A8F509EB2D574406B669CBA8C753AD0131ACC5ADA5397798D1A05ECAF53B71`.
  Ten protected files stayed byte-identical: Arena server/BigBrain, three Redeploy
  DLLs, APBS DLL/config, both UIFixes DLLs and its config. **Restart the game only**
  after existing mirroring. GPU mirror completion and live acceptance are not
  verified here; no GPU launch, push or merge. Pending user test: R reload with
  a full rig (e.g. an affected kit), factor changes, empty/tactical reload,
  interruption/death and native inventory integrity; mantle at valid airborne
  ledges, real falling/landing refusal, respawn and ordinary-raid isolation.

- **Mantle retest correction and weapon reload diagnostics (2026-09-10, 18:08):**
  on `feature/arena-mantle-reload`. Implementation commits `0244048` and `5f35bc8`
  retain the concurrently added preload correction `ea1a979`. Unity mantle now
  recognizes the actual Move/Sprint jump leaves through each leaf's own initialized
  native movement container. Landing/transition/geometry/hands checks remain;
  bounded diagnostics identify exact rejection stage and native state flags.
  The failed 17:18 archive confirms hooks loaded, 16 misleading unsupported-body
  refusals, five native refusals and two queued requests without dispatch proof.
  Unlogged source hashes mean individual failed attempts remain unclassified.
  See `AIR-MOVEMENT-REVIEW.md` for native asset evidence and the required GPU test.

  The user subsequently confirmed reload acceleration works. Their fast/slow
  weapon list compared different kits at one fixed setting, not matching reloads
  at 1 versus 3. Weight does not appear in the inspected native reload-speed path;
  weapon/magazine/mastery/skill and reload variant preserve native differences.
  Both slow/fast groups bind SpeedReload. No speculative timing replacement was
  made. New bounded `Arena reload diagnostic` lines record weapon template,
  factor, original/written/readback values, real Unity hands speed multiplier,
  refusals and completion. F12 is live; web config is not polled in active Arena.
  Compare the SAME weapon/magazine/reload type with F12 1 and 3 before comparing
  different kit weights. See `RELOAD-HANDLING-REVIEW.md`.

  Offline checks passed: mantle **320 assertions / 39 source faults**, reload
  **724 / 8**, handling **161 / 12**, including installed native IL contracts.
  Independent reviews found no remaining P0-P2. These are host contract checks,
  not Unity physics/clip playback or live transaction acceptance. Native GPU
  mantle and matched weapon timing tests remain pending with the user.

  Clean build from `5f35bc8`: zero errors, four existing shared nullable warnings.
  Client-only local delivery, verified SHA256
  `97A8F509EB2D574406B669CBA8C753AD0131ACC5ADA5397798D1A05ECAF53B71`.
  Verified previous-client backup and full protected-file receipt:
  `artifacts/mantle-reload-retest/delivery-5f35bc8-533972a6d72842a0a35481f2c3675404/`.
  The pre-delivery client was `48B5DB990F22F092FCB12F315DDC80B42AF50EF5BEE8667601817A03F9271D8D`,
  not the older delivery below. Arena server, BigBrain companion, all three
  Redeploy DLLs and APBS DLL/root config remained unchanged during this delivery.
  **Restart the game only.** Mirroring configuration was left alone; completion
  on the GPU machine is unverified. No game launch, push or merge performed.

- **Unity airborne mantle and faster reload delivery (2026-09-10):** on
  `feature/arena-mantle-reload`, based on `5b058ac`. Separate implementation
  commits: `1339f00` Unity airborne mantle, `3e26b57` firearm reload speed.
  Native Unity body support now issues one guarded CrossFade from real jump
  states into the initialized native Climb/Vault state. It retains native hands,
  geometry, stamina, animation events, root motion and collider cleanup. Dispatch
  commits the move; later key release/settings disable cannot strand native entry
  cleanup. Delayed entry retains its observer, while old world/body/controller
  identities cannot take over a new body. FastAnimator remains supported.
  See `AIR-MOVEMENT-REVIEW.md`; actual GPU mantle acceptance is still pending.
  Existing `AirVaultEnabled=true` applies, no new binding. With jump-on-release,
  press/hold vault again while airborne and approaching a climbable obstacle.

  F12 Movement `WeaponReloadSpeedMultiplier` defaults to **1.5**, range 1-3.
  All six reviewed regular reload families (including quick reload, internal
  magazines and revolvers) use separate native arms/body reload baselines with
  skill/injury refresh preserved. Owned values restore on completion, interruption,
  death, controller change and raid end. Ordinary raids, AI, firing, inventory
  round loading, malfunction clearing and underbarrel launchers remain unchanged.
  UBGL has an additional animator and needs separate synchronization validation.
  See `RELOAD-HANDLING-REVIEW.md` for limits and the GPU test checklist.

  Re-executed offline verification on the final implementation: mantle **227
  assertions / 35 source-fault controls**, reload **707 / 6**, existing handling
  **161 / 12**, air fire **92 / 10**, double jump **144 / 8**. Installed native IL
  contracts passed. Independent adversarial reviews: no remaining P0-P2. These
  are contract/ownership checks against native-extracted methods and host doubles;
  they do not prove actual Unity animation/physics or live inventory transactions.
  Final evidence: `artifacts/air-vault-checks/f9bd6fb12acb44cc951892184df9c563/`,
  `artifacts/arena-reload-checks/09cd793bfce940668910f2eb8e0c32d9/`, plus current
  handling/air-fire/double-jump evidence under their usual ignored artifact roots.

  Built from clean `3e26b57`, zero errors and four existing shared nullable
  annotation warnings. Only the client DLL was installed locally, with verified
  previous-client backup and receipt:
  `artifacts/mantle-reload/delivery-3e26b57-6c1dbf29096e4b05a2f7f159db852579/`.
  Client SHA256 `0CF4F9936CF3805471310104B88D582B1016078E06A7939419F579AAF69567A2`;
  previous client `4099C20E4D284650CA62A582801E2D8325F37B533B40EE41F70FEDE01B8C1F06`.
  Arena server, BigBrain companion, all three Redeploy DLLs, APBS DLL and root
  APBS config stayed byte-identical. **Restart the game only; no server restart.**
  Existing mirroring is left in place; GPU mirror completion is not independently
  verified here. No GPU game launch, push or merge performed. Pending user tests:
  airborne ledge climb/vault, double jump away from obstacles, pre/post-commit
  release, weapon reload/interrupt/death/respawn and a normal PMC raid afterward.

- **APBS user TDM acceptance (2026-09-10, 10:53–11:06 local):** user reports
  good behavior; archived `LogOutput-2026-09-10_11-06-50.log` and associated
  server/APBS records confirm successful 2.4.1 migration and adapter activation.
  Customs 5v5 ended normally after 600 playing seconds, USEC 35:22 BEAR;
  player 17 kills/six deaths. Applied range reaches 61–71; APBS's own generated
  bots reach Tier 7 (levels 65/69/71). One initial BEAR ActiveFail recovered
  automatically; no live-play error. Early raid-end deferred warning resolved:
  snapshot restored 11:06:11.928 and protected end persisted at .960 local.
  A single SAIN `BotComponent.OnDisable` NRE follows during pool unloading at
  11:06:12.097; possible indirect Arena involvement is not established.
  Five airborne-mantle attempts still reject UnityAnimatorWrapper. The current
  FastAnimator-only guard agrees with that log; earlier handoff claims about
  Unity mantle support are not current runtime acceptance. Nine of 146 movement
  endpoint samples are below 2m, mostly SeekCover; no proof of widespread stalls.
  See `APBS-COMPATIBILITY-REVIEW.md`, including frozen evidence hashes/location.
  This test predates `5cae5e7` and does not validate its preload optimization.
  Solo legacy modes, protected PFT transitions and ordinary-raid isolation remain
  pending live checks. Diagnosis changed documentation only; no DLL deployed.

- **APBS adapter delivery (2026-09-10):** built and delivered only the server
  DLL from clean implementation commit `503540e`. Receipt, build log and verified
  previous-server backup:
  `artifacts/apbs-compat/delivery-503540e-31dd0bbe0d1746ba94a2bd48d02eeae3/`.
  Server SHA256 `F573C6123444C2764FDEEA0908DF1C0FD7F51EB2EC57228CF25728163777E01A`;
  previous server `2258ADF8BC342ACDC435588D04ED5870C094CF43BF17EFE0912526544F9F1FA4`.
  Clean rebuild: zero errors, one existing MudTabs `PanelClass` analyzer warning.
  Current client retained unchanged:
  `D57398D64766E34FF76673CBCC7B4A22019CA427824F5D45642FBDC220CF341B`.
  BigBrain companion, both Redeploy DLLs and APBS DLL/config also stayed
  byte-identical; complete before/after hashes are in `delivery.json`.
  All three runnable compatibility checks were re-executed successfully before
  commit; independent final review has no open P0-P2. **Restart SPT server.**
  GPU mirror completion and native raid acceptance remain unverified. No push
  or merge; work remains on `feature/apbs-2.4.1-compat`.

- **APBS 2.4.1 compatibility (2026-09-10):** user installed the complete update.
  Work is on `feature/apbs-2.4.1-compat` from `ff18eab`, preserving the newer
  client load-boost work. Only the Arena server changes. The optional exact
  metadata-version adapter restores Arena's request-owned bot level range after
  APBS's new global raid-level/Weighted-or-Delta selection. Native deferred
  parallel waves and per-bot clones retain exact session-entry/arm ownership;
  normal raids, protected PFT, saved companions and APBS config stay untouched.
  See `APBS-COMPATIBILITY-REVIEW.md` and `tools/ApbsCompatibilityChecks/`.
  Full server suite: **516 passed, one pre-existing installed-Factory geometry
  skip**. The installed APBS test runs real native wave/clone/level processing;
  full inventory/cache bodies are stubbed. Actual USEC/BEAR/Scav and enabled
  prestige paths, all selection switches, deferred rearm and exception cleanup
  passed. Source-fault positive control: 13 passed; missing session validation
  then causes three assertion failures including same-token rearm. Installed IL
  and armband checks passed. Final independent review: no open P0-P2 after two
  exception-path fixes. No GPU raid, mirror verification, push or merge.
  APBS root config was still pre-migration and is left for APBS's next startup.
  Delivery source/hash evidence is recorded above. **Restart SPT
  server after delivery; client DLL is unchanged.** Pending user acceptance:
  short finite TDM, one solo legacy mode, saved-companion PFT round transition,
  ordinary PMC raid after Arena, and APBS startup/config migration log check.

- **Balance delivery (2026-09-09, 19:48 UTC):** bleeding code `dc642ec`, duration
  code `6327247`; both binaries were built from clean `6327247` and delivered
  locally with verified previous-DLL backups under
  `artifacts/duration-balance/delivery-6327247/`. Combined clean-build manifest:
  `artifacts/duration-balance/build/1b67304a8c774341b05c8022fb468e26/manifest.json`.
  Client SHA256 `B40F0B08757B1218EB7E5980556E3B4259C7CE39008A253347BD9F3C0930149E`;
  server SHA256 `3933A49BB3B2E5F5228A28619259886E5E18154DAA286EE76C57260ABA3D1440`.
  Redeploy stayed byte-identical (`7FAFDD65...`, full hash in delivery record).
  Final builds: client zero errors/four existing nullable warnings; server
  zero errors/warnings. All declared host/server/regression checks passed,
  including nine difficulty and six bleeding source faults; independent final
  review has no open P0-P2. **Restart game and SPT server.** GPU mirror completion
  and native playtest remain unverified. No push or branch merge performed.

- **Duration-scaled TDM and latest retest (2026-09-09, evening):**
  `DURATION-DIFFICULTY-REVIEW.md` records the 600s USEC 35:17 BEAR match and
  frozen log hash. Low-displacement endpoint samples declined; one bot still
  revisited the known Customs invalid-nav position. Keep that geometry issue
  and the earlier UnityAnimatorWrapper airborne-mantle issue open. No new
  in-raid error or activation failure; protected end completed in server log.
  Finite independent TDM now linearly scales existing pressure and equipment
  against frozen live-play duration, reaching full curve at 80% (5min -> 4min).
  Assembly/countdown are excluded; streak surge/caps remain. PMC impossible
  starts at 64% absent streak. Existing bots are not retroactively re-equipped.
  Native provider-scoped requests carry the exact arm clock without polling;
  one-profile demands, unused cache-reference exclusion and exact PMC backup
  suppression prevent early equipment stockpiles. Partial hook installation
  leaves generation native. Normal raids/unlimited modes/PFT preserve their
  documented boundaries (unlimited pressure live time, gear server raid time).
  Branch `feature/arena-duration-balance` inherits `ea26880` and its prior
  protected-loadout work. Host checks cover the actual clock/pressure/request
  patches plus nine source faults; actual TDM mode has 325 checks and its
  existing 28 source faults/burst regression. Server: 468 passed, one skipped
  installed-Factory geometry fixture. Staged builds succeeded; independent
  final review has no unresolved P0-P2. Delivery/source hashes are recorded in
  a following entry, not inferred from these pre-commit builds. No GPU raid
  run, push or branch merge is included in this change.

- **Arena bleeding balance (2026-09-09):** `ARENA-BLEEDING-REVIEW.md` documents
  native light/heavy bleed HP loss at default x3, configurable under F12
  Character / BleedingDamageMultiplier (1-10). Exact live Arena bodies only;
  bots remain affected while the local player respawns. Existing protection
  vetoes run first. No effect-duration, treatment, profile or ordinary-raid
  changes. Host checks: 130 assertions and six rejected source faults; native
  call-chain review found no recursive amplification and no open P0-P2.
  User GPU acceptance and delivery are recorded separately below/above; a
  successful host check does not certify Unity raid behavior.

- **Latest retest and bot search follow-up (2026-09-09, 19:19–19:29):**
  see TDM-SEARCH-TEMPO-REVIEW.md for the frozen log hash and exact evidence.
  TDM ended normally, USEC 23:13 BEAR. Repeated Freeze/DontWantTo samples have
  roughly 61–62s delays. The new optional exact-SAIN-4.5.1 adapter caps only the
  two native search predicate arguments to 5s for adopted live TDM opponents;
  native priorities and shared Info/personality values remain unchanged.
  Passive actual-mover detail is bounded to one snapshot per bot/raid after
  two low-displacement samples. No teleport or path rewrite. Normal raids/PFT
  remain outside this adapter. This is a new behavior requiring user GPU testing.
  **Airborne mantle still fails:** this run uses UnityAnimatorWrapper, so the
  previous FastAnimator-only fix rejected every logged attempt before queuing.
  The earlier acceptance below is superseded for mantle; no Unity repair is
  included in this bot change. Source branch: `feature/arena-bot-search-followup`,
  based on `5c9d00b` and retaining the later protected-loadout work.
  Offline checks passed: tempo 104 assertions/7 source faults, awareness 620/33,
  actual TDM mode 318 checks with 28 source faults plus burst-preparation
  regression. Independent final review: no unresolved P0–P2. Client staging:
  zero errors, four existing nullable warnings. **Delivered locally from clean
  source commit `acad49e`**, client SHA256
  `302986ADCD914B4140744FE66F185D12A335E424961867DD30548F05777B9861`.
  Clean staging: `artifacts/double-jump-build/e3286d8e79f240eeb9007dee89b0ac91/`.
  Verified previous-client backup and delivery record:
  `artifacts/bot-movement-improvements/delivery-acad49e/`; prior client SHA256
  `52F98D2116CD212E943C022C861A71CF7A3D483D253892DC0F80E4B1A0CF2220`.
  Server (`851409A1...`) and Redeploy (`7FAFDD65...`) stayed byte-identical; full
  hashes are in the delivery record. **Restart only the game.** New GPU test and
  mirror completion are not verified. No push or branch merge was performed.

- **Movement correction and bot-log diagnosis (2026-09-09, later follow-up):**
  the user explicitly reports airborne mantle still fails, including a second
  Space press held after jumping. This supersedes the earlier broad acceptance
  below: absence of errors never proved an actual Jump-to-Climb transition.
  The installed animator graph has no Jump-to-Climb/Vault edges. A client-only
  correction adds a one-shot local native transition with fresh geometry,
  stamina and hands checks; native callbacks retain movement/capsule ownership.
  It supports the actual behaviorless Jump_Loop via its verified Jump_Idle
  owner, consumes Jump only after confirmed target entry, and retains ordinary
  native transitions and exception cleanup. No fake grounding or global graph
  edits. See AIR-MOVEMENT-REVIEW.md and tools/ArenaAirVaultChecks/README.md.
  **Delivered locally from clean commit `8bb4547`.** The client SHA256 is
  `6093E5BB570C879D614EC84D57A29DAA941C91181CF3E60D35AAC9F817046E0D`.
  Clean staging: `artifacts/double-jump-build/daeb40bbc1034da4bb7964776b4e83a7/`;
  verified prior-client backup and delivery record:
  `artifacts/movement-followup/delivery-8bb4547/`. The previous DLL hash was
  `4826B7805D4182C0BC4D14BB4BB34C896BD44D1F9EE145043F7F28E8EF7ADF4C`.
  Final client build: zero errors, four existing nullable warnings. Mantle
  checks: 120 assertions/25 source faults; hip-fire/shared scope 92/10,
  double jump 144/8 and native TDM menu 80/8 passed. Final independent review
  has no unresolved P0-P2 findings. Server and Redeploy stayed byte-identical
  (`2E394A32...` / `6FCE5366...`); full hashes are in the staging manifest.
  **Restart only the game.** GPU mirror completion and native playtest are not
  newly verified. No push or branch merge. New GPU acceptance remains pending;
  test varied obstacle heights and prolonged/double jumps, failed attempts,
  no extra jump after mantle, native standing vault and normal PMC behavior.
  The latest frozen log is `artifacts/movement-followup/20260909-1540/LogOutput.log`
  (SHA256 `E2BFA57FCA2E850D7CF5C0D7661C3EB4B76E23917CDDB67D6061B56C594D0F05`).
  The diagnostic report there finds two bots repeatedly stranded at
  `(464.91,1.05,-43.64)` for about 182 seconds, a source also present in the prior
  test. Cached SAIN OnNavMesh is misleading at that spot; a sampled hit is
  about 0.94m away. It is 24.4m inside the contour and not a spawn, so do not
  remove a spawn or shift the border on this evidence. Wider low movement
  correlates with search delays up to 78.8s; one active-search stall still needs
  actual SAIN ActivePath/corner diagnostics. No AI behavior mutation is included.

- Historical broad movement playtest (2026-09-09, client log 12:28:36–12:43:28):
  user initially reported the requested changes worked. Later explicit mantle
  retests supersede that initial impression. All eleven handling/vault/fire patches enabled successfully;
  no movement fallback, request-error or mantle-timeout diagnostics appear.
  This remains user-observed acceptance for the other three changes with a
  log review, not an independently replayed test of every edge case. The historical delivery entry below records
  the pre-test state. The logged client has the 11:06 build timestamp and later
  quick-slot work; its exact DLL hash is not present in the log. Current checkout
  also includes `35abdeb`, `8cf0391` and `edb2418`; the movement sources themselves
  in that logged snapshot were unchanged since `f53bffa`. Do not treat the currently installed DLL as proof
  of the historical test binary or claim this run tested the later `edb2418`.
  Customs contour TDM reached 5v5 ready at 12:32:05.920, then ended normally at
  its 600-second limit (12:42:08.427), USEC 15:14 BEAR. Local player: 11 kills,
  three deaths; replacement pipelines completed in 0.78/0.76/0.80 seconds.
  One initial BEAR activation became native `ActiveFail` at 12:31:44.172;
  replacement recovered at 12:31:48.848, with no further activation failures.
  Native `BotOwner.Activate` swallows its exception when setting that state, so
  the exact activation cause is not exposed by this log.
  The client's deferred raid-end warning at 12:42:17.148 is resolved by the
  matching UTC server log: native end arrived 10:42:17.211, snapshot restored
  at .230 and protected end/persistence completed at .256. No stuck restore
  is indicated. The sole error is a SAIN `BotComponent.OnDisable` NRE during
  native pool/world unloading at 12:42:17.339, after profile persistence; no
  movement hook is in that stack. Its precise null/cause remains unattributed.
  Retain the isolated activation and unload findings for follow-up, without
  speculative patches. Evidence snapshots and summary are ignored under
  `artifacts/movement-playtest/20260909-1228/`; client log SHA256
  `850047ADE5075C69176A3B14212D38DFF560C811FA15FA8A34575625C5EFA41C`.
  This follow-up changes documentation only; no build, deployment or push.
- Arena movement expansion delivered (2026-09-09, code `f53bffa`, branch
  `feature/arena-air-movement`, inheriting the previous independent TDM flow):
  hard-landing speed-limit duration defaults to 0.5 of native; firearm draw and
  holster animations each default to 1.5 times native speed. Both use scoped
  local-body changes, without altering damage, reload/fire/global animator speed.
  Deliberate native vault input may request a mantle during a genuine jump;
  native geometry/stamina/hands restrictions and animation entry remain in use.
  Conventional firearms may hip-fire during that jump without unlocking aiming.
  A pending mantle takes precedence over double jump and the hip-fire exception;
  stale geometry or replaced raid/body/animator cancels ownership safely.
  All five settings are under F12 Movement and its existing master switch.
  No new key, forced animation, teleport, server/profile/AI/Redeploy change.
  See `AIR-MOVEMENT-REVIEW.md` for native anchors, limitations and GPU checklist.
  Offline checks passed: handling 161 assertions/12 deliberate source faults,
  air vault 70/13, hip fire/shared scope 92/10, double jump 144/8 and native TDM
  menu/settings 80/8, including final original-production reruns. Parent reran
  both delegated suites independently. Four-pass adversarial review has no
  unresolved P0-P2. All eight scoped implementation/delivery gates are met,
  zero unmet/abandoned; native GPU acceptance is explicitly outside those gates.
  Client rebuilt from clean `f53bffa` with zero errors/four existing nullable
  warnings and delivered locally, SHA256
  `375D7AE61F58A5AFE168EEAE4AEA3FB0CC6ADBB52B8F5EB2917FEA9338AA456F`.
  Clean staged manifest:
  `artifacts/double-jump-build/1087c40163954f3fb0779ec9a1d5fe5c/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/air-movement-delivery/e3b1314bf10d4551baf1b7416cf67528/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No GitHub
  push, merge to master, GPU mirror-completion verification or native playtest.
  Compare the client hash in `C:\SPT41` before testing. Airborne mantle graph
  acceptance/timing is unverified; its pending geometry tolerances need a real
  scene test. Walk-off falling and underbarrel launcher fire remain native.
  A potentially inlined native mid-operation speed refresh safely relinquishes
  acceleration for that operation; do not replace this with global speed hooks.
- Independent TDM match flow delivered (2026-09-08, code `94a8c38`,
  branch `feature/tdm-match-flow`): implements
  a complete-roster/initial-kit readiness gate, fresh three-second countdown,
  frozen client kill/time limits (default 50/600s, each zero disables), and an
  immutable victory/defeat/draw result. Native map-screen presets fit inside the
  current mode panel; other modes and PFT retain their controls. Damage/scoring
  are held during preparation/countdown and after result; actors may still move.
  The final scoreboard opens once the native replacement pipeline settles,
  uses existing win/loss audio (draw is neutral text), and offers Return to menu
  or automatic protected exit after 15 seconds. New bot requests/respawn entry
  stop immediately; an already-started body replacement may safely finish.
  Native exit requires the exact game/world/profile and a valid current owner;
  terminal factory failures never route through the generic unsafe-owner abort.
  No global AI pause, SAIN/path repair, server, Redeploy or profile-rule changes.
  See `TDM-MATCH-FLOW-REVIEW.md` and `tools/TdmMatchChecks/GATES.md` for verification
  and the native GPU acceptance matrix. No GPU playtest is implied by compilation.
  The latest user diagnostic run before this feature had 103 awareness samples
  with no invalid-path reproduction; the bounded position diagnostics remain.
  Offline verification passed: policy/native exit/extracted respawn guards
  227 assertions + 15 deliberate source faults; actual TDM runtime 313 + 26,
  preserved extracted burst checks 21 + 8; actual match/give-up 618 + 16;
  actual native-menu rules/views/settings 80 + 8. Each fault must fail its
  intended assertion, and the original production inputs are rerun afterwards.
  Parent independently reran both delegated suites; shared banner (36), area
  menu (397), warmup, native map/access, announcer, PFT/round expansion and the
  server/profile suite passed. Existing Labyrinth fixture was repaired in
  `7e4cf77` by supplying the already-existing area team-size setting; no
  production Labyrinth behavior or assertions changed. Independent adversarial
  review has no unresolved P0-P2. Staged client build passed with zero errors
  and four existing nullable warnings. All eleven scoped gates are met, zero
  unmet/abandoned; these gates explicitly do not certify native GPU behavior.
  Client rebuilt from clean `94a8c38` and delivered locally, SHA256
  `436C770A289480AAB9BC7407CAC5EA4D59A2702EEE97503F660076696969C1D7`.
  Clean staged manifest:
  `artifacts/double-jump-build/df64595a0ce642f7b6fd4a76864555ea/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/tdm-match-delivery/6c5be9363f7e448a87ac64a4e874ed30/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No GitHub
  push, native GPU playtest or GPU mirror-completion verification. Check the
  delivered client hash in `C:\SPT41` before running the acceptance matrix.
- TDM invalid-path positions delivered (2026-09-08, code `f209955` on `master`):
  the user's next log has eight
  sampled invalid paths, six for the same actor; another actor also cannot path
  to that actor. `Arena TDM path detail:` adds one bounded position snapshot per
  physical bot/raid after two qualified invalid-path samples at the existing
  30-second cadence. It reads actual body positions, cached SAIN NavData and last
  hits, signed XYZ offsets, the selected goal's last-known destination and cached
  path status/corner count/length. No sampling, path calculation, cache repair,
  teleport, loading, AI behavior, profile, server or Redeploy changes.
  SAIN's `IsOnNavMesh` and last-on time are cached heuristics, not fresh hit proof;
  last-on age is explicitly not hit age. See `TDM-AWARENESS-REVIEW.md`.
  Installed native getters checked; independent adversarial review has no P0-P2.
  Full staged client build passed with zero errors/four existing nullable warnings,
  without changing installed artifacts. The production-linked verifier passed
  596 assertions, 28 named negative controls and its final production rerun;
  evidence: `artifacts/tdm-awareness-checks/804fdf3544e848378a96f6b8f4023533/`.
  Client rebuilt from clean `f209955` and delivered locally, SHA256
  `DCD43FC0E60049C4F1D86FD22FA2E398C0CC43E5852B2B1AC1FFDC13A6ECB677`.
  Staged manifest: `artifacts/double-jump-build/2b969a3756c2434aa73feac8a0993a35/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/tdm-path-positions/delivery-f9cc8380573d4486ae02bc9bf14de670/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No new native
  GPU test, mirror-completion verification or push. Next test: allow a repeatedly
  failing bot to reach two qualified 30-second samples, then inspect its
  `Arena TDM path detail:` line with the adjoining awareness records.
- TDM search diagnostics delivered (2026-09-08, code `f619b7d` on `master`):
  the user's two Customs team-start
  USEC 5v5 logs confirm completed rosters after 104.141/37.059 seconds, preserved
  slow preparations and working hearing/search in both runs. Remaining sampled
  path failures need better attribution. `TdmSearchAwareness` now adds the selected
  SAIN goal with its own contact/distance fields and the exact cached
  `PathCalcFailReason`. Nullable `DebugShallSearch`, no-goal and `None`/`AvoidGrenade`
  checks prevent reporting retained reasons as a current evaluation. Cue target
  and EFT native goal stay separate. No movement, hearing, loading, profile,
  server, Redeploy or LootOutline changes. User removed LootOutline for now;
  its interaction with hidden/trimmed corpses remains a separate diagnostic lead.
  See the follow-up in `TDM-AWARENESS-REVIEW.md` and ignored log comparison
  `artifacts/log-comparison/20260908-201507/` (both matching server ends confirm
  snapshot restore/persistence; not a new independent profile-diff test).
  Verification: 577 production-linked assertions, 24 named negative controls and
  final production rerun passed, evidence
  `artifacts/tdm-awareness-checks/f607bc3c1aaa44fea1663f71e3b91ee6/`.
  Installed SAIN 4.5.1 contracts independently inspected; adversarial review has
  no remaining P0-P2 findings. Full staged client build: zero errors/four existing
  nullable warnings, installed artifacts unchanged by that build.
  Client rebuilt from clean `f619b7d` and delivered locally, SHA256
  `9C174F3DCEAE8988853B3F6CD0D8E807B6FD9823CA3EA096586D7EBB7C2A1A60`.
  Staged manifest: `artifacts/double-jump-build/d77c04233bd04412a194ca513f7e756e/manifest.json`.
  Verified previous-client backup and delivery record:
  `artifacts/tdm-search-diagnostics/delivery-8f92b6fbfdf048bcaa2f3dc899f9c5d7/`.
  Server/Redeploy remain byte-identical. **Restart only the game.** No new native
  GPU test, mirror-completion verification or push. Next test: capture the same
  `Arena TDM awareness:` lines during a stalled search; inspect `sainGoal`,
  goal contact, `shallSearch` and `pathReason` together with movement samples.
- GPU retest follow-up delivered (2026-09-08): `55164e4` was reviewed against
  its archived logs and installed native/SAIN contracts. Three reviewed fixes
  are now on `master`: `8fb502c` supplies a guarded SAIN hearing search cue per
  native contact episode and bounded decision/movement diagnostics; `8ee1b05`
  separates slow bot preparation from the 45-second native-arrival deadline;
  `4a979cf` defers optional kit-catalog loading until initial TDM teams and current
  native kit loading/respawn work settle. The 300-second preparation watchdog
  uses the existing mode-failure path, without retrying unresolved work, claiming
  native task cancellation or adding an automatic raid exit. Actual path/area
  guards, faction capacity, profile/end protection and normal raids are unchanged.
  See `TDM-RETEST-FOLLOWUP.md`, `TDM-PREPARATION-REVIEW.md` and
  `TDM-AWARENESS-REVIEW.md` for evidence and remaining uncertainty.
  Offline verification: all seven runnable gates passed, plus three reviewed
  manual gates (10/10 total, zero unmet/abandoned). Policy 14972 assertions,
  runtime 268, extracted actual async Burst methods 21, awareness 565,
  warmup/native-pool ownership 37, actual optic guards 67; 84 named source-fault
  controls across these suites. All 444 server tests passed without skips,
  existing menu/team/give-up/polygon integration passed, and supplies packing
  passed 1605 checks. Independent adversarial reviews have no unresolved P0-P2.
  Full clean client build has zero errors and four existing nullable warnings.
  **Client delivered locally** from clean code commit `4a979cf`, SHA256:
  `04D47F6DD8B7C9C252C564604819405F89E0ED9F3BD621999032571B8A349C9D`.
  Staged manifest: `artifacts/double-jump-build/907b9b5c983344b78b993bb40a13a63e/manifest.json`.
  Verified previous client and delivery record:
  `artifacts/tdm-retest/delivery-5f45d5094fe54298a490ea46e1f81d9c/`.
  Server and Redeploy remain byte-identical to the retest baseline below.
  **Restart only the game.** GPU mirror completion and new native raid behavior
  are not newly verified; check the above client hash in `C:\SPT41` before testing.
  No GitHub push. The ordinary authorized mirroring workflow needs no new stop
  confirmation. First retest quiet USEC/BEAR 2v2 and cold BEAR 5v5 without player
  shots: observe actual combat, roster time, refills and boundary containment;
  compare first casualties against initial full-roster READY. Early combat can
  delay that latch, but required kits/refills never wait for optional warmup.
  Preserve the profile checks. The 20-second first cold Vanguard load is not a
  proven timeout defect; the two Unity archive-close errors remain unattributed
  and were not suppressed or patched speculatively.
- Native GPU retest of `e2859e8` completed (2026-09-08, `C:\SPT41`): installed
  client `DEFC7DA2...`, server `D7879AEF...` and Redeploy `7D950A47...` match
  the delivery. Both separate USEC and BEAR profiles reached actual full new-preset
  rosters at all sizes 2v2–5v5; the previous navigation abort did not recur. BEAR
  5v5 took 7:03 to fill and BEAR 4v4 3:48, with recovered timed-out requests.
  Bot movement/combat remain inconsistent: both original USEC 2v2 samples, the
  original BEAR 2v2 and BEAR 5v5 stayed quiet in their observation windows. The
  later user-requested BEAR 2v2 firing trial showed movement, a kill and a refill
  BEFORE three own shots; it does not establish that shots awaken stalled bots.
  USEC 5v5, BEAR 3v3/4v4 and legacy contour show combat/refills. Legacy fixed 2v2
  ran 8:06 with three own respawns (1.44/1.49/1.48s), both-side bot refills and
  visible sights. All eleven Arena ends restored and persisted protected profile
  fields unchanged. Human faction labels, sampled replacement rendering, menu
  transitions, PFT Escape/settings and bounded double-jump cases passed. Precise
  movement/geometry and actual PFT group/buy/round/spectator coverage remain
  incomplete; no complete GPU approval. Normal PMC Customs/USP-1 ADS and F9
  separation passed the observed cases; its ordinary LEFT 2:13 gear loss and
  progression changes are documented separately from Arena protection. Two Unity
  archive-close errors before BEAR 3v3 arming are unattributed; no new raid
  camera exception observed. RDP interruptions and restored input bindings are
  documented. Final client setting and Controls comparisons are zero; server
  configuration unchanged. Final RDP display is 1920×1080 (initial 1920×945),
  with DLSS Balanced and other rendering options unchanged. Native game exit
  and owned test-server stop completed; both absent at 18:36:23 MESZ.
  See [GPU-RETEST-2026-09-08.md](GPU-RETEST-2026-09-08.md) and ignored evidence
  `artifacts/gpu-acceptance/2026-09-08_125900-retest/`. Only documentation changed;
  no code/build/deploy, manual profile repair or push during this acceptance.
- GPU report follow-up (2026-09-08): the user authorized implementation from
  report `2327f63`, with double jump merged into the main branch first. `master`
  includes that merge as `227fb10`; all three reviewed fixes are now integrated
  into `master` through code commit `e2859e8` (the source of the delivered DLL).
  `853a2ce` fixes the human PMC scoreboard role; `77469cc` guards the replacement
  grass-camera ordering and optional PFT inactive-menu warmup. Team-start
  readiness now requires the actual 1 PMC + N-1 allies versus N opponents and
  verifies contained bidirectional waypoint connections between existing native
  markers. Fresh final and placement checks reject stale paths; actual bot path
  corners are still checked independently. Legacy areas retain direct validation.
  See `TEAM-START-ACCEPTANCE-REVIEW.md`, `AREA-CONNECTIVITY-REVIEW.md`,
  `ROLE-LABEL-REVIEW.md` and `GPU-ACCEPTANCE-COMPAT-REVIEW.md`.
  Final verification: all seven executable gates passed, plus independent review
  and verified delivery (9/9 gates). Targeted checks: team runtime 369, connectivity
  630, role 174, double jump 143, render/PFT 47 assertions and 54 named source
  controls; ten connectivity source contracts. All 444 server tests passed with
  zero skips and the existing menu/team/polygon regressions passed. Full client
  build: zero errors, four existing nullable warnings.
  **Client delivered locally** in `D:\SPT41` from clean `e2859e8`, SHA256:
  `DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`.
  Server and Redeploy hashes remain identical to the GPU baseline below. Staging:
  `artifacts/double-jump-build/c96304a3078740c78c60610cf5757cca/manifest.json`;
  previous client and verified `delivery.json`:
  `artifacts/team-start-acceptance/delivery-267f829d91bf4dd98c88287380fef267/`.
  GPU mirror completion is not newly verified; check the delivered hash there.
  No GitHub push or native GPU success is claimed. Restart only the game.
  Retest new team starts on both factions at sizes 2-5, initial roster and refills,
  actual movement/boundaries, F9 role, upscaled rendering after respawn and PFT
  Escape/settings menu. Double jump also needs its first GPU acceptance. Only
  the client changes; no server/Redeploy rebuild is required for this follow-up.
- Native GPU acceptance (2026-09-08, `C:\SPT41`): **new team-start preset fails**.
  Expected DLL hashes matched before/after testing at documentation HEAD `62c7b70`.
  All four sizes are selectable in the native 1920x945 menu; 2v2 (including its
  sole retry), 5v5, 3v3 and 4v4 abort after 13 seconds during navigation validation:
  `BEAR starts: PMC 0`. No active new-preset teams, sector/boundary or refill pass.
  Legacy contour 2v2 played for 3:45 with three player respawns, friendly refills
  and working observed optics; preload guard diagnostic occurred, no camera NRE.
  All six Arena ends restored and persisted protected profile fields unchanged.
  Normal PMC/PSO test passed the observed start/ADS/hotkey separation; its ordinary
  LEFT gear loss is documented separately. PFT group and other-faction tests remain
  unexecuted. At that point the Unity render/menu errors were unattributed; the
  follow-up above records source-supported repairs and remaining uncertainty. See
  [GPU-ACCEPTANCE-2026-09-08.md](GPU-ACCEPTANCE-2026-09-08.md) for exact limits/times.
  At the end of this test run, game/server were stopped and only documentation
  had changed; no fixes/build/deploy/push occurred during the acceptance itself.
  Raw evidence and private test-profile backups are ignored under
  `artifacts/gpu-acceptance/2026-09-08_102800/`.
- Integrated into `master` before the navigation fix (2026-09-08):
  `feature/double-jump` / `fcfcd71` adds an optional extra
  jump after a normal local Arena takeoff, using the configured jump binding.
  F12 Movement: `DoubleJumpEnabled` (default true), `DoubleJumpHeightMult`
  (default 1, range 0.25-1.5). Native collision, lateral motion and stamina remain
  in charge; bots/ordinary raids and ledge-fall rescue are excluded. See
  `DOUBLE-JUMP-REVIEW.md` and `tools/DoubleJumpChecks/GATES.md`.
  At feature preparation: 143 production-linked assertions and seven deliberate source faults,
  full staged client build (four existing nullable warnings), independent review
  without unresolved P0-P2. The live test baseline stayed unchanged at that time.
  **Now delivered with `e2859e8`; GPU acceptance still pending.** The user
  authorized integration before correcting the report's navigation failures.
  See the current delivery entry above; server unchanged by the double jump.
- Latest implementation (2026-09-08): `2407351` scopes Arena's cold optic-pool
  callbacks; `6843e1e` adds the separate **Customs contour (team starts)** preset
  with native **2v2–5v5** selection. Choose Arena / Team Deathmatch / Customs /
  that new preset explicitly. USEC starts west and BEAR east; after initial
  assembly, respawns use connected in-area points ranked against live enemies.
  Position/identity reservations protect asynchronous bot and player handover.
  Legacy contours stay fixed 2v2; full-map/PFT/ordinary raid paths are unchanged.
  See `AREA-TEAM-SETUP-REVIEW.md`, its linked scope reports and
  `OPTIC-PRELOAD-REVIEW.md` for limitations and user test steps.
  The final implementation passed 444/444 server tests (zero skips), focused
  production-linked runtime/menu/optic checks and deliberate fault controls,
  full legacy area/polygon regressions and independent adversarial reviews.
  No native GPU raid or actual menu rendering is claimed for this update.
  Both DLLs were rebuilt from clean code commit `6843e1e` into isolated staging
  and installed locally in `D:\SPT41`, with matching SHA256 hashes:
  client `49762EB638B244F3E2D45ADB3BC040CA6A42FBAF895B7A2A3C80B1CC006839E9`;
  server `D7879AEFD593B8CA2915B8F8FAF7106C9F007A6E6BE2D02425D3892AF253AAF2`.
  Previous DLLs, prepared outputs and `delivery.json` are in ignored
  `artifacts/area-team-setup/prepared` in the shared repository.
  **Restart game and SPT server** before testing 2v2 and 5v5, initial team sides,
  several deaths/refills and optics during background kit preloading. Distance
  scoring is not a cover/visibility guarantee, and the broad start sectors still
  need a gameplay fairness check. GPU mirror completion is not newly verified.
  Both code commits are integrated in the shared checkout; continue there, not
  in the temporary `Documents/Codex/AreaTeamSetup` author clone. No GitHub push
  was performed for this follow-up. Normal mirrored delivery needs no repeated
  stop-confirmation prompt; the existing user-authorized workflow is unchanged.
- Latest user acceptance (2026-09-08): `714b80b` now loads and plays successfully.
  The 07:26-07:36 local log confirms contour READY with 17 player/11 bot spawn
  candidates, 2v2 readiness, 9.6 minutes of play, 8 kills/3 deaths, no reported
  boundary or activation failure, and completed server snapshot restoration.
  See `CUSTOMS-CONTOUR-REVIEW.md` for exact observations and the two isolated
  optic-camera/menu errors still worth tracking. This is one successful user
  raid, not exhaustive geometric or long-duration acceptance. No new DLL needed.
- Previous implementation: `fix/contour-corner-carvers`, `714b80b`, based on `922d656`. The user's
  07:04:53 local attempt on 2026-09-08 identifies the exact cause: corner 0's
  navigation capsule reports height 27.7499981 instead of assigned 55.4999962.
  The previous small-rounding hypothesis was insufficient. Physical capsules
  and edge-box checks pass. Corner navigation now uses full-height box obstacles
  via the same size API as edges; physical limits, grid, area/spawn rules and
  server remain unchanged. The boxes carve slightly more conservatively at
  diagonals (up to 0.125 m extra); all navigation readiness checks stay strict.
  See `CUSTOMS-CONTOUR-REVIEW.md` for implementation and subsequent user success.
  The delivered change required only a game restart, no server restart.
  Offline checks: 69,023 enclosure assertions, 13 deliberate fault controls,
  staged Release client build and independent review without open P0-P2.
  Locally installed client SHA256:
  `20D76AA252C3B2939DC17B6FC4E7A2A3B21C614F324D8EBC7512C7B640714167`.
  Prepared DLL/backup/manifest and failure excerpts are in ignored
  `artifacts/corner-carvers/prepared`. Continue in the integrated shared checkout,
  not the old isolated author clones; GPU synchronization is not newly verified.
- Previous follow-up: `fix/contour-integrity`, implementation `922d656`, based on `63affca`. The first user
  polygon raid failed at 06:45:16 local on 2026-09-08: all 28 markers were
  accepted, but the aggregate enclosure-integrity guard failed before navigation
  readiness. Exact capsule dimension comparisons are a suspected cause only;
  native getter values were absent from that log. The client now allows bounded
  0.1-mm scalar roundoff against planned capsule dimensions and propagates precise
  component/property diagnostics through preparation and active-match failure.
  Safety guards, server, contour and spawn policy are unchanged. See the dated
  follow-up in `CUSTOMS-CONTOUR-REVIEW.md`; the next GPU raid remains unverified.
  Follow-up verification passed 67,002 enclosure assertions, 11 fault controls
  and a staged Release client build; independent review has no open P0-P2.
  The client is installed locally, hash `326405FD7021EC2E9A7E60075EE70198851443370307549DA1FBA6D7754C891E`;
  backup/prepared DLL/manifest are in ignored `artifacts/contour-integrity/prepared`.
  Restart the game for this update; no server restart is needed. Continue from
  the integrated shared checkout, not the temporary isolated author clone.
- Previous follow-up: `feature/customs-contour`, implementation `4a1d0f4`, based
  on clean `235b285`. The
  user's 15-point Customs survey is registered as a NEW selectable
  `customs-factory-center-polygon-v1` / **Factory Center (contour)**, independent
  TDM 2v2 only. Choose it explicitly; legacy area IDs/defaults remain unchanged.
  The new preset has only lateral containment: no actor/path/spawn height band,
  physical floor/ceiling or vertical cap carving. Scene-derived side walls cover
  overlapping NavMesh triangles and spawn anchors with padding. The elevated
  sniper marker is excluded by exact identity on both client and server.
  See `CUSTOMS-CONTOUR-REVIEW.md` for scope, staging checks and user GPU tests.
  The implementation is integrated in the shared checkout; continue there,
  not in the old isolated author clone. Client AND server need a restart after deployment.
  Offline verification passed 386/386 tests (zero skips), focused real-data and
  native-menu checks, enclosure/fault-control checks and staged Release builds.
  Review has no open P0–P2. On 2026-09-08, both reviewed DLLs were installed in
  D:\SPT41 and their SHA256 hashes matched the prepared build. Retained client
  and server DLLs plus manifest are in ignored `artifacts/customs-contour/prepared`
  in the shared checkout. See the contour review for hashes; local installation
  does not by itself prove GPU synchronization or in-raid acceptance.
- Current follow-up: `fix/pmc-area-capture`, based on clean `83a26cf`. The user
  found Arena-only capture impractical because they cannot walk outside the old
  contour. F12 `Development - Area outline` now offers `AllowInNormalPmc`
  (default false), in addition to `Enabled`, for measuring in a normal local PMC
  raid. This is a read/draw/explicit-export exception only; normal raid/profile
  consequences remain and Arena borders stay closed. No server/geometry change.
  The production-linked capture checks pass 292 assertions plus nine deliberate
  fault controls; independent review has no open P0-P2. Release client compilation
  uses isolated deployment staging, with live DLL hashes unchanged during tests.
  That capture follow-up needs a game restart, not a server restart; its
  historical offline checks are distinct from user GPU testing.
- Current work: polygon boundaries and an opt-in scene capture/export helper on
  `feature/polygon-arena-areas`, based on clean `e61b6e3`. See
  `POLYGON-AREA-REVIEW.md` for controls, validation and pending user GPU checks.
  The user has no Customs vertices yet and explicitly chose capture help first;
  both existing presets stay unchanged. No draft is loaded into a live match.
  Work was prepared in an isolated checkout outside Syncthing; continue from the
  safely integrated shared checkout after delivery, not temporary author clones.
- Previous work: native area selection on `feature/native-area-menu`, based on
  the clean `ec11aa8` handoff snapshot. See `NATIVE-AREA-MENU-REVIEW.md` for the
  selection rules, verification and pending GPU menu checks. The source was
  prepared outside Syncthing in an isolated checkout; use the integrated shared
  checkout for subsequent work, not that temporary clone.
- Previous gameplay baseline: bounded Customs `a690ce2` on
  `feature/customs-arena-area`, based on `011beb5`. The subsequent user playtest
  reports overall trouble-free operation, with boundary refinement still needed.
  See `CUSTOMS-AREA-REVIEW.md`; this acceptance update changes documentation only.
  Prior Factory prototype `0c54b7745534798a09662511281978ffbb20acc4` includes the
  earlier kits/GPU menu redesign; its core behavior is user-confirmed below.
- Development host: `D:\SPT41\Development\ArenaMode`; no GPU raid execution there.
- User-confirmed GPU host installation: `C:\SPT41`, a mirror of this host's
  `D:\SPT41`. If Development is included, the corresponding repository is
  `C:\SPT41\Development\ArenaMode`. Check that checkout before editing. Mirror
  inspection on the GPU host confirmed bidirectional Syncthing of all `C:\SPT41`
  without exclusions, matching installed hashes, and working native capture/input.
- **The user performs actual in-raid gameplay tests.** Do not assign autonomous
  raids to the GPU session. Its next intended work is UI redesign and automated
  visual/menu checks, with local development/build/review here as useful.
- Use the existing mirrored installation for UI checks; no second full game copy
  is needed solely for testing. Before editing source on both machines, establish
  separate writable checkouts/branches outside any conflicting mirror scope.
- For simultaneous implementation, assign a separate feature/fix and branch to
  each session. Agree who integrates changes and who owns each test deployment.
- Commit every completed change with tests/documentation and
  `Co-authored-by: Codex <codex@openai.com>`. Do not push without authorization.
- Preserve normal raids and profiles. Protected PFT Tactical uses the PMC entry
  loadout and must not receive standard Arena kits or grenade additions.

### User clarification on the GPU host (2026-09-07)

- **Artifact delivery update (2026-09-08):** do not ask again whether the game
  and SPT server are stopped before routine delivery into the existing mirrored
  installation. The user states that their synchronization defers files while
  they are in use and explicitly removed the stop-confirmation prerequisite.
  An offline GPU host is not a reason to hold back local deployment. Keep local
  hash verification and report remote synchronization separately; source/build
  ownership rules below still apply. Restart changed components before testing
  the new version; copying a DLL is not proof a running process has loaded it.
- EFT/SPT is never run on both PCs simultaneously. The user accepts immediate
  mirroring/overwriting of logs and saves; these profiles are disposable test data
  and even their loss is acceptable. Routine menu checks do not require pausing
  Syncthing or restoring all runtime changes for test-data protection. The local
  `SPT41` folder was resumed at 11:03:21 MESZ after the initial menu inspection.
- This test-data policy does not change Arena's profile-isolation behavior or
  authorize autonomous raids. Concurrent source edits/builds still need the
  coordination described below; single-host game execution does not imply that
  two source checkouts cannot be edited concurrently.
- The baseline contains 22 screenshots covering menus, maps/times, all five modes
  and return to normal PMC selection; no raid was started. The initial report's
  claim that BACK was incomplete was incorrect and has been withdrawn. Do not
  treat missing BACK text or other unverified glyph gaps as an established bug.

## Current behavior and recent work

The surveyed Customs contour is now a separate selectable area alongside the
legacy rectangle. It is the first height-unlimited preset; existing box/polygon
presets and capture-export defaults keep their previous semantics. Source draft
JSON remains review data and is never autoloaded. Installed database checks give
17 PMC/11 bot candidate points, not yet a proof of loaded scene connectivity.

Polygon support now accepts simple concave XZ outlines plus enclosing heights.
Walls/grid, floor/ceiling caps, spawn containment and full path-segment checks
share that contour. F12 `Development - Area outline` optionally captures ordered
scene points (Ctrl+F6 aim, Ctrl+F10 feet, Ctrl+F7 undo, Ctrl+F8 export). JSONs go to
`BepInEx/plugins/ArenaMode/AreaDrafts`; capture is scoped to the exact local raid
and does not change settings/geometry for it. Normal PMC capture additionally
requires `AllowInNormalPmc`; Scav/Hideout and ending/invalid Arena identities are
rejected. Ordinary raids retain ordinary equipment/profile consequences; this
helper grants no Arena protection. Export before leaving/disabling.
The user subsequently supplied the Customs draft, now integrated as described
above. Waypoints remains the spawn viewer. CTF, team-size changes and autonomous
raids remain out of scope. See the polygon and contour reviews for test evidence
and what still needs actual Unity/GPU confirmation.

Historical polygon-stage verification at `83a26cf`: 363 complete database-backed tests (no skips),
focused geometry/capture/enclosure and native/TDM regressions, plus 14 deliberate
fault controls. Independent review closed the body/world ownership and visual
layer findings. The user confirmed server and game stopped before deployment.
Final Release outputs are hash-compared with the local installation; this is not
a fresh GPU host hash comparison or an in-raid test.

The previous native picker received the user's "ja passt" acceptance. This is
broad functional acceptance, not proof of every individual PFT/menu transition.

Native area selection now appears below the five Arena mode rows on the map
screen. Choose Arena -> Team Deathmatch -> Customs: the map is directly offered
and selects Factory Center automatically, with fixed 2v2 displayed. No F12
preselection is required. Factory daytime offers Whole map / Factory South;
South remains fixed 2v2. Factory nighttime, Labs and Labyrinth offer Whole map.
Area selection shares the existing F12 `ExperimentalArea` setting. Map/time/mode
changes preserve compatible choices and normalize incompatible ones during menu
selection; leaving independent TDM hides Customs and clears its selected map.
There is no whole-map Customs Arena or PFT area support. Final client/server
arm validation remains strict and never repairs settings. Configured native rows
are constructed before the mode view's PFT repair, avoiding an empty picker from
a stale F12 mode. No repeated native ShowInternal, geometry or gameplay changes.
Edited allowlists and explicit server disables remain authoritative. Restart the
game after this client change; server source and area protocol are unchanged.

This first Customs candidate spans X305..420 / Z-135..-65, with 15 PMC and seven
bot positions in installed data. These are coordinate checks, not live NavMesh
evidence. The northern face can cut a building footprint. In the subsequent
2026-09-07 playtest the user reports that Customs also works without overall
problems, but the boundaries should eventually be refined by hand. This confirms
basic practical operation, not every detailed test case below. Boundary polish,
explicit repeated-respawn/edge checks, Escape/re-entry, menu setting changes and
teardown into ordinary raids remain to be confirmed individually. Its live
readiness gate must reject an unusable component rather than spawn outside.
Factory South geometry remains unchanged; loading/readiness/respawn now share an explicit
PMC-side check so Savage-only Player markers cannot become human spawn choices.
`CUSTOMS-AREA-REVIEW.md` records checks, deployment evidence and the test list.
Historical Customs-stage local verification: 330 server/data tests passed without skips; targeted
client regressions and nine negative controls passed, independent review has no
open P0-P2 findings. Release outputs match all four DLLs installed on D:\SPT41.
At delivery the local Syncthing query reported SPT-GPU disconnected (not paused).
The later user playtest confirms the Customs preset ran there; it is not a fresh
remote hash comparison or a current synchronization-status check. No code,
boundary coordinates, DLLs or installed settings changed in this acceptance update.

Gameplay prototype `0c54b77` on `feature/bounded-arena-prototype`, based on `7298980`
including the GPU menu redesign: opt-in Factory South, Factory daytime,
independent TDM fixed 2v2. See [BOUNDED-AREA-REVIEW.md](BOUNDED-AREA-REVIEW.md)
for the exact area, server/client contract, verification and required user raid
checks. Enable before the raid via F12 -> Team Deathmatch -> `ExperimentalArea`
= `factory-south-v1`; default/disable is `full-map`. Other modes are unchanged.
Server and game need a restart after a gameplay deployment. User GPU playtest
on 2026-09-07 confirms: visible impassable grid, player and bot spawns always
inside the zone, and bots staying inside without observed stalls. This is the
user's report, not a new agent raid or log inspection. Remaining explicit checks:
Escape/give-up, another bounded match, full-map TDM and a subsequent ordinary
PMC/Scav raid with clean teardown/profile behavior. No gameplay code changed
in this acceptance update.
The Customs candidate above follows this successful core small-map test;
CTF, an editor and PFT region support remain unimplemented. The native area picker
is now implemented; its current verification is in `NATIVE-AREA-MENU-REVIEW.md`.

The user confirmed game and development work on the GPU host were stopped and
new DLLs may be installed. This change was prepared in an independent local
clone outside the Syncthing tree, then integrated into the shared repository;
do not resume that old isolated clone as a second source writer. Deployment
hash checks on D:\SPT41 do not by themselves prove completed GPU synchronization.
The local Syncthing query reported SPT-GPU disconnected at initial delivery.
The later user playtest confirms the prototype ran there; no fresh remote hash
or current synchronization-status verification is implied by that report.

Five modes: Endless Deathmatch, Rounds, Gun Game, Tactical, Team Deathmatch.
Team Deathmatch is independent of PFT; protected Tactical 5v5 is a separate path.
Maps: Factory day/night, Labs, Labyrinth; Customs only with its bounded independent
TDM preset. Factory night uses the native time selection on the Factory tile,
not a separate night tile.

GPU menu redesign on `feature/arena-native-menu-options`: Arena's five modes now
appear as five permanently visible native button rows in a left-side panel on
the map screen. Three short rules/respawn/equipment facts replace the mode
paragraph. The selected row keeps a marker after hover ends; native map points,
right-hand location information and navigation stay in place. Factory time
selection is back at its original native position. Player count/difficulty remain
future implemented options, with no placeholders or gameplay changes.

`MODE-SIDEBAR-REVIEW.md` records native row ownership, corrected mode copy, 123
passing selection-state checks and the GPU pass: all five modes, Factory day/night,
Labs/Labyrinth, clean Back/normal PMC maps and exactly one sidebar on Arena reentry.
The disabled PFT row/tooltip presentation and true 16:9 geometry were not tested
on the GPU. Actual captures are 1920 x 945 at native UI scale 0.875. Private
comparison artifacts are in the GPU task's `outputs/Arena-Modusauswahl` and logs
in `work/arena-mode-sidebar`; the final runtime log has no error entries or Arena
warnings. The earlier upward-opening dropdown is historical and documented in
`NATIVE-MENU-REVIEW.md`; its source/licensing research remains relevant.

The earlier character-selection polish at `328665d` aligned the Arena toggle with
SCAV/PMC and added a native-style `SEPARATE MATCH` subheading. At that historical
stage only Arena's wrapper moved; `CHARACTER-SELECTION-REVIEW.md` records its
contracts and evidence.

The character preview at `70a65c0` adds the current PMC's native posed appearance above
ARENA, with a separate fixed M4 USEC / AK-74M BEAR example kit. It moves the complete
native PMC/SCAV columns outward together, restoring them on Close/Show and before
PFT squad management. Model, camera, lights, render texture and bundle retention
have their own owner; no profile inventory or gameplay kit is used or changed.
Preview setup/load failure preserves the working Arena choice. See
`CHARACTER-PREVIEW-REVIEW.md` for native contracts, independent review, production
and fixture build identities, logs and exact GPU coverage. The 29 side/53 flow
checks still pass but do not execute the new Unity/async owner.

On `WIN-Q470OL2A14A`, the production menu run covered USEC/M4 rendering, label
selection, Arena and normal SCAV map/Back routes, repeated reopening, populated
MY SQUAD without Arena, and clean ordinary M60 inspection afterward. A temporary
fixture also rendered the BEAR kit on an unchanged USEC character and verified
closing during delayed bundle loading; the final production source/DLL contains
neither fixture override nor delay. Real BEAR profile appearance remains untested.
Final production standard-M4 smoke passed: all three figures, ARENA label and
description, then clean Back to the main menu. Its saved client log contains no
error-level entries, Arena warnings or fixture markers; game and owned server
were stopped. No raid was started. Captures are 1920 x 945, native UI scale 0.875; private
screenshots/manifest are in the GPU task's `outputs/Arena-Charaktervorschau`.

| Commit | Change | Verification still needed (raid work belongs to user) |
| --- | --- | --- |
| `20462c0` | Standard kits request two RGD-5s, no automatic Zarya. Explicit rig/pocket placement bypasses UseItemsAnywhere's backpack-first `GrenadeThrowingSlots` patch. Custom authored supplies and protected PFT gear are retained. | In-game placement and grenade-key use, including respawn/Gun Game. |
| `1119b56` | Labyrinth Way Up (`labir_exit`) has a 10-second extraction countdown on the Arena raid clone; availability timers remain zero. | Walking past the doorway must not immediately end a match. |
| `f17ae7b` | Read-only diagnostics for stationary foreign helmets/equipment in the item/weapon preview after Arena, in menu/Hideout. **No causal rendering fix yet.** | Reproduce affected preview and collect `Arena preview probe:` plus screenshot. |
| `50d2a89`, `fae3a43` | Restored Tactical preset descriptions and excluded generated reflex sights on empty scope rings. | UI appearance and generated weapon samples. |

The last two investigated Solo Tactical endings were native Labyrinth extractions,
not opponent-spawn failures; the user confirmed walking past the exit door.
Do not reopen that spawn diagnosis without new evidence.

Latest grenade verification: 222 server tests passed, zero skips; 1,605
production-linked supply assertions; both old-placement negative controls failed
at their intended assertions. Default and installed 20-kit catalogs each packed
40 RGD-5s in rig/pockets without displacing original supplies in the offline
grid model. Full grids still skip optional grenades, never spill into backpack.
Independent review found no open issues. These are not GPU test results.

GPU Release/installed hashes after the mode-sidebar client build
(2026-09-07; menu pass completed at the recorded window geometry).
The server binary remains the prior gameplay build; Redeploy.Core was rebuilt
from unchanged source `58abf5876629bb51be808d78ee2cd7a5d34af77a`:

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client | `67B55E0A532BFACE875AF556D6760B1AD454698AB9FE4B4125A8A99350390986` |
| ArenaMode.Server | `FCE6C394DF9184E81FE6D43B0D236380DE71E3BB110708497E2471D395B2DBE8` |
| Redeploy.Core | `F923B0D709908F75969B3101E9F8290CD2BB06DF457F64B1FFF8876A7BBFEA00` |

Hashes identify these built files. A fresh build on another machine may differ;
always record that build's hashes and compare them with its installed copies.

## Continue on the GPU host

1. Open a new session in `C:\SPT41\Development\ArenaMode` if present. Read this
   file and `AGENTS.md`, inspect branch/commit/uncommitted files, and verify that
   references/deployment resolve to `C:\SPT41` and `C:\SPT41\SPT_Runtime`.
   Confirm the latest handoff/commits arrived through the existing mirror or an
   agreed Git transfer; do not create a competing sync mechanism. A new session
   must not assume access to the original conversation or live agent memory.
2. For UI checks only, use a fixed mirrored build with its commit/hash record.
   A complete development toolchain is unnecessary for simply running the game.
   For development, transfer/clone the repositories and dependencies listed below.
3. Use the existing disposable test profile and compare relevant mod versions/settings
   between hosts. The user permits live bidirectional mirroring of this test data
   under the single-host execution policy above; routine menu checks need no
   mandatory profile backup/restore. Keep test configuration changes explicit so
   a difference in mods is not mistaken for an Arena regression.
4. Keep one deployment owner and deliver one coherent build through the existing
   mirror without an additional game/server stop question. Verify installed
   hashes locally and synchronization separately. The user manages restart and
   test timing; identify the actually loaded build when recording test results.
5. Begin with the UI workflow below, not the pending raid tests. For the existing
   preview-contamination investigation, the user supplies the post-match state
   after their manual raid; the agent can then inspect the affected item in
   menu/Hideout and collect the probe. Reopen after ten seconds if throttled.
   Compare clean/affected previews; do not destroy guessed scene objects.
6. Return a report per test run: hostname, source commit, actual DLL hashes,
   mod versions/config changes, mode/map/kit, local time and time zone, steps,
   expected/observed behavior, pass/fail/blocked, and exact log/screenshot paths.
   Save logs before the next launch only when needed as diagnostic evidence;
   routine menu checks do not require log backups. Raw logs/screenshots belong
   in a private artifact location (local `artifacts/` is ignored); commit a small
   redacted report if useful. Never commit profiles, tokens or unrelated logs.

## Next work: UI redesign with automated visual checks

- Run the UI-focused session directly on the GPU host. First inspect available
  tools; if native Computer Use is available, verify that it can capture EFT's
  window and perform one harmless menu interaction. Browser-only tools cannot validate the native Unity
  UI. Native game capture/input were verified on the GPU host on 2026-09-07.
  RDP-disconnect behavior remains untested; do not infer it from the presence of
  a GPU or a mirrored installation.
- Native Computer Use on Windows requires the active unlocked desktop, a visible
  target window and app permission. Do not compete with the user's mouse/keyboard
  during a run. See [official Computer Use documentation](https://learn.chatgpt.com/docs/computer-use).
- Start with reachable menu flows: PMC/Scav/Arena selection, map/time/mode
  selection, back navigation, and overlap/readability checks at recorded
  resolution/UI scale. Stop before starting a raid. Save before/after screenshots
  and associate them with the tested build.
- Raid HUD, scoreboard, Tactical buy panel and spectator UI depend on live match
  state. For their redesign, prefer a development-only preview using the same
  rendering code with fixed sample data (long names, empty/full lists, different
  scores), without spawning bots or simulating a real match. This preview is a
  proposed implementation, not an existing tool or completed feature. Keep it
  opt-in and isolated from profiles, normal raids and production gameplay gates.
  Do not fake `ArenaSession.IsActive` or call match-start hooks just to show a
  panel. Preview buy/ready controls must not purchase, start or end anything;
  preview teardown must not stop a real spectator service. The existing
  `ArenaPreviewDiagnostics` is unrelated item-preview diagnostics, not this UI
  preview facility.
- Distinguish visual results (layout, clipping, fonts, hover/click, scaling) from
  gameplay integration. A preview pass does not prove scoring, respawn, purchase
  transactions or raid input ownership; the user verifies those in actual raids.
- Iterate one agreed screen at a time: capture baseline, implement, review,
  build/deploy between runs, reopen and compare. No autonomous raid playtest or
  scheduled/unattended loop was configured by this handoff update.

## Parallel development without collisions

- Use a separate clone on each machine; exchange reviewed commits using Git
  through the agreed remote or a Git bundle. Do not live-mirror two writable
  working directories or `.git` folders. If the existing SPT mirror includes
  Development, exclude/separate the GPU working checkout before parallel edits;
  otherwise the next mirror pass may overwrite local UI changes. Do not change
  synchronization configuration without inspecting it and agreeing its scope.
- Within one machine, worktrees can isolate feature edits, but they share Git
  metadata and do not isolate Arena's deployment directories or external projects.
- Agree the starting commit, scope and touched areas before parallel work. Avoid
  having both sessions independently fix the same code. Integrate selected commits,
  rerun affected checks, build once, then test that exact integration build.
- When moving a whole task, finish/commit local work and hand over the current
  state. When keeping both sessions active, one handles integration and updates
  this shared summary; each test run can have its own report to avoid conflicts.

## Build and dependency traps

- Keep the normal layout when practical: `<SPT>/Development/ArenaMode`, sibling
  `Redeploy`, `_decompiled`, and the needed private `_reference` sources.
  ArenaMode does not contain those other repositories or game DLLs.
- The client references `../Redeploy/Redeploy.Core` relative to the Arena repo.
  Verified Redeploy checkout at handoff: `c938f5c43cb311a8a9ba1f81b2eb10573b99b936`,
  clean (stage 0 of the respawn hardening; older Core builds refuse nothing). Transfer compatible source separately if building; do not assume a DLL
  alone satisfies the project reference.
- `Shared.props` resolves the game root two directories above the repo and the
  server root under `SPT_Runtime`. In a different layout, verify/override both
  `SPTPath` and `SPTUserPath`. Local `Shared.props.user` is imported and ignored by
  Git, so it must be configured separately on a new machine. Redeploy has its own
  props/deployment target. Check its effective paths too. With both repos under
  `C:\SPT41\Development`, relative defaults already resolve to C:; only override
  where necessary, and inspect mirrored `.user` files for stale D: paths.
- Builds auto-deploy, including the server build triggered by `dotnet test`.
  A generic `DeployToSpt=false` switch is not honored by the current Arena targets.
  Do not build from an arbitrary worktree until paths, references and deployment
  ownership are settled. Never run concurrent builds into the same installation
  or shared Redeploy output. Prefer Release; rebuild the client last if another
  check rebuilt the shared dependency in Debug.
- Usual commands from the verified repo layout:
  `dotnet build ArenaMode.sln -c Release` and
  `dotnet test ArenaMode.Tests/ArenaMode.Tests.csproj -c Release`.
  `tools/KitPolishChecks/Verify-Supplies.ps1` covers the latest grenade change.
  Many harnesses use local `_decompiled` sources, native DLLs or D: defaults;
  inspect their parameters before running elsewhere. Set `SPT_DATABASE` to the
  actual `SPT_Runtime/SPT_Data/database` **directory** for server tests. This is
  separate from a harness's `-Database` parameter pointing at `templates/items.json`;
  the nested server tests do not automatically inherit that parameter. Require
  the expected tests to run, not silently skip. On the GPU host the environment
  variable is `C:\SPT41\SPT_Runtime\SPT_Data\database`.
- Keep licensed audio and attribution together with client assets; use the
  build's asset deployment or a complete verified package, not a guessed DLL set.
  The full solution also needs the installed BigBrain reference DLL. Optional
  runtime BigBrain/PFT/SAIN dependencies must match the actual compatibility
  checks; re-read source and installed versions rather than trusting old notes.

## Where to look next

Client log: `<SPT>/BepInEx/LogOutput.log` (local time).
Server log: `<SPT>/SPT_Runtime/user/logs/spt/sptYYYYMMDD.log` (UTC).
Kits/config: `<SPT>/SPT_Runtime/user/mods/ArenaMode/config/`.

- `KIT-POLISH-REVIEW.md`: grenade conflict, placement rules, evidence.
- `FUTURE-WORK.md`: deferred difficulty controls, further bounded map areas and
  objective-based modes such as Capture the Flag; the area section also records
  current prototype progress and links its review/test evidence.
- `PREVIEW-DIAGNOSTICS-REVIEW.md`: unresolved post-match preview issue.
- `CHARACTER-PREVIEW-REVIEW.md`: separate third menu figure, ownership, GPU/fixture
  evidence and missing genuine-BEAR-profile coverage.
- `MODE-SIDEBAR-REVIEW.md`: five visible map-screen modes, compact facts and
  GPU coverage; PFT presentation and true 16:9 appearance remain untested.
- `tools/ArenaLabyrinthAccessChecks/README.md`: Way Up and Arena-only map access.
- `PRESET-DESCRIPTION-REVIEW.md`, `OPTIC-MOUNT-REVIEW.md`: latest kit/UI fixes.
- `TEAM-DEATHMATCH-REVIEW.md`, `PITFIRETEAM-MULTIROUND-GATES.md`: team modes.
- `ANNOUNCER-REVIEW.md`, `ANNOUNCER-AWARDS-REVIEW.md`: audio behavior/licensing.
- `CORPSE-CLEANUP-REVIEW.md`, `PFT-GOAL-HANDOFF-REVIEW.md`: bot lifecycle/movement.

Read the relevant topic document and recent commits when starting a change,
rather than loading every historical review. Maintain this file as a concise,
dated snapshot of decisions, open evidence gaps and the next owner's scope.
