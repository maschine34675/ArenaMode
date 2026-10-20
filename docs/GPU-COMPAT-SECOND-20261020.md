# Compatibility acceptance on SPT-GPU-2 — 2026-10-20

## Preparation and scope

Status: temporary-package installation complete; no raid has started. Root
authorized stopping the idle game/server and preparing the five working adapters,
but native execution waits for the reviewed frozen Arena build. This document
must be updated with observed results before it is treated as acceptance evidence.

The initial read-only check found SPT-GPU-2 at MainMenu, no raid, no active input
or foreign control lease. UiBridge was 0.9.4, EFT 0.16.9.5-40743 and the designated
profile was `SPT-GPU-2`. All 41 installed plugins and six libraries matched their
loaded images and the development installation. These are pre-deployment values,
not evidence that the new adapters have loaded.

Exact temporary versions:

| Component | Published version | Test purpose |
|---|---|---|
| Pause Continued | 1.5.0 | Ordinary pause before/after Arena; Arena-only pause suppression |
| EFT Effects Manager | 1.1.0 library | Exact live-body/controller binding and callback ownership |
| Broken Limbs Don't Heal | 1.1.0 plugin | Real registered consumer of Effects Manager |
| Skills Extended | 3.1.4 | Initial XP subscribers, Signals receipts, ordinary behavior |
| Recoil Rework Legacy | 2.0.0 client | Initial kit swap, death respawns, live refill and actual firing |
| ISB Special Forces | 2.0.1 | Separate client-only/server-only Arena admission refusal |

ISB Aishi remains a source-only admission identity check unless an exact published
package is available. No dummy plugin is to be used as native proof of the real
mod. Recoil's server package has an independently documented JSON-format concern;
client ownership tests do not require installing that unrelated server package.

At 19:52 local time the game and server were confirmed stopped. The test leaf
captured 465 preexisting configuration files and the profile's protected fields,
then installed Pause, Effects Manager, Broken Limbs, Recoil's client and Skills
Extended: 102 manifest-owned files per installation root, with payload hashes
verified on development and GPU2. No existing target file was overwritten.
The server remains stopped until root releases the final build.

ISB and MoreBots are not installed. The earlier ISB audit directory contained
only seven DLLs and was not suitable as a complete test installation. The
published 2.0.1 archive was instead extracted into ignored evidence: 721 client
and 862 server payload files, including assets/configuration/server enum patches.
ISB's client can migrate saved SAIN presets, so its later phase requires a
separate exact SAIN baseline and reviewed file-by-file restoration.

The actual [MoreBotsAPI 2.1.1 release](https://github.com/TacticalToaster/MoreBotsAPI/releases/tag/2.1.1)
was downloaded for that later dependency phase. Archive SHA256:
`F8ADC16303C8FECF76940A9F73764E6989906CE28A90AF85230E22FFF0314656`.
The package has three DLLs and `config.jsonc`; native plugin metadata confirms
2.1.1. Its client DLL SHA256 is
`6A3E04A9CB46171B7EEF300E46AF69B0E461F99278D91BD25AE772EAEB411BAC`.

## Bounded test sequence

1. Record exact current DLL hashes/MVIDs, test settings, profile fields, companion
   persistence and relevant foreign journal files. Install only reviewed package
   payloads with durable path/hash manifests and backups. Use separate phases for
   ISB and working adapters; do not let a deliberate ISB rejection hide another
   adapter's startup result.
2. Start/restart only through `gpu-run`; confirm the actual loaded client,
   companion, server and Core builds plus every temporary DLL. Check the client
   and server startup logs before entering any raid.
3. Ordinary PMC control on Factory: use existing stash equipment via native item
   operations, not profile edits. Toggle Pause with its actual configured key;
   read `Pause.PauseController.IsPaused` and `UnityEngine.Time.timeScale`.
   Confirm resume. Observe Effects Manager's `RaidPlayer`, registrations and
   penalty registry, Recoil's two component owners/cache, and Skills' initial
   event subscriber. Fire a real shot; a successful trigger command alone does
   not count. Extract or use the normal leave flow.
4. Arena Deathmatch on Labs: capture the profile/journal before entry. Confirm
   the initial living kit swap and current ownership in Effects Manager and
   Recoil. Attempt Pause, confirm the visible Arena explanation, `IsPaused=false`
   and positive timeScale. Fire real shots, change weapon/ADS when applicable,
   then run two controlled native deaths and verify distinct surviving successor
   bodies, current foreign owners and fresh shots after replacement. Observe
   Skills XP subscriber removal and the real Signals runtime if present.
   Complete a real Signals interaction if its required item/placement allows
   this without changing persistent profile data. An empty unchanged receipt
   file without a reward attempt is not positive receipt-suppression evidence.
5. PFT Zombies on Factory with one existing companion: invite through the PFT UI
   recipe and use a short wave. Observe one living-player wave refill and, if
   feasible, one dead-player spectator-to-next-wave transition. Verify Effects
   Manager and Recoil point to the fully committed successor, not the corpse.
   Do not count test-hook bot kills as natural combat acceptance.
6. Ordinary PMC control after Arena in the same process: Pause must function,
   Effects Manager must bind the new ordinary body, Recoil must fire and Skills'
   ordinary subscribers must be present. A native heal/mastery or Signals reward
   is recorded separately if actually exercised.
7. Separate ISB admission phases: actual client package and actual server package
   independently show the visible reason, refuse Arena before arming/snapshot
   capture, and leave ordinary PMC/Scav entry usable. Remove the exact temporary
   installation and restart both processes; Arena entry must return.
8. Compare protected profile fields around each Arena interval, inspect merged
   client/server/EFT logs, restore captured settings, clear only the test-created
   companion invitation and restore moved stash gear via native inventory.
   Remove only manifest-owned temporary files, including generated configuration
   and journals absent at baseline. Preserve preexisting data and unknown files.
   A clean restart and final path/hash/settings inventory close the test.

## Observability and limits

UiBridge's read-only object inspector can observe cached physical player
references, components, native event delegates, effect registrations and temporary
penalty registries. Its health hook destroys a limb at zero but does not directly
create a fracture. Therefore a healed-fracture/re-fracture acceptance claim
requires a real fracture plus a native splint/Surv12 treatment; inspecting a
registered type or inflicting a blacked limb does not prove that behavior.

No deliberate failed-respawn injection is planned. The prior offline recovery
tests remain separate from native successful replacements. A test that cannot
reach an actual Signals reward, injury treatment, killcam or live PFT transition
must be recorded as untested, with the observed narrower result retained.

Prepared scenarios and raw local evidence belong under
`artifacts/compat-second-20261020/gpu/`; raw profiles and downloaded binaries are
not documentation or release payloads.
