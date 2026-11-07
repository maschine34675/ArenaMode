# ArenaMode 0.5.0

7 November 2026. SPT 4.1.x. Single-player only. Successor of
[0.4.1](https://github.com/maschine34675/ArenaMode/blob/v0.5.0/docs/RELEASE-0.4.1.md).
Bundles shared **Redeploy.Core 1.0.20**, identical to the Core in Redeploy 1.1.2.

## New

- **Iceberg** through ArenaMapPort 0.5.0 (published 2026-11-07): Gun Game, Deathmatch,
  Team DM, Domination and solo or PitFireTeam Zombies, with the gold map label, its
  lighting and pickup placement and eight participants by default. Team DM uses the
  original USEC west / BEAR east pockets for 2v2 to 10v10; combat waits for the spawn
  gates and the gates stay open for respawns. Domination plays the three original
  points A, B and C. Tactical is not offered on Iceberg (its centre leaves too few
  distant spawn points for five opponents). An existing custom map allowlist is kept;
  the exact previous default list gains Iceberg once. With ArenaMapPort 0.4.0 the map is
  simply not offered.
- **Crowd reactions on ArenaMapPort maps** (F12 Audio, *Crowd Reactions (ArenaMapPort
  Maps)*, default on, frozen once the raid is armed, not in Zombies). The murmur grows
  in the final round, at match point and in the last minute or last five kills (this
  part already works with ArenaMapPort 0.4.0). With ArenaMapPort 0.5.0 the crowd also
  cheers your kills, awards and leads, jeers your deaths, groans at a lost lead and
  calls round starts and results, and every reaction makes all stands roar or boo. A
  kill and a death within the same second give one reaction, the stronger. Nothing in
  the match depends on it; without a crowd nothing happens.

- **Domination is complete:** the mode row reads DOMINATION, without the WIP mark, and
  a new installation starts with a score limit of 300 points (an existing F12 value is
  kept). Capture, defence, the wall monitors, the DynamicMaps objectives and Iceberg's
  three points were run natively in the 0.4.x cycles and on 2026-11-01.

## Fixed

- **Profile rewards switched off at every server start** when another server mod's
  startup step also used SPT's `Watermark + 1` slot (BetterFlea 2.4.1 and 2.5.0 do):
  since 0.3.0 the reward step refused the whole start without naming a cause, no
  profile was enrolled and the Arena session's card said "Activation pending" forever.
  The step now runs first, refuses only a step that really runs before it, names each
  such step in the Warning, says "Rewards are off: …" for a refusal that recurs, and the
  session's card shows the reason. Reported in
  [maschine34675/ArenaMode#1](https://github.com/maschine34675/ArenaMode/issues/1).
- **Arena map screen stutter:** the screen ran at about 48 FPS with one long frame every
  0.2 s because every refresh checked optional-mod compatibility four times and each
  check of an absent mod rescanned all patched game methods. It checks once per
  refresh now, absent mods no longer rescan (that part is in the shared Core), and the
  screen was measured at about 60 FPS on the GPU test host.
- **Failed session switches** now say in one Warning whether the profile on disk
  differs from RAM, whether RAM changed during the readback, or both, with a profile
  alias and the final resume permission; no profile contents are logged.

## Compatibility

- **Shared Core 1.0.20** (identical to Redeploy 1.1.2) adds handovers for Raid Review
  1.10.0 (separate statistics per physical life), HollywoodFX 3.0.0 and Hands Are Not
  Busy 1.7.1, and includes the absent-adapter fix above. The three new handovers were
  validated offline only, not in the game. This ArenaMode refuses Core 1.0.19 or older.
- **PitFireTeam 1.1.1:** see the compatibility entry in the
  [changelog](https://github.com/maschine34675/ArenaMode/blob/v0.5.0/CHANGELOG.md#050---2026-11-07).
- The whitelist records ArenaMapPort 0.5.0, Redeploy 1.1.2 and PitFireTeam 1.1.1; a
  differing recognized version stays advisory, and user policy is preserved.
- Fika is not supported.

See the [versioned changelog](https://github.com/maschine34675/ArenaMode/blob/v0.5.0/CHANGELOG.md#050---2026-11-07) for the full list.

## Profile rewards

Optional **PROFILE REWARDS** are on by default for every profile that is not yet
registered. At the next normal server start ArenaMode.Server adds a small data
block to the profile file. It credits RUB mail and PMC XP only after Arena
matches. Switch rewards off with the rewards card in the Arena map menu.

## Installation

Close game and server first, back up your profile, then extract
**ArenaMode-0.5.0.zip** into the SPT root, merging `BepInEx/` and `SPT_Runtime/`.
Keep one copy of each Arena DLL and the supplied **Redeploy.Core 1.0.20**. The same
Core is bundled in Redeploy 1.1.2; the standalone Redeploy client/server are
optional. Never replace a newer Core with an older copy. BigBrain, its
prerequisites and ArenaMapPort are separate downloads; Iceberg and the crowd
reactions need ArenaMapPort 0.5.0. Existing F12 settings, whitelist choices, kits
and ladder are preserved.

## Validation and remaining limits

Iceberg was run on the GPU test host on 2026-10-31 and 2026-11-01 (Domination with
simultaneous A/B/C, Team DM from the pockets, Deathmatch, a plain PMC raid), the crowd
reactions on 2026-11-02 and with the roar layer on 2026-11-07 (Team DM and Deathmatch;
Gun Game, Tactical, Domination and the spectator camera were not run with the crowd),
the reward startup order on 2026-11-05 with a probe mod at `Watermark + 1`, and the map
screen on 2026-10-31. The session-switch diagnostics are covered by unit tests only.
PitFireTeam 1.1.1 refuses to spawn a companion that was wounded in a normal raid; Tactical
and Zombies with companions then do not start until the companion is healed in PFT's own
menu.
Tactical, Zombies, Akimbo and Molotov remain WIP, Domination has no profile rewards,
and the limits of
[0.4.0](https://github.com/maschine34675/ArenaMode/blob/v0.5.0/docs/RELEASE-0.4.0.md#validation-and-remaining-limits)
still apply.

## Native release check

On the GPU test host SPT-GPU-2 (SPT 4.1.6) with these exact binaries loaded, together with
Redeploy 1.1.2, ArenaMapPort 0.5.0 and PitFireTeam 1.1.1: a Redeploy respawn near the corpse in
a normal Factory raid; Team DM on Iceberg with an Arena respawn through Core 1.0.20; Domination
on Bowl with the crowd's start, lead and death reactions and their roar from every stand; a
PitFireTeam Tactical match with two companions over three rounds, in which the companions of
rounds 2 and 3 respawned through Arena's lease with the served saved-spawn-settings preflight
(the 1.1.1 adaptation of this release) and the profile snapshot was restored at the end. No
mod warning or error beyond the known pursuit diagnostics. Not run: Gun Game, Zombies, the
spectator camera, the Session.exe path. One timing note: an Arena raid started within a second
of the previous match's deferred post-raid processing can be refused with "PitFireTeam
post-raid finalization is still settling; retry shortly"; the next attempt starts. Record:
[GPU-RELEASE-050-20261107.md](reports/2026-11/GPU-RELEASE-050-20261107.md).

## Package checks

Built from `063a7684fd8d63fdaefc654eb80cdbad14176580`. ZIP SHA-256:
`524ab7491e7693f34c772aaa15688df3969f8f6795791eb94eb5c28d2c836a14` (3,892,127 bytes, 74
files). The package manifest records Core 1.0.20 from Redeploy `3b68193`; the bundled Core is
byte-identical to the one in Redeploy 1.1.2. Package contents, paths, hashes, defaults, 31
notice links and 41 audio provenance records passed the package verifier, and the PitFireTeam
rule (no PitFireTeam binary, no byte-identical reference payload) passed against the 1.1.1
reference tree.

Fresh checks in the isolated source snapshot: 3,699 passed with four environment-dependent
skips (two reward-recovery fixtures, the installed-APBS probe and the installed-PitFireTeam
transaction-target probe). The release state was verified in the working tree before packaging
as well (xunit 3,700 passed, PitFireTeamChecks 407, DominationPolicyChecks 21,596,
DominationAdmissionChecks, PftTacticalKitChecks, ArenaSessionClientChecks, DeployLayoutChecks,
ArenaGateChecks). No credential scan was run for this release.

VirusTotal results below apply to these exact binaries (7 November 2026). The five DLLs had no
malicious or suspicious labels. The unsigned helper `maschine-ArenaMode.Session.exe` has **4
malicious labels** (VirIT `Trojan.Win32.MSIL_Heur.A`, Elastic `malicious (moderate
confidence)`, APEX `Malicious`, McAfeeD `ti!1F89B982B6D6`) and 67 engines without detection.
These findings are unresolved; they are not established false positives. No antivirus
protection needs to be disabled.

| File | SHA-256 / scan |
|---|---|
| `maschine-ArenaMode.Gate.dll` | [c5ac2bbeaf14…](https://www.virustotal.com/gui/file/c5ac2bbeaf14b3aaf61a0743940e0540ab845ef6ad9b86559aeeb78dc6a9f2f6) |
| `maschine-ArenaMode.BigBrainCompat.dll` | [0cc66b158f4f…](https://www.virustotal.com/gui/file/0cc66b158f4fd204dafc4d4605fe2be55e82d0698db6cbb620863a82069e6612) |
| `maschine-ArenaMode.Client.dll` | [eaae8f5086af…](https://www.virustotal.com/gui/file/eaae8f5086af9e0118c456c87a782b73b3e4f80e0c3e4269e26dbda0c2d70dd7) |
| `maschine-ArenaMode.Session.exe` | [1f89b982b6d6…](https://www.virustotal.com/gui/file/1f89b982b6d6bfe311af9570d1c0e133eaa0f8eab3d0388ee212c7678dce4b76) |
| `maschine-Redeploy.Core.dll` | [150851f557dc…](https://www.virustotal.com/gui/file/150851f557dc14356de53f75023128b591fb607d2cac99388e91c54148e8c87b) |
| `maschine-ArenaMode.Server.dll` | [beb502b46610…](https://www.virustotal.com/gui/file/beb502b46610c60b0b7c2defb60ce1ba9d554f6587ceda4859444d7e2043360a) |
