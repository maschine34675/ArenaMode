# Deathmatch GPU r3 triage — 2026-09-12

The immutable report is `GPU-DEATHMATCH-MENU-TEST-RESULTS-r3.md` (ee0a421).
It ran the prior client 4F33B337..., not the later four refinements.
Two menu cycles and the observed C1/C2 round rules passed. C2 exited with
three exceptions; profile comparisons found no persistent gameplay delta,
with the explicitly reported Hideout heartbeat timestamp exception.
No new GPU test was run on the development server.

## R3-01: duplicate terminal result

The original C1 terminal screenshot shows the independent HUD canvas banner
above the F9 result text. `ArenaAssemblyBanner` now hides only terminal result
notices while the Arena panel is visible in BattleUI. Preparation/assembly
notices remain unchanged. F9 already displays the heading, detail and exit timer;
closing it immediately restores the banner. Panel transitions refresh the notice
without waiting for the HUD tick, and disable/destroy releases panel visibility.
Optional HUD refresh errors cannot prevent input/cursor cleanup. No scoring,
round timer, protection, spawn or native exit changes.

The production-linked ArenaAssemblyChecks passed 44 assertions and rejected
five compiled faults, including restored duplicate results and lost preparation
notices. The previously stale external doubles for RoundsMode/ScorePanel were
updated so this helper again compiles current production code. Independent
read-only review checked the actual panel/banner integration. The helper does
not execute ArenaPanel's IMGUI, Unity draw ordering or live lifecycle events.
Those still require GPU acceptance.

## R3-02: retired native callbacks and uninitialized SAIN component

At client 14:48:36.259 native Stop commits. At +414 ms a BotItemTaker pickup
animator callback throws ArgumentException. After protected server restoration,
two SAIN OnDisable NREs arise through DestroyImmediate / Pools.UnloadPools.
These are distinct from the earlier menu-preview reparenting issue.

Native evidence: BaseLocalGame.Stop calls BotsController.StopGettingInfo;
BotsList.Stop disposes the tracked BotOwners before the one-second black fade
has ended. BotOwner.Dispose nulls Medecine but does not cancel the player's
pending MovementContext.PickupAction. PickUpState later invokes that callback,
whose installed IL constructs the Medecine.RefreshCurMeds instance delegate
before scheduling or starting its inventory transaction. This is an established
stale-callback hazard and fits the historical exception; the exact historical
receiver/IL offset was not captured, so that diagnosis is not claimed as proof
of its precise null field.

`ArenaPickupTeardownSafety` cancels only one still-pending native
CG_PickupAction.method_0 for the same stamped Arena BotOwner, body and ItemTaker
when real Dispose begins. Native PreActive's no-op disposal, foreign/multicast
callbacks and ordinary raids stay unchanged. Native pickup placement was only
simulated at this point, so no already-started transaction is intercepted.
The original Dispose always runs. The cancellation is logged when it happens.

Installed SAIN 4.5.1.0 SHA256:
`C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.
Its MVID matches r3. SAIN attaches BotComponent at native PreActivate, before
Init/CreateClasses supplies BotActivation. OnDisable dereferences that property
without a null guard; GameWorld unloads pools before its OnDispose event calls
SAIN's manager. An attached but never initialized component can reach this path.

`ArenaSainDisableSafety` validates the entire reviewed 19-byte, two-call cleanup
body with resolved method tokens, as well as version/signatures. Only a null
BotActivation and the exact stamped Arena BotOwner on the same GameObject permit
the missing activation step to be skipped; StopAllCoroutines is still called.
An existing activation, mismatched assigned owner, unsupported body, missing
owner or lookup error keeps the original method. No exception finalizer or
blanket OnDisable suppression. Logs identify guard binding and up to eight
actual uninitialized cleanup cases per process.

Independent source/installed-IL review found no established P0–P2 defect.
Remaining limit: same-GameObject BotOwner lookup availability during Unity pool
destruction is not guaranteed by managed source. If the owner has already gone,
the guard fails open. The historical logs do not identify those two components;
therefore both NREs still need a focused GPU retest, not a claim of runtime cure.
There is no established cause in Arena corpse trimming: these paths preserve
all native registry and lifetime ownership, and DynamicMaps integration was not
even present in the tested client.

The production-linked exit helper passed 69 assertions and nine compiled faults;
an independent installed-contract check also passed. It models external objects
and does not run Unity, the native animator or an actual SAIN component.
See `tools/ArenaExitChecks/README.md` for evidence scope and repeat commands.

## Additional close-click observation

The reported single-ammunition decrease is not independently reproduced.
The native input path has separate collection/delivery and an immediate final
owner release in `ArenaInputBlocker`; it deserves a focused reproduction.
The installed ClientApplication normally delivers during Update, so a delayed
FixedUpdate delivery is not an established explanation of this particular run.
Do not claim that Event.Use alone clears native commands, or that the banner
change fixes firing. Retest explicit F9 Close clicks, hotkey and Escape while
recording ammo before/after; keep this observation separate from exit failures.
