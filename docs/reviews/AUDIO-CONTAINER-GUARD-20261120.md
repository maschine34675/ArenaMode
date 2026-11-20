# Audio container return guard (2026-11-20)

Contract of `ArenaAudioContainerReturnGuardPatch` (`ArenaMode.Client/Audio/`), built on the owner's word after the
investigation in [AUDIO-OCCLUDER-EXCEPTION-20261120.md](../reports/2026-11/AUDIO-OCCLUDER-EXCEPTION-20261120.md),
which holds the game's mechanism with file and line references and the native evidence.

## The game bug in short

`AudioSourceContainerData.ReturnContainer` clears a sound container and hands it to `PoolWithoutAlloc.PutObject`,
which writes it into the lowest empty slot without looking for it. An infected bot's spine container that the game's
`ObservedPlayerAudioSourceLimiter` paused and resumed also sits in `SpatialAudioSystem._activeSoundContainers`. At that
bot's death the dead-player coroutine returns it and, in the same frame, `method_26` returns it again through
`UnregisterSourceContainer`. The pool then hands one object to two owners. Seen natively on 2026-11-20 for every one of
6 such containers; the duplicates lived on as containers shared by two infected for the rest of the raid. When a bot
becomes the second owner of a container that the main multi-source occluder still lists under an older id, the game
throws `Can't handle not proposed occlusion test : Combined` every frame until that bot dies, and one-shot sounds the
occluder lists after that entry stay silent.

## What the guard does

- A Harmony prefix on `AudioSourceContainerData.ReturnContainer(AudioSourceContainer)`. When the pool's backing array
  (`_audioContainersPool.gparam_0`) already holds the very container object (reference identity,
  `AudioContainerReturnPolicy.IsAlreadyPooled`), the prefix returns false and the game's method does not run: no second
  `Clear()`, no second slot. Every other return runs as in the game.
- For the path above the skipped call is the second return: the first one has cleared and pooled the container, and
  `UnregisterSourceContainer` has already removed it from the active list and the registry.
- Not changed: the occluder, the limiter, the pool type, any draw (`GetContainer`), any other caller's behaviour for a
  container the pool does not hold.

## Scope

- Only in the exact active Arena raid: `ArenaSession.IsActive` first (an inactive session reads nothing of the game),
  then the exact-raid check on the game and world singletons. Every mode; in practice Zombies, because the limiter
  registers only bots with the simplified (infected) skeleton.
- A normal raid, also one with infected, stays as the game makes it (non-negotiable 2). It is not a loading-time
  exception.
- Fails open: any exception inside the prefix lets the game's method run (Warning once per raid, armed again by the
  raid reset). A failure to enable the patch at plugin load is a Warning and costs only this guard.

## What it does not do

- It does not undo a duplicate that already exists; it is enabled at plugin load, before the first raid.
- One window stays open on purpose: a container that somebody draws between its two returns is not in the pool at the
  second one and is pooled again while its new owner holds it (the game's behaviour for that container).
- A return that the game nests inside another return of the same container is not seen. At the raid's end
  `SpatialAudioSystem.Dispose` returns game-object containers that still have sources; their `Clear()` empties them, the
  empty-container event runs `ClearGoComponents` and a second `UnregisterSourceContainer`, which pools the container
  before the outer call does, and the outer call's prefix ran before both. The pool is dropped with the raid. (Found by
  the adversarial review of 2026-11-20; `SpatialAudioSystem.cs:422-431, 957-962`, `AudioSourceContainer.cs:295-310`.)
- It does not catch an alias of another origin in the occluder. The occluder-side prefix that the report describes as
  the minimal guard (drop a key whose container carries another id) is not built.

## Logging

Debug, once per skipped return: `Arena audio container guard: sound container <id> is already in the pool and was not
pooled a second time.` Warnings: `Arena audio container guard unavailable; a sound container returned twice is pooled
twice as in the game: <message>` (plugin load) and `Arena audio container guard skipped; this sound container is
returned as in the game: <message>` (at most once per raid). Nothing at Info.

## Verification

- Offline: `tools/AudioContainerGuardChecks` (stand-ins with the game's pool shapes, the stand-in method patched with
  real Harmony, 29 source mutants) and `AudioContainerReturnPolicyTests` in ArenaMode.Tests.
- Inlining: the target is a two-call method, and Mono decides inlining when it compiles a caller, from the method's IL;
  load order does not protect against it. An inlined copy would bypass the patch silently. The native Debug line is the
  proof that the patch is reached; if it never comes while a container stands in two pool slots, patch the callers
  (`ClearPlayerOcclusionWithDelay`, `UnregisterSourceContainer`) instead.
- Native acceptance (to be run): a Zombie raid on SPT-GPU-2 with the pool monitor of the report
  (`artifacts/audio-occluder-20261120/monitor3.mjs`): the Debug line appears when an armed infected dies (the proof
  that the patch is reached and not inlined), no object stands in two pool slots and no two players own one container
  for the whole raid, and the loaded client matches the deployed hash.

## Rules for changes

- Keep it a sibling of `ArenaBackupProfileDuplicatePatch`: own file, own state, own enable and reset; never merge the
  two.
- Never patch `PoolWithoutAlloc<T>.PutObject` instead: a generic over a reference type shares its code with every other
  such pool in the game.
- Do not add a finalizer on `MultiSourceSpatialAudioOccluder.GetTargetOcclusionType`: it would hide the exception and
  can make the occluder release a bot's persistent source (report, section 4).
