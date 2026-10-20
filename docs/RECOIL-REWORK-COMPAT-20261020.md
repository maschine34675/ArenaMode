# Recoil Rework Legacy: Arena body replacement

This implements the client lifecycle correction identified in
[the source/binary review](RECOIL-REWORK-REVIEW-20261020.md). Reviewed baseline:
Recoil Rework Legacy 2.0.0, GUID `com.pein.camerarecoilmod`. Matching newer
contracts may load with one warning; this is not native certification of future
versions.

## Behavior

- Arena's initial player, kit replacements, death respawns and live round/wave
  replacements receive their own initialized recoil and camera components.
- The foreign singleton handoff happens before either new component's `Awake`
  can destroy the replacement player's GameObject.
- Old components are disabled during construction. Only after the replacement
  is authoritative are the **component objects** destroyed. Player objects,
  corpses, inventory, camera hierarchy and native rendering are never destroyed
  by this adapter.
- Recoil values use the incoming physical firearm controller rather than the
  preceding weapon or retired body. A native recoil recalculation runs once
  when an authoritative local body gets a different physical hands controller.
- Ordinary PMC/Scav/Hideout construction and saved recoil settings are unchanged.
  An unrelated singleton owner is never cleared merely because Arena was
  selected; the exact Arena operation instead rejects the unsafe handoff.
- A missing/old/changed contract or absent required native/adapter hook produces
  an Arena admission rejection. Root integration keeps that diagnostic visible
  in the existing menu path and checks it again at start.

## Native sequence and ownership

`LocalPlayer.Create` sets `IsYourPlayer`, then calls `Player.Init(async:false)`.
All waits in that path are conditional on `async`; its final registration calls
`GameWorld.RegisterPlayer`, which sets `MainPlayer` to the new body. Thus at the
foreign `PlayerInitPatch.PatchPostfix`:

1. `world.MainPlayer` is the **new body**.
2. `game.LocalPlayer` is still the **old body**, or null during initial creation.
3. Core's replacement flag is armed for a replacement.
4. The old body and both previous foreign components still exist.

The adapter requires the exact current LocalGame/GameWorld pair, native running
lifecycle, PMC side, matching native/settings map, matching physical Profile
object and exact new-body registration. Before Arena activation it additionally
requires the accepted server arm token and the corresponding native raid ID.
The initial constructor exception requires Running with null `game.LocalPlayer`;
replacement requires a previously leased old body and Core's active pipeline.
Profile IDs alone and selected maps do not establish ownership.

It patches the foreign initializer itself, so both static owners are checked
and cleared before the original synchronous `AddComponent` calls. Native
`Player.Init` is not replaced or treated as an asynchronously completed event.
The finalizer verifies component identity, spring initialization and captured
player, then leaves the new pair disabled until authoritative commitment.

## Failure and late work

The previous pair is retained while creation can fail. A partial initializer
failure destroys only newly created recoil components and restores the previous
pair where the exact old game/body still owns recovery.

Core's `ReplacementFailed` event precedes the requester's recovery decision.
The handler therefore marks recovery pending; the following tick waits for the
pipeline to finish and accepts either the recovered old body or the retained
new body only when both native current-body pointers agree. It does not assume
that every failure means the old body was restored.

Retired or pending component updates cannot rotate old players. Recalculation
uses a complete cache snapshot and finalizer; exceptions or stale ownership
restore the previous fields only while the operation still owns the controller
slot. A nested unrelated writer is not overwritten.

Body/controller tombstones use conditional weak tables. After component
release they still reject delayed old-controller callbacks, including callbacks
after another raid starts, without retaining native bodies through a global
strong-reference list. Raid end clears only statics/cache entries owned by
Arena and retires the remaining owned components. Normal settings and unrelated
controller entries are not reset.

## Integration

`ArenaRecoilLegacyCompat.Initialize`, `GetRejectionReason`, `Tick` and `Shutdown`
are the integration entry points. `Tick` runs with normal plugin updates, not
behind a movement/recoil preference. The adapter subscribes internally to
Core's `Replaced`, `ReplacementFailed` and `RaidEnded` events.

Reflection resolves only the exact optional assembly and member signatures.
No compile-time third-party dependency or bundled Recoil Rework DLL is added.
The dead `Util.GetFirearmController`/`GetGameWorld` helpers and unused
WeaponSway component are deliberately untouched.

## Validation and remaining tests

`tools/RecoilLegacyCompatChecks` compiles all three production adapter files
against small native boundaries, a foreign assembly fixture and actual published
DLL metadata/IL assertions. Its negative controls preserve the original
destructive singleton behavior, and its mutation runner separately removes the
handoff, retired-update guard, incoming-controller correction and rollback.
Each must fail its corresponding behavioral assertion; compilation failures do
not count as a killed mutation.

Final offline evidence: **114 assertions passed**, and all four behavioral
mutations failed at their expected assertions. The unmodified baseline passed
again after the mutations. Source SHA-256 values stayed unchanged throughout
the run. Receipts:
`artifacts/compat-first-20261020/recoil-impl/checks-0151414509f0434190b3a13b0fefbab8/`.
The harness emits one harmless unused fixture-name compiler warning; no
production-build success is inferred from this boundary build.

This does not simulate Unity's deferred destruction, Mono/Harmony detours,
the real weapon animator or native game callbacks. Native acceptance still
requires initial kit replacement; repeated deaths and live swaps; failure
recovery; PFT wave refill; replay/spectator transitions; ADS/left shoulder;
alternating Akimbo fire; weapon changes; and a subsequent ordinary raid in
the same process. Production build results and any later native run should be
recorded by integration rather than inferred from this harness.

The separate upstream server-package JSON/List mismatch documented in the
investigation is not modified. No server-wide weapon template/configuration
changes or general recoil rebalance are part of this adapter.
