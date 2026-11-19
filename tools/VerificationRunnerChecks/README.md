# VerificationRunnerChecks

Offline checks of `tools/Run-ArenaVerification.ps1` and its input model `tools/ArenaVerificationInputs.ps1`:
the result cache reuses a PASS only while every input of the step is unchanged.

```bash
pwsh -NoProfile -File tools/VerificationRunnerChecks/Verify.ps1                      # every case and every fault, side by side
pwsh -NoProfile -File tools/VerificationRunnerChecks/Verify.ps1 -Case production,environment   # a fix loop
```

The full run starts the baseline (`-Baseline`: every case, in order, in one sandbox) and every fault
(`-Fault <name>`: the two priming cases and the fault's own cases in a sandbox of its own) as child processes;
`ARENA_MUTANT_PARALLEL` faults run beside the baseline (default 4; `Run-ArenaVerification.ps1` sets it for the
steps of its workers). The sandboxes share nothing (checkout, cache, counter file, and the worker checkouts of
the parallel cases, which are siblings inside each sandbox); only the toolchain (dotnet, MSBuild nodes, the NuGet
cache, `%TEMP%`) is common. A fault proves something only against a green baseline: a red baseline fails the
run whatever the faults did, every fault must report exactly once, and every fault that was not killed by its
named assertion is listed, not only the first. Where a named assertion would also trip on an unrelated failure
(a toy build or worker sync that breaks under load fails "channel faults pass their harness" just as well), the
fault carries its own symptom as `Evidence`, which must appear in its output too. The cases are deliberately not split across sandboxes: they
run one after another on the cache state the earlier ones left, and in a fresh sandbox an assertion such as
"reruns the step" could pass merely because that step had never run there.

Each case runs the real runner, copied with its helpers into a throwaway git checkout under `%TEMP%` that has
a toy production tree, toy harness folders, a toy installation (`ARENA_VERIFICATION_INSTALL_ROOTS`), toy
Redeploy/_decompiled/_reference siblings and a private cache (`ARENA_VERIFICATION_CACHE`). A toy harness
appends its step name to a counter file whenever it really runs, so a case counts runs instead of trusting the
verdict text. Nothing outside the temp directory is written; `ARENA_RUNNER_CHECKS_KEEP=1` keeps the sandbox
for inspection and `ARENA_RUNNER_CHECKS_DEBUG=1` prints every runner output.

What the cases pin, in short: unchanged inputs are cached (the verdict says since when, the log is the cached
log, the summary counts `cached=`); an unnamed docs record, the docs index, an unreached harness folder, a
loose entry script and the runtime state mods write into their installed folders keep the cache; a production
file, an untracked file, tracking a file, a referenced harness folder, a named docs record, a licence file, a
shared helper, an environment variable, the installed game assemblies, a new plugin assembly, the SPT database,
installed config for a step that names it, the Redeploy sibling, the decompiled sources and, for a step that
calls git, a commit rerun the step; a FAIL is never cached; `-NoCache` runs the step; a stage is a step of its
own; a step whose inputs change while it runs is not cached.

The `select` case pins `-ChangedSince`: a production file linked by a project glob selects that harness (the
evaluated MSBuild items, not a literal), a production path named in a script selects its harness, unrelated
harnesses are not selected, committed and untracked changes select like uncommitted ones, an unregistered
harness folder is reported, `-ListOnly` runs nothing, and without it the selection runs.

From the adversarial review of 2026-11-14: `select` also pins that a renamed file selects by its old path, that
a path built from quoted segments (`"ArenaMode.Client", "Answer.cs"`) selects, and that an unregistered loose
script is reported; `wide-helper` that a step reaching an enumerating loose helper reruns on any change;
`artifact-input` that a registered fixture under `artifacts/` is keyed; `env-path` that a folder named by a
variable is keyed by content; `parallel-restore` that a `--no-restore` step passes in a fresh worker;
`worker-guard` that a runner started inside a worker runs sequentially and keeps the worker's uncommitted files.

The `helper` case runs a toy harness through the real `Invoke-ArenaSourceChecks.ps1`: its killed mutants
pass, and a surviving, a non-compiling and a wrongly killed mutant each fail the harness. `helper-channels` runs a second toy
through the helper's other channels (a per-mutant environment variable, chained edits, a `Prepare` block
with its own copy name, failure prefix, marker, whitespace collapse, program arguments, a raw failure text): its
faults pass, and an anchor with two sites, a kill without the marker, a non-compiling mutant whose compiler
error contains the raw failure text and a `Prepare` block that returns more than `-p:` properties each fail the
harness; a per-mutant variable set process-wide fails it too, through the final run that would inherit it. The `parallel` case
runs three toys in two workers: the workers are siblings of the checkout, mirror an uncommitted edit (with an
old write time), an untracked file, a deletion and a staged file, give copied files a fresh write time, leave
the checkout under test untouched, cache their PASSes, and a runner started inside a worker stays
sequential. `parallel-guard` deploys from one worker while another step runs (and restores the file before
it ends): both fail and both are named.

`parallel-redeploy` (2026-11-19) builds a toy project of the sibling Redeploy checkout from two workers at the
same time, with `--no-restore`, after the same project was built in place (so its own obj/ holds generated
sources): each worker builds it into its own `artifacts\redeploy-build`, the shared obj/ and bin/ stay
untouched, the two builds overlap, and a second run finds the worker's build current (the sync keeps that
folder). `parallel-touch` lets a step change a file in its worker and makes the next step take that worker:
the changed step is not cached, the worker is synced again before the next step, and that step sees the
checkout under test. Both checks after a worker step run in the step's thread job. The sandbox checkout has
`core.autocrlf=false`: with the machine's `true`, a worker would check out CRLF where the sandbox holds LF, and
the sync would copy such a file with a fresh write time on every run (which rebuilds everything importing it).

The faults (`$faults` in `Verify.ps1`) remove one of those inputs from the copied model, drop the compile
items, the named paths or the untracked files from the selection, let the copied runner cache a FAIL, skip
the after-run check (in the runner's thread or in a worker step's job) or ignore `-NoCache`, break the worker
sync (copy, deletion with and without the comparison, write time), skip the drift check of a worker, let
workers start workers, blame a deploy on one step only, drop the Redeploy props from a worker step's
environment, drop its exclusion of the in-place obj/ and bin/, restore without the worker's environment, wipe
the worker's Redeploy build on every sync, or let the helper accept survivors; each must fail its named
assertion.
