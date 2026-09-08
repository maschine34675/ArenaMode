# GPU retest follow-up evidence

`GATES.md` records parent integration/review/delivery, `preparation.md` and
`awareness.md` record the parent's independent child re-verification. All ten
gates were met before the documentation commit. Native GPU acceptance is an
explicit test handoff in HANDOFF.md, not an offline success claim.

The scripts default to this development installation at `D:\SPT41`. Read all
CHECK scripts before running. With the local Unlazy checker, parse these files
with `--status` first. Run the two child ledgers with explicit repository `--cwd`
and `--approve --reverify --jobs 3`; wait for both to finish before running the
parent ledger the same way. Do not run child preparation and parent integration
concurrently: both execute the existing TDM runtime project and share its outputs.
Manual gates require direct review of the recorded evidence and current artifacts.

Build-Staged compiles only into a fresh ignored directory and hash-checks the
installed client/server/Redeploy before and after. It does not deploy. The final
local client delivery was a separate verified atomic replacement from clean
`4a979cf`, with backup and manifest recorded in HANDOFF.md. Re-running a build is
not proof of delivery or GPU mirror completion.
