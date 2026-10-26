# Multiplayer Arena with Fika — concept

Status: **proposal and source review, 2026-10-26**. No multiplayer implementation,
build, deployment or native multiplayer acceptance is implied. Arena's existing
Fika refusal and published incompatibility remain in place. This expands the
[earlier planning note](FUTURE-WORK.md#private-online-arena-with-an-optional-fika-integration).

## Recommendation and intended experience

Build an optional Fika integration for private games with friends. Keep solo
Arena independent. Start with two humans; expand to small teams after repeated
network respawns work. Public matchmaking, ranked play, anti-cheat and large
public servers are outside this first scope.

Proposed user flow:

1. Choose **Arena Multiplayer**, then host a private room or join a saved server.
2. The room owner chooses map/area, mode, teams, kits and gameplay mutators.
   Joiners see these settings; each player keeps their own HUD/audio preferences.
3. A compatibility check verifies the actual gameplay content and protocol.
   All participants acknowledge the frozen rules and finish loading before the
   shared countdown starts.
4. Play, see the same score/result, then return to the room for another match.

Reuse the existing installation's game assets through the controlled session
approach where appropriate; users should not have to maintain a second complete
manual installation. The online session orchestration is nevertheless a new
path: the current local helper cannot be pointed at a remote server unchanged.
Fika remains a separately installed dependency for multiplayer, not a dependency
of solo Arena and not bundled source or binaries.

## Reviewed baseline

| Component | Candidate examined | Evidence boundary |
| --- | --- | --- |
| ArenaMode source | `86407c0` | Local source; newer development than the published 0.3.0 |
| Installed game/backend | EFT `0.16.9.40743`, SPT `4.1.6` | Local executable versions |
| Fika Plugin | `2.4.6`, commit `9572504f9a1a338944013a33e74ee9867b809a80` | Official release targets that EFT build; five central reference files matched the pinned source after line-ending normalization |
| Fika Server | `2.4.2` | Release and local `FikaModMetadata`: SPT range `~4.1.3` |
| Fika Headless | `1.5.3` | Optional later target; no native test performed |

The matching version declarations make this a sensible test baseline, not a
compatibility claim. Pin an exact client/server/Core/content combination for
the first implementation; don't begin by accepting arbitrary newer Fika APIs.
The modding wiki is labelled for SPT 4.0, so the pinned 2.4.6 source takes
precedence when the documented API differs.

## Responsibilities

| Part | Responsibility |
| --- | --- |
| Shared SPT + Fika backend | Arena-only accounts, lobby/match admission, content contracts, per-participant profile protection and durable results |
| Fika raid host | Existing world/AI replication and native network player lifecycle; can be a human game or later a Headless game |
| Arena match authority on that raid host | Teams, countdown, round state, time/kill limits, spawn allocation, bot population, confirmed score and pickup ownership |
| Human clients | Input and existing Fika combat paths, local presentation, requests to the authority; never competing round-win decisions |

The room owner choosing settings is not necessarily the simulation host: with
Headless, even the human who created the room is a client. The backend process
is also not the raid simulation. Do not run Unity/AI match logic in SPT.Server.

Fika already exposes `FikaEventDispatcher`, raid/network/peer events and
`IFikaNetworkManager.RegisterPacket`, `SendData` and `SendDataToPeer` for custom
messages. Use these for Arena state instead of duplicating native position,
weapon and inventory replication. Keep subscription ownership explicit; the
player callbacks exposed as static Actions must be combined/removed, not replaced.

**Rule authority does not mean authoritative ballistics.** Fika's human player
owners apply received damage, while the raid host owns real AI. A central Arena
scorekeeper must reconcile confirmed life transitions through this model.
It does not turn Fika into an anti-cheat or a trusted public competitive server.

## Changes required in Arena

### 1. Separate local runtime from match rules

Introduce logical seams, not a wholesale rewrite. Suggested names below are
design proposals, not existing public APIs:

- `IArenaRaidRuntime`: raid lifecycle, authority role, participants, controlled
  local body and end operation; solo and Fika implementations.
- Shared match rules: consume participant events and an authority clock, then
  produce state transitions. Reuse existing plain policies where practical.
- Participant registry: `MatchId`, `RoundId`, `ParticipantId`, `LifeId`, team,
  native profile ID and current Fika network entity. "Local" is a viewer-relative
  presentation property, not the global participant identity.
- Body/kit operations: keep Redeploy.Core's existing local API intact and build
  a separately verified network implementation. Isolate Fika-referencing types
  so loading solo Arena still works without Fika.

Current obstacles are concrete:

| Current seam | Why enabling Fika alone is insufficient |
| --- | --- |
| `ArenaMatchService`, `ArenaRespawnDriver`, movement/area paths | Require `LocalGame`; Fika uses `CoopGame : BaseLocalGame<EftGamePlayerOwner>` |
| Redeploy.Core `RespawnRequest` / `RespawnApi` | Published contract is local body replacement and explicitly excludes Fika |
| `LiveKitRespawn.AtRaidStart` | Even assigning the first kit can replace a living local body; the first prototype must stage its kit before initial network spawn instead |
| `IArenaMode.OnLocalDeath/OnLocalRespawn`, local awards | Model one human, not generic participant events |
| `CombatPatches.SeenVictims` | Nonlocal victims are deduplicated by profile ID for the raid; a respawning human must be identified by participant plus life |
| `ArenaScoreboard` | Its special `local` row must become a projection of a stable participant roster |
| `ClientSettingsStore` / `SettingsSync` | One process-global last-writer settings mirror cannot arbitrate multiple players' rules |
| `ArenaPickupRuntime` | Placement/rotation/reservations are local, and human collection only accepts the exact local player |

### 2. Freeze and replicate room rules

Create a room-specific immutable rules revision containing map/area geometry,
teams, kits/ladder, limits, gameplay mutators and required gameplay content hashes.
Do not reuse the global client-settings mirror. Cosmetic preferences stay local.

Require all peers to acknowledge protocol, rules and content before activation.
Use one curated combination first; later define explicit host-only and cosmetic
exceptions rather than demanding identical Headless and human plugin lists.
Configuration changes after readiness revoke readiness; changes during a match
wait for the next agreed boundary.

Use small reliable ordered messages for readiness, round transitions, scores,
pickup grants and life changes. Include match/round/life identity, a monotonic
revision or event sequence and an operation ID. Bind requests to the actual
connected peer, not a profile/participant ID claimed in the payload. Apply each
operation once, reject stale lives/rounds and keep periodic state snapshots for
repairing presentation drift. These are proposed protocol requirements.

The host sends a countdown deadline based on its clock; clients render the
remaining time. Start only after bodies, kits and the rules revision are ready.
Timeout means a defined abort, not independently starting on each machine.

### 3. Add actual PvP teams

Fika 2.4.6 initializes human `GroupId` and `TeamId` as `Fika`, including observed
players. Its damage handling can reject same-group damage when friendly fire is
off. An Arena `TeamId` must therefore be independent of faction/native group.
Scope damage admission to the active Arena room and cover bullet, explosive,
melee and delayed damage paths; do not toggle shared Fika settings for all raids.

Coop information also needs team-aware handling: health/name displays, pings,
spawn/death notifications, freecam player lists and spectator targeting must not
reveal enemies by default. Arena team colors/markers and the killfeed use the
viewer-relative team mapping. Client-side filtering is gameplay behavior for
trusted peers, not secrecy against a modified client. Human teams do not need PFT.

### 4. Prove network respawn before promising continuous Deathmatch

No ready-made equivalent of Arena's fresh-body respawn was found in the reviewed
Fika source. Downed-player revival keeps the existing body and is a different
operation. Native death stops packet sending, changes the CoopGame exit state,
detaches handlers and may save through `ForceSaveOnDeath`.

A network respawn needs one coordinated transaction:

1. Confirm one terminal death for the current life; retain its attribution.
2. Retire the old network body while retaining its corpse and pending projectiles.
3. Allocate a new life and safe network identity, assign the kit and spawn.
4. Create/rebind the local controller and every peer's observed representation.
5. Confirm inventory/hands and required peer readiness before releasing play.

`CoopHandler.Players` and observed corpses are keyed by `NetId`. Native damage
packets lack Arena's life/round identity. An Arena `LifeId` alone cannot protect
a new body if an old native packet can still reach a reused `NetId`: prove a safe
entity-generation strategy across the actual Fika routes. Never casually clear
native lifetime dictionaries or reinterpret revive as respawn.

If this requires invasive Fika changes, a temporary elimination-only mode that
loads a new raid between rounds is a valid fallback. Do not advertise DM/Gun Game
until repeated body and weapon replacement passes from both viewpoints.

### 5. Extend shared gameplay only after the lifecycle works

- Only the authority fills bot slots and chooses AI relationships/spawns. Clients
  must not each run Arena's population controller. AI/headless cost is measured.
- The host chooses pickup positions and rotation. Concurrent collection is one
  request/grant operation; the owning inventory applies an acknowledged effect
  once. Test weapon placement, ammo and timed effects on remote bodies.
- Movement stays responsive locally, but approved values come from room rules.
  Test bunnyhop, double jump, air control and climbing from another client's view.
- Keep Akimbo/Molotov and other extra damage sources out of the first baseline.
  Enable each only after correct remote visuals, ammo and damage attribution.
- Killcam/spectating is a separate follow-up: avoid premature raid shutdown and
  ensure permitted targets, camera ownership and post-respawn cleanup.

## Profiles and rewards

**Initial multiplayer uses a separate Arena backend with Arena-only profiles,
supplied kits and no transfer into normal SPT profiles.** No normal stash import,
ordinary session credentials or local reward-signing secret goes to the host.
This provides a usable safety boundary while Fika save/death/disconnect routes
are being tested. Keep protecting/resetting Arena profiles per participant too.

Existing `ProfileSnapshotService` and `ArenaSessionService` are already keyed by
session/profile and can inform that work; the whole backend is not intrinsically
single-profile. However, current schema-3 session switching binds one selected
profile, local directories/processes and loopback control. It drains and stops
the entire ordinary server. It must not run when one user leaves a shared Fika
backend, and local file locks are not distributed ownership.

Later reward options need distinct designs:

- Same shared normal backend: project one authoritative match ledger into
  per-recipient results, preserving profile binding and duplicate-payment records.
  Initially settle at the next planned server startup, not by restarting the
  whole shared server whenever someone returns. Live payout needs separate native
  save coordination.
- Each person's local normal SPT: explicitly enroll a trusted host and a limited
  participation grant. Host-signed results identify issuer, match, participant
  and sequence; the local server owns consent, target profile, rates, caps and
  settlement. Use a separately designed asymmetric receipt contract, not the
  current local HMAC `RewardHandoff` key or client-provided payout amounts.

A signature identifies the issuer; it does not prove an honest match. Profile
integrity and trust in earned rewards are different requirements. The existing
startup payout core may be reused, but current single-`LocalParticipantId`
authorization/restoration evidence is not a multiplayer result contract.

## Hosting and connectivity

All participants need the same reachable SPT/Fika backend and a route to the raid
host. Documented direct defaults are TCP 6969 and UDP 25565 respectively. These
may be on different machines. NAT punching only assists raid connectivity; it
does not expose the backend and is router-dependent. An invite code cannot fix
an unreachable endpoint.

Start on LAN with the existing GPU test hosts, then verify separate Internet
connections and realistic latency/loss. No router/firewall or public endpoint
changes are authorized by this concept. Keep any relay work separate; the earlier
RaidLink planning note is not evidence of working Fika WAN transport.

Headless is a useful later hosting option for the machine without a GPU. It is
still a full EFT/SPT/Fika game instance with rendering disabled and game files/RAM
requirements, not just SPT.Server. Keep host rule/AI code independent of UI/camera
initialization so this remains possible. Verify actual performance before setting
a player/zombie capacity.

## Delivery sequence and acceptance

| Step | Deliverable | Evidence required before expansion |
| --- | --- | --- |
| M0 — baseline | Isolated exact Fika versions, dedicated test profiles, native two-client raid; protocol/room contracts and mocked rule tests | Plain Fika works; wrong protocol/content/peer rejected; no access to normal profile storage |
| M1 — first Arena match | Private 1v1, one life, fixed kits, vanilla Factory, shared countdown/result; no bots, pickups, advanced movement, PFT or rewards | Both sides see identical kits/teams/result; hostile damage works; no enemy coop overlays; each final death counted once; normal profiles unchanged |
| M2 — network lives | Repeated host/client deaths, new bodies, corpse retention, round reset | At least 20 alternating replacements per role; observed hands/inventory/HP correct; delayed/duplicate events cannot affect a later life; no stale entity/camera/score state |
| M3 — first continuous modes | Small DM/TDM, then Gun Game | Shared score/time/kill limit; authoritative winner; replicated kit/weapon swaps; repeated full matches and ordinary solo/Fika controls |
| M4 — coop and scale | Host-owned bots, co-op Zombies, Headless | One population owner; correct bot hostility and wave completion; measured frame/network cost at chosen capacity |
| M5 — extensions | Pickups, movement, spectator/killcam, additional modifiers; PFT separately if desired | Per-feature remote-view, life-transition and concurrent-action tests; no implicit compatibility claim |
| Separate reward milestone | Trusted recipient-specific result transport | Replay/duplicate/wrong-recipient/revoked-consent/forged-result negatives and exact synthetic-profile settlement before real profiles |

For M1/M2 include role reversal, simultaneous lethal hits, self/environmental
death, grenade/bleed attribution, clean quit, lost connection, backend/host crash
and version mismatch. First disconnect policy: explicit abort/no-contest; no
synthetic kill for a missing peer. No host migration, joining mid-match or automatic
mid-life reconnect in the first version. Fika's own reconnect does not restore
Arena's round/life ledger automatically.

Recommended next implementation task: **M0 and the admission/handshake slice of
M1**, with no general Fika unblock. Establish the separate profile boundary and
first-kit path before the first Arena PvP death. Record two-client observations
separately from unit/source checks. Keep current solo behavior covered throughout.

## Source pointers

Current local seams: `ArenaMode.Client/Modes/ArenaMatchService.cs`, `IArenaMode.cs`,
`ArenaScoreboard.cs`; `Combat/CombatPatches.cs`; `Respawn/LiveKitRespawn.cs`;
`Pickups/ArenaPickupRuntime.cs`; `Movement/ArenaMovementScope.cs`;
`ArenaMode.Server/Services/ClientSettingsStore.cs`, `ProfileSnapshotService.cs`,
`ArenaSessionService.cs`; `Rewards/RewardResultContracts.cs`, `RewardRuntimeFiles.cs`,
`RewardReturnQuiescence.cs`; sibling `Redeploy.Core/Api/RespawnApi.cs` and
`Engine/RespawnEngine.cs`.

Related local contracts: [session ownership](ARENA-OFFLINE-SESSION-20261025.md),
[reward capture/transfer](REWARDS-RUNTIME.md), [return and payout](REWARDS-RETURN.md).

Official sources checked 2026-10-26:

- [Fika limits and features](https://wiki.project-fika.com/general-information)
- [Modding events and packets](https://wiki.project-fika.com/modding-fika)
- [Plugin 2.4.6 release](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.6),
  [Server 2.4.2 release](https://github.com/project-fika/Fika-Server-CSharp/releases/tag/v2.4.2),
  [Headless 1.5.3 release](https://github.com/project-fika/Fika-Headless/releases/tag/v1.5.3)
- Pinned 2.4.6:
  [network interface](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Networking/IFikaNetworkManager.cs),
  [FikaPlayer](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Main/Players/FikaPlayer.cs),
  [ObservedPlayer](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Main/Players/ObservedPlayer.cs),
  [CoopGame](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Main/GameMode/CoopGame.cs),
  [CoopHandler](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Main/Components/CoopHandler.cs),
  [damage packets](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Networking/Packets/Player/Common/SubPackets/DamagePacket.cs),
  [pings](https://github.com/project-fika/Fika-Plugin/blob/9572504f9a1a338944013a33e74ee9867b809a80/Fika.Core/Networking/Packets/Generic/SubPackets/PingPacket.cs)
- [Direct hosting](https://wiki.project-fika.com/hosting-a-fika-server/host-using-port-forwarding),
  [NAT punching limits](https://wiki.project-fika.com/advanced-features/nat-punching/fika-nat-punch-server),
  [Headless requirements](https://wiki.project-fika.com/advanced-features/headless-client)
