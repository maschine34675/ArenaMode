# Amand's Graphics and Amand's Sense Updated, 8 November 2026

The development whitelist admits the published client **Amand's Graphics 1.8.0**.
Release 0.5.0 predates the fix. **Amand's Sense Updated 3.1.0** was tested with
an explicit, temporary allowance and stays outside the default whitelist.

## Labyrinth, SPT-GPU-2

Graphics falls back to an object named `!settings` on an unknown map. The
Labyrinth object is actually called `---Labyrinth_levelsettings---`. Arena now
supplies EFT's active `LevelSettings` object when that lookup fails for the
current local Arena FPS camera. Graphics still runs its own activation with
the `default` graphics profile, including its normal ON/OFF choice. The change
does not run in normal PMC or Scav raids. [Released mod and known issue](https://sp-mod.com/mod/592/amandss-graphics)

Two Deathmatches started on Labyrinth with two participants. EFT initialized
Graphics and issued the first kit. In the first run the FPS camera and firearm
controller were live, and the probe matched Graphics' `levelSettings` to EFT's
own instance `498404`. The first death changed physical player `-1408294` to
`-1517594` with Graphics ON. Insert toggled Graphics OFF. It stayed OFF when
the second death produced player `-1524314`. Each replacement also received a
different FPS camera, bound to the new body.

The second launch used the final shared Core and the shipping whitelist file.
Only the diagnostic probe had a user override. Labyrinth started again.
Graphics stayed ON after another death, player `-1407744` to `-1519480`, and
still referred to the scene settings and current camera. Both raids returned
through the results screens to the main menu. No Amand startup exception or
Core baseline refusal occurred.

| Loaded on GPU-2 | MVID in the process and on disk |
| --- | --- |
| Arena client 0.5.0 development, both runs | `6cca08cc-88b5-4a36-92b4-16f60d81c577` |
| Graphics 1.8.0 | `437b005a-baeb-438c-a0a1-9407ff5af842` |
| Core 1.0.22, first / final run | `2a651362-6c69-4e90-a648-90d28b4ede0c` / `3607abaf-f0f9-4f08-90c7-94a73db3dd19` |

## Factory, SPT-GPU-1

The shipped Graphics DLL (SHA-256
`0BAD292DA16386DE8A737C5C627016030A343256A3670823ABEE805439CB75BD`) and the
shipped Sense DLL (`861E750D9056B330CC13ACA50D50CE21CF6A1E4C7A70317F4E421D3A190EE872`)
ran together in Deathmatch, process `395c694c`, with the first Core 1.0.22
candidate. All three matched the host files in memory. The Core change after
that candidate guards recovery from an optional adapter failure. The final
Core has a separate Labyrinth run and automated coverage of that failure path.

Two deaths changed the player and camera each time. Graphics retained ON at
the first death and OFF at the second. Insert switched it back ON. Sense was
configured to pulse automatically over a 45 m radius. It had 369 and 317
active marker entries immediately before the respective deaths, and 357 and
391 on the successors. Sense pointed to each living player's inventory. The
second replacement fired two actual MCX rounds (31 to 29).

The integration in Core prevents a collection-modified exception in Sense's
`Player.Init` hook and keeps the raid's existing corpse history. A corpse scan
after retirement and continuing as one's Scav were tested offline. This Factory
run did not have a Sense corpse record, use the manual pulse key, or exercise
standalone Redeploy. Thermal optics, killcam transitions, Fika and maps beyond
Factory and Labyrinth were outside these runs. [Sense release](https://sp-mod.com/mod/2521/amands-sense-updated)

Before restoration, whole-profile comparisons on both hosts found only native
health/hideout timestamp changes. PitFireTeam's companion records were intact.
The test mods, temporary allow entries and read-only probe were removed. GPU-1
returned to its stopped state. GPU-2 returned to the original client and Core
in the main menu, with its server running and synchronization restored.
Its temporary rollback copy remains outside the installation in
`C:\Temp\ArenaAmands-20261108`.

Offline verification passed 3,731 Arena tests (three environment-gated skips),
617 Redeploy tests and the release-DLL scene harness (25 assertions). The
Arena session and optional-mod admission checks rejected 174 and 12 intentional
faults respectively. Core's body-replacement checks cover the optional
recovery failure as well as the original active-marker case.
