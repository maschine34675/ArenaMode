# Barbed-wire death notice checks

Run `pwsh -NoProfile -File tools/BarbedDeathChecks/Verify.ps1` from the repository.
No client build, deployment, game launch or profile access occurs.

The executable links the actual capture patch, presentation DTO and administrative
death lease. The HUD's `ShowUncreditedDeath` method is extracted verbatim for each run;
only its settings, feed and match boundaries are doubled. Checks exercise ordinary
raids and non-barbed damage, physical-body dedupe across same-profile respawns,
stale match/session rejection, completed-before versus completed-during deaths,
administrative deaths, existing aggressors, HUD switches and contained failures.
The DTO and minimal compile surface cannot publish an Arena kill, assist or award.
Two compiled negative controls remove physical-body dedupe and the aggressor guard;
both must fail their specific runtime assertion, not compilation.

Native source-contract checks verify the protected `IPlayer LastAggressor` field,
null attacker produced by `ProceedBarb`, clearing before HP loss, and the skipped
credited-kill callback without an aggressor. The hook targets the actual method name
and injects a field with the native name/type into a native-shaped player double.
A separate process also attempts the real installed HarmonyX patch on that double.
Its status is explicitly recorded: success verifies desktop-CLR injection only;
the observed desktop `AccessTools` static-initializer failure inside
`MethodInvoker.GetHandler` is recorded as unavailable. Only that exact startup
failure is allowed; other binding/runtime errors fail verification. The normal
behavior harness uses a narrow reflection lookup double for `AccessTools`.
An unavailable probe is never counted as a passing native hook test.

Evidence, source hashes, logs and modified negative-control sources are written
below ignored `artifacts/barbed-death-checks/<run>/`. These are offline boundary
checks, not proof of Unity/Mono dispatch, native wire damage, rendering or a raid.
