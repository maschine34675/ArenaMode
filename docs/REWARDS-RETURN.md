# Automatic rewards on return — 2026-10-25

Development change after 0.2.0. **Current local entry/return is extended by
[offline Arena sessions](ARENA-OFFLINE-SESSION-20261025.md): the ordinary server
now saves and stops before copying the Arena profile, stays stopped during play,
and starts on return.** The save/drain safeguards below are reused at entry.
The flow and native evidence below describe the preceding implementation, retained
as the schema 1/2 compatibility path when no offline entry capability is issued:

1. Start an Arena session from normal SPT; rewards default ON for eligible profiles.
2. Play and leave Arena normally. Existing result validation/restoration rules apply.
3. Select **RETURN TO NORMAL SESSION**. The helper closes the private game/server,
   transfers immutable signed receipts and asks the normal server to finish saving.
4. With pending enabled rewards or initial enrollment, the normal server shuts down
   gracefully and restarts. Its existing startup consumer pays before native profile
   loading. EFT opens only after the replacement server is ready.
5. RUB is collectible system mail; PMC XP is already credited and listed in that mail.

There is no manual restart in this path. No pending work or rewards OFF means no
extra server restart. Direct Arena play in an ordinary session, manual game exits
and watchdog cleanup still defer payment to the next normal server start. None of
these paths copies a private profile back.

## Authority and order

Normal entry preparation issues a distinct random return capability into
`user/arena-rewards/<profileId>/return.json`. It is separate from consent authority,
kept in server memory and bound to profile, entry control ID, runtime, process ID,
process start time, arguments, loopback HTTPS origin, certificate pin and expiry.
The helper copies it as `arena-rewards-return.json` only for the matching private
marker. Client status contains a boolean availability indicator, never the token.
The new route bypasses SPT's generic request-body logging.

Only an explicit return invokes the capability. Failed-entry recovery, the watcher
and stop-server never restart the normal server. The helper first positively stops
the private writer and transfers receipts, then verifies the actual normal executable,
PID/start-time and listening PID. The server independently authenticates the exact
installed helper and refuses another EFT/server process or an active profile raid.

The native `HttpServer.HandleRequestAsync` task lifetime covers requests, native
WebSockets and subsequent web middleware. During an authorized return, new work is
blocked and existing work drains. At native update-service startup, the adapter
resolves the actual registered IOnUpdate implementations and tracks their returned
tasks. Their native component identity and update timing remain intact. During
return it pauses new callbacks and awaits running callbacks without cancelling
their tokens. Thus stopping the update loop cannot interrupt an in-flight native save.
A preflight native save/readback catches a previously
poisoned native save hash before irreversibly stopping updates. The exact reviewed
`SPTStartupHostedService.StopAsync` cancels its update loop; its ExecuteTask must
actually complete. Final native SaveAsync plus semantic comparison of every loaded
profile against disk verifies the final state. Only then does the reply schedule
`IHostApplicationLifetime.StopApplication` after HTTP completion.

The preflight and final native saves also receive no cancellation token: once a
write begins, its actual completion is awaited with both gates held. A deadline
is checked before and afterwards. A retry timing out while waiting behind that
save still reports the held state, so the helper cannot reopen EFT against a
server whose save is unfinished.

The helper waits for the exact old process to exit, checks the port is free and starts
the original normal executable/arguments/working directory. It retains a replacement
process across readiness retries instead of starting duplicates. Existing startup
locks, receipt signatures, approval binding, XP/RUB journal and permanent payment
history remain the only payout path.

## Refusal and recovery

- This adapter reviews Windows/native SPT 4.1.6 with a strictly loopback configured
  listener. Remote/wildcard hosting, mismatched process identity, missing/expired
  capability or unsupported native contracts retain the old queued-payment path.
- Native ASP.NET web hosting and cookie-authentication key-ring warmup are reviewed
  alongside SPT's update service; the key-ring service has no shutdown work or player
  profile access. Additional unreviewed hosted services refuse restart before quiescence. The normal
  server remains usable. Arbitrary detached mod threads that write profiles outside
  native lifecycle are outside the same cooperative model as startup settlement.
- Before update cancellation, a failed drain/save/admission reopens ordinary request
  processing. A real refused automatic payment is reported instead of claiming payout.
- After cancellation, failure keeps the server alive with its RAM and request gate.
  A visible helper Retry/Cancel dialog permits the same authenticated operation to
  retry; it never calls native StartAsync (which would rerun initialization), kills
  the normal server, resets save hashes or repairs a profile. Cancel keeps the process
  and receipts for investigation. Read the normal server log before closing it.
- An already reserved exact operation can finish across its grant expiry; new
  operations cannot. PID/start-time/helper/token checks remain mandatory on retries.
- Old/new server timeouts, busy ports and failed starts also show a retry/cancel
  dialog. A still-running replacement is reused. Mail collection never repeats XP.

The request gate is inert outside this authenticated transition. No normal PMC/Scav
rule, reward amount, OFF preference or profile-restoration boundary was changed.

## Mixed Windows integrity levels (2026-10-28)

Native testing reproduced an immediate `helper-identity-mismatch` when normal SPT
ran at Medium integrity and the helper ran elevated, under the same Windows user
and session. The server refused before quiescence or profile copying. A Medium
diagnostic process could read the helper's limited process information, while the
older managed process getters failed with access denied. That diagnostic used
.NET Framework; the server's first failing individual getter was not logged.

The server now opens one process handle with limited query and synchronization
rights. On that same handle it checks exact creation time, exact installed image
path and a nonsignaled live process before and after the read. It requests no
process-memory access, adjusts no privileges and has no permissive fallback.
Self-PID rejection, local-path/reparse validation and capability authentication
are unchanged. A terminated process cannot pass merely by using exit code 259.

The reverse combination also reproduced a refusal: a Medium helper could not
inspect an elevated normal server. The shipping .NET Framework `HasExited`
implementation requests `QUERY_INFORMATION | SYNCHRONIZE`. All twelve helper
liveness sites now use the limited-query equivalent, retaining explicit grant
start-time, image and listener comparisons. `TryOpen` distinguishes a missing or
positively exited process from an unreadable process: access/query failures
propagate and cannot authorize deleting ownership evidence or copying a profile.
The native wait itself already requires only synchronization rights and remains
unchanged. Eleven shipping-framework checks cover the picker/shortcut contracts
plus live, missing and exit-code-259 process observations.

The native APIs document these rights: [image path](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamea),
[creation time](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes)
and [process wait](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-waitforsingleobject).
See the [two-host native report](DIRECT-START-NATIVE-20261028.md) for actual token
combinations, final build identity and entry/return outcomes. This reproduction
does not retrospectively prove the cause of the earlier uninstrumented errors.

## Verification

Focused synthetic tests cover capability tampering/expiry/replay, separate secrets,
private marker binding, request draining, native-loop completion, failed saves and
readback mismatches, refusal/resume, held retry, no-work decisions and helper restart
ordering without duplicate replacement processes. Native acceptance and earlier
failed attempts are recorded in [the GPU report](GPU-REWARD-RETURN-20261025.md).
The accepted native test returned two genuine matches through the button alone:
two collectible mails totaling 22,000 RUB and 2,200 PMC XP were present when normal
EFT opened. A second return without a match kept the normal server process and
added no payment or XP. Complete profile comparisons preserved all unrelated
progress, and all 126 saved configuration hashes remained unchanged.
The complete offline run passed
2,960 unit tests (one known APBS environment skip), including 98 return-specific
cases, and the four menu/receipt/control/net472-helper regression checks.

An additional disposable-process probe used the installed SPT 4.1.6 and Harmony
assemblies. It compiled all eight native callback-entry patches and exercised an
actual patched controllable callback through drain, skip and resume, plus native
HTTP rejection. It did not start a server or read/write profiles. This check caught
the initial async-state-machine emitter incompatibility that synthetic tests alone
could not detect; the shipping adapter does not rewrite that state machine.
