# ArenaMode documentation

Working notes, feature reviews, GPU test instructions/results and historical
build/deployment JSON records live here. The automatically discovered
[agent instructions](../AGENTS.md) remain at the repository root.

## Start here

- [Current handoff and test status](HANDOFF.md)
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
