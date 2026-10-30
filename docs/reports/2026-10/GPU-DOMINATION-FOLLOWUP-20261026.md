# Domination follow-up: native GPU acceptance, 2026-10-26

SPT-GPU-2, EFT 0.16.9.5-40743, Unity 2022.3.43f1, UiBridge 0.9.6,
1920×1080. Tests used the host's test profile and native Arena menus. EFT and
the SPT server were running in MainMenu at entry and were left running in
MainMenu at completion. No production source/build/deployment changes were
performed by the test agent.

## Builds and scope

| Candidate | Client SHA-256 | Companion SHA-256 |
|---|---|---|
| Pickup placement / HUD / first ring fix | `E4385856B891A8A31D4A14F4A5A9315C8DD69F9724A2F18506938F86DC2CE5B5` | `97ED2451F6DF6AD48F8505CF3849D477A6F2E0D07CE79C7428215E1EE18E4C8D` |
| Final saturated world ring colors | `AAD46D00745FFEF666E286267D7A10E61CCB1D55ADC5A1D5BDCE1444FB0E9870` | `7D06BAE14BA8E5D5AB8DFE8900046E682A2B5733DA68A012DAD8B7EF6A03D4CC` |

Server remained `12127A1B6F61801F939E26EBAE37D39149772B00AE3FE62EA45532349D9C7A8F`.
Both candidate processes reported all 16 relevant plugins and both companion
libraries identical between VM build, host disk and loaded process. The final
process was `5033ec579b0b441db558088dbddae576`, started 15:13:34 local time.

The first candidate supplied the pickup, rotation and real pickup-HUD evidence.
It exposed a white ring despite correct CPU vertex colors. The parent then
changed only the world-ring palette and its separation from label color in
`DominationPointView`; pickup and HUD implementation remained unchanged. The
final candidate received focused neutral/BEAR/USEC capture tests. The earlier
pickup screenshot is therefore deliberately identified as the earlier
candidate, not presented as a capture from the final process.

The parent separately measured the menu fix: matching ten-second idle windows
improved from approximately 14 FPS / P95 228.79 ms to 56 FPS / P95 27.27 ms.
This leaf did not repeat that benchmark.

## Native results

- Factory day Domination started through the native selection/READY screens
  with ten participants. All six actual pickup types appeared on the first
  placement attempt in both longer runs. One attempt reported 272 real geometry
  probes, rejecting 228 obstructed pedestals and 36 missing floors; six final
  placements remained. This is evidence that native checks ran, not a claim
  that every generated candidate is usable.
- The actual prepared objective centers were A `(-5.890, 0.031, -27.460)`,
  B `(-6.770, 0.031, 10.010)` and C `(-20.756, 0.031, 22.769)`.
  `gpu/final-centers.json` contains the native debug lines.
- Two longer runs yielded 36 logged selected/rotated final positions covering
  all six types, including 24 rotations. Re-evaluation against all three native
  centers found zero violations of the six-metre horizontal / two-metre
  vertical exclusion envelope. The parent's independent calculation agreed;
  the smallest horizontal distance was approximately 13.083 m. Runtime reads
  separately recorded exact floors, appearance generations and beam state.
- Available beams remained active. The damage pickup was visually inspected:
  a tall pink beam, pedestal and matching ground ring remained visible. Local
  boots were collected through a real short walk after teleporting nearby,
  confirmed by the native collection log at 15:10:02.274. Its notice and active
  30-second effect occupied a distinct row below the objective strip.
- The first ring candidate rendered white even though B's start/end vertex
  colors were `#FF6B6BE6`, shader `Sprites/Default`, material white and no
  keywords. After the world-palette correction, the same B viewpoint showed
  neutral grey and then unmistakably red for BEAR, and in a second short test
  blue for USEC. Each owned-state screenshot includes the matching actual HUD
  owner after seven seconds of physical occupancy. No ownership or capture
  policy was set through reflection or a test hook.
- One longer run and the first final-build camera run ended normally at the
  score limit. Other runs used native DISCONNECT and the ordinary result-screen
  path back to MainMenu. The focused color runs each exited immediately after
  the capture evidence.

Evidence below is relative to
`artifacts/domination-followup-20261026/`:

| Evidence | Purpose |
|---|---|
| `gpu/spacing-evaluation.json`, `parent-spacing-check.json` | 36 real floors, 24 rotations, independently checked exclusion |
| `gpu/pickups-pass1/results/`, `gpu/pickups-pass2/results/` | Actual runtime floors, types, phases, generations and beam states |
| `gpu/second-pickups-log.json` | Placement, rotation and real local collection |
| `gpu/damage-beacon.png` | Preserved colored pickup beam and ring, first candidate |
| `gpu/pickup-real-hud.png`, `gpu/collected-second/` | Real boots notice/effect below objectives, first candidate |
| `gpu/day-b-ring.png` | Rejected first-candidate white ring |
| `gpu/focused-neutral.png` | Final neutral grey B ring with neutral HUD owner |
| `gpu/focused-bear.png`, `gpu/focused-bear-final/` | Final red B ring, BEAR owner and native capture |
| `gpu/focused-usec.png`, `gpu/focused-usec/` | Final blue B ring, USEC owner and native capture |
| `gpu/final-build-status.json`, `gpu/end-status.json` | Loaded binary agreement and final host/game state |

The initial final-build camera at A faced intervening tanks. Those images do
not count as ring-color evidence. `final-b-ring.png` establishes only neutral
grey; ownership is established by the later focused screenshots and their
matching state readbacks. A setup attempt to select a nonexistent literal
`4` button failed before READY; a subsequent attempt to set the hidden map
participant entry was denied by UiBridge. That protection was respected;
participants stayed at ten. These setup attempts are not gameplay failures.

## Logs and limits

Client, server and EFT warning/error streams were exported together. There was
no Domination/pickup placement abort or exception attributed to these new paths.
The first candidate logged one medical callback exception cluster at
15:06:08.703 near the end of its first match, repeated by EFT and Unity logging:

```text
BotFirstAid+CG_ApplyToSelf.method_1(Result applyResult)
EFT.Player+MedsController+MedsInHandsOperation.Finish()
MedsInHandsOperation.OnEffectRemoved(IHealthEffect)
ActiveHealthController+Effect.NextState()
BaseHealthController.ManualUpdate(...)
```

That is an unresolved native-medical lifecycle observation. The stack alone
does not prove whether Arena teardown, another mod or native code initiated the
problem. It is not claimed fixed by the palette change. The final-process
warning export contains no repeat of that exception. Other entries included
Factory spawn-marker category repairs, missing Unity scripts, `JetMaterial ==
null`, unknown custom item types, existing Harmony patch-declaration warnings,
profile display fallbacks and Fence armor-slot warnings. Raw evidence is
`gpu/day-merged-warnings.json` and `gpu/final-merged-warnings.json`.

This follow-up tested Factory day. Factory night and the general DOM/TDM/PMC
isolation lifecycle had separate prior acceptance in
`GPU-DOMINATION-20261026.md`; the new expanded pool was checked offline for both
Factory variants. No new native night acceptance is claimed here. Contested
yellow is covered by production-linked view tests, not a reliable captured
native contested frame in this run. Teleports were test tools: native navmesh,
zone and room checks remained on; several deliberately skipped only the route
from the current player position (`reachable=false`) to reach the verified
view/collection positions. These teleports do not prove bot route success.

## Cleanup

- All 228 Arena settings exactly match the initial readback. The restored
  config SHA-256 is `92CCF21772FB1F440940C767BBBC5DFB1374994C57FEE36AB9A95883D8EAEB07`
  on both VM and GPU host, identical to the original backup. BEAR/Deathmatch
  were restored through the native menu; DeveloperMode was switched off.
- The complete serialized test-profile inventory is exactly unchanged, with
  303 items. No profile reset or manual inventory write was performed.
- Final status shows godmode/infinite ammo off, no walk, tracking, pending
  fire, time or weather override. Used inspector handles and the lease were
  released. `ArenaMatchService.Current` is null; pickup entries are empty and
  both cached layout fields are null.
- EFT and the original SPT server remain running in MainMenu. No second
  server/game process was left behind. See `gpu/cleanup.json`,
  `gpu/config-comparison.json`, `gpu/inventory-final-comparison.json` and
  `gpu/end-server-status.txt`.
