# Skills Extended compatibility — 2026-10-20

This adapter is limited to **Skills Extended 3.1.4 or newer with the checked
runtime contracts**. Newer matching versions produce one warning. A mismatched
plugin, native binding, receipt caller, or event contract closes Arena admission;
it does not disable ordinary PMC/Scav raids or edit the user's settings.

Reviewed release:
[Skills Extended V3.1.4](https://github.com/CJ-SPT/Skills-Extended/releases/tag/V3.1.4),
source `1e00eee4a1e24b0bc6ae61f8b8fab249145a850a`.
Published `SkillsExtended.Client.Skills.dll` SHA256:
`6af2b7461e39d47b1482e8b16bbf6907bc9e6614dda3a67188e75d5d4e0a3ec5`.
This is the continuation of the
[candidate audit](COMPATIBILITY-CANDIDATES-20261020.md), not a claim that all
Skills Extended functionality has been tested in EFT.

## Intended behavior

Arena suspends the foreign mod's Signals reward delivery and its three initial
player event consumers: First Aid/Field Medicine medical XP and NATO/Eastern
weapon mastery XP. Arena already discards permanent profile progress, so copying
these subscriptions to every replacement player adds risk without a persistent
benefit.

The foreign world-start original still runs, including its Customs door fix.
Only its three exact XP delegates are removed from the Arena player's medical
and mastery events. Other mods' subscribers are preserved. Callback guards also
prevent the foreign consumers from dereferencing their retained Arena player.

No new Arena body receives those event subscriptions. This deliberately covers
the initial kit swap, death respawns, Tactical round changes and live PFT Zombie
wave refills without replaying `GameWorld.OnGameStarted` or the foreign plugin's
initialization. There is no per-frame rebind loop.

All other Skills Extended systems are left as implemented by that mod:
existing skill levels and perks, lock picking, hacking, Signals interactions and
loot, and its own Signals hit-subscription refresh. This patch does **not** disable
all XP producers or all skill functionality. Other profile-contained changes
still rely on Arena's existing snapshot/restore. Movement and medical perk
overlap remains a separate native-test requirement.

One player-facing Info line explains the suspended progression when the exact
Arena world is captured. Routine setup is Debug; contract failures are warnings.

## Persistence boundary

`SignalsRuntime.ApplyXp()` is the only caller of
`SignalsXpLedger.Reserve(string, float)` in the reviewed published client. It
writes `BepInEx/config/skills-extended-signals-xp.json` **before** it invokes
`Player.ExecuteSkill`. A later skill-XP filter would therefore be too late.

The adapter prefixes `ApplyXp()` and returns before the original can reserve a
receipt, update its in-memory delivery state, or execute the reward. It never
opens, clears, replaces, snapshots or restores the journal. Existing ordinary
receipts and a pending ordinary runtime's delivery remain untouched, including
when another Arena world is current.

Ownership follows the Signals runtime's own `World`, not whichever player/world
happens to be the current singleton. Weak exact-world leases survive session
cleanup and subsequent same-map ordinary raids. A delayed old Arena runtime
therefore remains suppressed even if its `World.MainPlayer` now points elsewhere.
There are no profile-ID or map-name-only exemptions.

## Loading and body ownership

There are two distinct Signals boot calls:

1. `SignalsInitPatch` awaits `GameWorld.InitLevel` and calls `Boot`. This can
   precede `LocalRaidStarted` and creation of the native `LocalGame`. The adapter
   cannot prove Arena ownership there and leaves that call untouched.
2. `SignalsLootCompletionPatch` wraps native `BaseLocalGame.SpawnLoot`. After
   native loot completes it calls `Boot` again, even if the runtime already
   exists. In the reviewed native sequence, `BaseLocalGame.Run` has set status
   `Running`, created the local player and received the exact native server
   identity, but has not initialized the bot game yet. This second call captures
   the confirmed Arena world before later initialization can fail.

Loading capture requires the exact native `LocalGame`/singleton/world pair,
PMC settings, matching settings/world/armed maps and the server-confirmed arm
token. When a local player already exists, it must be the world's exact local
player and use the native game's exact profile object. The no-local-player
construction case still requires the same native game, profile and confirmed
identity; it does not admit a selected-but-unconfirmed menu option.

At `Started`, capture also requires the exact `IBotGame`. The foreign world-start
prefix captures the initial event body before the original subscribes it; the
postfix removes only the exact delegates from that body's controllers. The
original and cleanup execute synchronously. Failed loading worlds already
captured at the second Boot remain protected after the arm/session flags clear.

Ordinary worlds, Scav raids, hideouts, wrong-token/same-map worlds and unconfirmed
selections do not gain a lease. Once captured, cleanup does not depend on a live
global Arena flag. The process-lifetime adapter is shut down only with the Arena
plugin, not when a match ends.

Native source checked: `EFT/TarkovApplication.cs` (`LocalRaidStarted`,
`LocalGame.Create`, `Run`, `OnGameStarted`), `EFT/BaseLocalGame.cs` (`Run`,
`SpawnLoot`), `EFT/Player.cs` (skill/controller owners),
`EFT/SkillManager.cs` and `EFT.HealthSystem/BaseHealthController.cs` (event types).

## Contract and failure behavior

No hard reference to a Skills Extended assembly is added.

- Validate plugin GUID `com.cj.skillsextended`, plugin class and assembly identity,
  attributed/loaded/assembly versions and minimum 3.1.4.
- Resolve the exact Boot, world getter, reward entry, world-start postfix, bound
  player field and three callback signatures.
- Verify that the foreign postfix is installed on native `GameWorld.OnGameStarted`.
- Inspect IL member references: the receipt entry has only the guarded reward
  caller; external ledger access, additional bound-player consumers or callback
  registrations reject admission.
- Verify the three native event subscriptions and delegate types before any
  detach is possible. Other event handlers are never enumerated or cleared.
- Verify all adapter Harmony receipts again at menu/final-start admission.
  Partial installation removes only Arena's own patches.
- Unknown runtime identity fails open for unproven ordinary work and latches a
  visible rejection for the next Arena start. Already leased worlds continue to
  be recognized before reading current native settings.

These contracts permit compatible forward versions; they do not certify arbitrary
future changes or third-party patches that replace Skills Extended's behavior.

## Offline verification and remaining native tests

Run:

```powershell
pwsh -NoProfile -File tools/SkillsExtendedCompatChecks/Verify.ps1 `
  -PublishedClient <path-to-SkillsExtended.Client.Skills.dll> `
  -NativeAssembly <path-to-Assembly-CSharp.dll>
```

The harness links all three production adapter files. It executes installed
prefix/postfix routing with synthetic foreign methods and native event fixtures,
checks actual published DLL/native metadata separately, and compiles mutated
production copies that must fail named assertions. It does not pretend to
exercise Harmony's Mono detours or Unity lifetime behavior on desktop CLR.

Completed checks: **93 assertions and 16 compiled negative controls**, followed
by a second passing production baseline. Exact evidence:
`artifacts/compat-first-20261020/skills/checks/cac556ea558b45d4a9a450084a0b317c/`.
The supplied native assembly SHA256 was
`ee25cee1259777b38ed8b3e7841fdc2db3c98540b1469fa539b1ff183476e436`.
Independent source/native-contract review reported no material findings; it
did not perform a GPU test.

Behavioral controls include ordinary–Arena–ordinary progression; unrelated
subscribers; every replacement type; same-map wrong identity; loading failure;
pending ordinary rewards while Arena runs; late Arena rewards after the next
ordinary world; forward versions, metadata drift, additional external writers,
partial install, removed hooks and shutdown ownership.

No GPU installation, deploy or in-game acceptance is included in this change.
Required native cases remain:

- Labs/Customs with a real Signals event and before/after journal/profile checks.
  Factory is not a sufficient reward test because Skills Extended excludes it.
- Initial kit replacement, two death respawns and live PFT wave refill.
- Medical and mastery events before/after replacements, followed by an ordinary
  raid where both progression paths and Signals receipts work again.
- Elite First Aid/Strength interactions with surgery, fractured legs, obstacle
  traversal and Arena movement.
- Cancelled loading after native loot completion and subsequent same-map normal
  raid; verify no surviving old-world event produces XP or journal writes.

Acquisition/decompilation and check logs stay ignored under
`artifacts/compat-first-20261020/skills/`. No foreign source or DLL is packaged.
