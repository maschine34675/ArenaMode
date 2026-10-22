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
- `ArenaWhitelistAdmission.cs`: the whitelist verdict for a normal session (installed plugins, the preloader
  patchers below `BepInEx\patchers`, a DLL without a `TargetDLLs` member being a library and no patcher, and the
  server's mods evaluated with the shared policy) and for an Arena session
  (gate active, a policy that loads, no patcher that ran before the gate, every loaded plugin allowed by the policy
  as read now, server is an Arena instance, the server's mods allowed by the policy as read now, the server's policy
  hash the gate's, in that order); the START ARENA SESSION offer; per-ping caching; Fika; failure modes.
  `SessionReviewChecks.cs` holds the checks added for the review of 2026-10-22 (Arena session refusals, patcher
  units, deny wins over dependencies in the refusal, the helper handshake); `SessionPolicyChecks.cs` the third pass
  (an Arena session without a usable policy, the plugins the game loaded, and the order of the session checks).
- `ArenaSessionSwitch.cs`: profile id, request file, the helper's own parser run on the request before anything is
  written or started, helper launch, the wait for the helper's acknowledgement (`ArenaHelperHandshake`: the game
  quits only after `<request>.accepted`, a helper that is gone or silent leaves it running and every failure takes the
  request file back; the Unity coroutine is
  run by hand through the `Plugin` stand-in and the clock is a seam), the two-click gate.
- `ArenaMode.Shared/Session/*`: the shared policy and decision logic. The decision source can be mutated too
  (`PolicyOverride.cs` follows the policy-override note, `"unlisted": "allow"`, from the decision into the notes
  line, its cap, the Info log and the Arena session notice).
- `ArenaMode.Session/SessionLogic.cs`: the helper's request parser (`HelperRequest`), which the client links.

What is a stand-in (`StandIns.cs`): BepInEx (`Paths`, `Chainloader`), Unity's `Application.Quit`, `Time` and
`Coroutine`, the plugin object that hosts the wait (`Plugin.StartCoroutine` only records the routine), the
server ping state (including its `policyHash`), `RequestHandler.IsLocal`. The real ping parser is covered by `NativeMenuChecks`
(`CheckSessionPingFields`), the admission chain order by `OptionalModAdmissionChecks`.

The Unity/Harmony wiring (`ArenaSideSelectionPatch`, `ArenaAvailabilityNotice`, `ArenaOptionalMods`) cannot
run offline and is pinned by source text in `SourcePins.cs`. That proves the calls and their order, not how
the screen looks or that a click lands: that is a raid on the GPU host (acceptance steps 2 to 5 of the spec).
