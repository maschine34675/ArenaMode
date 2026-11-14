# VerificationRunnerChecks

Offline checks of `tools/Run-ArenaVerification.ps1` and its input model `tools/ArenaVerificationInputs.ps1`:
the result cache reuses a PASS only while every input of the step is unchanged.

```bash
pwsh -NoProfile -File tools/VerificationRunnerChecks/Verify.ps1                      # every case, then every fault
pwsh -NoProfile -File tools/VerificationRunnerChecks/Verify.ps1 -Case production,environment   # a fix loop
```

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
pass, and a surviving, a non-compiling and a wrongly killed mutant each fail the harness. The `parallel` case
runs three toys in two workers: the workers are siblings of the checkout, mirror an uncommitted edit (with an
old write time), an untracked file, a deletion and a staged file, give copied files a fresh write time, leave
the checkout under test untouched, cache their PASSes, and a runner started inside a worker stays
sequential. `parallel-guard` deploys from one worker while another step runs (and restores the file before
it ends): both fail and both are named.

The faults (`$faults` in `Verify.ps1`) remove one of those inputs from the copied model, drop the compile
items, the named paths or the untracked files from the selection, let the copied runner cache a FAIL, skip
the after-run check or ignore `-NoCache`, break the worker sync (copy, deletion with and without the
comparison, write time), let workers start workers, blame a deploy on one step only, or let the helper
accept survivors; each must fail its named assertion.
