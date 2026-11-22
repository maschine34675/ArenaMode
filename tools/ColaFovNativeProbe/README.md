# Cola/FOV native observer

Temporary UiBridge actions for the 2026-11-22 compatibility trial. This project is
not part of the solution, deploy targets, offline harness register or release payload.
Its project suppresses deployment; stage its DLL explicitly only on a reserved test host.

The mutating actions require a living disposable profile named `ColaFovTest`.
`observe` writes full JSON snapshots to the game's temporary directory and returns
the path, avoiding UiBridge's text truncation. It reads physical player/controller
identity, CommonLib delegates, FOV ownership and Cola transient state. No action
invokes the compatibility adapters or writes their fields.

* `transition death` triggers an actual native death, leaving the normal Redeploy
  prompt or Arena respawn driver in control.
* `transition swap` calls the real shared Core pipeline and retains the outgoing
  living body until raid end. This exposes controllers that continue running after
  their body has ceased to be the local player.
* `downedSwap` requires a genuinely consumed Quick Revive bottle and the native
  revive augmentation plus a non-Cola stim. Lethal native damage must enter Cola's
  downed state before the real replacement is requested. It does not manufacture
  a downed flag. The old body's ten-second deadline must not kill its replacement.
* `offering` exercises Cola's real pending queue; `route=true` calls its real server
  route with a one-rouble fixture. Run only with the disposable profile: the normal
  raid control legitimately receives that mail; the Arena trial must reject it.
* `supply` builds an unmodified weapon preset from the separately staged
  `BepInEx/plugins/ColaFovTestWeapons.json`. The fixture is an array of native item
  arrays, with internal item IDs, parents, slot IDs and ammunition stack counts.

Back up all canonical profiles/configs/PFT data first, pause synchronization on
the reserved host, and record the loaded candidate identity. Keep source, loaded
binaries and observations together. Afterward remove this DLL, the weapon fixture,
the probe's temporary whitelist override and the disposable profile's sidecars;
restore changed settings byte-for-byte. Do not replace unrelated live profiles.

Results and limits: [native report](../../docs/reports/2026-11/COLA-FOV-NATIVE-20261122.md).
