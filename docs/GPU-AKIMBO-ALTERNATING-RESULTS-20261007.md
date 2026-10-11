# Alternating Akimbo and pickup audio — GPU-2, 2026-10-07

## Scope and installed build

User decision: one projectile per trigger, alternating right/left, while retaining
two shots per loaded cartridge (7 -> 14). No delayed automatic second projectile.
The supplied IENBA pickup cue plays after confirmed local collection.

Client source `65b81153911bb14f686431f8c1074c17cc4b255a`, SHA256
`63361A5BDC19ADC7EBCCE36AB153880C63D4ECB89E8E52C564E6BC15DFCA9F46`.
Installed locally and on SPT-GPU-2. UiBridge confirmed the loaded MVID
`14299204-a7a1-4ea6-b0ce-54f3c544ed5a`, source revision and disk identity after
restart; all 24 checked plugins matched. Server, Redeploy.Core and BigBrainCompat
were not replaced. The new client also includes the already committed Molotov
100 HP/s change; this run did not test that balancing change.

UiBridge `0.7.4+8218b027478e61f98b4119d179fc2a8fe41c928a`, unchanged SHA256
`306606209ADCFF09B94BC1DC01B574C27609F39AB23234E76D915EDE9223D79E`.
EFT `0.16.9.5-40743-f137e819`, Unity `2022.3.43f1`.
Own lane `akimbo-alternating`, profile SPT-GPU-2, Factory day, two Gun Game
sessions followed by one Deathmatch session. No other session was interrupted.
The idle old client was restarted before acceptance checks. Raw evidence lives
under ignored `artifacts/akimbo-alternating-gpu/`.

## Native results

| Check | Observation | Result |
| --- | --- | --- |
| PB ammunition economy | Started with exactly seven total loaded rounds. Fourteen separate 80 ms trigger presses produced seven right and seven left shots, zero obstructed fallbacks, and zero remaining rounds. Native pre-trigger totals: 7,7,6,6,5,5,4,4,3,3,2,2,1,1. | Passed |
| Empty weapon | A fifteenth trigger left both counters at seven and ammunition at zero. | Passed |
| Reload | Native reload completed; replacement magazine bound after animation, eight total rounds ready. | Passed |
| OFF/ON during pair | Right/free shot left eight rounds. Toggle OFF/ON, then left/paid shot reduced this to seven. No new free half from rebinding. | Passed |
| Knife redraw during pair | Right/free at seven rounds, knife and pistol selection, then left/paid reduced to six. | Passed |
| Non-handgun | MP-155 in second session had no Akimbo binding. | Passed in Arena |
| Local pickup cue | Native walk into `factory-boots` completed collection at 11:16:05.932 local; one audio play request at volume 0.7. Runtime source owned by application, enabled, unmuted, 2D, exact Master group, expected loaded clip. | Output path passed |
| Bot pickup | Earlier `factory-damage` collection by a bot did not increment the audio request count. | Passed |
| Raid cleanup | After returning to menu: no pickup entries, play counter zero, clip/source null. | Passed |

The free half is reserved when native preparation succeeds. Cancellation can
forfeit that free half; it cannot grant repeated free shots. Automatic pistols
retain native automatic-fire behavior: one projectile per native firing event,
with alternating sides. An obstructed left origin can safely fall back to right.

This run did not certify revolver fire or automatic-pistol bursts. The first test
fixture used IDs from the shipped fallback ladder, while the live server draws
and renumbers a fresh ladder per raid. The first draw fell back to PB; a second
draw's rung19 was MP-155, not the previous draw's RSh-12. A later CR 200DS created
through the test hook lacked its cylinder; attempted native equip/install did
not assemble a usable held gun. No shot was accepted as revolver evidence.
The files named `m1911-before`/`m1911-binding` contain PB, not M1911, and
`revolver-before` contains MP-155; their names are not acceptance evidence.

No direct device recording/listening test was performed. An output request and
correct AudioSource binding do not prove physical audibility. The first read
occurred after the 600 ms cue had finished. Native animation quality, left-hand
finger grip, rapid fire, revolver hammer/cylinder transitions, wall fallback and
a normal PMC/Scav control raid remain outside this bounded run. Offline tests
cover the new cylinder preparation and scope guards, not their live animation.

## Profile, config and logs

Fresh profile/config copies were taken before restarting the idle old client.
The first strict profile comparison was **not byte-equivalent**: eighteen items
gained explicit `upd.StackObjectsCount: 1` where that field had been omitted;
energy 93 and hydration 94 became 100, and Health.UpdateTime changed. The complete
leaf diff is retained in `profile-gungame-delta.json`. No item, template, parent,
slot, ammunition count, durability, XP, skill, quest, trader or encyclopedia
change was found. These default/health differences were not erased by restoring
the profile and are not attributed to this feature without a startup-only control.
The temporary CR 200DS/cylinder/ammunition did not persist.

Using the post-Gun-Game snapshot as the immediate pre-Deathmatch baseline, the
subsequent Deathmatch with pickup collection and one local death passed all seven
strict comparisons: Inventory, Skills, Quests, TradersInfo, Encyclopedia, XP and
Health (excluding only UpdateTime). Evidence: `profile-deathmatch-check.json`.

AkimboEnabled, GunGameLadder and the previously selected zombie mode were restored.
The only remaining config addition is the new `Arena pickups / SoundVolume=0.7`.
The client was gracefully closed; the previously running server remains running.

Exported UiBridge log: 4,252 lines, sequence 1–4252, no dropped prefix and no
Error-level entries; one stored line was truncated at the bridge's 2,000-character
limit. Of 63 warnings, Arena logged the three invalid test ladder IDs and a
cancelled solo preparation immediately after the final raid exit. Other warnings
include native missing scene scripts/negative-scale collider, stale camera sampler
cleanup and the installed RespawnApiProbe's deliberately throwing subscriber.
No Akimbo failure/quarantine or pickup-audio failure appeared.

## Offline evidence and delivery

- Akimbo production-linked lifetime/credit checks: 2,127 assertions and 15 fault
  controls. Native cylinder adapter: 23 assertions and three fault controls.
- Pickup runtime/audio: 253 assertions and four audio fault controls, plus WAV
  format, signal, provenance and hash verification.
- Infinite-ammo adapter regression: 2,843 assertions, four fault controls and 32
  installed native contracts.
- Existing hitmarker/announcer output checks passed after repairing a pre-existing
  duplicate ArenaSession test double (partial class only): 54 hitmarker and 315
  announcer assertions, plus the announcer fault controls.
- Isolated final build: zero errors, four existing nullable warnings; installed
  files unchanged by building. Manifest:
  `artifacts/double-jump-build/2743657b9cb64ae39742ffa52bbb1e1a/manifest.json`.
- Independent adversarial review's concrete cylinder/scope findings were fixed
  and rechecked before deployment. Final documentation audit corrected historical
  receipt-lifetime and obsolete no-audio statements.

Commits: `147fcad` test-double repair; `9fd5558` confirmed collection audio;
`8e42a31` byte-stable pickup license/manifest assets; `65b8115` alternating Akimbo.
No push performed.

Only client DLL, pickup WAV/provenance/license files and AUDIO-LICENSES.md were
deployed. Untracked Molotov audio was neither added nor copied. `collect.wav`
SHA256 `F2C38A1218CB974DCF1F946BFADA7ECF6CEED3B1DD5FF531807280624592710F`.
Source and CC0 license: https://freesound.org/people/IENBA/sounds/762911/.
