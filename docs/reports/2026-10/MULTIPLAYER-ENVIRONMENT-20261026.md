# Fika multiplayer test environment — inventory

Read-only installation audit, **2026-10-26**. The user corrected the VM path to
`D:\SPT41Fika`; the older `D:\SPTDevFika` is not the intended server installation.
No game/server was started or stopped, no mods were removed and no installation
was updated during this audit. This is preparation for the
[multiplayer concept](MULTIPLAYER-FIKA-CONCEPT-20261026.md), not multiplayer acceptance.

## Existing installations

| Machine | Installation | Intended role | Installed SPT / Fika |
| --- | --- | --- | --- |
| SPT-VM | `D:\SPT41Fika` | Shared backend and Headless raid host | SPT 4.1.1; Fika client/server 2.4.0; Headless 1.5.1 |
| SPT-GPU-1 | `C:\SPTDevFika` | Automated human client | SPT 4.1.1; Fika client 2.4.0 |
| SPT-GPU-2 | `C:\SPTDevFika` | Automated human client | SPT 4.1.1; Fika client 2.4.0 |
| MASCHINE11 | `C:\SPTDevFika`, exposed locally as `Z:\` | User's additional human client | SPT 4.1.1; Fika client 2.4.0 |

All four use EFT **0.16.9.40743**, Unity **2022.3.43f1** and BepInEx
**5.4.23.5**. The inspected Assembly-CSharp, UnityPlayer and Fika.Core binaries
match across all four installations. The two GPU clients also match the VM's
EFT executable and BepInEx hashes. Local Fika server copies on the clients are
2.4.0, but the intended backend is the VM's server.

Representative shared SHA-256 values:

| File | SHA-256 |
| --- | --- |
| `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` | `3d1b0c637467b773ef64c31a321b6c7cdefc05025b88db4e482489fa6f59feba` |
| `UnityPlayer.dll` | `bf491512c0122395c4ba0316b936f22cb2d586bbf320c9417904dac3c07cc9be` |
| `BepInEx/plugins/Fika/Fika.Core.dll` | `c6760d9b4c1d97106c9f41650de84581831b39f5e631052fdd20e6a6da8bd654` |

The ordinary development installation `D:\SPT41` already runs SPT 4.1.6. It is
not the Fika test backend. Its managed assembly differs from the 4.1.1 copies,
despite the same EFT executable build: apply the complete supported SPT update
payload rather than exchanging only the server executable or Fika DLLs.

## Backend and Headless configuration

- All three human launchers target the VM's LAN backend on HTTPS port 6969.
- Fika overrides the native loopback HTTP setting with `0.0.0.0:6969`.
  The loopback value in SPT's own `http.json` alone is therefore not a LAN blocker.
- The VM's Headless manager uses `https://127.0.0.1:6969`; its referenced profile
  file exists. Manager file version is 1.4.0.
- The Fika server requests one Headless profile and generates Headless scripts
  using that loopback URL. `headless.profiles` contains `amount` and `aliases`,
  not a list of profile IDs.
- Fika clients use UDP 25565 with no forced address, NAT punching or port mapping.
  This is a plausible LAN setup; actual connectivity still needs a joint raid.
- The backend and the Headless raid simulation are separate processes. A healthy
  backend login alone does not validate Headless registration or UDP replication.

The VM had no EFT/server/Headless-manager process during inspection. A direct
process query also found no EFT/server/launcher on GPU-1. GPU-2's process query
timed out and MASCHINE11 was inspected through its file share; their process
states must be rechecked before replacing installed files. Historical client
logs are from September and do not establish current runtime health.

## Extra mods and test control

The clients contain a mixed older mod set: Airburst and MedicRound (including
their Fika adapters), AutoIFF, DualSideDoorBreach, OpenSesame, UIFixes,
UseItemsAnywhere and WTT-CommonLib. MASCHINE11 additionally has Freecam and
DevBalaclavaFix, and a different AutoIFF version. The VM additionally carries
server mods including SVM 2.2.0, Airburst, MedicRound, UIFixes and WTT-CommonLib.
LogTools is present as a preloader patcher. Do not assume this mixed set has been
validated for the newer baseline.

Neither GPU Fika client has UiBridge installed. The existing gpu-run entries for
**both** GPU hosts still select `C:\SPT41`. Add explicitly separate Fika targets
and verify process path plus UiBridge status before automated game interaction;
using the current defaults would exercise the ordinary installation instead.
MASCHINE11 is accessible for file inspection but is not an automated UI target.

No ArenaMode or Redeploy installation was found in the human Fika trees. Keep
the published Arena/Fika incompatibility intact until the multiplayer adapter
exists; copying today's Arena package into these trees is not an integration.

## Recommended next preparation

1. Preserve profiles, launcher settings, configuration and the current mod list.
   Recheck process ownership before updates, particularly on MASCHINE11.
2. Bring the four installations to SPT **4.1.6**, Fika Client **2.4.6**, Fika
   Server **2.4.2** on the VM and Headless **1.5.3** on the VM. These are the
   candidates examined in the concept, not yet a natively accepted combination.
   Existing EFT 40743 assets can serve as the base. Use binary release packages,
   not the similarly named source archives already in Downloads.
3. Start with a minimal SPT/Fika baseline and the necessary diagnostic tools.
   The user allows removal of installed mods; archive the old set outside the
   scanned plugin/patcher/server-mod folders before removal. Introduce Arena AI
   and content dependencies later, after a clean network baseline passes.
4. Install UiBridge in the GPU Fika trees and configure separate test targets.
   Use fresh test profiles on this shared backend; preserve the old ones.
5. Verify both clients can log in, form a lobby and complete a small native
   Factory raid, first with a human host, then with the VM's Headless process.
   Check joins, damage/death replication, disconnects and clean raid ending.
6. Only then implement the concept's M1 Arena match: two humans, fixed kits,
   one life, no rewards and no custom network respawn yet.

Official candidate releases:
[Fika Client 2.4.6](https://github.com/project-fika/Fika-Plugin/releases/tag/v2.4.6),
[Fika Server 2.4.2](https://github.com/project-fika/Fika-Server-CSharp/releases/tag/v2.4.2),
[Fika Headless 1.5.3](https://github.com/project-fika/Fika-Headless/releases/tag/v1.5.3).
The client release explicitly targets EFT 0.16.9.40743; reviewed server metadata
requires SPT `~4.1.3`. Do not broaden runtime admission based on these declarations
without the relevant native tests.

## Verification boundary

Evidence: local and remote file inventories, executable/assembly versions,
selected hashes, sanitized configuration fields, bounded process checks and
official release/source inspection. No credentials or profile contents are
included. This audit changed documentation only: no build, deployment, live
connection test or multiplayer raid was performed.

## Tool deployment — 2026-10-27

After the user cleaned the installations and deferred SPT/Fika updates, the
requested diagnostic tools were installed in the four Fika trees above:

| Target | LogTools | UiBridge |
| --- | --- | --- |
| SPT-VM, `D:\SPT41Fika` | 1.8.0, patcher and companion plugin | Not installed |
| SPT-GPU-1, `C:\SPTDevFika` | 1.8.0, patcher and companion plugin | 0.9.6, enabled/configured |
| SPT-GPU-2, `C:\SPTDevFika` | 1.8.0, patcher and companion plugin | 0.9.6, enabled/configured |
| MASCHINE11, `Z:\` share | 1.8.0, patcher and companion plugin | Not installed |

Payload: `BepInEx/patchers/maschine-LogTools.dll`,
`BepInEx/plugins/maschine-LogTools.Plugin.dll`, and on GPU clients only
`BepInEx/plugins/maschine-UiBridge.dll`. Existing Release artifacts were reused
from clean LogTools `ac0b9fdb` and UiBridge `baca6c6`; they match the normal
development installation. All ten destination DLLs passed independent SHA-256
readback. All four existing `BepInEx/config/maschine-LogTools.cfg` files retain
their original hashes; new LogTools keys will use defaults until first startup.

Both new UiBridge configurations use their host's LAN address, port 38765 and
the existing MCP authentication and client allowlist. Fika gets separate
`C:\Temp\uibridge-fika-host\state` and `captures` directories. The existing
MCP endpoints can reach the Fika client when it owns that port; the ordinary
and Fika game cannot both serve that endpoint simultaneously. gpu-run's normal
installation/start targets were not changed; its shared-backend Fika start
support remains pending.

Old LogTools DLLs/configurations and the exact deployment manifest are retained
locally under ignored `artifacts/fika-tool-deploy-20261027/77266ec716b8462cbdcfad527a2ebd18/`.
No processes were started or stopped. Exclusive access to the old DLLs was
verified before replacement. This is verified deployment on disk, not loaded
runtime or native Fika acceptance; a new game process must load the tools.
