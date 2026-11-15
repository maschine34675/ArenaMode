# Selectable Translocator

Unreleased optional Arena mutator, disabled by default. This first stage implements
one local reusable device: fire throws one disc, fire again recalls it, aim
teleports to its current position, including in flight. No Unreal Tournament
code, meshes or sounds are included. Device/disc geometry and short tones are original.

## Item and input

The server registers private template `a12e00000000000000000002` before its native
profile snapshot, cloning the reusable rangefinder's native rig without altering
the donor. No ordinary loot, trader, handbook, flea-price or weapon-shelf entry is
added. A confirmed raid response explicitly grants the capability to that arm only.

After protected entry, the client supplies one device per equipment tree using
free rig/pocket space and native inventory operations. The first free 4–0 slot is
bound once; occupied/user-changed slots are preserved. Full equipment produces a
short notice, never eviction. Drop/toggle does not replenish an equipment tree.

Only native fire/aim commands from the exact currently held, granted device act.
Release commands are consumed without action; both press and continuous bindings
work. ADS remains suppressed for that device. Original model changes belong only
to the private instance; pool return/get, shutdown and late completion restore or
discard them without editing shared assets or ordinary rangefinders.

The private device leases only the native hand-animation flag while its idle
operation is ready. The current and next native animation must be IDLE or AIMING;
actual inventory, look, gesture, draw and hide phases remain blocked. The donor's
generic CanInteract predicate requires an IDLE-named animation, so it cannot gate
this raised device. It never enters native ADS or enables the donor's optics.
The lease restores on hand/operation changes, pool transitions and shutdown.

## Flight and relocation

The disc launches at 18 m/s with a small upward component, gravity and damped
bounces. A sphere sweep covers every movement and impact separation; bounded
120 Hz substeps cap hitch work at twelve steps per frame. Concave area paths use
the disc radius as inset. Invalid/saturated geometry evidence retires the disc.
A resting disc persists; loss of support resumes gravity. Thirty seconds of
actual flight expires an abyss projectile.

Teleport validates the prospective native player capsule and additional controller
shapes against synchronized scene geometry and living actors. It requires neither
floor nor NavMesh, so airborne discs and physically clear elevated destinations
remain usable. The entire capsule must fit the selected Arena volume. No telefrag.

An exact one-call ticket binds body, world, controller, area and disc destination.
The existing native teleport prefix consumes it, including on whole maps. Ordinary
and bot teleports retain their own guards. Actual position readback determines
success; a refusal retains the disc. Native teleport owns camera/environment and
fall-height origin reset. World-space jump and motion histories are rebased while
momentum, native phase and consumed double-jump credit remain intact. Old positional
coyote/slide/mantle requests are cancelled. Active slide, vault/climb, prone,
mounted/platform and asynchronous controller transitions refuse relocation.

After relocation, both stale grounded witnesses are invalidated and the native
ground query runs at the synchronized destination. This is required before the
next input: an idle native controller otherwise retains contact with the old
floor and floats after a ground-to-air teleport. Only the native query establishes
new support; no jump impulse or artificial falling phase is added.

## Lifecycle and presentation

Holstering and Gun Game promotion preload retain the disc while actions are
blocked. Device ownership includes exact equipment ancestry and reachability.
Drop, body destruction/replacement, death, respawn, round/wave boundary, result,
raid/world change or disabling the mutator clears it. Normal raids have no
supply, action, disc, HUD or movement change. The HUD is one short status line
and a small aiming dot, only while the device is held, respecting the Arena HUD switches.

Telefrag, destructible discs, bot usage and multiplayer are outside this stage.

## Verification

`TranslocatorItemChecks`, `TranslocatorDeviceChecks`, `TranslocatorPoseChecks`,
`TranslocatorTeleportChecks` and `TranslocatorRuntimeChecks` link production code
and reject compiled defects.
They exercise native ABI/IL contracts and modeled boundaries; they do not prove
Unity physics, actual equip/hand appearance or native player movement acceptance.
The complete xunit suite and automatic affected harness selection remain required.
Native results and candidate identity are recorded separately in the dated report.
