# Arena entry restart hardening — 2026-10-28

## Report and diagnosis

The reported helper accepted its request, observed EFT exit after 3.6 seconds,
then received an explicit server rejection after approximately 45 seconds.
This matches the server's bounded drain/save operation. The old helper discarded
the rejection details and did not reopen normal EFT for schema 3.

That log alone does not identify the pending server stage. A request/WebSocket
drain, update drain or preflight save are possible; neither an antivirus block
nor a damaged profile is established by this report.

The client also used Unity/Mono `Process.Start` for the helper. Unity's Mono
implementation can inherit handles even with no redirected streams. Clearing
INHERIT flags later in the helper does not release already inherited references.
An inherited connection can therefore outlive EFT. This is a concrete lifetime
hazard, but is not proven to be the cause on the reporting user's machine.
Reference: [Unity Mono's Windows process launcher](https://github.com/Unity-Technologies/mono/blob/unity-main/mono/metadata/w32process-win32.c).
The exact Mono binary revision shipped in EFT was not independently disassembled.

## Change

- Start only the session helper through `CreateProcessW`, with explicit executable,
  existing Windows argument quoting, no inherited handles and no console window.
  Close both native process/thread handles. Request acknowledgement is still
  mandatory before EFT quits; launch failure keeps EFT open.
- Report the failed drain/save stage, allowlisted cause and active request/update
  counts in the server warning and, for explicit pre-stop refusals, the helper log. Do not echo arbitrary wire text,
  exception messages or capabilities.
- A pre-stop rejection may carry `canResumeNormal=true` only after both owned
  gates have resumed and the server lifetime is still running. Scoped holds
  suppress this permission. The listener grants it only for entry.
- The helper additionally requires typed `accepted=false`, `held=false` and
  `reason=return-not-ready`. It checks the original PID, start time, executable,
  sole local listener and absence of an active/Ready lease twice. It releases
  its startup lock and starts EFT with normal arguments only if no EFT is running.
  It does not copy profiles or start/stop a server on this recovery path.
- A recovered normal game needs no desktop acknowledgement. If relaunch also
  fails, show one short notice with the helper log path only after releasing the
  helper's session mutex/startup lock. The normal game must survive its initial
  start check to suppress that notice. The existing save Retry/Cancel dialog is
  shorter and brought to the foreground. Missing, lost, malformed or held replies
  retain the existing
  Ready-plus-process-exit protection; they never authorize this recovery.

Old server replies remain accepted by the parser but cannot authorize the new
return path. New fields are additive. Ship client, helper and server together.

## Verification

`tools/Verify-EntryRestartHardening.ps1` runs unit tests, linked client failure
checks, client mutation checks, .NET Framework helper checks and a Release build
with deployment disabled. `EntryRestartHardeningTests` connects the actual
server serialization to the helper parser and covers both drain deadlines,
scoped holds, final-save failure, malformed replies and log sanitization.

`tools/ArenaHelperLaunchChecks/Verify.ps1` exercises the production launcher on
.NET Framework with a live child and an explicitly inheritable pipe. EOF must
arrive while the child is alive. Mutating handle inheritance to true must fail
that exact check while argument fidelity still passes. Unicode, spaces, quotes,
trailing backslashes and missing executables are covered.

The optional `drain-probe <wss-loopback-url> <server.crt> <output>` command in
that fixture holds an ordinary notifier socket, pins its certificate, consumes
notifications without sending game data, and closes when `<output>.stop` exists
(six-minute maximum). Early server closure is a test failure. It is a temporary
test process, not a shipped helper feature.

Offline results: **3,323 passed, zero failed, four environment skips**. The linked
client harness passed 750 assertions and rejected 174 mutations; the focused
offline-entry harness rejected all three ambiguity/downgrade mutations. The
Framework helper passed nine checks and rejected all six mutations. The launcher
passed its three process checks and rejected handle inheritance. Release solution
build passed without deployment; subsequent installation is separate evidence.

## Native acceptance (GPU1, final helper)

All times below are UTC on 2026-10-28. A temporary user policy excluded only
LowAmmoCue from the private session so the real character-selection restart button
was offered. The test used the existing GPU1 profile; no profile was replaced,
edited or deleted by the test.

- At 07:27:46, the real button launched the final helper and EFT exited after
  3.4 seconds. A separate pinned notifier socket remained open. At 07:28:36,
  server/helper reported `http-drain/timeout`, one active request, zero active
  updates, `held=false`, and explicit normal-resume permission.
- Normal server PID **9512**, created at 07:08:41, stayed unchanged. EFT restarted
  as PID **8628** on port **6969** and reached MainMenu. The helper exited,
  no desktop acknowledgement remained, and no active private lease was created.
  The socket was closed explicitly at 07:30:08, after this result.
- The same restart button was used again with no held socket. The ordinary server
  saved and stopped at 07:30:39; only then was the private tree created. Private
  server PID **4056** and EFT PID **9260** reached MainMenu on port **6970**.
  The old normal process was absent. All 15 admitted client plugins matched disk
  and VM builds; LowAmmoCue was filtered as requested by the temporary test policy.
- The real return button at 07:33:20 shut down the private session and reopened
  normal SPT/EFT. Normal MainMenu and all 16 normal plugins were confirmed.
  `closed.json` and `completion.json` agree for session
  `e94d3456b5914da7a1878d00f4731a34`; the active pointer is absent, zero receipts.
- Comparing the frozen original profile with the returned normal profile found
  changes only in `Health.UpdateTime` and `Hideout.sptUpdateLastRunTimestamp`.
  Inventory, XP, skills, faction and all other fields were identical. All **238**
  Arena settings remained equal. Locale-formatted comments changed transiently;
  after shutdown the config was byte-identical to its original baseline.
- Temporary policy removed, probe processes closed and files archived outside the
  installation; GPU1 EFT and server stopped gracefully. Eight pre-existing user
  review/inventory files in the checkout retain their original hashes.

An earlier candidate passed the same refusal/reopen test but exposed a hidden
desktop notice that held the helper mutex. That candidate was replaced; the final
test above verifies the no-dialog successful-return behavior. No raid was played
for this startup-only change. Native final-save failure and the exact reporting
user's installation remain unexercised; held/unknown cases are covered by tests.

Final installed SHA-256 (VM and GPU1 matched):

| Binary | SHA-256 |
| --- | --- |
| Client | `19820404E5128079C2BCDE9666BECAE8C241BE6200618542A706446A7BB5B141` |
| BigBrain companion | `6D6935902A683E73B79FAAD7D7B650799AB8B7ED0A188A9B276DDF9BD061EE11` |
| Session helper | `A5FF3C510B9456EDFA13C493B0D4E61F11B68B4501F733B32117A4D1CDCC6DB2` |
| Server | `F1D1967AD3EDF25EC8AF02A29DE40B94F78B206AF1AD513C4EA0E2DE9AAD6078` |

Client loaded MVID: `01a67094-1717-4825-b237-64b8d1373fed`. Builds contain the
working-tree changes over base `5b5e25b`; base informational versions alone do not
identify this candidate. Full native artifacts, raw local profiles and verification
logs remain ignored under `artifacts/entry-restart-hardening-20261028/`.
