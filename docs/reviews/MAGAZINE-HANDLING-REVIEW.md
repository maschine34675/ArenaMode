# Arena magazine handling review

## Behavior

`Character/MagazineHandlingSpeed` accelerates loading and unloading loose rounds
through the raid inventory, including transfers involving a weapon's internal
magazine. The default is 2x, matching Arena's medical-use default; 1x retains vanilla
timing and values above 4x are capped. The feature does not accelerate weapon or hands
reload animations; loading a currently held internal magazine therefore remains on
the native hands-animation path.

The patch is active only while `ArenaSession.IsActive` and only when the process
belongs to the living, non-AI `GameWorld.MainPlayer`. Both the controller's `Player`
field and the player's current `InventoryController` must match by reference. This
rejects bots, followers, ordinary raids, foreign controllers and a dead body's stale
controller after an Arena respawn.

## Verified SPT 4.1 path

The local SPT 4.1 decompile shows that `PlayerInventoryController.LoadMagazine` and
`UnloadMagazine` first calculate a per-round delay from `BaseLoadTime` or
`BaseUnloadTime`, Mag Drills and `Magazine.LoadUnloadModifier`. They pass that result
to the public constructors of `LoadMagazineProcess` and `UnloadMagazineProcess`.
Each constructor assigns the argument to both `_loadOneAmmoSpeed`, which is reported
to inventory UI events, and `_cooldown`, which drives `Task.Delay` between rounds.

Arena prefixes those two exact constructors and divides only their final delay
argument. This preserves the native skill and magazine modifiers while keeping the
visible progress and actual transaction synchronized. Arena never lowers an
above-floor constructor base delay below 0.025 seconds. A pre-existing base delay at
or below that floor passes through unchanged, so Arena never slows a more aggressive
external speed mod. The native elite Mag Drills load path may progressively shorten
later intervals to 40% of that base; Arena deliberately leaves that vanilla skill
behavior intact.

The user supplied [SPTarkovSpeedLoader](https://github.com/ragnaroks/SPTarkovSpeedLoader)
as the behavior reference. That SPT 4.0 project uses the old obfuscated constructor
names and is licensed AGPL-3.0. No source or binary from it is copied or distributed;
the Arena implementation was written independently against the named local SPT 4.1
types and adds its own Arena, actor, lifecycle and compatibility safeguards.

## Verification boundary

`tools/ArenaMagazineChecks` exercises the production policy and prefixes with
stand-ins and verifies the exact installed Assembly-CSharp metadata. It covers both
directions, limits, normal-raid isolation, actor/controller identity, respawn drift,
shutdown and source wiring. The Release build verifies the Harmony code against the
installed client references. Actual asynchronous inventory transactions and their UI
animation still require a GPU-equipped Arena raid.
