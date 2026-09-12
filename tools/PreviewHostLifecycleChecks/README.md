# Preview host lifecycle checks

Run `pwsh -NoProfile -File tools/PreviewHostLifecycleChecks/Verify.ps1` from the
repository. This only builds the isolated test project. It does not build/deploy
the mod or modify production sources; evidence is written under ignored
`artifacts/preview-host-lifecycle/<run>/`.

The executable links the complete current `ArenaPreviewFlow.cs`, including
`ArenaPreviewHostLifetime`, `ArenaPreviewFlowGuard`, surface snapshots and exact
ownership guards, plus the actual lifetime/surface state file. The verifier
extracts `IsAvailable`, `Suspend`, `HoldForFlow` and `SetFlowGuard` verbatim from
`ArenaCharacterPreview.cs` into a partial test wrapper. Consequently a structural
operation accidentally reintroduced in a host callback is actually executed by
the test; source-string assertions are not used as behavioral evidence.

Unity, native screens, object construction, visual loading and release I/O are
controlled boundary doubles. An ancestor deactivation callback is deliberately
marked as a simulated hierarchy mutation lock; `SetParent` and `SetActive` reject
and count attempted mutations even when production cleanup catches the exception.
The test calls the production guard's private `Update` outside that callback.
Destroyed Unity objects are represented by a deterministic fake-null boundary.
This is **not actual Unity event ordering, native rendering, animation, screen
navigation or GPU acceptance**. The close patch's real type/signature binding is
verified separately by the existing native hook compilation and review.

Covered behaviors include deferred fallback cleanup, reentrant close from a hide
callback, proactive same-host/ancestor close, same-frame hide/show and original
native flag restoration, stale callbacks, unavailable view/world fallback,
load-deferred exactly-once release, unselection, foreign settings/hosts, teammate
visuals and reset ownership. Loading re-show also checks subsequent local requests,
account ownership, teammate switch/back and exact drag subscription cleanup.
The five compiled negative controls restore inline callback cleanup, remove the
pre-attachment flush, erase the current native request while draining the hide,
admit reentrant inactive Close, and remove native object availability checks.
Every control must reach an
executable assertion failure, not merely fail compilation.
