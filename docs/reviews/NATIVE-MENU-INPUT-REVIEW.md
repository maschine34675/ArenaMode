# Native Arena menu resume — 2026-09-12

`0a49898` completes the bounded r5 report: four isolated F9 Close comparisons
preserved ammunition, each corresponding release filtered one shooting command.
Deliberate firing/reload/movement and the three-round autoexit passed; FFA B
also returned without a new exception. Protected PMC/Scav fields matched.
Remaining button/continuous/duration/ordinary-menu cases stay open as recorded.

The separate `bd95b72` refinements report stopped after a BotWeaponSelector NRE
and unexpected AK-12 ammunition loss from 30/30+1 to 29/30+1. Six mouse clicks
and Escape span the two ammunition images. There is no isolated click or command
telemetry establishing exactly where it happened. This is not a reclassification
of the successful F9 tests, nor evidence that DLSS caused either problem.

Native sources and installed Assembly-CSharp IL confirm that screen environments
write `GamePlayerOwner.SetIgnoreInput(bool)`: Menu and Settings keep it true;
returning to BattleUI writes false. Its body only stores static `bool_0`.
The native orphan-UI recovery also calls this setter. The existing F9 owner
drain did not cover this separate native release boundary.

The blocker now observes true-to-false before the native store and starts the
same fresh released-pointer drain. It requires the exact active game/world and
`GamePlayerOwner.MyPlayer` to be the current local, non-AI MainPlayer. Repeated
false, menu-to-settings true-to-true, ordinary raids and old bodies do not latch.
Native navigation remains under the native input tree: this flag does not add
an Arena dispatch owner while the menu is open. The movement-facing `Blocked`
query does recognize the native gate so optional airborne actions cannot use
stale input while a menu owns it. Existing Arena owners remain authoritative.

Tests link the production blocker, include reentrant native resume and failed
unpatch, and reject removal of native release/identity/transition checks plus
accidental filtering of menu navigation. Installed contracts also verify the
setter's exact static field store. Independent adversarial source review passed.
These checks do not execute Unity's native menu or establish the original
ammunition loss's precise cause. The GPU follow-up must isolate menu clicks and
verify ammunition, Escape/Tab/settings navigation and subsequent deliberate fire.

New ready marker: `Arena input blocker: native menu resume release safeguard ready.`
Resume adds `Arena panel input: native menu released gameplay input; pointer drain pending`
to the existing capped diagnostics (24 lines per process). Pending/released
generation and command counts still describe the actual drain, not sound output.
No server, profile, graphics setting, keybind or audio change is part of this fix.
