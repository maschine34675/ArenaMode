# ArenaMode documentation

Working notes, feature reviews, GPU test instructions/results and historical
build/deployment JSON records live here. The automatically discovered
[agent instructions](../AGENTS.md) remain at the repository root.

## Start here

- [Changelog since 0.1.0](CHANGELOG.md)
- [RUB reward settlement core and synthetic profile tests](REWARDS-CORE.md)
- [Protected ordinary-server reward startup consumer](REWARDS-SERVER.md)
- [Server result validation and durable payout approvals](REWARDS-RESULTS.md)
- [Optional rewards: gameplay capture, private-session transfer and player consent](REWARDS-RUNTIME.md)
- [XP and other mail rewards: Give-UI/native feasibility](REWARD-XP-FEASIBILITY-20261025.md)
- [Atomic XP rewards and native progression](REWARDS-XP.md)
- [Automatic reward settlement when returning to the normal session](REWARDS-RETURN.md)
- [Automatic return: native mail, XP, pending totals and empty-return checks](GPU-REWARD-RETURN-20261025.md)
- [XP rewards: private combat, startup payout, level and collection on GPU-2](GPU-REWARD-XP-20261025.md)
- [Reward card on the Arena map screen: GPU-2 menu checks](GPU-REWARD-MENU-20261024.md)
- [Private Arena session reward control: GPU-2 checks](GPU-PRIVATE-REWARD-CONSENT-20261025.md)
- [Arena transits, first three Gun Game stages and reduced ammunition: GPU-2 checks](TRANSITS-GUNGAME-AMMO-20261024.md)
- [PitFireTeam 1.0.0 and version-filtered Arena sessions](PITFIRETEAM-1.0.0-20261024.md)
- [Spawn-gate completion: MapPort contract and verification status](SPAWN-GATE-COMPLETION-20261024.md)
- [Spawn-gate completion and refreshed DynamicMaps: GPU-2 results](GPU-SPAWN-GATE-COMPLETION-20261024.md)
- [Bowl original Team DM boxes and gate opening](BOWL-TEAM-BOXES-20261023.md)
- [Bowl team boxes: GPU-2 native results and delivery](GPU-BOWL-TEAM-BOXES-20261023.md)
- [Bowl map admission, migration and generic spawns](BOWL-INTEGRATION-20261023.md)

Reviews, contracts and test results of the adapters and per-mod refusals removed on
2026-10-22 (MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead, Smoothie, Late To
The Party, Classic Movement, StatTrack, Deminvincibility, Skills Extended, Pause
Continued, EFT Effects Manager / Broken Limbs, and the code-level refusals of ORBIT,
Questing Bots, Adaptive Bot Culling, ISB Special Forces/Aishi) are kept as dated
records, marked "history" below. The code and the offline harnesses they describe no
longer exist; the whitelist replaced them.

- [Arena session: whitelist by not loading (binding specification)](ARENA-SESSION-20261021.md)
- [Arena session gate: build notes and offline evidence](ARENA-GATE-20261021.md)
- [Arena session: answer to the external review of 2026-10-22](ARENA-SESSION-REVIEW-RESPONSE-20261022.md)
- [Arena session: answer to the external review of 2026-10-23](ARENA-SESSION-REVIEW-RESPONSE-20261023.md)
- [Arena session: solution, deployment and packaging](ARENA-SESSION-DEPLOY-20261021.md)
- [Release 0.2.1: the Arena session with content mods left out](RELEASE-0.2.1.md)
- [Release 0.2.0: Arena sessions and the whitelist, Bowl, shared Core 1.0.10](RELEASE-0.2.0.md)
- [Release 0.1.3: compatibility changes and verification boundaries](RELEASE-0.1.3.md) (history: the per-mod adapters it describes were removed on 2026-10-22)
- [ISB Special Forces/Aishi: interim Arena-only admission](ISB-ADMISSION-20261020.md) (history: adapter removed 2026-10-22)
- [First candidate batch: integration, offline results and pending native tests](COMPAT-FIRST-INTEGRATION-20261020.md) (history: the per-mod adapters it describes were removed on 2026-10-22)
- [Pause Continued: Arena-only activation suppression](PAUSE-COMPAT-20261020.md) (history: adapter removed 2026-10-22)
- [EFT Effects Manager / Broken Limbs: body and skill ownership](EFFECTS-COMPAT-20261020.md) (history: adapter removed 2026-10-22)
- [Candidate compatibility: native GPU tests and cleanup](GPU-COMPAT-SECOND-20261020.md) (history: the per-mod adapters it describes were removed on 2026-10-22)
- [Skills Extended: scoped XP and external receipt protection](SKILLS-EXTENDED-COMPAT-20261020.md) (history: adapter removed 2026-10-22)
- [Shared body-replacement adapters in Redeploy.Core 1.0.9](SHARED-BODY-COMPAT-20261023.md)
- [Recoil Rework Legacy: published lifecycle conflict review](RECOIL-REWORK-REVIEW-20261020.md)
- [Recoil Rework Legacy: scoped body/component handoff](RECOIL-REWORK-COMPAT-20261020.md)
- [Additional compatibility candidates: skills, health, ISB, pause, doors and stances](COMPATIBILITY-CANDIDATES-20261020.md) (history: the per-mod adapters it describes were removed on 2026-10-22)
- [Forge top 200: compatibility candidates, pinned sources and review limits](COMPATIBILITY-SURVEY-20261019.md)
- [Arena-only optional AI-mod admission](OPTIONAL-MOD-ADMISSION-20261019.md) (history: adapter removed 2026-10-22)
- [MoreBotsAPI spawn, relationship and revenge isolation](MOREBOTS-COMPATIBILITY-20261019.md) (history: adapter removed 2026-10-22)
- [AI Limit: exact Arena-world component pause](AI-LIMIT-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Looting Bots: scoped autonomous-looting pause](LOOTING-BOTS-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Remove The Dead: retained corpses and pickup protection](REMOVE-THE-DEAD-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Smoothie: moving pickup markers and optional GC policy](SMOOTHIE-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Continuous Healing: native bot healing and retired-body isolation](CONTINUOUS-HEALING-COMPAT-20261019.md)
- [Late To The Party: exact weapon-pickup source protection](LATE-TO-THE-PARTY-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Health and loot compatibility: combined implementation and verification](HEALTH-LOOT-COMPAT-INTEGRATION-20261019.md) (history: partly about adapters removed on 2026-10-22)
- [GPU2 healing, pickup protection and ordinary-raid controls](GPU-HEALTH-LOOT-COMPAT-20261019.md) (history: partly about adapters removed on 2026-10-22)
- [Classic Movement: exact Arena movement-state ownership](CLASSIC-MOVEMENT-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [StatTrack: Arena statistics and body-lifecycle isolation](STATTRACK-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Movement and statistics compatibility: integration and verification](MOVEMENT-STATS-COMPAT-INTEGRATION-20261019.md) (history: adapter removed 2026-10-22)
- [GPU2 movement/statistics initial checks and lifecycle interruption](GPU-MOVEMENT-STATS-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Continuous Load Ammo: body changes and delayed completion](CONTINUOUS-LOAD-AMMO-COMPAT-20261019.md)
- [SVM: protected and ordinary raid-end transactions](SVM-COMPAT-20261019.md)
- [Deminvincibility: exact Arena-local damage and death](DEMINVINCIBILITY-COMPAT-20261019.md) (history: adapter removed 2026-10-22)
- [Ammo, raid end and health: integration and native followup](AMMO-RAID-HEALTH-COMPAT-20261020.md) (history: partly about adapters removed on 2026-10-22)
- [GPU2 lifecycle retest, ammo/health/SVM checks and terminal-exit correction](GPU-OPTIONAL-COMPAT-20261020.md) (history: partly about adapters removed on 2026-10-22; the kept adapters are Continuous Healing, Continuous Load Ammo, Recoil Rework Legacy, PitFireTeam, DynamicMaps, SVM and APBS)
- [Combined optional-mod integration and verification](OPTIONAL-COMPAT-INTEGRATION-20261019.md) (history: partly about adapters removed on 2026-10-22)
- [Native optional-mod startup and pending gameplay matrix](GPU-OPTIONAL-COMPAT-20261019.md) (history: partly about adapters removed on 2026-10-22)
- [Isolation and profile review, 2026-10-19](ISOLATION-REVIEW-20261019.md)
- [Bot spawn shields only for ported-map Team DM boxes](BOT-SPAWN-SHIELD-SCOPE-20261019.md)
- [Depth-occluded bot effect contours](BOT-EFFECT-OUTLINES-20261019.md)
- [GPU2 bot contours, cover, DLSS and spawn-shield controls](GPU-BOT-EFFECTS-20261019.md)

- [Optional mod minimum versions and forward admission](OPTIONAL-MOD-VERSIONS-20261019.md)

- [Native model repair: integration, ownership and verification](MODEL-REPAIR-INTEGRATION-20261019.md)
- [Cold PMC/PFT model-fault controls and exact restoration](GPU-MODEL-CONTROLS-20261019.md)
- [GPU2 model repair: actual replays, PFT rebind and preserved data](GPU-MODEL-FIXES-20261019.md)

- [Model preview ownership and PitFireTeam lifecycle investigation](MODEL-PREVIEW-INVESTIGATION-20261019.md)
- [Native animation fault attribution and diagnostic limits](MODEL-ANIMATION-INVESTIGATION-20261019.md)
- [Model asset, pooled equipment and Killcam clone investigation](MODEL-POOL-INVESTIGATION-20261019.md)

- [F12 settings menu: names, order, advanced developer tools](CONFIG-MENU-20261018.md)
- [Audio folders and consolidated package notices](AUDIO-LAYOUT-20261018.md)
- [Killcam rename, config migration and native replay/respawn acceptance](KILLCAM-RENAME-20261018.md)
- [Implemented log-audit recommendations](LOG-FOLLOWUP-20261018.md)
- [GPU2 log-follow-up regression and restoration](GPU-LOG-FOLLOWUP-20261018.md)
- [Bounded native-model fault diagnostics](NATIVE-MODEL-DIAGNOSTICS-20261018.md)
- [Representative GPU2 gameplay and merged-log audit](GPU-LOG-AUDIT-20261018.md)
- [ArenaMode, Redeploy and KillcamReplay log attribution](LOG-SOURCE-AUDIT-20261018.md)
- [PFT Tactical: own gear or a supplied PMC kit](PFT-TACTICAL-KITS-20261018.md)
- [Native PFT Tactical kit checks on GPU2](GPU-PFT-TACTICAL-KITS-20261018.md)
- [Arena-owned BigBrain library and Redeploy server boundary](BIGBRAIN-LIBRARY-20261018.md)
- [GPU2 powerup indicators, Akimbo lasers and mode-preview acceptance](GPU-FEEDBACK-20261018.md)
- [Automatic Akimbo aiming lasers](AKIMBO-LASER-20261018.md)
- [Bot Shield and Double Damage indicators](POWERUP-INDICATORS-20261018.md)
- [Same-bullet Collateral announcement](COLLATERAL-ANNOUNCEMENT-20261018.md)
- [Disabled Capture the Flag and Domination previews](MODE-PREVIEWS-20261018.md)
- [GPU2 map-feature tests and rendering follow-up](GPU-MAP-FEATURES-20261017.md)
- [All-map lighting and one-second deploy countdown](MAP-LIGHTING-20261017.md)
- [All-map pickup layouts and longer beams](ALL-MAP-PICKUPS-20261017.md)
- [Factory pickup connectivity independent of player entry](FACTORY-PICKUP-ANCHOR-20261018.md)
- [Visible Team DM bot spawn protection](TDM-BOT-SPAWN-SHIELD-20261017.md)
- [Unified build/deploy and maschine-ArenaMode folder](UNIFIED-DEPLOY-20261017.md) (partly superseded by AUDIO-LAYOUT-20261018 and ARENA-SESSION-DEPLOY-20261021; the layout is v6 now)

- [Test package layout of 2026-10-17 (historical v2) and optional BigBrain loading](package-layout-20261017.md)
- [First local test release ZIP (superseded layout)](TEST-RELEASE-20261017.md)
- [Player README and source-build instructions](../README.md)
- [Complete implemented feature inventory and mutator list](FEATURES.md)
- [Forge description — seven tabs, ready for the editor](FORGE-DESCRIPTION.md)
- [Licenses, asset sources and redistribution boundaries](../THIRD-PARTY-NOTICES.md)
- [Current handoff and test status](HANDOFF.md)
- [AirPit map admission and saved-map migration](AIRPIT-INTEGRATION-20261017.md)
- [Equator original spawn gates and preparation safety](EQUATOR-SPAWN-GATES-20261017.md)
- [Bot-profile batches built under a per-frame budget (ProfileBuildBudgetMs)](PROFILE-BUILD-BUDGET-20261017.md)
- [Zombie body culling and bot hot-path switches (four performance toggles)](ZOMBIE-BODY-CULLING-20261017.md)
- [Zombie performance levers, assessment after two control raids](ZOMBIE-PERFORMANCE-IDEAS-20261017.md)
- [AirPit and Equator GPU acceptance, deployment and recovery audit](GPU-AIRPIT-EQUATOR-20261017.md)
- [Realistic generated weapon-category pictures](WEAPON-RENDERS-20261016.md)
- [Four credited game-icons.net HUD symbols](UI-GAME-ICONS-20261016.md)
- [Original categorical UI artwork and validation](UI-ARTWORK-REPLACEMENT-20261016.md)
- [Native Factory verification of the new UI artwork](GPU-UI-ARTWORK-20261016.md)
- [Confirmed unused-code cleanup and final validation](DEAD-CODE-CLEANUP-20261016.md)
- [Earlier UI artwork research and native weapon-icon feasibility](UI-ASSET-REPLACEMENT-REVIEW-20261016.md)
- [Announcer assets and retained license notices](ASSET-MAINTENANCE-20261016.md)
- [Unused-code audit and cleanup candidates](DEAD-CODE-REVIEW-20261016.md)
- [Logging levels and developer diagnostics](LOGGING.md)
- [Latest player-session log review](LOG-REVIEW-20261015-EVENING.md)
- [Zombie opening countdown and protected gear refill](ZOMBIE-WAVE-REFILL-20261015.md)
- [Dedicated Molotov integration](MOLOTOV-NATIVE-INTEGRATION-20261015.md)
- [Additional Molotov supply](MOLOTOV-SUPPLY-20261015.md)
- [Native acceptance of the October 15 follow-ups](GPU-FOLLOWUPS-20261015.md)
- [Dependencies, optional mods and release boundaries](DEPENDENCIES.md)
- [Future work](FUTURE-WORK.md)
- [Variable protected PitFireTeam squads](PFT-SQUAD-SIZE-REVIEW.md)
- [Map/mode compatibility](MAP-MODE-COMPATIBILITY.md)
- [Repository cleanup and recovery archive](GIT-CLEANUP-20261011.md)
- [Gitleaks findings and local-backup precautions](GITLEAKS-20261011.md)

## Finding a record

- `*-REVIEW.md`: feature contracts, implementation rationale and test limitations.
- `GPU-*-TEST-INSTRUCTIONS*.md`: instructions for a specific test/build.
- `GPU-*-RESULTS*.md` and `GPU-*-TEST-RESULTS*.md`: dated native-test evidence.
- `*-DEPLOYMENT-*.json` and `*-BUILD.json`: historical artifact versions/hashes.
- `*-GATES.md`, `*-PLAN.md` and `GATES.md`: recorded acceptance/planning notes.

These are dated records, not automatic proof that the current build passed a
scenario. Check the cited source commit, installed artifact hashes and stated
limitations before carrying a result forward.

## Path convention

Actual Markdown links are relative to the document containing them. Commands,
source/asset paths and artifact paths in prose or code blocks remain relative to
the **repository root**, unless the document explicitly says otherwise. Run the
recorded commands from that root, not from `docs/`.

The documentation move did not rewrite historical absolute paths, artifact
contents, recorded hashes or original test results. References to removed
worktrees can be recovered using the archive described in the cleanup record.
New reports and review documents should be added here rather than to the root.

- [AirPit original team boxes](AIRPIT-TEAM-BOXES-REVIEW.md): MapPort 0.2.2, independent TDM, GPU1 acceptance.
