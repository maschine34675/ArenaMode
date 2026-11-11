# Kit screen hover intent: native run on GPU-2 (2026-11-11)

Change under test: `4b1e61be` ("Keep the kit screen's detail on its kit while the pointer
crosses other cards", [contract](../../reviews/KIT-PICKER-20261110.md), "Hover"). Only the
client DLL was replaced: `maschine-ArenaMode.Client.dll` `0.5.0+1fb93c69` (SHA256 `45A515C6…`)
in `D:\SPT41`, mirrored to SPT-GPU-2; the previous DLL (`0.5.0+9543fed0`) is in
`D:\SPT41-Backups\kit-hover-intent\20261111-110106`. Core, BigBrainCompat, Gate and session
helper unchanged; server `0.5.0+e664eff1`.

`gpu-run start --host host2` (16/16 files identical), normal session, Arena map screen,
KITS. The pointer was driven by a helper on the host (`C:\temp\arena-hover-glide.ps1`,
`SetCursorPos` every ~8 ms, no click), because Windows-MCP moves the cursor in one jump
and glides only while dragging, which would scroll the cards. UiBridge read the pane's
kit name after the helper's log reported it done.

| Gesture | Pane before | Pane after | |
| --- | --- | --- | --- |
| Jump onto Drifter and wait | RANDOM EACH LIFE | Drifter; COPY TO MY KITS at its bottom | rest takes the pane |
| Rest on Drifter 0.6 s, glide 902 px in 0.5 s to COPY TO MY KITS (over Warlord, Comrade), rest there 0.8 s | Warlord | Drifter | passed |
| Rest on Wraith, glide 573 px in 1.5 s (about 380 px/s) to the button (over Spartan, Comrade) | Drifter | Wraith | passed |
| Rest on Drifter, L-shaped path straight down the column (Minuteman, Shadow, Crusader copy), then right to the button (Conscript, Loyalist), 1,225 px in 0.9 s | Wraith | Drifter | passed |
| Glide from the button to Warlord and rest there (control: the driven cursor reaches the game) | Drifter | Warlord | passed |

No ArenaMode warning or error in the BepInEx log (the pointer fallback did not trigger);
the only warnings were three HarmonyX notices of other mods at startup. No card, FAV or
action was clicked, so no setting and no own kit changed. EFT was quit, the server
stopped; the helper script and its log remain in `C:\temp` on the host.
