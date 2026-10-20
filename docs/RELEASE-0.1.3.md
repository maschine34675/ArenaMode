# ArenaMode 0.1.3

Changes since 0.1.2, for SPT 4.1.x. Native checks used SPT 4.1.6.

## Changes

- **Skills Extended 3.1.4+:** isolate Signals reward receipts and the mod's
  initial-player medical/weapon mastery XP subscriptions during protected Arena
  raids. Preserve the journal, other features and ordinary-raid callbacks.
- **Recoil Rework Legacy 2.0.0+:** transfer player components and caches when
  Arena replaces the local body. This prevents the old singleton from causing
  the replacement player to be destroyed during initialization.
- **Pause Continued 1.5.0+:** prevent new pause activations in confirmed Arena
  raids and show a short explanation. Ordinary raids and unpause/cleanup paths
  retain their normal behavior; saved settings are not changed.
- **EFT Effects Manager / Broken Limbs Don't Heal:** move effect ownership to
  the current Arena body, retire old-controller contributions and preserve
  separately owned temporary skill bonuses/penalties across replacements.
  This does not restart the foreign raid lifecycle or clear global effect state.
- **ISB Special Forces and ISB Aishi:** block Arena when either client or server
  component is present because their independent progression stores are not
  protected. Display the reason in the menu. Normal PMC/Scav raids and Arena
  recovery remain available.
- Expand the compatibility documentation, including the English dependency
  overview and the reviewed candidates that do not yet have dedicated adapters.

## Compatibility and test boundaries

These changes target Arena's protected lifecycle. They do not globally disable
other mods or change their saved configuration. Newer dependency versions are
attempted with API checks and warnings, not guaranteed compatible.
See [the current compatibility matrix](DEPENDENCIES.md) for versions, requirements
and remaining restrictions.

Native tests covered initial kit replacement, two Deathmatch death/respawn cycles,
living and dead-player PFT-Zombie wave refills, real weapon firing, ordinary raids
before and after Arena, the visible pause notice and ISB client-only refusal.
The PFT test used one companion; its dead-player transition followed the existing
stuck-wave watchdog, not a companion combat victory.

The tests did **not** exercise a real Signals reward, every Skills Extended perk,
actual fracture treatment/re-fracture, the installed ISB server or Aishi,
failed-replacement recovery, Tactical PFT or full Recoil camera/ADS/Akimbo behavior.
Server-only ISB admission has offline checks. The version-only release build is
not another native gameplay test. Full evidence and limits:

- [Combined native checks and test-mod cleanup](GPU-COMPAT-SECOND-20261020.md)
- [Skills Extended](SKILLS-EXTENDED-COMPAT-20261020.md)
- [Recoil Rework Legacy](RECOIL-REWORK-COMPAT-20261020.md)
- [Pause Continued](PAUSE-COMPAT-20261020.md)
- [Effects Manager / Broken Limbs](EFFECTS-COMPAT-20261020.md)
- [ISB admission](ISB-ADMISSION-20261020.md)

Skills Extended adds its own persisted skill entries outside Arena. Removing
that mod/prepatcher can leave profiles that the unextended server cannot read.
Back up profiles before adding or removing it; Arena's adapter is not a general
uninstaller or protection for ordinary-raid changes.

## Updating

- Close the game and server; back up profiles and customized configuration.
- Merge the ZIP's **BepInEx** and **SPT_Runtime** folders into the SPT root.
  Update both Arena client DLLs and the server together, and remove duplicate
  old Arena DLLs. Preserve customized kits, ladder and configuration files.
- Keep one copy of the bundled **Redeploy.Core 1.0.8**, byte-identical to the
  published Redeploy 1.0.0 library. Standalone Redeploy is not required.
- The bundled BigBrain integration is a library and remains inactive without
  BigBrain. Optional third-party mods and development/test tools are not bundled.

The GitHub release includes the exact source revision, ZIP hash, build/test
results and VirusTotal links for all four packaged DLLs. Older releases remain
available.
