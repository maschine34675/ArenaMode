# Arena character selection by model click

The side-selection layout owns a transparent UI button with the native PMC
model button's geometry, shifted by the same horizontal offset as the Arena
preview. It changes only the existing Random/Arena toggle. Native toggle events
still perform eligibility, PFT exclusion, server availability and selection.
Repeated selection does not add a second direct selection/server request.

The target checks the current layout identity, visible screen/preview and the
native toggle's actual `IsInteractable()` state. It sits below the native Arena
text/toggle in sibling order. No native listeners or hover handlers are cloned.
The input target is removed before preview transfer, column restoration or
disposal; the persistent preview remains input-transparent on subsequent screens.
Early synchronous/asynchronous preview failure cannot leave a ghost target.
Optional button creation failure preserves the working preview and native toggle.

Native references: `MatchMakerSideSelectionScreen.Awake` has separate PMC/Scav
big buttons; each enables its toggle. `AnimatedToggle.IsToggled` dispatches the
native toggle/group event. This is a UI hit target, not a 3D collider.

Independent read-only review found the early-loader/partial-button failure
paths; both were corrected. Existing NativeMenuChecks, ArenaCharacterPreviewChecks
and PreviewHostLifecycleChecks cover selection/preview surrounding behavior;
the staged client compile verifies the actual new Unity API usage. These offline
checks do not prove the rendered hitbox or screen overlap.

GPU acceptance after restarting EFT: click PMC, then the Arena figure (torso and
head), then Scav/PMC again; click Arena repeatedly; go forward/back and reopen
the selection screen. Check that later menu buttons and loading-model drag work
normally. A PFT management screen must not gain a selectable Arena figure.
