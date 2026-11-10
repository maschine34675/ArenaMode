# Inventory UI mod admission, 10 November 2026

| Reviewed release | Plugin GUID | Client | Server |
| --- | --- | --- | --- |
| [Caliber Under Ammo Name](https://github.com/slpf/CaliberUnderName/releases/tag/1.2.0b) | `com.slpf.caliberundername` | 1.2.0 (tag `1.2.0b`) | — |
| [Compatibility Highlighter](https://github.com/awnova/CompatibilityHighlighter/releases/tag/v1.3.2) | `com.awnova.compatibilityhighlighter` | 1.3.2 | — |
| [Trader Modding and Improved Weapon Building](https://github.com/Soulztorm/ChooChoo-TraderModding/releases/tag/2.1.2) | `com.choochoo.tradermodding` | 2.1.2 | 2.1.2 |

Caliber Under Ammo Name patches ammunition labels and sorting. Amand's Sense
is a soft dependency. Compatibility Highlighter uses item and slot views and a
temporary drag context to preview which items fit. Neither needs a respawn
adapter or a server plugin. Their nine regular EFT patch targets apiece were
checked against the installed game.

Trader Modding has two separate DLLs. Its server exposes
`/choochoo-trader-modding/json` and `/choochoo-trader-modding/json-flea`
for offers and prices. Its editor can initiate native purchases and move
attachments. EFT gates access to that editor on being outside a raid
(`MainMenuShowOperation.ShowWeaponBuildScreen`). Arena's Tactical kit shop
does not open it. [Client transactions](https://github.com/Soulztorm/ChooChoo-TraderModding/blob/2.1.2/ChooChoo-TraderModding_Client/Client/TraderModding/TraderModdingScript.cs)

The shipped policy adds these three identities and records both sides of
Trader Modding under its shared GUID. A newer recognized version continues to
produce the usual version notice. The release payload writer copied the tested
policy byte-for-byte.

## Factory Deathmatch on SPT-GPU-2

The test host loaded the three published client DLLs. Their loaded module IDs
matched the host files. The server loaded Trader Modding 2.1.2 under its shared
GUID. Arena started with the candidate default policy and no personal override.
This was a direct Arena start on the ordinary backend (port 6969).
The unchanged Arena client was `0.5.0+4ab2584f` and the shared Core was
`1.0.22+b1d69e17`. The native test bridge was `0.9.9+92b998ad`, SHA-256
`D04B8CAD5F1A8676F8D064374604913758C48F4E75679C73A3003F75F4934D12`.

Five BP ammunition stacks showed the extra 7.62 caliber line. Hovering an item
marked two matching targets. The test killed the player while the inventory and
highlight were active. After reopening the inventory on the replacement, a
magazine hover highlighted its matching PP-19-01, pointer exit cleared the tint,
and the replacement could fire its Saiga. A subsequent magazine drag returned
the Saiga to 30 rounds plus one chambered. The next hover worked as well.

Compatibility Highlighter retained references to the previous hover while the
inventory was closed. Closing the inventory without dying produced the same
behavior. Reopening alone did not clear the preview; the next hover updated it
correctly and the two-cell border pool did not grow. This is the foreign UI's
ordinary close behavior and needs no Arena body-switch patch for this case.

On the same SPT server, both Trader Modding routes returned HTTP 200 for the
test profile. Seven traders supplied 375 offers; the flea-inclusive response
contained 2,326. All returned offers carried prices. No purchases were made.
The normal test profile lacked a workbench. On a disposable copy with a
level-one workbench, the native M4 preset editor fetched offers itself. In
trader-only view its magazine selector displayed a STANAG for $18 and a GEN M3
for $27. The editor was closed without buying parts or saving a build.

SPT recorded Trader Modding in its installed-mod history on first use. A second
Factory run compared the disposable profile after that registration with its
saved state after raid exit. The only differing field was
`characters.pmc.Hideout.sptUpdateLastRunTimestamp`. All 53 pre-existing
PitFireTeam files and the disposable profile's own companion file matched
their pre-raid hashes after the save.

After removing the test mods and disposable profile, all 1,181 original files
matched their pre-test hashes. Offline checks passed 3,739 tests with three
environment-gated skips. The session-admission harness rejected all 174
intentional faults.
