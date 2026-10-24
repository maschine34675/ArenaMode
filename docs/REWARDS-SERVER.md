# Ordinary-server reward startup integration (2026-10-24)

The ordinary server now has a guarded consumer for **already approved** reward
receipts. The user accepted payout at the next real server start for this first
version. Returning from the private Arena session while the ordinary server keeps
running does not trigger payment. No profile reload is forced to simulate a start.

This is still not a player-facing rewards feature: automatic opt-in/enrollment,
authoritative kill/win accounting, rates, transfer from the private session and
approval production are not implemented. No existing player profile was enrolled,
no ready receipt was created in an installation, and nothing was deployed during
this work. All executable profile tests use generated files.

## Native boundary

`ArenaRewardStartup` is an `IOnLoad` component at `OnLoadOrder.Watermark + 1`.
The installed SPT 4.1.6 Program awaits these pre-load callbacks before `app.RunAsync`;
DI orders them by priority. The native Watermark at priority 0 only prints startup
information. Normal profile loading, HTTP handling and native update/autosave work
have not started at this seam. The new consumer writes the approved disk document;
SPT later loads and migrates that file through its **unchanged** normal pipeline.
It never swaps a cached/live `SptProfile`, bypasses a migration or clears save hashes.

Installed evidence was checked independently of the reference source:

- `SPT.Server` 4.1.6: MVID `8c0a9968-e950-422f-b4f7-66cef149a618`;
  awaited `RunPreSptLoadCallbacks` precedes the port check and `app.RunAsync`.
- Core 4.1.6: MVID `94603c02-ae83-4a1b-8915-f5cd1598fe7d`.
  The installed hosted service awaits later load callbacks in `StartAsync`;
  the older local source incorrectly placed them in `ExecuteAsync`.
- DI 4.1.6: MVID `3d409546-b961-466b-81b0-6343511b2f4f`;
  component registration is ordered by `TypePriority`.

The consumer therefore admits only the reviewed Core assembly version 4.1.6.0.
Other SPT versions defer rewards; Arena and ordinary raids remain available.

## Admission and concurrency

Payment requires all these checks together:

- The entry assembly is the native SPT server; its executable, working directory
  and application directory belong to the same unlinked runtime root.
- No private `arena-session.json` marker exists. A directory marker, access error
  or unreadable origin is not evidence of an ordinary server.
- No native profiles are loaded, and no unexpected earlier/equal-priority load
  component precedes the consumer. The profile count is checked again on lease
  operations; closing the startup window invalidates further use.
- No other recognizable SPT server process is running. V1 conservatively defers
  even for another installation: its executable path does not prove its working
  directory or profile root. Unknown process identity also refuses payment.
- Enrollment binds the exact canonical ordinary runtime path, installation ID,
  native profile ID and epoch. Copying the profile or deleting a private marker
  cannot reconstruct this separate authority.
- Explicit enabled enrollment and a complete approved receipt exist. The initial
  profile must match the approved clean serialized-file hash exactly, be initialized
  with PMC/scav Info and PMC inventory, not marked wipe/invalid, and have native
  persisted `inraid` fields set to `none`. The adapter never resets these fields.

Two process locks have different purposes. `.startup.lock` covers the short
consumer callback and is acquired **before** peer checks or reward refusal. Another
participating startup waits until the current settlement ends before it may
continue toward native profile loading. Cancellation propagates; if coordination
is still unavailable after two minutes, this startup aborts instead of loading
profiles concurrently. A path/permission failure at this coordination boundary
also aborts startup. This exceptional behavior protects integrity; it is not the
ordinary response to an invalid receipt.

`.server.lock` remains open for the admitted server's lifetime and excludes a
second cooperating payout owner. Receipt refusal, malformed profile metadata or
unsupported SPT version otherwise disables only payment, and normal startup
continues. Process probing is necessary because SPT's own port check occurs after
the pre-load callbacks and would be too late to protect payment.

These checks establish the **regular native startup boundary**, not a universal
lock against arbitrary code. Mod DI constructors run before load callbacks and
could start their own threads or write profile files directly. Such writers,
editors, deliberately renamed external processes, nonparticipating older server
versions racing a new startup, and changes to authority files outside the same
coordination protocol are unsupported. No blanket foreign-mod block was added.
The future trusted coordinator must only approve the supported cooperative
startup model. Runtime payout for arbitrary active mod lists is not enabled.

## Durable authority and payment

Each enrolled profile has a directory under
`SPT_Runtime/user/arena-rewards/<profileId>/`. This location is outside the session
helper's disposable tree copy plan. The consumer never creates this directory,
re-enrolls a missing profile or reads a client-selected profile path.

| File | Meaning |
|---|---|
| `enrollment.json` | Schema, explicit opt-in, absolute ordinary runtime path and installation/profile/epoch binding. |
| `approved.json` | Schema, required `RestoredAndApproved` state, one aggregate receipt and the trusted clean profile hash. |
| `journal.json` | Existing schema-1 permanent kernel journal. Missing is not treated as empty. |
| `acknowledged.json` | Receipt key, fingerprint and message ID, written only after the kernel confirms durable payment. |

These are internal server-owned protocol files, **not an import format or setup
instructions for players**. A future producer must create approval only from the
ordinary server's authoritative clean state and verified results. An empty RAM
snapshot service or an incoming client's assertion is not restoration proof.
This change deliberately supplies no producer that could accidentally do that.

The first attempt requires exact equality with `CleanProfileRevision`. On a
retry after a successful write, the profile necessarily has another hash. That
exception is accepted only when the journal and profile already contain this
same payment, and the journal's original revision matches the approval. Such a
lease is technically **confirmation-only**: any profile write throws. The kernel
then performs its normal full-history/recovery checks. A previously committed
receipt remains paid after collection/deletion of its mail. An incomplete journal
commit still requires the exact original uncollected mail.

Profile and journal writes use staged sibling files, flush and atomic replacement,
with the kernel's independent read-back. All paths are root-derived and checked
against traversal/reparse points. A failed acknowledgement does not roll back a
payment: the next start confirms the same receipt and writes its acknowledgement.
No notification is sent before commit; the normal client later sees persisted
mail when it logs in. The kernel's joint-backup-rollback limitation still applies.

## Verification and next scope

`RewardStartupTests` uses generated runtime/profile trees to exercise enabled and
disabled enrollment, private roots/markers, stale approval, wipe/inraid states,
missing/malformed authority, confirmation-only recovery, locked acknowledgements,
startup/lifetime coordination, process admission and linked paths.

`RewardNativeLoadTests` invokes real `SaveServer.LoadProfileAsync`, native profile
migration, all four built-in save-load routers, native saving and fresh loading.
The synthetic migration deliberately changes unrelated fields, proving the paid
file is not later replaced by a stale unmigrated clone. Its exclusive test
collection restores process CWD and native JSON serializer settings afterward.
These methods are from the referenced SPT package; installed 4.1.6 startup ordering
is separately established by the IL review above. No live server/game ran.

Final verification on 2026-10-24: **45/45 new startup/native-load tests passed**,
**138/138 reward tests passed**, and the complete unit suite passed **2,464 tests**
with zero failures and one existing environment-gated APBS test skipped. The
unified runner's installed-artifact hash checks passed. Local TRX evidence is
`artifacts/run-arena-verification/run-arena-verification.trx` (ignored).

Independent review identified and closed two concurrency/identity gaps before
completion: a peer refusing rewards must still wait for another startup's commit,
and a peer executable's directory does not establish its profile working directory.
The short startup gate and conservative recognized-server refusal cover these
cases; they do not broaden the cooperative-writer guarantee above.

A negative native test explicitly distinguishes durable mail from successful
native login: removing the synthetic RUB template can make later native validation
reject the profile. The adapter does not claim client collection worked or repair
that foreign/native failure. Future rollout still needs a copied-profile startup
and mailbox collection test on the actual supported installation.

Next is a trusted enrollment/approval producer, authoritative outcome accounting
and the private-session receipt transport. That stage must coordinate with the
normal profile restoration and freezing of the native currency policy. It must
not turn `approved.json` into a client-writable amount or use live reload to bypass
the startup-only constraint.
