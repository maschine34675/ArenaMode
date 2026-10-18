# Quiet successful settings polls — 18 October 2026

The client already avoids RequestHandler's convenience-wrapper Info lines.
SPT still logged its periodic successful `GET /arenamode/client-settings` once
per poll. This optional server adapter omits that routine line only after it
successfully registers an ASP.NET response-completion observer.

## Narrow boundary

- Exact, case-sensitive `GET` and `/arenamode/client-settings`, empty PathBase
  and query string, no WebSocket, and response not already started.
- Pushes, other verbs, trailing slash, case variants, encoded-path lookalikes,
  query/path-base variants and every ordinary route keep native request logging.
- Final **2xx** results are quiet. All other HTTP statuses and aborted requests
  produce one Arena Warning with status and client IP, without cookies, request
  bodies or query data. Repeated invocation of the same completion observer
  cannot duplicate its report.
- Native `InvokeAsync`, its awaited request delegate, authentication, route
  execution, response data, 404 Error and exception Critical handling are never
  patched. An exception with a still-default 200 status remains visible through
  SPT's original exception path.
- If completion registration fails, the original request line remains and a
  compatibility warning is emitted once. Missing/changed native signatures
  retain native logging and do not prevent Arena startup. A throwing diagnostic
  sink cannot break HTTP completion.
- `HttpConfig.LogRequests`, BepInEx settings and poll cadence remain unchanged.
  If the user disables native request logging, native middleware never calls
  this helper; the adapter does not override that preference.

This is an endpoint-specific exception to the general rule that Arena does not
change another logger. It removes only successful routine output for Arena's
own periodic endpoint. No global filter or separate listener is introduced.

## Installed contract, not just reference source

Installed `SPT_Runtime/SPT.Server.dll` SHA-256:
`8656B91BFD9225132654587C5569B4BDD3682CECC9F0628FC885C6AA91655C9C`.

Reflection and Cecil verify the synchronous public instance method:

```text
SPTarkov.Server.Middleware.SptLoggerMiddleware.LogRequest(
    HttpContext, IPAddress, bool isLocalRequest, bool isWSRequest) -> void
```

The installed `InvokeAsync` contains an additional hard-coded `/health` prefix
bypass absent from the reference snapshot. It also bypasses native error
observation, so the adapter deliberately does not reuse or extend it. There is
no supported per-route quiet-success option in the reviewed middleware/config.
The adapter patches only `LogRequest`, resolving the loaded host at startup;
no compile reference to the executable server assembly is required.

## Verification

From the repository root:

```powershell
pwsh -NoProfile -File tools/SettingsPollLogChecks/Verify.ps1
dotnet build ArenaMode.Server/ArenaMode.Server.csproj -c Release -p:CustomAfterMicrosoftCommonTargets=D:/SPT41/Development/ArenaMode-log-followup/tools/AirPitChecks/NoDeploy.targets --nologo -v:quiet
git diff --check
```

The source-linked harness uses real ASP.NET `DefaultHttpContext`, a completion
feature that explicitly drives registered callbacks, the installed Harmony,
and the real installed server metadata. It passes **417 assertions**, including
loopback, IPv6 loopback, private and public IPs; status boundaries; request and
authentication-input preservation; exact rejection cases; abort; registration
failure; diagnostic failure; and unchanged downstream exception identity.
It also installs the production detour on the installed `LogRequest` method
inside the isolated harness process. An intentionally uninitialized middleware
object proves the exact poll skips only its log helper, while an ordinary route
reaches the original helper. The detour is removed in `finally`; the test does
not run or modify the installed server.

Two compiled negative controls broaden exact matching or hide failed outcomes;
both must fail at their intended assertion. The harness then rebuilds against
the original production source. Success token:
`SETTINGS_POLL_LOG_VERIFICATION_PASSED`.

The no-deploy server build succeeds with the two existing warnings (nullable
hostility IDs and MudTabs PanelClass), zero errors. Installed Arena server SHA
remained `BA468562C633445CC668D709748861456102F1DAFEF5DA61457B732C3348845A`
before and after. This leaf performs no deployment and claims no live HTTP/GPU
acceptance; parent integration registers the adapter in PatchManager and owns
the final artifact and native check.

## Four-pass review

1. Inspected actual installed middleware and supported config before selecting
   the optional helper-only seam. Discarded the unsafe `/health` bypass.
2. Kept admission exact and defer-first: no original routine line is skipped
   unless outcome observation was registered successfully.
3. Exercised failed registration, failed outcomes, exceptions, unchanged request
   data, actual Harmony attachment and ordinary-route negative controls.
4. Reviewed source/patch scope, bounded diagnostics, no-deploy artifact identity
   and compiled negative controls. No gameplay, authentication, polling cadence,
   global logging preference or native error handler is changed.
