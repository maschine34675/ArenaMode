# Tactical purchase, readiness and countdown menus

2026-09-13. Applies to solo Tactical unless the protected-team case is explicitly
named. Normal raids, PFT entry-loadout restoration and death interception remain
unchanged. Native GPU raid acceptance is still required after this implementation.

## Opponent readiness and announcements

Solo Tactical previously emitted RoundStart at the asynchronous spawn request.
It now counts physical Active opponents, while recognizing opponents already
eliminated during assembly. A complete roster starts combat timing and emits one
cue; the existing partial-roster fallback remains after the 45-second assembly
window, with five seconds of activation grace for late physical arrivals.
No ready opponents retries the request without buying/swapping the kit
again; unresolved bodies are retired and old callbacks remain epoch-guarded.
An already-eliminated squad can finish without a belated fight announcement.
The assembly banner remains until actual readiness rather than first arrival.

The logical round resets award state once before dispatch. The later audible
cue explicitly skips that reset, preserving early First Blood, hit records,
revenge state and multi-kill chains. This separation fixes an independent review
finding; otherwise waiting for the last bot would reset awards mid-round.
Protected Tactical keeps its existing cue after all opponent identities and
hostility are ready, using the default award reset at that transition.

## Opening purchase behavior

The opening solo kit is the cheapest buildable catalogue entry (stable catalogue
order breaks equal-price ties), explicitly labelled as a free starter in the
buy panel. It is captured before installing the mode's kit provider. RandomKit
or SelectedKit can no longer grant arbitrary paid equipment for free. Purchases
still charge once at round start, browsing does not charge, survivors retain
and resupply their prior loadout, and the existing post-death automatic purchase
remains. Protected PFT startup returns before any starter/provider assignment.

Loading-screen preloading asks the same Tactical starter supplier and never
reserves a random base kit for Tactical. Protected PFT asks no kit supplier.
A failed solo initialization uses the existing protected failed-start abort;
it cannot continue as an uncoordinated raid with a generic random kit swap.

## Escape and native menus

Vanilla EftGamePlayerOwner rejects a dead player's commands before translating
Escape. Solo Tactical can deliberately hold that body during the next buy
countdown. The new narrow prefix routes only Escape to native exit-menu
translation for the exact active LocalGame, GameWorld, local body and started
owner, while BattleUI is current and no body pipeline or stand-down owns it.
No alive flag, death hold, global input permission or profile guard is changed.

The buy overlay yields Escape and closes while the native main/settings menu is
open. Existing pointer-release draining remains active. Buy/staging/aftermath
ticks wait while the native menu owns the screen, extending their phase deadline
by elapsed game time so a round does not replace the owner while leaving.
The solo initial-swap hold cap likewise excludes native menu time; its existing
maximum still applies to time actually spent waiting outside that menu.

## Evidence and remaining checks

`tools/TacticalPolishChecks/` executes exact production method bodies, the full
buy panel, spawn policies and actual award-reset methods/policy against explicit
native I/O doubles. It reproduces all three historical defects and separately
rejects a removed award-reset guard. Source hashes bind each run. Independent
review found no remaining consequential issue after the award correction.

The real client compiles against installed EFT assemblies. Unit preload tests,
solo-spawn, assembly-banner, input-drain, PFT and give-up regressions accompany
the change; final results/build binding live in HANDOFF.md. Offline evidence does
not prove native detour ordering, input scheduling, animation, bots or profile
restoration. User tests must cover first-round no purchase/purchase, delayed
opponents, early kills, Escape while alive/dead, resume and protected exit, plus
the unchanged protected PFT 5v5 opening.
