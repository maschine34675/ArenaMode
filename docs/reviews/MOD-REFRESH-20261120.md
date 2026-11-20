# PitFireTeam, Tarkova Cola and VisualAssist refresh — 2026-11-20

Development changes after ArenaMode 0.6.0. The whitelist review targets the official
releases [PitFireTeam 1.1.2](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/1.1.2),
[TarkovaCola 3.0.1](https://github.com/Yendaxddd/TarkovaCola/releases/tag/3.0.1) and
[VisualAssist 1.2.0](https://github.com/SleepingPills/VisualAssist/releases/tag/v1.2.0).
Reviewed-version warnings remain advisory. Identity, reflected contracts, profile
protection and user allow/deny rules remain mandatory and unchanged.

## PitFireTeam

1.1.2 replaces the single-entry spawned-leader list with `LocalRaidLeader`, and
its prefetched follower tasks now carry an explicit ordered account roster.
Arena binds that layout only from 1.1.2, checks the exact authenticated account
set, preserves the native cache order and passes a copy into the new profile and
spawn entry points. A task or physical leader from another generation is refused.
Older reviewed versions keep their existing path; the forward-admission floor
remains 0.10.3, with the historical 0.10.0 exception.

Bounded-area dispatch now handles both the older void method and 1.1.2's private
async method. A deferred async call returns a completed task, and the replay's
failure is observed. The protected stop resets the new leader and prefetch cache.

The new server companion-inventory cache registrations are suppressed in the
same protected session as the older persistence boundaries. Suppressed dropped
item registration returns an empty accepted-ID array. Normal PMC/Scav raids
retain native behavior, and no companion data or foreign DLL is bundled.

## Tarkova Cola

Both 2.2.1 and 3.0.1 research catalogs are supported explicitly. Old reward
receipts and pending plans keep their old catalog and challenge goals; 3.0.1
receipts carry the new catalog. Existing higher counters and added perk state
cannot regress when an older receipt is settled after the upstream update.
APEX progress, cores, equipped choices and ordinary inventory remain outside the
reward delta. Native full-profile restoration and readback are unchanged.

During a protected Arena lease, the new companion-perk editor cannot consume or
refund bottles or write `pit_perks.json`. The machine editor cannot write its
global file. Machines initialize once per raid, not on each replacement body.
New perk caches are cleaned up at Arena exit, including shared grenade fuse
templates. Normal raids retain these features.

Vulture Aid's delayed reroll continuation is bound to its exact request closure,
lease, physical body and container. The HTTP callback checks only managed
identities; the main-thread application additionally verifies the current living
body and world. Repeated requests for the same container remain possible, while
late replies cannot overwrite another request's target or enter a later raid.

## VisualAssist

The client GUID is `com.janky.visualassist`. Plugin version is 1.2.0, while the
official assembly version is 1.2.0.23951; the generated revision is normalized.

Redeploy.Core 1.0.25 owns the generic physical-body repair. VisualAssist originally
creates its grenade preview only at world start and destroys it at death. Core
reattaches it to the confirmed successor, cleans up only the old preview's own
line, sphere and materials, and prevents an old disposal from clearing the new
singleton. Preparation does not adopt the body. Failure recovery, later deaths
and world retirement use the same exact-owner rules; the public Core API stays v1.

Arena separately protects the foreign throw prefix. It only lets ordinary
grenade hands with matching high/low operation and the exact current local owner
use VisualAssist's freshly recalculated prediction. Quick throws, stale owners,
missing or invalid predictions fall back to EFT's native throw. This does not
disable the preview or change ordinary raids. Core does not change throw physics.

## Verification boundaries

Offline checks cover released metadata and reflected signatures, native API
compilation, lifecycle/ownership decisions, persistence suppression, both Cola
catalogs and historical plans. Their passing does not establish native behavior;
the dated GPU report records the exact tested binaries and scenarios separately.

User decision: retain these three official mods after testing. Additional probes
and the temporary Fast Grenade Throw test installation are removed afterwards.
