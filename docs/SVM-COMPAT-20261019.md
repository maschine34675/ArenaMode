# SVM protected raid-end compatibility

SVM 2.2.3 or newer is optional. The adapter does not change its preset, database
edits, Scav health, loot rules or ordinary raid result policy. No foreign source
or assembly is included in Arena's package.

## Verified boundary

Reviewed [SVM 2.2.3 release](https://github.com/GhostFenixx/svm-csharp/releases/tag/2.2.3),
source commit `5d8c5cfab332c98fbfd6d0300be4be5a4a19ecad`, and the published
`ServerValueModifier.dll`, SHA256
`7A142971404A4D84A20D855837EBED1E1E07921B05BA714C9F49B47BE7CFCADB`.
The published binary matches the relevant
[LocalRaidPatch source](https://github.com/GhostFenixx/svm-csharp/blob/5d8c5cfab332c98fbfd6d0300be4be5a4a19ecad/ServerValueModifier/HarmonyOverrides/LocalRaidOverrider.cs).

Its metadata GUID is `fika.ghostfenixx.svm`; metadata and assembly versions must
agree. The exact static Boolean `LocalRaidPatch.Prefix(ref ValueTask<string>,
MongoId, EndLocalRaidRequestData, CancellationToken)` is registered under
Harmony owner `LocalRaidPatch` on `MatchCallbacks.EndLocalRaidAsync`. SVM loads
at Preload+5, before Arena's Preload+60 manager. Native SPT 4.1.6 confirms the
callback → MatchController → LocationLifecycleService end chain. The first two
are thin asynchronous wrappers; Arena's existing protection replaces the last.

SVM's SaveLoot preset returns an immediate null response. Its Ignore result
mapping rewrites the end result to TRANSIT and returns the same response. Either
would otherwise keep the protected snapshot/end transaction from completing.

## Exact protected exception

The adapter captures only a synchronous invocation of the real end callback.
Its lease contains the physical callback instance, physical request, route and
session. Only `/client/match/local/end`, the exact request object and session may
skip SVM's prefix. Nested invocations restore their previous lease; the finalizer
releases it even on exceptions. No async task inherits this thread-local lease.

Within that lease, `ArenaSessionService.ClassifyLocalRaidEnd` is authoritative.
First ends, finalizing/completed retries and rejected protected requests bypass
SVM so Arena's existing end handler can respectively restore, retry, acknowledge
or reject. A stale/forged request is never converted into success. A tagged end
after a server restart still reaches the existing rejection; no identity is
invented and no token-recovery route changes. The existing exact ordinary-end
execution exception retains precedence.

Skipping the *foreign prefix* sets its Boolean result to true, allowing the
native callback chain. It does not assign the native ValueTask, mutate the
request result, restore a profile itself or call the protected end service.

## Ordinary lifecycle and HarmonyX

Arena already tracks ordinary starts/ends to prevent protected-boundary races.
Letting SVM return before that wrapper would strand an ordinary generation.
Calling MatchController directly would bypass SVM's intended result decisions.

For a validated SVM topology only, Arena's wrapper owns the transaction first.
It invokes the real callback once inside its existing lifecycle lease. A
one-use permit requires the exact callback instance, request object, route,
session and authoritative server ID. SVM then runs normally inside that call.
Its actual returned string closes the ordinary generation and is retained for
identical completed retries. Wrong nested requests fail instead of self-awaiting.
Overlapping callers share the transaction; caller cancellation only cancels that
caller's wait. The execution lease is revoked even in inherited async contexts.

HarmonyX executes later prefixes even when an earlier prefix returned false.
Therefore the outer Arena wrapper explicitly marks only its exact callback
lease as owned. The SVM adapter defers the outer foreign prefix without replacing
the pending ValueTask. The inner callback has a fresh unmarked lease and runs
SVM once. This marker is unrelated to general foreign `__runOriginal=false`
decisions and does not suppress ordinary SVM globally.

Only Arena's own wrapper prefix is reordered, after successful validation.
The effective sorted order must be capture → Arena wrapper → SVM. Both startup
and every Arena arm recheck the actual receipts and sorted order. Setup rollback
restores Arena's prior ordering. No foreign patch is removed or reordered.

Without SVM, or if its startup contract is unsupported, the callback bridge stays
off and Arena retains its original direct-controller ordinary-end path/order.
Unsupported SVM blocks Arena admission only; this is not a certification of
unreviewed SVM settings or a repair of their preexisting ordinary interactions.
Unknown outer callback hooks prevent bridge installation, avoiding duplicate
execution of unreviewed callbacks. Topology is also checked before each new
ordinary end, even without an Arena admission poll. Later drift revokes future
bridges, restores only Arena's original wrapper order, warns and blocks new Arena
arms until restart. The already captured transaction/response keeps its frozen
bridge decision. A read-only exact generation/tombstone lookup premarks those
pending/completed retries before SVM, even after order restoration; the real
inner callback is explicitly excluded. Active generations still outrank old
same-second tombstones. Existing guards remain installed for protected ends.

## Contract and verification

Newer versions warn once, with no upper cap, but still require metadata/assembly
identity, exact native/foreign signatures, one known SVM end prefix, no extra SVM
downstream end hooks, no unknown outer hook topology, own guard receipts and
effective ordering. Prefixes, postfixes, transpilers, finalizers and HarmonyX IL
manipulators are considered. Technical successful setup is Debug; failed
protection/admission and unreviewed newer versions remain Warning.

Run `pwsh -NoProfile -File tools/SvmCompatChecks/Verify.ps1`. The harness uses the
separately downloaded published DLL, installed SPT/Harmony assemblies, real
native callback/controller dispatch, production Arena session/snapshot/end
patches and the ordinary lifecycle service. Only foreign preset IO, native
profile IO and final persistence are controlled fixtures. It does not install
mods, start EFT, contact a server or change saved configuration. Mutants compile
isolated copies of the two production files into a complete server assembly;
every negative must compile and fail the named behavioral assertion.

Coverage includes SaveLoot/Ignore, ordinary PMC/Scav, restored-entry persistence,
server-ID forgery, stale/untagged requests, completed and failed finalization
retries, direct/other-route/other-session calls, physical request/instance scope,
nested cleanup, ordinary subsequent starts, same-second identity reuse, exact
response replay, faults, overlapping retries, caller cancellation and inherited
async lease revocation. Separate fresh-process controls verify absent and
unsupported SVM do not enable the bridge or change wrapper ordering.

Native GPU end-to-end testing with an actual SVM preset remains separate
evidence; offline callback execution alone does not prove the complete
client/server raid experience.

Baseline native-harness result: **124 assertions**, plus **8 absent-SVM** and
**8 unsupported-topology** fresh-process assertions. On 2026-10-20 all **14
compiled negative controls** failed at their intended behavioral assertion, and
the repeated clean baseline passed all **124 assertions**. Final marker:
`SVM_COMPAT_CHECKS_OK`. Evidence:
`artifacts/svm-compat-checks/cb094e381af849c3abf79c7ea6c24c91`.
Independent root repetition passed the same baseline, both fallback controls and
all 14 compiled negative controls:
`artifacts/svm-compat-checks/24a78492b8d24f84a864a9e63c7c73cb`.

The reproducible script prints its final evidence directory only after every
named control and the repeated clean baseline pass. Test data and published DLL
remain ignored under `artifacts/`; neither is a release asset. No deployment or
native GPU acceptance was performed by this verification.

## Native followup — 2026-10-20

The [Factory results](GPU-OPTIONAL-COMPAT-20261020.md), Phase 1, exercise the
published SVM 2.2.3 with ArenaMode `235cc69`. Ordinary SaveLoot and Ignore ends,
a disabled-override control and an ordinary Scav end completed. Two Arena
intervals, including SaveLoot/Ignore cases, restored the protected entry snapshot;
the following ordinary raids worked without restarting the server. Native
movement and StatTrack observations provide additional lifecycle controls.

The preexisting 2.2.1 installation was an admission-negative control, not a
supported legacy version. The user chose to remove that installation rather
than add legacy support; no legacy adapter was retained and the minimum remains
2.2.3. Final removal belongs to the combined cleanup record and is not inferred
from these tests. No preset is bundled or installed by Arena.

These checks do not certify every SVM database option, future release, retry
race or foreign callback combination. The report retains test-path failures and
unattributed log messages rather than treating them as a clean-log guarantee.
