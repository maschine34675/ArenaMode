# Arena session client checks

Offline checks of the client side of the Arena session feature
(`docs/ARENA-SESSION-20261021.md`, section 7).

```powershell
pwsh -File tools/ArenaSessionClientChecks/Verify.ps1
```

`Verify.ps1` builds with the no-deploy properties, runs the harness, then runs the fault controls: each
mutates one production seam in a copy outside the repository and must fail on its named assertion.

What is compiled unchanged from production:

- `ArenaSessionMode.cs`: the `-arena-session` flag and the gate's report.
- `ArenaWhitelistAdmission.cs`: the whitelist verdict for a normal session (installed plugins and the
  server's mods evaluated with the shared policy) and for an Arena session (gate active, server is an
  Arena instance); the START ARENA SESSION offer; per-ping caching; Fika; failure modes.
- `ArenaSessionSwitch.cs`: profile id, request file, the helper's own parser run on the request before anything is
  written or started, helper launch and quit order, the two-click gate.
- `ArenaMode.Shared/Session/*`: the shared policy and decision logic.
- `ArenaMode.Session/SessionLogic.cs`: the helper's request parser (`HelperRequest`), which the client links.

What is a stand-in (`StandIns.cs`): BepInEx (`Paths`, `Chainloader`), Unity's `Application.Quit`, the
server ping state, `RequestHandler.IsLocal`. The real ping parser is covered by `NativeMenuChecks`
(`CheckSessionPingFields`), the admission chain order by `OptionalModAdmissionChecks`.

The Unity/Harmony wiring (`ArenaSideSelectionPatch`, `ArenaAvailabilityNotice`, `ArenaOptionalMods`) cannot
run offline and is pinned by source text in `SourcePins.cs`. That proves the calls and their order, not how
the screen looks or that a click lands: that is a raid on the GPU host (acceptance steps 2 to 5 of the spec).
