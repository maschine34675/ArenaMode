# Dedicated Molotov — native integration acceptance, 15 October 2026

Passed on SPT-GPU-2, Factory daytime, Deathmatch, fixed Spartan kit and four
participants. This verifies the current merged item and the single client
supply authority; the October 12 branch results are not substituted for this run.
Only the default GPU test profile was used. No profile JSON was overwritten,
no item was injected by a give-item hook, and no infinite-ammunition hook was used.
Godmode was enabled after the real raid-start kit replacement for controlled throws.

Private snapshots, raw logs, inspector answers and images are ignored beneath
`artifacts/molotov-native-item-20261015/native/`. Paths below are relative to that
directory. Source/build/deployment remained owned by the parent task; the native
tester changed no production files or DLLs.

## Exact loaded build and startup

Client and server were stopped through gpu-run before deployment, then both
started fresh after two matching sync polls. Server PID 13372 became ready;
EFT instance `08401790` started at 21:22:25 CEST. All 19 maschine assemblies and
Redeploy.Core matched VM, remote disk and loaded process.

| Component | Identity |
|---|---|
| Arena client, build `b06d8fe` | SHA256 `74694DA3CFF91869B9C70239D6AA2743EBFB756C4A9AC57A22D1A82FC56F9E27`; loaded MVID `59d43a18-d5d5-4f6a-80ef-bc56caa10f9b` |
| Arena server | SHA256 `A5921A299E35EA6C969D104FA12AA3743FB46F34D60EE9550702E0AB02116C53` |
| BigBrain companion, unchanged | SHA256 `5701DFF516540CD3DAA7B8BC3E42132E96E3184391EE1676928AEB918818B0F9` |
| Redeploy.Core 1.0.7.0, unchanged | SHA256 `6319028DF7A828999EE2B4CCC8B4ECD8213CE22529EA07A34A1ABA61F4148577` |
| KillcamReplay 0.9.13, unchanged | SHA256 `26809E10FE8C676A10A93CC298D22372FCEA9F7E6BC48FE2E61D365F58D98350` |
| UiBridge 0.9.3+e1af7e24, unchanged loaded plugin | SHA256 `A442BE6EE9C04E0CDD00DB603C4B9A6B128DAF212063F03E93C96526FC583D6A` |

Server log 19:21:40.235 UTC (21:21:40.235 CEST): private Molotov registered with
native blast disabled and no trader/flea/loot entry. Native kit-supply PostLoad
still reported exactly two RGD-5s. The live client later returned
`ArenaFireSupply.HasVerifiedTemplate = true`. This is successful registration
and confirmed client admission, not merely finding the private ID on disk.

Evidence: `preflight-stop/`, `preflight-result.md`, `deployed-hashes.json`,
`core-hash.json`, `loaded-status.json`, `server.log`, `client.log`.

## Supply, icon, G priority and model — passed

The mutator was off when the raid loaded. After the real Spartan kit replacement,
the native inventory contained two RGD-5s and no private Molotov. Enabling the
mutator loaded one previously cold native item bundle, then added exactly one
`a12e00000000000000000001` Molotov in the rig. Both original RGD item identities
remained unchanged. Turning the mutator off and back on retained exactly those
same three identities, with no duplicate grant.

`TopPriorityGrenade.Id` was the extra Molotov identity. Actual native
`PressThrowGrenade` (the G action) placed that exact identity in the hands.
The inventory and G slot show a distinct amber bottle icon labelled Molotov;
the hand model visibly has the bottle and cloth wick. Inspector confirmed the
active instance mesh name `Arena original Molotov bottle`.

Evidence: `off-inventory.json`, `on-inventory.json`, `toggle-inventory.json`,
`molotov-hands.json`, **`inventory.png`**, **`hands.png`**.
These two images were also independently inspected by the parent task.

## Actual throws and cleanup — passed

Native `TryLowThrow` followed by `FinishLowThrow` threw the Molotov. At
21:25:37.040 the runtime bound the exact supplied item; at 21:25:37.218 it ignited
31 floor cells around (15.78, 4.53, 38.78), for six seconds. The captured rendered
frames show the free flying bottle and then broad orange ground fire. The two
original RGD-5s remained after the Molotov was consumed; no second bonus appeared.

The next actual G action selected one original RGD identity. Its hand model was
again the native green grenade; the former Molotov model lease was inactive,
showing restoration on pool return. Native inspected RGD properties remained
Strength 100, FragmentsCount 70 and ExplDelay 3.5 seconds. Its own real low throw
produced a brief native explosion flash, with no new incendiary bind or ignition.
`ArenaFireRuntime.HasFire` was false afterward, and the unthrown original RGD
remained as the sole grenade. This run did not measure blast damage against a bot.

Selected native backbuffer frames, with capture indices retained:

- Flying bottle: **`molotov-throw/frame-0060-f15717.png`**.
- Ground fire: **`molotov-throw/frame-0200-f15857.png`**.
- Ordinary RGD hand appearance: **`rgd-hands.png`**.
- Ordinary RGD explosion flash: **`rgd-explosion/frame-0180-f34217.png`**;
  the later `frame-0220-f34257.png` has no persistent fire.

Both capture indices contain 258 encoded consecutive game frames; selected
original frames were copied into the artifact directories, rather than claiming
that every image listed in the host index was copied locally. Additional identity
evidence: `next-rgd-hands.json`, `post-rgd.json`. The parent independently viewed
the inventory, hand, fire and RGD explosion images before completion.

## Logs — no new Arena errors

Client log `LogOutput-SPT-GPU-2-2026-10-15_21-22-17.log`: 11 Warnings,
zero Errors/Fatals. All warnings were inspected including continuation stacks:

- Four absent optional-Fika references at startup.
- Four inherited Harmony target warnings: DynamicMaps, UIFixes dialog/cursor,
  and WTT commonlib; their adjacent patch declarations identify their owners.
- One deliberately throwing `RespawnApiProbe.ProbeRuntime.AlwaysThrow` subscriber
  during the real kit replacement. Its InvalidOperationException is retained in
  the evidence, not filtered out; Core continued dispatch and completed the swap.
- One Redeploy stale PerfectCulling camera cleanup warning during that swap.
- One native negative BoxCollider-scale warning at `TRANSITS/Road_to_woods`.

Server log, fresh startup from 19:21:30 UTC through the captured post-test menu:
nine Warnings, zero Errors. Four are PFT stored-companion profile-display
fallbacks; four are Fence armor front/back-slot randomization warnings. One is a
native missing-trader refresh placeholder at startup; the specific trader's
provenance was not established in this bounded run. None names the private item,
registration, supply or model. No causal Arena regression is shown by these
warnings; this is not a claim that all other mods are warning-free.

Arena's protected end completed at 19:29:44.010 UTC: ordinary post-raid processing
was skipped and the snapshot persisted. No Molotov supply/model warning,
inventory exception, duplicate grant, damage exception or post-exit error appeared.
Counts and complete matching warning entries: `log-summary.json`; raw stacks:
`client.log` and `server.log`.

A transient concurrent edit outside Arena broke gpu-run's scenario-module regex
while taking a still. The failed invocation did not reach the host. No tester or
parent edit was made to UiBridge; its owner corrected the file, and normal
gpu-run leave/config-restore subsequently succeeded. MCP captured the missing
still in the meantime. This tooling failure is separate from game acceptance.

## Restoration and limits

Returned through native Disconnect and result NEXT buttons to MainMenu, without
buying treatment. Godmode was explicitly disabled before leaving; captures were
cleared. No tracking, infinite ammo, fake roster or profile rewrite was used.
Config ledger restoration plus the menu-changed GameMode reset restored all
304 stored values: Arena 224, Killcam 19, PitFireTeam 61. All twelve PFT store
files are byte-identical to the fresh preflight snapshot.

The complete 401-entry Inventory compares exactly, including original item IDs
and contents. Skills, Stats, Quests, TaskConditionCounters, Achievements,
InsuredItems and Encyclopedia also compare exactly. Health values are identical;
only native `Health.UpdateTime` advanced across login/raid, explicitly reported
by the verifier. No Molotov leaked into the persistent inventory.
`restoration-verification.json` has `success: true`; `final-status.json` confirms
MainMenu, no raid, godmode off, free control and unchanged loaded assembly matches.

Scope limits: one Factory DM kit test; no separate cold own-gear/no-RGD raid,
full-rig case, PFT firefight, other map or friendly-fire damage measurement was
repeated. The custom bundle was genuinely cold when enabled in this raid. Audio
was not independently auditioned. These limits do not reduce the observed
2+1 supply, icon/hand/world presentation, priority, actual fire/normal explosion
and protected-restoration results above.
