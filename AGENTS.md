# ArenaMode — working notes for agents

A total-conversion mod for SPT 4.1 that turns Tarkov into an Unreal-Tournament-style
arena: instant respawn, kits instead of loot, and a profile that is never touched.
No Fika dependency. Arena is solo by default; the optional exact-version
PitFireTeam 0.10.0 compatibility path supports protected Tactical 5v5 matches.
Four modes: Endless Deathmatch, Rounds, Gun Game, Tactical.

Read this before changing anything. Most of it was learned the expensive way.

---

## Layout

| Path | What |
|---|---|
| `ArenaMode.Client/` | BepInEx plugin (netstandard2.1, Harmony). The bulk of the mod. |
| `ArenaMode.BigBrainCompat/` | Optional BigBrain 1.5.0 action owner for post-death Tactical 5v5 pursuit. |
| `ArenaMode.Server/` | SPT server mod (net10.0) + the Blazor config page. |
| `ArenaMode.Shared/` | Linked source compiled into BOTH — routes and DTOs only. |
| `tools/` | Offline generators for the kit catalogue and the weapon ladder. |

External, outside the repo:

| Path | What |
|---|---|
| `../_decompiled/` | The complete decompiled game source. **Verify every claim about game internals here.** |
| `../_reference/` | Other people's mods, for technique. Licence rules below. |
| `D:\SPT41\` | The game. Client DLLs deploy to `BepInEx\plugins\`. |
| `D:\SPT41\SPT_Runtime\` | The server. Server DLL deploys to `user\mods\ArenaMode\`. |

## Build and deploy

```bash
dotnet build ArenaMode.sln -c Release   # builds and deploys both halves
dotnet test  ArenaMode.Tests            # ~30 tests, under a second
```

All mod projects copy themselves into place on build. The tests cover the SERVER
half and the generated data; the client half is Harmony patches against Unity and EFT
types that cannot load outside the game, so **for anything client-side, verification is
still a raid.**

- **Client changes** need the game restarted, nothing else.
- **`config.json` changes** need nothing: hot-reload via the config page.
- **`kits.json` / `ladder.json` changes** need the SERVER restarted — the catalogue is
  read at load.
- **Config page**: `https://127.0.0.1:6969/arenamode` — **https**, not http. It is a
  self-signed certificate, so `curl` and most browsers fail silently on `http://`.

Logs: client `D:\SPT41\BepInEx\LogOutput.log` (local time), server
`D:\SPT41\SPT_Runtime\user\logs\spt\sptYYYYMMDD.log` (**UTC** — the two disagree by
your offset, which has cost hours before).

## Tests

`ArenaMode.Tests/` — xunit, no game required (checks that need the item database skip
themselves; point `SPT_DATABASE` at it to run them elsewhere).

They exist because these are the things that broke repeatedly and quietly:

- **Kit catalogue invariants** — unique ids, one root, one primary weapon, no orphaned
  parents, no slot filled twice, every template real. The client REFUSES a kit that
  breaks these, so the symptom is a player respawning naked mid-raid rather than an
  error at build time.
- **Ladder draw** — the right number of rungs, still ordered weakest to strongest, and
  every ammunition site on a weapon holding the same cartridge. A revolver whose camoras
  disagreed with its chamber shipped once and threw nothing.
- **Location mutation** — raid time, bot cap, boss handling, and that every generated
  wave names exactly ONE real zone. The engine matches a zone by exact string, so a
  comma-separated list matches nothing and the wave is dropped in silence; that shipped,
  and it emptied the Lab.

When adding a test here, check it fails against the bug it claims to catch. The zone
test above was verified that way — reintroduce the comma and three tests go red.

## Non-negotiable

1. **The profile is sacred.** An arena raid must cost and gain nothing. Two layers
   protect it: an exact-ID `EndLocalRaidAsync` prefix that replaces the complete native
   post-raid pipeline, and a snapshot/restore around the raid. Never weaken either.
2. **Normal raids must be bit-identical.** Every patch starts with an
   `ArenaSession.IsActive` gate and fails open to vanilla. A setting that leaks into a
   non-arena raid is a bug even if it is harmless.
3. **Licences.** ABPS, APBS, SAIN, RaidOverhaul, MoreBotsAPI, SVM, PitFireTeam, BigBrain:
   technique only — read them, never copy code, never bundle their DLLs. MIT/Apache
   sources (ORBIT, DragonDen, FieldKit, traumacore, bullettime) may be adapted with
   attribution. LacywayPvETweaks and LetMeRightClick are proprietary: do not touch.
4. **Adversarial review after every implementation.** This has caught a genuine
   critical bug in nearly every milestone — a hands softlock, a permanent-invulnerability
   latch, a deadlocked game mode. Do not skip it because a change looks small. The tests
   are a floor, not a substitute: none of those three would have been caught by one.

## Traps this project has already fallen into

Each of these cost a debugging session. They recur.

**`MongoId` is a struct.** `FirstOrDefault()` on an empty sequence returns twenty-four
zeroes, never null. A null check will not fire and you will write a node referencing a
template that does not exist.

**Max-armorClass over a preset counts parts you are about to remove.** Bit us twice:
bare plate carriers scored class 6 (the plate slots' filter, not the contents), and the
Maska helmet scored class 6 for a face shield the generator drops. Always compute over
the nodes that survive. Same for a plate carrier at runtime — its own template is class 0.

**Any item conjured at runtime needs its bundles preloaded.** `ObjectsFactory` returns
null for a template with no prefab pool, the NRE lands *after* the weapon-change
operation opened and before anything closes it, `IsChangingWeapon` stays latched, and the
player has empty hands — no weapon, no meds, no grenades — for the rest of the raid.
`KitBundlePreloader` exists for this. Cache by TEMPLATE, never by kit id: Gun Game names
merged kits `basekit+rung`, so an id-keyed cache never hits.

**`GlobalConfiguration` is shared with bots.** Never mutate it. The two exceptions are
argued in place: the deploy countdown (one reader) and the vaulting limits (bots have no
vaulting component at all — `InitVaultingComponent` returns immediately for AI).

**Server: only prefixes on async methods.** A postfix fires at the first suspension
point, not at completion. "After the raid" logic lives in its own route.

**`OperationResult<T>` is not a bool.** Discarding it means the container mutates but
nothing is notified — items stay visible, models stay in the pool. Call `RaiseEvents`.

**BSG's own flags lie.** `IsBoss()` is true for PMCs, exUsec, arenaFighter and more —
use `IsPureBoss()`. `ShallBossAttack == true` means "do NOT attack". `ActivateBotsByWave`
is a boss spawn path despite the name.

**The client caches server answers per game process.** Bot difficulties are fetched once
and never again. Anything that must vary per raid cannot live there.

## Where the knowledge is

There is no design doc. The reasoning lives in two places, both worth reading before
touching an area:

- **Commit messages.** 77 of them, and they explain *why*, including the mistakes. `git
  log` over a file you are about to change is the fastest way in.
- **Doc comments.** Every non-obvious decision is argued at the code, not summarised
  elsewhere. If a comment says a rule is deliberate, it is — and if you find one that
  disagrees with the code, the comment is a bug too.

## State

M0–M7 are done and playtested. The Blazor config page, Labs as a third map, and the
Tactical mode are in and lightly tested.

Open:

- **M8 polish** — KillcamReplay short arena replay (the compat exists but only skips),
  README, release packaging. Never bundle ABPS/APBS/SAIN DLLs.
- **Tactical** needs a longer playtest; only the first rounds have been played.
- **Performance** degrades around minute 20 of a long raid. Unexplained; the respawn
  curve was a separate bug and is fixed. Needs a long test raid before guessing again.
- **Bounding the play area** on large maps (filter `SpawnPointParams` on the per-raid
  clone).
- **PitFireTeam 5v5** — the optional 0.10.0 compatibility path now has a guarded
  best-of-N round lifecycle and still needs a GPU playtest. It preserves the PMC entry
  loadout after a death, rebuilds fresh follower/opponent bodies between rounds, offers
  post-death third-person spectating, refreshes both teams' pursuit knowledge, and uses
  an optional companion for exact BigBrain 1.5.0 to own bounded movement toward live opponents while
  the PMC is down. It excludes teammate bots/corpses from Arena cleanup/counts and blocks
  both SPT-profile effects and PitFireTeam's separate teammate persistence. Every
  teardown/rebind step fails closed as no-contest when the exact reflected contract or
  body identities drift.

## How to work here

Create a commit after every completed change, including its tests and documentation;
do not leave finished work uncommitted or combine unrelated changes. Use small,
self-contained commits with a message that explains the reasoning. Verify game
internals against `../_decompiled/` rather than assuming — the decompiled source is
complete and it has contradicted confident guesses many times. When a playtest disagrees
with your explanation, the explanation is wrong; measure before you theorise again.
