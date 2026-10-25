# ArenaMode 0.3.0

Release for SPT 4.1.x, dated 2026-10-25. Native checks use SPT 4.1.6.
These notes describe changes since the actual `v0.2.1` tag, including work
which was already in development while the 0.2.1 hotfix was published.

Final package hashes, native checks and the session helper's three unresolved
VirusTotal detections are recorded in [release verification](RELEASE-VERIFICATION-20261025.md).

## Changes

- **Profile rewards:** eligible kills award 1,000 RUB + 100 PMC XP; DM/TDM/Tactical
  round wins award 10,000 RUB + 1,000 XP. Each match is capped at 200,000 RUB and
  20,000 XP. Gun Game and Zombies currently reward kills only. No-contest results
  do not pay. A normal exit can retain kills from an unfinished round without a win.
- **Rewards in the Arena menu:** eligible initialized profiles default ON at normal
  server startup, while existing OFF choices are preserved. The card above the
  map preview shows rates, status and the switch in either session.
- **Automatic return and payout:** on the supported local Windows/native SPT 4.1.6
  path, entering a private Arena session saves and stops normal SPT before making
  the private copy. Returning starts normal SPT, imports the final reward setting
  and validated results, then opens EFT. RUB is in the mailbox; PMC XP is already
  credited. Collecting the mail does not add XP again. Direct Arena play without
  a private session still settles at the next normal-server start.
- **Offline reward control:** the private menu can switch rewards while normal SPT
  is stopped. Empty and OFF sessions also return normally. Concurrent ordinary
  startup is refused while the private session owns that installation.
- **Tarkova Cola:** with its client/server 2.2.1+ installed, optional Zombie research,
  augment/synergy progress and newly earned achievement prizes use protected
  settlement. Other modes retain temporary perks only. Bottle/grenade refunds and
  equipped-perk selections are not transferred. VisitAPI storyline progress is
  outside this integration.
- **PitFireTeam 1.0.0:** protect its companion databases and queued reports during
  Arena. Record 1.0.0 as reviewed while retaining older supported contracts.
- **Optional-mod handling:** offer a temporary Arena session without mods whose
  versions differ from the reviewed version, omitting their hard dependents too.
  Direct Arena play still treats version differences as advisory. User policy
  files are unchanged; refusal messages preserve the server's reason without
  duplicating it.
- **Shared Core 1.0.13:** FOV Fix camera ownership, Manimal ammo-loading visuals
  and Magazine Check Interrupt panels follow player replacement. Add tested
  Fontaine FOV Fix 4.1.0, Manimal 2.0.0, Increase Look Direction 1.3.0 and Magazine
  Check Interrupt 1.1.1 to the default list. Auto Shoulder Swap 1.1.1 remains
  unlisted after its native knife/lean error.
- **Optional HUDs:** Accurate Circular Radar 1.3.4 and Simple Crosshair 1.4.0
  survive body/camera replacement through Core. Better BepInEx 1.0.0 is also
  reviewed; keep its stock ConfigurationManager dependency installed. Radar
  colors still represent faction/role, not Arena teams.
- **Redeploy version review:** the default list records standalone Redeploy
  Client/Server 1.0.2, verified with a normal PMC death, respawn and extraction.
  Installing the companion release no longer produces a stale 1.0.0 notice.
- **Gameplay/loading:** disable native transits in confirmed Arena raids, preload
  the first three validated Gun Game weapons, and halve loose ammunition reserves
  in supplied kits/weapon swaps. Loaded rounds and spare magazines are unchanged.

## Updating

Use Items Anywhere 2.1.6 currently conflicts with Arena's magazine-query patches
at plugin startup, including in the normal process. A disabled mutator or later
private-session filter does not resolve that overlap. ImpactCue, Enemy Markers,
Amand's Graphics, HUD Breakout, Manimal's Quick Cancel and Picture in Picture
Disabler also remain outside the default whitelist pending the fixes/tests
described in [the seven-mod review](SEVEN-MOD-COMPAT-20261025.md).

Close EFT and both normal/private SPT sessions before replacing files. Merge the
release's `BepInEx` and `SPT_Runtime` folders into the SPT root. Update the client,
server, companion, gate, helper and shipped default policy together. Keep exactly
one shared `maschine-Redeploy.Core.dll`, using the verified **1.0.13** bundle for
this source; the complete standalone Redeploy mod is not required.

Preserve custom kits, ladder, configuration, areas/pickups and the user whitelist.
Also preserve normal runtime `user/arena-rewards` and any private session with
pending recovery/results. Reward history is the duplicate-payment record, not
disposable cache. Private sessions transfer signed result metadata; their full
profile is never written over the normal profile.

## Verification already completed

- Latest HUD integration unit run: **3,197 passed, zero failed, one known APBS environment
  skip**. Separate helper, control, result and client-session harnesses cover
  recovery/ownership boundaries. This is recorded evidence, not a rerun of a
  future version-stamped release ZIP.
- GPU-2: actual stop-before-copy, offline ON/OFF, concurrent-start refusal, one
  real kill and one-time 1,000-RUB/100-XP payout; two empty final-build returns
  preserved OFF then restored ON without duplicate payment. Original inventory,
  skills, quests, unrelated profiles and settings were preserved.
- Earlier bounded GPU runs cover a DM victory/XP level advancement, mail
  collection, Tarkova Cola solo Zombie progress/achievement payout, DM exclusion,
  ordinary PMC pass-through and the input/body-replacement integrations.
- HUD checks on GPU-2 cover a living kit swap, two deaths, a second Factory raid,
  Radar disabled during respawn and Better BepInEx input restoration. Core's
  274 tests and coordinator mutation checks also passed. This HUD matrix did
  not include a new standalone Redeploy raid or PFT.

See [session verification](GPU-OFFLINE-SESSION-20261025.md),
[XP verification](GPU-REWARD-XP-20261025.md),
[Tarkova Cola verification](GPU-TARKOVA-COLA-20261025.md),
[PFT 1.0 review](PITFIRETEAM-1.0.0-20261024.md),
[FOV/Manimal review](FOUR-MOD-COMPAT-20261025.md) and
[input-mod review](INPUT-MOD-COMPAT-20261025.md), and
[HUD review](HUD-MOD-COMPAT-20261025.md).

No new functional release blocker was found in these sources and reports.
Fika, distributed ownership across simultaneously running mirrored hosts, native
power-loss injection and every PFT/Cola/optional-mod combination are not covered.
Held save failures preserve live RAM for the documented retry/investigation path.
These limits remain explicit; they are not claims of completed native tests.

## Build and release verification

All Arena assemblies and the session helper carry 0.3.0. The package includes
shared Core 1.0.13, byte-identical to the library in Redeploy 1.0.2. Source
revisions are recorded in the package manifest and embedded product versions.

The release pipeline exports committed Arena/Core sources, builds without
writing to the installed game, runs the unit suite and verifies the archive
layout and binary versions. The ZIP root contains only the installation folders;
Arena assets and notices remain below its plugin folder. Core is rebuilt from
its frozen source rather than taken from an old local build directory.

The GitHub release records the final package hash, build/test results and
VirusTotal links for all five DLLs plus the session-helper EXE. Final source and
payload undergo Gitleaks checks; the uploaded ZIP is downloaded again and its
hash compared with the verified local artifact. Older scan results do not attest
to newly built bytes. The bounded final-package native result is reported
separately from the implementation checks above.
