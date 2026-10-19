# Native Arena menu checks

Run from the repository root:

```powershell
dotnet run --project tools/NativeMenuChecks/NativeMenuChecks.csproj -c Release
```

For an isolated checkout outside the game tree, pass `-p:SPTPath=D:\SPT41`
(or the actual installation root). This resolves only the existing Newtonsoft
reference; the standalone project has no game-project dependency or deployment target.

The harness links the production menu/session code and uses small EFT and SPT
stand-ins. Alongside Arena selection and protected raid-lifecycle behavior, it
checks that the periodic client-settings read uses SPT's authenticated low-level
HTTP client without passing through the noisy `RequestHandler` wrapper. Empty
success responses and real transport failures remain visible in Arena diagnostics.

Optional-mod admission is checked at display/freeze, before server arming and
again after settings refresh. Ordinary PMC/Scav starts must not even invoke the
new provider. Closing setup preserves its process lifetime. Failed MoreBots
binding rejects the exact returned Arena start before kit issue and cancels only
that arm. Separate optional-mod harnesses cover the actual adapters/discovery;
these tests use adapter responses as controlled boundary inputs.

The area cases run the production arming hook and transport with controlled
readiness inputs: exact area acknowledgement/token capture, wrong/missing area
echo, unsupported map, PFT exclusion, missing hooks and full-map compatibility.
The shared area catalog is production source. `AreaStandIns.cs` records the
arming call only; it proves no Unity scene setup, NavMesh or live raid behavior.
The 2026-09-07 standalone run passed 87 assertions.

The October 9 map matrix additionally covers whole Customs across all solo modes,
PFT discovery, and retaining its three surveyed TDM contours beside the new whole-map
choice. Retired contours still fail at final start, even though the menu can offer
a valid replacement. Whole-map selection must never implicitly become a contour.

`Verify-QuietSettingsTransport.ps1` runs a temporary source mutant that restores
`RequestHandler.GetJson` for the periodic read. The matching assertion must fail;
the script then reruns the production checks and verifies that the production
source hash did not change. Actual BepInEx log output still requires a game
restart and a live menu session.

`CaptionChecks.cs` (2026-10-08) covers the "PVE ZONE" -> "ARENA ZONE" caption on the
Arena setup screens: the pure `ArenaSessionModeCaption.Resolve` and the whole
set/restore state machine (`ArenaSessionModeCaptionLedger`, driven here with a
stand-in label) are compiled and executed, and the Unity-bound Harmony patches
(`ArenaSessionModeCaptionPatch`, the side-screen sync in `ArenaSideSelectionPatch`,
the `Plugin` wiring) are pinned by source text: every screen's gate as one complete
`if (condition) Apply(...)` statement, the reconnect exclusion, no direct caption
write outside `Apply`, and `Apply` delegating to the one static ledger. Every
source pin reads the sources with comments removed (a commented-out `Enable()`
line, `Plugin` call or side-screen sync is a missing one), `Apply` must hold its
ledger call in a `try` closed by a plain `catch (Exception)` without a `throw` (it
runs inside the side screen's own postfixes, which must never see its failure),
and the side-screen sync must be an UNGUARDED statement (a guard on the ARENA
toggle would silently drop the restore half). The deploying-screen gate is the
exact `RaidSettings` selection only: `ArenaSession.ArmedForNextRaid` is set after
that screen is shown, so it is never consulted there (see NATIVE-MENU-REVIEW.md).
`Verify-SessionModeCaption.ps1` hands 35 mutated copies to the harness
(`ProductionSessionModeCaptionSource` and the `ARENA_CAPTION_*` variables); each
must fail its named assertion. Harmony binding and the rendered label still need
the game window.
