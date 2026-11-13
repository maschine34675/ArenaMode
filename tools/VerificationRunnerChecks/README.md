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

The faults (`$faults` in `Verify.ps1`) remove one of those inputs from the copied model, or let the copied
runner cache a FAIL, skip the after-run check or ignore `-NoCache`; each must fail its named assertion.
