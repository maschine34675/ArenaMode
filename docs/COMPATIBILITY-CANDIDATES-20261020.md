# Additional compatibility candidates

Reviewed on 2026-10-20 against ArenaMode **0.1.2**, source
`e399658cf7e5c320fee6266ad27403068cc88337`, for SPT 4.1.6.

This is a static compatibility assessment, **not newly implemented support or
blocking**. No candidate was installed or executed and no GPU test was performed.
The existing supported-mod list remains in [DEPENDENCIES.md](DEPENDENCIES.md).
Recommendations below must preserve ordinary PMC/Scav behavior.

Official sources and published DLLs were inspected for eleven candidates.
**ISB Aishi is source-only:** its 4.33 GB release was not downloaded; the fetched
source/project files were verified against the pinned Git tree. Binary equivalence
and actual in-game symptoms remain unverified. A successful startup would not
establish compatibility with Arena's initial kit swap, respawns, retained corpses,
round/wave transitions or protected profile restoration.

## Recommendation matrix

| Candidate | Reviewed version | Recommendation |
|---|---|---|
| [Skills Extended](https://sp-mod.com/mod/2383/skills-extended) | 3.1.4 | Add scoped protection for the external Signals receipt journal and rebind/suspend old-body medical/mastery listeners. Test movement stacking. |
| [Disable Traitor Scavs](https://sp-mod.com/mod/1474/disable-traitor-scavs) | 2.1.0 | No concrete conflict found; no adapter or block justified. |
| [Headshot Damage Redirection](https://sp-mod.com/mod/2953/headshot-damage-redirection) | 1.9.0 | Test protection/counter and damage-order behavior; no blanket block justified. |
| [ISB Special Forces](https://sp-mod.com/mod/2485/isb-special-forces) | 2.0.1 | Recommend an interim Arena-only block until dedicated persistence, event and spawn isolation exists. MoreBotsAPI support is insufficient. |
| [ISB Aishi](https://sp-mod.com/mod/2478/isb-aishi) | 2.0.4, source-only | Recommend an interim Arena-only block until anomaly knowledge persistence and conditional death prevention are isolated. |
| [Pause Continued](https://sp-mod.com/mod/3170/pause-continued) | 1.5.0 | Suppress its pause activation only in Arena until a shared body/timer pause contract is implemented. |
| [Sage's Door Overhaul](https://sp-mod.com/mod/3144/sages-door-overhaul) | 1.0.0 | Keep unblocked; test destruction, AI traversal and death during door interactions. |
| [SPT Stance Overhaul](https://sp-mod.com/mod/3136/spt-stance-overhaul) | 0.0.2 | Retire old-body controllers and modifier subscriptions; otherwise use an interim Stance-only Arena block. |
| [SPT Realism Common Lib](https://sp-mod.com/mod/3135/spt-realism-common-lib) | 0.0.2 | Separate scoped lifecycle/reload integration. Do not block solely because it is a Realism dependency. |
| [Broken Limbs Don't Heal](https://sp-mod.com/mod/3121/broken-limbs-dont-heal) | 1.1.0 | Handle together with Effects Manager's body-rebinding adapter. |
| [EFT Effects Manager](https://sp-mod.com/mod/3123/eft-effects-manager) | 1.1.0 | Rebind the library's current body, callbacks and temporary penalties after Arena body replacement. |
| [Sage's Incendiary Grenade](https://sp-mod.com/mod/3113/sages-incendiary-grenade) | 1.0.2 | Add thrower attribution, round cleanup and bounded fire-escape targets; otherwise exclude its item from Arena, or block Arena if reliable item exclusion is unavailable. |

The interim blocks and pause/item restrictions in this table are proposals.
**ArenaMode 0.1.2 does not yet enforce them.**

## Findings and counterevidence

### Skills Extended

`SignalsRuntime.ApplyXp` reserves a receipt before granting skill XP.
`SignalsXpLedger.Reserve` writes
`BepInEx/config/skills-extended-signals-xp.json`; that file is outside the
`SptProfile` restored by Arena. This establishes an external-journal gap,
**not proven persistent skill XP gain**: profile skill values are still restored.
Factory day/night are excluded by SignalsMaps, so a Factory-only test misses it.

The `OnGameStarted` patch binds medical-effect and weapon-mastery listeners to
the initial local body. Arena replaces that body during supplied-kit setup as
well as death respawns, without replaying the world-start event. Other subsystems
are safer: SignalsRuntime resolves the current player and rebinds its hit listener.
First Aid walking/use-time and Strength obstacle restrictions also overlap Arena's
elite buffs and surgical/air movement; overlap alone does not prove a crash.

Protect reward delivery before the receipt write, preserve ordinary journal
entries, and retire/rebind only Arena's old-body listeners. Do not reset all
skills or delete the external journal.
Sources: [receipt writer](https://github.com/CJ-SPT/Skills-Extended/blob/1e00eee4a1e24b0bc6ae61f8b8fab249145a850a/Client/SkillsExtended.Client.Skills/Skills/Signals/SignalsXpLedger.cs),
[initial subscriptions](https://github.com/CJ-SPT/Skills-Extended/blob/1e00eee4a1e24b0bc6ae61f8b8fab249145a850a/Client/SkillsExtended.Client.Skills/Skills/Shared/Patches/OnGameStarted.cs).

### Disable Traitor Scavs

The complete released implementation changes
`InRaidConfig.PlayerScavHostileChancePercent` at server startup, either to zero
or a configured value clamped to 0–100. It adds no client damage/lifecycle hook,
extra spawn loop or separate progression file. Arena enters as PMC and owns its
mode-specific relationships. No concrete conflict is established; a routine
ordinary-Scav/Arena smoke test is still absent.
Source: [2.1.0 release](https://github.com/acidphantasm/disabletraitorscavs-csharp/releases/tag/2.1.0).

### Headshot Damage Redirection

The released `Player.ApplyDamageInfo` prefix splits local-player damage across
body parts using direct `ActiveHealthController.ApplyDamage` calls and retains
attacker/source fields. Those calls still reach Arena's lower staging/friendly-fire
guards and native `DamageCoeff`, which Core uses for spawn protection.
**A general spawn-shield or friendly-fire bypass is not demonstrated.**

Its global hit-limit counter resets on scene load, not per Arena life. The
prefix's counter, random roll and messages can run before another prefix rejects
the hit, depending on Harmony order. Verify shield spending, contact/assist
bookkeeping and the configured hit limit before choosing a small exact-body/
protected-hit guard. Do not silently redefine its advertised per-raid limit.
The released DLL also logs some post-damage body-part values at Info regardless
of its Debug setting; that potential log noise is not generated by Arena.
Source: [actual 1.9.0 release](https://github.com/AntiGravPilot/Solarint-HeadshotDamageRedirect/releases/tag/1.9.0);
the tagged source is older than the published behavior.

### ISB Special Forces

The mod owns `data/revenge.json`, `data/quest_truce.json` and
`data/spawn-progression.json`. Its client starts separate raid/progression
records, and its own outer `/client/match/local/end` router calls revenge,
progression, loyalty and spawn completion. These paths are outside Arena's
replacement of native `LocationLifecycleService.EndLocalRaidAsync` and outside
the existing MoreBotsAPI adapter. Some changes depend on settings and previous
records; zero SF spawns is not sufficient evidence that no state was written.

It also owns checkpoints and scene/event/spawn systems for its relevant roles.
This is not a claim that it takes control of every ordinary PMC. An adapter needs
exact raid/request ownership at producers, delayed senders and server commits,
plus scene-event and population isolation. Turning off a single AI setting or
blocking only extra bot creation does not protect these external files.
Sources: [outer router](https://github.com/Sam-C-137/ISB-Special-Forces/blob/c8c58b332b42d56897835a660e153febe4c6d2aa/Server/ISBRaidLifecycleRouter.cs),
[revenge controller](https://github.com/Sam-C-137/ISB-Special-Forces/blob/c8c58b332b42d56897835a660e153febe4c6d2aa/Server/Controllers/ISBRevengeController.cs).
The relevant published client/server paths were also decompiled.

### ISB Aishi

An in-raid anomaly analysis sends `/aishi/anomalous-items/reveal`. The server
saves the profile and then stores discovered knowledge in `kte_data.json`
beside its assembly. Later requests merge that knowledge back into the profile.
Arena's profile rollback cannot undo the separate journal and may restore an
analyser charge while external knowledge remains. An actual analysis is required;
installation alone does not prove a write.

A carried, charged anomalous defibrillator can veto fatal `ApplyDamage`,
`ChangeHealth` and native `Kill`, including an Arena GiveUp path. This is an
item-conditional death conflict, not a property of every Aishi loadout. Its runtime
does resolve replacement players, so an initial-player-only cache is not the
reason for the recommendation. A separate post-raid recovery wrapper needs actual
Harmony-order testing; no additional snapshot bypass is inferred from it alone.

Protect anomaly analysis and delayed server writes for exact Arena requests,
and let Arena's native death policy win for its current health controller.
Do not remove the ordinary trader/content or erase the journal.
Sources: [journal](https://github.com/Sam-C-137/ISB-Aishi/blob/84781e0437bde157b1d0fa71fcad78b983a2f8dc/AishiKteData.cs),
[reveal persistence](https://github.com/Sam-C-137/ISB-Aishi/blob/84781e0437bde157b1d0fa71fcad78b983a2f8dc/AishiAnomalyReveal.cs),
[fatal hooks](https://github.com/Sam-C-137/ISB-Aishi/blob/84781e0437bde157b1d0fa71fcad78b983a2f8dc/AishiKeysV2/AishiKeys/AishiAnomalyS2.cs).
These findings remain source-only.

### Pause Continued

The published pause controller captures `GameWorld` and `MainPlayer` at world
start. It later disables that cached player, pauses effects, toggles other actors
and changes timeScale. Arena's kit swaps and respawns invalidate the cached owner.
Native update patches do not stop Arena's separate plugin Update.

Not all Arena clocks continue while paused: scaled match time can stop correctly.
However, Team DM checks its 300-second preparation watchdog using
`Time.realtimeSinceStartup` **before** its scaled update throttle, so a pending
preparation can fail during a long pause. Other wall-time deadlines can expire
when execution resumes. Arena's protected native exit already waits for timeScale
to become positive; a timeout verdict is not proof of immediate exit while paused.

The first safe adapter should suppress Pause activation for the exact confirmed
Arena world, including loading, with a visible explanation. A full pause feature
needs current-body ownership and explicit handling of Arena/Core asynchronous
deadlines, spectator state and previously inactive bots. Rebinding one field or
checking timeScale alone is insufficient.
Source: [controller matching the continued plugin](https://github.com/Senyasensey2026/Pause/blob/f647c78cecc51ef9c3b8aeb4e9251a2fad6908a0/source/PauseController.cs).

### Sage's Door Overhaul

The destruction registry already excludes keycard, sliding, switch,
extraction/trigger and unsupported doors. Its runtime resolves the current
`MainPlayer`; weapon-kick offsets reset when the player changes. Arena's grid
uses private colliders/navigation obstacles, not EFT Door components, so the
door registry does not directly target it.

Destroyed doors and replacement debris still change local collision/navigation.
Test SAIN traversal, partial openings, spawn booths and round starts after
destruction. Also test death while peeking/kicking. Native death releases the
current door, and the inspected peek callers supply no completion callback:
a stale callback or hands lock was **not** established. If a pending peek survives
a body swap, cancel only that owned interaction. No blanket block is justified.
Source: [reviewed door implementation](https://github.com/SageSPT/Door-Overhaul/tree/058a2b6c34e7924beea9381f0763b2138986e604).

### SPT Stance Overhaul

Each local player receives a StanceController and its own static modifier handles
and event subscriptions. Cleanup occurs on destruction. Arena deliberately keeps
dead player bodies, so their controllers/handles can remain while a new controller
is created. The old controller's Update and callbacks consult the **new global**
player/stance state without verifying their own body.

Consequently multiple retained controllers can contribute the same stance/reload
bonus through multiplied handles. Disabling only Arena's speed bonuses does not
resolve ownership. Living kit swaps eventually destroy the old object; they must
not be described as identical permanent duplication.

Retire old Arena subscriptions/handles before binding the new controller, make
retained corpse controllers inert, and make later destruction idempotent. Review
reload/movement ownership and Akimbo, vault/ADS and spectator visuals separately.
If this cannot be validated, block only Stance for Arena temporarily rather than
removing its shared library for every dependent.
Sources: [controller](https://github.com/space-commits/spt-stance-overhaul/blob/352ef1becc938b172963075ee436d57521676da6/src/Controllers/StanceController.cs),
[reload subscriptions](https://github.com/space-commits/spt-stance-overhaul/blob/352ef1becc938b172963075ee436d57521676da6/src/SubSystem/StanceReloadSpeedSystem.cs).
Fontaine's FOV Fix is an additional advertised prerequisite, not independently
certified by this review.

### SPT Realism Common Lib

This is separate from full SPT Realism and from Stance. Empty movement-modifier
collections are neutral; their mere presence does not prove a movement conflict.
The library nevertheless installs active hooks even without Stance.

The published `PlayerState` cleanup method is named **`onDestroy`**, with a
lowercase `o`, so Unity does not invoke it as `OnDestroy`. Static subscriptions can
retain old bodies even after living replacements are destroyed. Events catch
handler exceptions, so this is evidence of stale callbacks/retention, not proof
that every raid crashes. Other cleanup paths look up global state rather than the
originally subscribed instance and need ownership review.

Its check-ammo/chamber, bolt, malfunction and rechamber hooks also write/clamp
animation speeds independently of Stance. Add narrow old-body cleanup and review
the actual ordering with Arena's reload/bolt handling. Never clear every subscriber
or global modifier list. A library-wide block solely because of its name or
dependency role would reject unrelated dependents.
Sources: [PlayerState](https://github.com/space-commits/spt-realism-common-lib/blob/84255a4a49357ec125d43af1349e6823d2453bbe/src/StateControllers/InstanceState/PlayerState.cs),
[reload hooks](https://github.com/space-commits/spt-realism-common-lib/blob/84255a4a49357ec125d43af1349e6823d2453bbe/src/Patches/ReloadPatches.cs).

### EFT Effects Manager and Broken Limbs Don't Heal

Effects Manager binds its cached `RaidPlayer`, health/effect callbacks, temporary
skill bonuses and movement penalties once at `GameWorld.OnGameStarted`, and
cleans up at world disposal. It does not follow an Arena body replacement, which
can happen at initial kit assignment before the first death.

Broken Limbs registers a nonpersistent healed-fracture effect and checks the
library's exact cached controller for re-fracturing and penalties. After a swap,
the new body fails those checks. Positive-damage and finalizer protections already
exist; a profile-save leak or re-fracture from fully blocked damage was not found.

One shared adapter should detach old callbacks/temporary penalties and bind the
confirmed replacement while preserving effect registrations and ordinary state.
Assigning `RaidPlayer` alone is insufficient; globally replaying world-start or
clearing all effects is unsafe.
Sources: [Effects Manager](https://github.com/harmonyzt/EFTEffectManager/tree/7ec87db63f77a4ac066c0c523b32156fe22c2624),
[Broken Limbs](https://github.com/harmonyzt/BrokenLimbsDontHeal/tree/1691d5e57e324fabf86959241b30753c4f9e1f4d).

### Sage's Incendiary Grenade

SAGA-6 fire creates Flame damage with `DamageInfo.Player=null`: the throwing
player is not retained. Arena cannot identify friendly fire or assign the owner's
kill/assist credit. Lower staging and native spawn-damage guards still apply.

Sage clones RGN into its own `6aabad0ff49370bbdc709173` item. Arena's Molotov has a
different private ID (`a12e00000000000000000001`) and donor. **No template collision
or mutation of Arena's grenade was found.**

Fire cleanup is world-scoped, not tied to Arena round changes. Its BigBrain escape
layer has priority 200, above Arena pursuit 74/fire avoidance 79, but only activates
near its own fire. Sampled destinations have no Arena-area check. This establishes
specific attribution/round/boundary integration work, not universal bot takeover
or a measured memory leak.

Retain the thrower/body/world through fire nodes, retire stale round work and
validate escape destinations. If reliable attribution is too invasive, exclude
the exact item from Arena entry/creation/throw paths; use an interim Arena-only
block if that cannot be guaranteed. Preserve ordinary raids.
Source: [1.0.2 implementation](https://github.com/SageSPT/Incendiary-Grenade/tree/ed13a8177d5d8067a21ca9a92f84ca5bc95030d4).

## Identity cautions and evidence

- Headshot's `1.9.0` tag still declares 1.8.0 and lacks the published hit limit.
  Findings use the actual release DLL, runtime GUID `com.AntiGravPilot.dmgRedirect`.
- Disable Traitor Scavs' published 2.1.0 DLL embeds older informational commit
  `63201c7`; the decompiled implementation was checked rather than trusting it.
- Pause's v1.5.0 tag retains the original GUID; the actual continued DLL uses
  `com.rejzbihai.pause`. The newer matching main source is pinned separately.
- Stance/Common Lib plugin versions are 0.0.2 despite file versions 1.0.0.0.
- Broken Limbs' plugin version is 1.1.0 despite assembly version 1.7.0.0.
- Effects Manager release 1.1.0 has assembly version 1.0.0.0 and **no BepInPlugin**.
  Its Harmony ID is not a Chainloader plugin registration. Validate its assembly
  and API rather than assuming a Forge GUID will be in PluginInfos.

Selected published DLL SHA256 identities:

| DLL / component | SHA256 |
|---|---|
| Skills Extended client Skills | `6AF2B7461E39D47B1482E8B16BBF6907BC9E6614DDA3A67188E75D5D4E0A3EC5` |
| Disable Traitor Scavs | `D0E838AF4A13D881CAC89833F6087FCA5D01ED5A6DD630C11C8A26E26D2F09B9` |
| Headshot Damage Redirection | `B99E863F1D831F9295F17BF7810FDB9C42FB1AC59EE19B5AA95C1731ED0E138C` |
| ISB Special Forces client | `BD707324E25AC5EEA97D61D97C835DC25E581174E392CEA2F757619015B986F8` |
| ISB Special Forces server | `02AB31720997BC63E8E7EDDC22F1AE78FD560572BCD83D1DE3277D9673D4F204` |
| Pause Continued | `4C58ED8A8CE4DF38ED0D87E77FB8A6A28DF8822DED65857874702F1E5ECC3254` |
| Door Overhaul | `1F8B156EC1487C1EA05A31C8CB307A378B987BD742FB0FC5A0D4293096857AD2` |
| Stance Overhaul | `76644097C498F5FF0D2C8B1CC741829256A0B93C753A739DB13711321A58FBF7` |
| Realism Common Lib | `05DCF71B1323DDAD519212354FE2678BB7B11DAF86A6843912FFE2920827D92E` |
| Broken Limbs | `415F05A729F49E63FDF80CE3251252391C6EF467CE8D8185F7E880A7C6BDC1C2` |
| Effects Manager | `CEC8B4A666DA448CDFBC42CA428A70274327BFAC13EC51BCA2C75C7D051DBF62` |
| Incendiary client | `48B74141627F5F77769CA4FCB21A8B12B4650F73D4D014A37F6325FAE2B46337` |
| Incendiary server | `0F6F041FF75577EB4BB237B44649952FD3188B2516F6B7939710A70522E916EF` |

Local, ignored evidence is under `artifacts/candidate-audit-20261020/` in
`ai/report.md`, `health/report.md` and `systems/report.md`, with release metadata,
immutable sources, targeted decompilations and complete acquired DLL inventories.
Those files are not part of the release and no foreign code/assets were added
to the tracked repository.

## Proposed implementation/test order

1. Protect external persistence first: interim Arena admission for ISB Special
   Forces/Aishi; scoped Signals reward and old-body guards for Skills Extended.
2. Add the exact-Arena Pause suppression, then the shared Effects Manager/Broken
   Limbs rebind. Test ordinary–Arena–ordinary, initial kit swaps, repeated death
   and living PFT wave refills.
3. Handle Common Lib/Stance lifecycle and modifier ownership as a separate
   integration. Compare Common Lib alone and with Stance; include retained corpses,
   Akimbo, reload/bolt handling, vault/ADS and spectators.
4. Integrate or exclude SAGA-6. Test FF off/on, kill credit, dead throwers, round
   transitions, PFT and Customs boundaries.
5. Perform narrower native checks for Headshot and Door Overhaul. Keep Disable
   Traitor Scavs unblocked unless a real counterexample appears.

No FPS result, reproduced runtime exception, profile before/after experiment or
general compatibility certificate is claimed by this review.
