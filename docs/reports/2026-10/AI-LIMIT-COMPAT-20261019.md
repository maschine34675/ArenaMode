# AI Limit: Arena-only pause

AI Limit 1.9.2 (`com.dvize.ailimit`) normally keeps six nearby bots active, puts
the others into `NonActive` and disables their objects. Arena Team DM instead
requires every admitted live opponent to remain active; `NonActive` makes it
retire and replace that body. AI Limit also caches the initial human body and
does not remove retired bots from its tracking list, which is unsuitable for
Arena's respawns and physical corpse cleanup.

The adapter suppresses **only** `AILimit.AILimitComponent.Enable()` when its
current native game is proved to be the protected Arena match. It does not
change the global F12 switch, bot-count settings, installed AI Limit DLL or
AI Limit's global `OnWeaponTaken` / `ComplexUpdate` patches. The weapon callback
replacement is essentially native code plus a null guard; it is not by itself
a demonstrated Arena conflict.

## Ownership and timing

The AI Limit `GameWorld.OnGameStarted` prefix directly calls `Enable`, before
Redeploy.Core's started postfix dispatches `ArenaSession.OnRaidStarted`. Checking
only `ArenaSession.IsActive` would therefore miss the initial construction.

The early predicate requires all of:

- live `LocalGame` in `Singleton<AbstractGame>`, the same object in
  `Singleton<IBotGame>`, and the exact `game.GameWorld` in `Singleton<GameWorld>`;
- native `GameStatus.Started`, PMC local settings and equal native/settings map;
- `ArmedForNextRaid`, acknowledged pending server arm, equal armed map, valid
  token and the matching server-returned **native end identity** in the actual
  `LocalGame._raidSettings` object.

An outgoing request's start marker is insufficient here. Native
`TarkovApplication.LocalGameCreate` writes `localSettings.serverId` into that
same settings object before `LocalGame.Create` (decompiled lines 2991–3004),
awaits `localGame.Run`, and only then invokes `OnGameStarted` (3012–3018).
`BotsController.Init` has already registered the native `IBotGame` (line 183);
`BaseLocalGame.vmethod_6` sets `Started` (line 824). `Run` awaits `PrepareSession`,
whose coroutine invokes that method before completing its task (767–769).
PFT uses this same local
game path; its constructor patch merely observes the `LocalGame` instance.

Later Enable calls use `ArenaSession.IsExactActiveRaid(game, world)`, because
successful confirmation consumes `PendingArmToken`. The exact world/map/PMC
checks still apply. Scope is recomputed on every call, never retained by map
name or an old arming. There is no persistent suppression lease to contaminate
the next ordinary raid on the same map. A weak world reference only throttles
the player-facing notice; it grants no authority.

If final confirmation fails, the initial guarded call has not created a limiter
component. Arena does not publish active state and schedules its existing
protected failed-start exit. Subsequent calls no longer qualify from the failed
arming. An ordinary selection, Scav raid, hideout, wrong/expired token or mismatched
world runs the original entry point.

The upstream pending-disable queue `disabledBotsLastFrame` belongs to the
component instance (AI Limit `Component.cs:24`); it is not static. The upstream
`botList` and `playerInfoMapping` are static and are **not** cleared by our adapter.
Native loading creates a new GameWorld object (decompiled
`TarkovApplication.cs:2675`); native teardown disposes its registered unsubscriber
(`TryUnloadGame:3029`) and destroys the old world's GameObject (2687–2691), including
the attached limiter. All three limiter mutation loops are instance methods.
Skipping construction therefore gives the new Arena world neither an updating
limiter nor a subscription to its bot spawner. Old eligibility timers only read
the old mapping, stop their timer and mark the old record eligible
(`Component.cs:449–482`); they do not deactivate bots. We do not rewrite another
mod's static state or repair its existing teardown/event/timer defects.
Ordinary → Arena → ordinary, including a freshly spawned bot near the first exit,
remains a native regression test: offline checks verify scope and published field
shape, not Unity destruction or third-party asynchronous teardown.

## Contract and failure behavior

The optional plugin must have matching GUID, assembly name, plugin/assembly
version and expected plugin/component inheritance. Minimum version is **1.9.2**;
newer matching versions are attempted with a once-per-process warning, not a
compatibility promise. The checked public static `Enable(): void` must have a
managed body, and `AIlimit.NewGamePatch.PatchPrefix` must remain a direct forwarder
with no earlier work. No hard reference to the AI Limit assembly is added.

`Initialize()` discovers the integration. `GetRejectionReason()` returns null
when absent or safely bound, otherwise a player-facing **Arena-only** refusal;
the menu and final pre-arm negotiation both call it. It can discover a plugin
whose instance was not ready during Arena's own initialization. It verifies
that our prefix still exists before admitting a new match. `Shutdown()` removes
only our Harmony owner; even an unpatch failure leaves its remaining prefix inert.

Unknown runtime identity is never used to suppress an ordinary raid. An
unexpected identity-reader fault logs once and closes subsequent Arena admission;
it does not mutate the other mod's settings or silently guess a world owner.

## Evidence and limits

- Upstream source: [AI Limit commit 5396ec94](https://github.com/asler/SPT-AILimit/tree/5396ec9415e901286b6d736e22befb51d39e7eb8).
- Released DLL: [AI Limit 1.9.2](https://github.com/asler/SPT-AILimit/releases/tag/1.9.2),
  SHA-256 `578ebfe8510087f1b6b58eea7f53f4b4ee8979640d929e735661a4f3e476ca75`.
  Downloaded only to ignored audit evidence, never installed/executed.
- `tools/AiLimitCompatChecks` compiles production adapter/policy/contract,
  exercises normal and malformed cases, and reads the published DLL with Cecil.
  See its README for repeatable commands and the optional published-DLL input.
- The 2026-10-19 run passes 72 assertions with the published DLL, plus seven
  compiled mutants rejected by their intended boundary assertions. Production
  checks pass before and after the mutation run; no installation or deployment.
- No native acceptance claimed. Still test ordinary PMC before/after Arena,
  10+ participant TDM, player respawns/corpse release, PFT death/spectating/rebind
  and a 32-alive zombie wave with AI Limit installed. Saved limiter settings
  must stay identical and only the Arena match must omit its component.
