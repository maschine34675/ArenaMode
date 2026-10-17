# ArenaMode documentation

Working notes, feature reviews, GPU test instructions/results and historical
build/deployment JSON records live here. The automatically discovered
[agent instructions](../AGENTS.md) remain at the repository root.

## Start here

- [All-map lighting and one-second deploy countdown](MAP-LIGHTING-20261017.md)
- [All-map pickup layouts and longer beams](ALL-MAP-PICKUPS-20261017.md)
- [Visible Team DM bot spawn protection](TDM-BOT-SPAWN-SHIELD-20261017.md)
- [Unified build/deploy and maschine-ArenaMode folder](UNIFIED-DEPLOY-20261017.md)

- [Current test package layout and optional BigBrain loading](package-layout-20261017.md)
- [First local test release ZIP (superseded layout)](TEST-RELEASE-20261017.md)
- [Player README and source-build instructions](../README.md)
- [Complete implemented feature inventory and mutator list](FEATURES.md)
- [Forge description — six tabs, ready for the editor](FORGE-DESCRIPTION.md)
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
