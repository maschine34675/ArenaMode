# Arena session: whitelist by not loading (spec, 2026-10-21)

Status: implementation spec for branch `feature/arena-whitelist-session`. This file is the
contract between the five parts below. If an implementation detail has to deviate, change this
file in the same commit and say why.

2026-10-25 extensions: [automatic reward return](REWARDS-RETURN.md) supplies the
native save/drain adapter. [Offline Arena sessions](ARENA-OFFLINE-SESSION-20261025.md)
now use it to stop the ordinary server before copying the profile, including OFF
and no-result sessions. Schema 3 pins that lifecycle and private consent authority;
return starts normal SPT for recovery/payment before EFT. Schema 1/2 retain the
older live-server path only when no offline capability was issued. A held native
save is preserved for Retry/Cancel, never force-closed to make the switch succeed.

## 1. Goal and user decisions

Many third-party mods break Arena raids. Per-mod adapters are no longer an option. Arena now
runs only with **whitelisted** mods:

- Mods that are not on the whitelist are **not loaded at all** in an *Arena session*: a separate
  EFT process (plugins filtered by a preloader patcher) talking to a separate SPT server process
  (a private runtime tree with only whitelisted server mods).
- Switching between a normal session and an Arena session is a controlled restart of the game
  (about 1.5 to 2 minutes). Accepted by the user (2026-10-21).
- The Arena session uses a **disposable copy** of the profile and offers **only Arena raids**.
  Accepted by the user.
- Default whitelist besides ArenaMode: **SAIN, APBS, ABPS, BigBrain, Waypoints** (decision 2026-10-21), and since
  2026-10-22 **every mod that is installed on the development hosts** (section 2), minus a **deny list** of the five
  mods ArenaMode already refused by code before the whitelist existed: ORBIT, Questing Bots, Adaptive Bot Culling,
  ISB Special Forces and Aishi. Their code-level refusals were removed on 2026-10-22 (user decision, last entry
  of section 11): the deny list alone refuses them.
- A whitelisted mod whose installed version differs from the reviewed version produces a
  **warning** for direct Arena entry, not a version-only block. Since 2026-10-24,
  that warning also offers an optional Arena session that omits differing mods
  on both sides by GUID, plus their hard dependents. The user explicitly chose
  omission for this restart; the normal installation and policy files stay unchanged.
  The offer also remains available when a separate adapter refusal prevents direct entry.
  See [the extension contract and verification](PITFIRETEAM-1.0.0-20261024.md).
  Strict enter requests use schema 2 (old helpers must refuse); their temporary
  plan is checked before acknowledgement and tree construction, then pinned by
  the process flag, marker, gate report and server policy hash. Legacy requests
  without a version plan retain schema 1.
- The user may turn the whitelist into a blacklist (`"unlisted": "allow"` in the user file, decision
  2026-10-21, section 3): everything that neither the user file nor the default file denies is then allowed.
  The shipped default is always a whitelist.
- A normal session whose loaded mods are already all whitelisted may play Arena directly, without
  a restart (it is equivalent to an Arena session except for the disposable profile).
- No code specific to one foreign mod. The adapters for twelve foreign mods (MoreBotsAPI, AI Limit, Looting
  Bots, Remove The Dead, Smoothie, Late To The Party, Classic Movement, StatTrack, Deminvincibility, Skills
  Extended, Pause Continued, EFT Effects Manager / Broken Limbs) and the code-level refusals of the five denied
  mods were removed on 2026-10-22. What remains under `ArenaMode.Client/Compat` and in the server
  (PitFireTeam, DynamicMaps corpse markers, ContinuousHealing, Continuous Load Ammo, Recoil Rework Legacy, SVM,
  APBS, BigBrain and the ArenaMapPort gates) is active in an Arena session when its mod is allowed there
  and dormant when it is absent.
- Fika is never filtered. With Fika present Arena stays unavailable as today and no session is
  offered.

Non-goals for v1: pausing mods inside a running process, Arena with Fika, checking enum consistency
of denied preloader patchers beyond neutralising them. (Removing the existing adapters was a v1
non-goal; the user decided it on 2026-10-22, see section 11.)

## 2. Units and the default list

A unit is identified by its GUID: the BepInEx plugin GUID on the client and the SPT `ModGuid` on
the server. All units below use the same GUID on both sides.

| Tier | GUID | Client | Server | Reviewed version |
|---|---|---|---|---|
| platform (code) | `com.SPT.core`, `com.SPT.custom`, `com.SPT.debugging`, `com.SPT.singleplayer` | yes | - | 4.1.6 |
| platform (code) | `com.bepis.bepinex.configurationmanager` | yes | - | any |
| platform (code) | every GUID starting with `com.fika.` | yes | - | never filtered |
| arena (code) | `com.maschine.ArenaMode` | yes | yes | this build |

Neither policy file can replace, change or remove these rows: an entry for one is skipped and a `deny` of one is skipped
(section 3, step 1).

The default file (section 3) lists the units below. A GUID entry applies to the client plugin and to the server mod
with that GUID; "Client" and "Server" say which side has a reviewed version (an absent side means any version, no
warning, and a mod that exists on one side only simply is not installed on the other).

| Group | Unit (GUID) | Client | Server |
|---|---|---|---|
| mod, bot stack | SAIN (`me.sol.sain`) | 4.5.1 | 4.5.1 |
| | APBS (`com.acidphantasm.progressivebotsystem`) | - | 2.4.1 |
| | ABPS (`com.acidphantasm.botplacementsystem`) | 2.1.0 | 2.1.0 |
| | BigBrain (`xyz.drakia.bigbrain`) | 1.5.0 | - |
| | Waypoints (`xyz.drakia.waypoints`) | 1.9.0 | - |
| mod, own | ArenaMapPort (`com.maschine.ArenaMapPort`) | 0.3.0 | 0.3.0 |
| | Killcam (`com.maschine.Killcam`) | 1.0.1 | - |
| | Redeploy (`com.maschine.Redeploy`) | 1.0.0 | 1.0.0 |
| | AutoCorpseSearch (`com.maschine.AutoCorpseSearch`) | 2.2.0 | - |
| | DefaultAutoFireMode (`com.maschine.DefaultAutoFireMode`) | 2.0.0 | - |
| | DualSideDoorBreach (`com.maschine.DualSideDoorBreach`) | 2.0.2 | - |
| | LinkedSearchInStash (`com.maschine.LinkedSearchInStash`) | 2.0.1 | - |
| | LowAmmoCue (`com.maschine.LowAmmoCue`) | 1.0.0 | - |
| | RaidMenuCleanupFix (`com.maschine.RaidMenuCleanupFix`) | 2.0.0 | - |
| | StashFilter (`com.maschine.StashFilter`) | 1.0.0 | - |
| | SurroundAudio (`com.maschine.SurroundAudio`) | 1.0.0 | - |
| | UnloadAllMagazines (`com.maschine.UnloadAllMagazines`) | 2.2.1 | - |
| mod, other | PitFireTeam (`xyz.pit.fireteam`) | 1.0.0 | 1.0.0 |
| | DynamicMaps (`com.mpstark.dynamicmaps`) | 1.2.1 | 1.2.1 |
| | UIFixes (`com.tyfon.uifixes`) | 6.0.3 | 6.0.3 |
| | WTT-CommonLib (`com.wtt.commonlib`) | 3.0.6 | 3.0.6 |
| | WTT-ContentBackport (`com.wtt.contentbackport`) | 2.0.5 | 2.0.5 |
| | SVM (`fika.ghostfenixx.svm`) | - | 2.2.3 |
| | give-ui (`com.agavalda.giveui`) | - | 5.0.0 |
| | ContinuousHealing (`com.lacyway.ch`) | 1.6.3 | - |
| | Continuous Load Ammo (`com.ozen.continuousloadammo`) | 1.2.0 | - |
| | Recoil Rework Legacy (`com.pein.camerarecoilmod`) | 2.0.0 | - |
| | Freecam (`com.terkoiz.freecam`) | 1.4.8 | - |
| | DoorDash (`com.tarkin.doordash`) | 1.2.0 | - |
| | Anvil-WebOverlay (`com.anvil.weboverlay`) | 1.11.0 | - |
| | ArmorClassIcon (`com.slpf.armorclassicon`) | 1.0.0 | - |
| | BetterAmmoLoadingList (`katrin0522.betterammoloadinglist`) | 1.1.1 | - |
| | BrighterInteriors (`7Bpencil.BrighterInteriors`) | 1.1.1 | - |
| | DevBalaclavaFix (`com.acidphantasm.devbalaclavafix`) | 1.2.0 | - |
| | HealingAutoCancel (`HealingAutoCancel`) | 1.1.0 | - |
| | SmajlecLights (`com.smajlec.lights`) | 1.3.0 | - |
| | Tarkova Cola (`com.tarkovacola.server`) | 2.2.1 | 2.2.1 |
| tool | UiBridge (`com.maschine.UiBridge`), ModProfiler (`com.maschine.ModProfiler`), LogTools (`com.maschine.LogTools`), ConsoleToMonitor (`com.maschine.ConsoleToMonitor`) | any | - |

Continuous Load Ammo and Recoil Rework Legacy were added after the shared-Core
GPU checks on 2026-10-23; ContinuousHealing was already listed. Recoil's server
package uses the same GUID and is therefore also admitted if installed, but was
not tested; the entry records only the reviewed client version.
See [the bounded native results](SHARED-BODY-COMPAT-20261023.md).

Tarkova Cola was added on 2026-10-25 with an explicitly requested external-state
protection and Zombie-reward adapter. See [its scope](TARKOVA-COLA-COMPAT.md).
The optional `admissionFailure` ping field is emitted only if that adapter's
required protection contract failed; ordinary PMC/Scav remains available.

Preloader patchers are matched by file name. Platform patchers (code):
`FixPluginTypesSerialization.dll`, `spt-prepatch.dll`, `maschine-ArenaMode.Gate.dll`.
File patchers on the default list: `maschine-LogTools.dll` (tool) and `WTT-ContentBackportPatcher.dll` (mod,
WTT-ContentBackport's patcher). Every other patcher is neutralised in an Arena session.

The default file also carries a **deny list** (section 3). Its five entries are the mods ArenaMode already refused by
code before the whitelist existed; the reason in every case is "refused by ArenaMode before the whitelist existed":

| Denied mod | GUID | Formerly refused in code by (removed 2026-10-22) |
|---|---|---|
| ORBIT | `com.chazut.orbit` | `ArenaOptionalMods` (loaded ORBIT blocks Arena) |
| Questing Bots | `com.danw.questingbots` | `ArenaOptionalMods` (active Questing Bots blocks Arena) |
| Adaptive Bot Culling | `com.hysocs.adaptivebotculling` | `ArenaOptionalMods` (loaded plugin blocks Arena) |
| ISB Special Forces | `com.samc137.isbspecialforces` | `ArenaIsbAdmission` and the server ISB admission (client or server components block Arena) |
| Aishi | `com.samc137.aishi` | `ArenaIsbAdmission` and the server ISB admission |

Those code-level refusals were removed on 2026-10-22 (user decision, section 11). The deny list is now the only
mechanism: it keeps the mods out of an Arena session and names them as "Blocked for Arena" in a normal one. A user
`allow` entry lifts it, at the player's own risk. Because nothing else refuses these mods any more, a session
of either kind whose shipped whitelist is missing, unreadable or invalid, or whose evaluation fails, refuses Arena
(section 7); ordinary raids are unaffected.

Libraries without a `BepInPlugin` (for example `maschine-Redeploy.Core.dll`,
`maschine-ArenaMode.BigBrainCompat.dll`, `spt-common.dll`) are not plugins and are never filtered;
they load only when an allowed plugin uses them.

Tools are listed because the GPU test hosts need them; the user can deny them in the user file.

## 3. Policy files

- Shipped default: `BepInEx/plugins/maschine-ArenaMode/arena-whitelist.json` (overwritten by updates).
  Source in the repo: `assets/arena-whitelist.json`.
- User file (optional, never shipped): `BepInEx/config/com.maschine.ArenaMode.whitelist.json`.

Default file (abridged; the shipped one lists every unit of section 2):

```json
{
  "schema": 1,
  "mods": [
    { "guid": "me.sol.sain", "name": "SAIN", "kind": "mod", "clientVersion": "4.5.1", "serverVersion": "4.5.1" },
    { "patcher": "maschine-LogTools.dll", "name": "LogTools patcher", "kind": "tool" }
  ],
  "deny": [
    "com.chazut.orbit"
  ]
}
```

Entry fields: `guid` or `patcher` (exactly one), `name`, `kind` (`mod` or `tool`), optional
`clientVersion` / `serverVersion` (reviewed versions; absent = any version without warning).
`deny` is optional: an array of non-blank strings, each a plugin/server GUID or a patcher file name (the syntax of
the user file's `deny`). The names of the denied mods are not in the file (JSON has no comments): they are in
section 2 and in the README.

User file: `{ "schema": 1, "unlisted": "deny" | "allow", "allow": [ <entries as above> ], "deny": [ "<guid or patcher file>" ] }`.
`unlisted`, `allow` and `deny` are each optional; schema stays 1. A JSON `null` for any of the three is the same as a
missing member (below).

The policy is a combined allow/deny list (user decision 2026-10-21; the default file's deny list was added by the
decision of 2026-10-22). For one unit, in this order:

1. **Platform and arena units** are always allowed. No file can change or remove them: a default or user `deny`
   that names one is skipped with a diagnostic, and so is a default `mods` entry that names one (with a
   diagnostic) or a user `allow` entry (without one), so the code entry keeps its tier, its sides and its reviewed
   versions. Every GUID that starts with `com.fika.` counts as one of these units.
2. A unit in the user `deny` list is **excluded** (`denied-by-user`). It is not pulled in as a hard dependency
   of an allowed unit either: that unit is excluded too (deny wins over hard dependencies, below and section 4).
3. A unit that a user `allow` entry names is **allowed** (`whitelist` or `tool`), with the user's reviewed
   versions. This is how the user brings back a unit the default file denies.
4. A unit in the **default** file's `deny` list is **excluded** (`denied`), in whitelist and in blacklist mode
   alike, and not pulled in as a hard dependency either (same rule as step 2). Only step 3 (the user's
   `allow`) or step 2 (the user's own `deny`, which then names the reason) is ahead of it.
5. A unit that a default entry names is **allowed** (`whitelist` or `tool`). A reviewed version that differs from
   the installed one is a warning; this is the only source of version warnings, so it exists only for entries
   that carry a reviewed version.
6. Every other unit follows `unlisted`. `"deny"` (the default) **excludes** it (`not-on-whitelist`):
   the whitelist behaviour. `"allow"` **allows** it (`unlisted-allowed`, no warning of its own): the
   blacklist behaviour, everything the user did not deny (and the default file did not deny) is allowed.

Consequences: a user `allow` re-enables a default-denied mod; a user `deny` always wins, even over such an `allow`
(the user file applies its `allow` before its `deny`); in blacklist mode (`"unlisted": "allow"`) the default
denies still block, because they are not a statement about being unlisted.

**Deny wins over hard dependencies (2026-10-22, external review).** A unit that a deny list names (and no `allow`
entry lifts) is never allowed, not even as a hard dependency of an allowed unit: the closure of section 4 never
pulls it in. A unit that would be allowed (a listed one, an `unlisted-allowed` one, or an unlisted library that an
allowed unit needs) but has such a unit among its installed hard dependencies, directly or through other units
that need it in turn, cannot start without it: BepInEx skips a plugin with a missing hard dependency, and SPT's
mod validator refuses to load every server mod when one `ModDependencies` entry is missing. That unit is
therefore **excluded** itself, with the reason `requires-denied:<guid>` (the GUID of the denied unit it needs)
and one warning `<name>: excluded because it requires <dependency name> (denied by the user's policy file)`, or
`(denied by the default list)` when only the default file denies it (the user's file is named when both do).
So A that requires B that requires a denied C excludes A and B, both with `requires-denied:C` and C's name in
the warning. A unit that is merely unlisted is still pulled in with the warning of section 4; under a
blacklist an unlisted library is allowed on its own, so only a denied dependency can ever block there. Platform
and Arena units are the exception (step 1): no list can remove them, so this rule does not either. The same
rule holds on the server, with the mod's `ModDependencies` as hard dependencies. The normal-session refusal
(section 7) lists the excluded dependants under "Blocked for Arena" with the denied mods.

**Testing override (user decision 2026-10-22).** The rule stays: what is not on the list blocks Arena and does not
load in the session. The user's way to test without listing every mod is the blacklist file above, and nothing else
(no second switch): `{ "schema": 1, "unlisted": "allow" }`. It is made visible. A decision that allowed at least one
unit with the reason `unlisted-allowed` adds ONE last warning per side (client plugins, server mods):
`policy override: N unlisted mod(s) allowed by your policy file: <names>`, with the display names sorted ignoring case,
at most six, then `, and K more`. Units pulled in as dependencies are not counted and a whitelist never gets one. The
client shows these notes first in the notes line (client note, then server note, not counted into the four-note cap),
as the second notice line of a refusal and, once ARENA is selected, as an information notice and in the Arena session
notice, and logs each once at Info. The gate's Info summary line gets the suffix ` (policy override: unlisted mods
allowed)` whenever the policy allows unlisted units, and its report carries `"unlistedAllowed": true` (the policy's
mode, whether or not an installed plugin was let through). The user file is mirrored to every machine by the user's
Syncthing setup, so it has to be removed again after the test (README, "Testing override").

The same rules apply to client plugins and server mods (by GUID) and to preloader patchers (by file
name). Because `deny` is checked before `unlisted`, a blacklist is just `"unlisted": "allow"` plus a
`deny` list; `allow` entries are then only needed to attach reviewed versions or to lift a default deny.

The default file's `deny` member is parsed as strictly as its entries: a value that is not an array, JSON `null`,
or an entry that is not a non-blank string makes the default file invalid, so the Arena session is unavailable (a
lost deny would let through a mod the build refuses). A GUID or patcher that the default file both lists in `mods`
and names in `deny` is **denied** (deny wins); the loader reports `default whitelist: '<id>' is both allowed and
denied, the deny wins and the unit stays out`, the file stays valid, and `tools/Write-ArenaAssets.ps1` refuses to
ship such a file. A user `allow` can still bring that unit back. A `mods` entry or a `deny` entry of the default file
that names a platform or Arena unit (step 1, `com.fika.` GUIDs included) is skipped and the file stays valid: the
loader reports `default whitelist: mods[<i>]: '<id>' is ignored, platform and Arena units are fixed in code and cannot
be listed` or `default whitelist: deny of '<id>' is ignored, platform and Arena units cannot be denied`. Before
2026-10-22 (external review `gate-policy`, appendix) only the deny was skipped: a `mods` entry replaced the code entry
of its unit (another tier, both sides, the file's versions), and a deny of the same name, which protects code-tier
entries only, then removed the unit, so a shipped file could make `com.SPT.core` or `com.maschine.ArenaMode` not
allowed. A
malformed entry still fails the load first, whatever unit it names. Such a file is a mistake in the build, not
something to load around, so `tools/Write-ArenaAssets.ps1` and `tools/Verify-TestRelease.py` refuse to ship it (any
`mods` or `deny` entry that names one of these units; a platform patcher by its file name, also written as a path).
Their lists are the contract's (`ArenaSessionContract`), and the tests run both scripts against every unit of the
contract. The loader also reads only the exact key `deny`
and accepts a default file without one, so `tools/Write-ArenaAssets.ps1` additionally refuses to ship a default file
whose `deny` member is missing (or spelled differently) or does not name the five refused mods; the release check
`tools/Verify-TestRelease.py` requires the same five names and the two file patchers of section 2.

A user file with an unknown schema, an unknown `unlisted` value (anything but the strings `deny` and
`allow`, compared ignoring case and surrounding white space; the empty string, numbers, booleans,
arrays and objects included; JSON `null` counts as absent) or a parse error is ignored with a
warning, as a whole: guessing a mode would silently invert what the user wrote. In blacklist mode
(`"unlisted": "allow"`) the same goes for a `deny` member that cannot be read in full, that is, one that is
not an array or has an entry that is not a non-blank string (JSON `null` is neither: `"deny": null` is a missing
member there as everywhere, not an unreadable list, so the file applies and hashes like the file without the
member, while `"deny": ""` or `"deny": {}` discard it): there the deny list is the whole filter, and
applying part of it would load a mod the user excluded, so the file is ignored as a whole and the shipped
whitelist stays in force. A mistyped `allow` member or entry, and under a whitelist a mistyped `deny` member
or entry, are skipped individually with a warning (a lost `allow` entry only leaves a mod unlisted, and under
a whitelist nothing unlisted loads). A JSON `null` for `allow` or `deny` is a missing member in both modes and costs
no warning. A `unlisted` member in
the shipped default file is not read: the default list is always a whitelist. A missing, unreadable, oversized
or invalid default file makes the Arena session unavailable (the gate stays inactive and reports the error) and
makes the client refuse Arena in a session of either kind, in an Arena session that is already running as well
(section 7, check 2 of the Arena session).

Both files are read through one function, `ArenaPolicyFiles.TryRead` (section 4), by the client and by the gate: at
most 256 KB, as UTF-8 text (a byte order mark is honoured), with the sharing mode of `File.ReadAllText`. A file above
the limit is not a policy file, none of it is read: the user file is ignored with a warning (the gate's diagnostic reads
`user whitelist: cannot read '<path>', the file is ignored: larger than 256 KB`, the client logs
`user whitelist is larger than 256 KB and was ignored`), a default file of that size makes the Arena session
unavailable. The reason is the policy hash: the client builds the
request and so the server tree from its reading of the files, the gate applies its own, and the Arena session refuses
when the two hashes differ (section 7). A limit that only one side enforced would make them disagree about the same
file for ever ("the policy changed during the switch" on every attempt, although nothing changed), so there is one
reader and one limit; a source pin in `ArenaMode.Tests` keeps a private reader from coming back on either side, and
the gate harness has a world with an oversized user file (`user-oversize`).

The policy hash covers the entries after `deny`, and the default file's deny list when it has one (one line
`default-denied|<lower-case file name>` per name: a default file without a deny list hashes exactly as before the
list existed). It also covers the user's denied names (one line `denied|<lower-case file name>` per name; a deny of a
platform or Arena unit is skipped and is not hashed, and neither is a default `mods` entry or `deny` for one: a
default file that names such a unit hashes like the one without it) and, in blacklist mode, the mode (`unlisted|allow`). In blacklist
mode the denied names are the whole filter. In a whitelist they are part of the policy since the deny-wins rule
(2026-10-22): a deny of a library that no list names cuts off every allowed unit that needs it, so two whitelist
policies that differ only in such a deny decide differently and must not share a hash (the Arena session compares the
hash of the server tree with the gate's, section 7). A whitelist policy without a user deny and without a default deny
list hashes exactly as before the mode existed (the pinned values of the tests).

GUID comparison is ordinal and case-insensitive. Versions compare as `System.Version` after
dropping a leading `v` and any suffix after `-` or `+`; an unparsable version counts as different.

## 4. Shared logic (`ArenaMode.Shared/Session/`, linked source)

Linked into the gate (netstandard2.1, preloader), the helper (net472), the client
(netstandard2.1), the server (net10.0) and the tests (net10.0). Therefore: no NuGet dependency, no
Newtonsoft, no System.Text.Json, no Unity, no BepInEx types, C# features that compile for all four
targets.

- `ArenaMiniJson`: minimal JSON reader (object, array, string with escapes, number, bool, null)
  returning plain `Dictionary<string, object>` / `List<object>` / `string` / `double` / `bool` /
  `null`, plus a small writer for the report. Rejects trailing garbage. Never throws out of
  `TryParse`.
- `ArenaWhitelistPolicy`: `TryLoad(string defaultJson, string userJsonOrNull, out policy, out
  List<string> diagnostics)`; queries `IsAllowedPlugin(guid)`, `IsAllowedPatcher(fileName)`,
  `IsAllowedServerMod(guid)` (a named entry, or blacklist mode and not denied), `ReviewedVersion(guid,
  side)`, `UnlistedMode` / `AllowsUnlisted`, `IsUnlistedAllowed(id)` (blacklist mode and denied by neither file),
  `IsDeniedByUser(id)`, `IsDeniedByDefault(id)`, `ExclusionReason(id)` (`denied-by-user`, `denied` or
  `not-on-whitelist`, in that order), `Hash` (stable hash of the effective policy for logs and the marker).
- `ArenaWhitelistDecision`: input = installed units `(guid, name, version, file, hardDependencies)`
  for one side; output = `Allowed` (reason: `platform`, `arena`, `whitelist`, `tool`,
  `unlisted-allowed`, `dependency-of:<guid>`), `Excluded` (reason: `not-on-whitelist`,
  `denied-by-user`, `denied`, `requires-denied:<guid>`) and `Warnings` (`version <installed> differs from reviewed
  <reviewed>`, `pulled in as hard dependency of <name>`, `<name>: excluded because it requires <dependency name>
  (denied by the user's policy file)` or `(denied by the default list)`).
  `unlisted-allowed` occurs only in blacklist mode and carries no warning of its own; the units of that reason
  are announced together by the one policy-override warning at the end of `Warnings` (section 3), which does not
  count excluded units. Closure and deny rule (section 3, "Deny wins over hard dependencies"): a hard dependency
  of an allowed unit (a listed one or an `unlisted-allowed` one) that is installed but unlisted is allowed with a
  warning, transitively (whitelist mode; in blacklist mode an unlisted unit is allowed anyway). A unit a deny
  list names is never allowed by the closure. A unit that has a denied unit among its installed hard dependencies,
  directly or through other units, is excluded with `requires-denied:<guid>` (the denied unit's GUID as
  installed) and one warning, instead of being allowed; so is an unlisted library between it and the denied
  unit. A cut-off unit pulls nothing in: a library only it needs stays `not-on-whitelist`. An unlisted unit
  that no allowed unit needs keeps its plain reason even if it needs a denied unit. A unit with several denied
  hard dependencies names the first one in its own dependency order in the reason and in the warning (of one GUID
  that is installed twice, the first installed copy that is denied; the GUID in the reason is the same for both).
  Platform and Arena units are never cut off. Same function for the server side, whose hard dependencies are the mod's
  `ModDependencies`, sent in the ping (section 8): SPT's mod validator refuses to load EVERY server mod when one
  listed dependency is missing, so a server mod that needs a denied library goes out of the tree with it, and an
  unlisted library of an allowed server mod stays in. The closure is per side: a client plugin pulled in as a
  dependency does not pull in a server mod that merely shares its GUID, nor the other way round. A ping without
  `dependencies` (an older server) means none.
- `ArenaPolicyFiles` (in `ArenaSessionContract.cs`): `TryRead(path, out text, out detail)` reads a policy file for the
  client and the gate (section 3) and says what it found: `Absent`, `Read`, `TooLarge` (above `MaxBytes`, 256 KB) or
  `Unreadable`. It never throws.
- `ArenaSessionContract`: constants and the report format.
  - command-line flag `-arena-session`
  - AppDomain data key `com.maschine.ArenaMode.Gate.Report`
  - runtime root `%LOCALAPPDATA%\maschine-ArenaMode\session`, server tree `<root>\server`,
    request file `<root>\request.json`, helper log `<root>\helper.log`
  - marker file name `arena-session.json` (in the server tree root = server working directory)
  - default arena port 6970 (fallback 6971 to 6979 if 6970 is taken by a foreign process)
  - helper file name `maschine-ArenaMode.Session.exe`
  - gate report JSON: `{ "schema":1, "active":bool, "error":string|null, "policyHash":string,
    "unlistedAllowed":bool, "allowed":[{guid,name,version,file,reason}], "excluded":[...], "warnings":[string],
    "patchersNeutralised":[file], "patchersRanBeforeGate":[file], "patchersAllowed":[file] }`
  - `patchersRanBeforeGate` (since 2026-10-22, external review) is present in every report, an empty array when
    none: the file names of the not-allowed patchers the gate neutralised although their initializer had already
    run, because they sort before the gate in the preloader's patcher list (section 5, step 2). It is a subset of
    `patchersNeutralised`, in the same report (provisional, final and every inactive one). For these patchers the
    process is not clean although the plugin list is filtered: their initializers could not be stopped. A patcher
    whose file the gate cannot determine appears as `(unidentified) <type name>`, and when the gate cannot find its
    own place in the patcher list every neutralised patcher with an initializer is listed (any of them may have
    run). A report without the member (an older gate) reads as an empty list.
  - `patchersAllowed` (since 2026-10-23, external review) is present in every report, an empty array when none: the
    file names of the preloader patchers the gate let run because the policy of the moment allowed them (each once,
    platform patchers included; never the gate itself or BepInEx's own patcher, which are not judged). The client
    judges them again with the policy as it reads now (section 7, check 4): a patcher allowed at the start and denied
    since has run in this process, and neither the plugin nor the server-mod comparison sees a patcher. Kept by the
    provisional, the final and every inactive report. A report without the member (an older gate) reads as an empty
    list.
  - the Info summary line `Arena session: N plugins allowed, M excluded, K warnings` gets the suffixes
    ` (policy override: unlisted mods allowed)` (when `unlistedAllowed`) and, after it, ` (N patcher(s) ran before the
    gate)` (N = the length of `patchersRanBeforeGate`, only when it is not empty); `ArenaGateReport.SummaryLine()`
    builds it.

## 5. Gate: preloader patcher (`ArenaMode.Gate`, `maschine-ArenaMode.Gate.dll`)

Deployed to `BepInEx/patchers/maschine-ArenaMode/`. Same patcher contract as
`D:\SPT41\Development\LogTools` (`TargetDLLs` empty, `Initialize`, `Patch` never called, `Finish`).
The work is split over `Initialize` and `Finish`, in the order BepInEx runs them; the measurements
behind the split are in [ARENA-GATE-20261021.md](ARENA-GATE-20261021.md).

- Both callbacks: if `Environment.GetCommandLineArgs()` has no `-arena-session` (case-insensitive),
  return on the first line: no Harmony, no logging, no file access.
- With the flag, `Initialize()`:
  1. Load the policy (paths from `BepInEx.Paths`, read through `ArenaPolicyFiles`, section 3). On failure: report
     `active:false` + error, do not filter anything, return.
  2. Neutralise every patcher that is not allowed, through the public members of
     `PatcherPlugin`: its target list is replaced by an empty one and its patch, initializer and
     finalizer delegates by no-ops. The gate itself and BepInEx's own entry-point patcher are
     recognised by the assembly their delegates come from and are never touched. Initializers
     that already ran (patchers that sort before the gate) cannot be undone; each gets a warning
     in the report and its file name goes into the report's `patchersRanBeforeGate` (once per file, only for
     patchers that are not allowed and have an initializer; an allowed patcher is never listed, wherever it
     sorts). The list is part of the provisional report and of every later inactive one, so it survives a
     failure of the gate. The patchers the policy allows are left untouched and listed in `patchersAllowed` (each once;
     2026-10-23, external review), in the same reports, so that the client can judge them again (section 7, check 4).
     Fail closed in two places (2026-10-22, second review). (a) A patcher whose file cannot
     be determined (its delegates come from an assembly without a location, or from no assembly: the gate reads
     the assembly from the target list, initializer or finalizer delegate) cannot be on the whitelist: it is
     neutralised like a patcher that is not allowed, under the name `(unidentified) <type name>`, with its own
     warning, and listed in `patchersRanBeforeGate` when it has an initializer and sorts before the gate. (b) When
     the gate does not find its own position in the patcher list, it cannot tell which initializers ran, so every
     neutralised patcher that has an initializer is listed in `patchersRanBeforeGate` and one warning says so (a
     process that is not clean must not look clean). Never touch `FixPluginTypesSerialization` lists or any file on
     disk.
  3. Publish a provisional `active:false` report ("the plugin list has not been filtered yet"),
     so that a postfix that never runs cannot look like an active gate.
- With the flag, `Finish()`, which runs after the preloader has loaded its patched assemblies:
  4. Install a Harmony postfix on the closed generic
     `BepInEx.Bootstrap.TypeLoader.FindPluginTypes<BepInEx.PluginInfo>(...)`, resolved and closed
     by reflection. This cannot happen in `Initialize`: patching that method makes the runtime
     load `PluginInfo` and with it `UnityEngine.CoreModule`, and during `Initialize` the
     preloader has not yet patched and loaded that assembly, so the unpatched file would be
     loaded from disk, BepInEx's entry-point patch would land in a copy Unity never uses, the
     chainloader would never start and no plugin would load, ArenaMode included. For the same
     reason `Arm()` refuses to install when `UnityEngine.CoreModule` is not loaded yet (report
     `active:false` with that reason). The postfix takes `object __result` (never a typed
     BepInEx.Bootstrap signature) and reads every `PluginInfo` member by reflection, so the gate's
     own types stay free of the dependency. It acts only when the runtime type is the dictionary
     of plugin infos for the chainloader plugin directory, collects every `PluginInfo` of every
     DLL (GUID, name, version, hard dependencies), runs `ArenaWhitelistDecision` once and removes
     excluded `PluginInfo` items from their lists. The type cache file stays complete because the
     postfix runs after caching. `PluginInfo.Location` is not set yet at that point, so the
     report's `file` is the DLL path from the dictionary key (the path `Location` gets later).
  5. Publish the final report with `AppDomain.CurrentDomain.SetData(key, json)` and log through a
     BepInEx log source named `maschine-ArenaMode.Gate`: one Info line
     (`Arena session: N plugins allowed, M excluded, K warnings`, followed by ` (policy override: unlisted mods
     allowed)` when the policy allows unlisted units and by ` (N patcher(s) ran before the gate)` when
     `patchersRanBeforeGate` is not empty), one Info line from
     `Initialize` that names the neutralised patchers when there are any (Debug would not reach
     the log: the preloader keeps only the levels of `Logging.Console.LogLevels`, default Fatal
     to Info), and one Debug line per excluded plugin.
- Any exception: report `active:false` + error, leave the plugin list unfiltered (all plugins
  load), never exit the game. ArenaMode then refuses Arena in that process. Patchers that were
  already neutralised stay off (the preloader has consumed their lists); the Error line and the
  report's `patchersNeutralised` say so, and `patchersRanBeforeGate` keeps saying which of them had already run.

## 6. Helper (`ArenaMode.Session`, `maschine-ArenaMode.Session.exe`)

.NET Framework 4.7.2 `WinExe` without NuGet packages (no console window), deployed to
`BepInEx/plugins/maschine-ArenaMode/`. `enter`, `leave` and `stop-server` run one at a time (named
mutex `Global\maschine-ArenaMode.Session.<SID of the user>`, below); `status` only reads and takes no mutex. Every run appends
to `helper.log` (rotated at 1 MB) and exits 0 on success, 1 on failure, never shows UI. The
manifest declares `longPathAware`: the server tree sits deeper than the install. The order of the steps of
one `enter`/`leave` run (stale acknowledgement, mutex, request, acknowledgement, command; see "Acknowledgement"
below) is kept by `HelperHost`, which `Program.Main` hands the parsed command to; it is linked into the tests.
`HelperHost` also stamps the request file (last-write time and length) before it reads it and lets the command look
at that stamp while it waits for the game ("A request the client takes back", below).

The mutex is `Global\` and named for the user (review 2026-10-23, `HelperMutex` in `HelperHost.cs`): a `Local\`
object is one per Windows session (the Terminal Services session a logon lands in), while the runtime root,
`server.pid` and the tree are one per user, so two sessions of one account (a console and a Remote Desktop session)
each had a mutex of their own over the same files and did not serialise. The SID in the name keeps the accounts of one
machine apart in the shared namespace, and the helper creates the mutex with an access list that grants only that
user and SYSTEM: the default list of a `Global\` object comes from the creating token (measured for an elevated
administrator: SYSTEM, the Administrators and the creating logon session, no user SID), so whether another session of
the same user could open it would depend on that token; the explicit list makes it independent of elevation and of
the session. Once the helper holds the mutex no other account can acquire it; an account that creates the name first
only denies service. The unit tests run the same code on net10.0 without the access list
(System.Threading.AccessControl is not part of that runtime); `tools/ArenaSessionHelperNet472Checks` reads the name and
the list of a mutex the shipping build creates. A mutex of that name that exists but cannot be opened (another account
created it) ends the helper with an error, and a helper that cannot determine the user's SID ends before it touches
anything.

Writes never follow a link (review 2026-10-22, third pass). Every file the helper creates or truncates is opened without
following a reparse point at its last component (`HelperNative.OpenFileForWriting`: `CreateFile` with
`FILE_FLAG_OPEN_REPARSE_POINT`, then a check of the handle's attributes, then the truncation): a file symbolic link
(a dangling one included) under that name is refused with a `HelperException` and nothing is written. A junction or a
folder under the name never gets as far as the attribute check: without `FILE_FLAG_BACKUP_SEMANTICS` the open of a folder
for writing fails with "access denied", so it surfaces as an `UnauthorizedAccessException` (nothing is written either; the
sandbox's retry repeats it, six attempts and about three seconds of delays in all, before it fails with `cannot open '...'
for writing: ...`, and `helper.log` gives up after three attempts). The sandbox
(`WriteAllText`, which also writes `server.pid`, and every copy) additionally removes a link at the destination first, as
a link and without entering or following it (a dangling one included), and writes a plain file in its place; the refusal
of the open covers the instant between that check and the open. The two files written outside the sandbox follow the same
rule. `helper.log`: a link under the log's name is removed as a link and the log goes on in a plain file whose first line
says so; a link that appears after the check makes that write end without writing anything (the log never throws). Only
the log's own name is judged: a plain `helper.log` inside a folder that is a link or junction is written through. If the
log's own name is a link and its folder is a link as well, the link cannot be removed (the sandbox refuses to work inside a
link), so nothing is written. The temporary file of the acknowledgement: a link under its name fails the write, so the
helper ends without acknowledging and the target is untouched. Not covered: a hard link (a second name of a plain file),
and the way down from the sandbox root, which `RequireClean` checks as before.

The runtime root, the mutex, `server.pid` and the tree are shared by every SPT installation of one
Windows user, and by every logon session of that user (the mutex is `Global\`, see the start of this section). A
helper therefore never stops, replaces or deletes the arena server of another
installation (see `stop-server`). That protects only between helpers that know the rule. An older helper
(the 0.1.3 builds from before the server binaries moved into the tree) does not understand a marker with
`serverExe`: it takes the tree's server for "not ours", deletes `server.pid` and leaves the server running,
and the new helper can no longer find it through the pid file. The helper's version is the repository's
(the packager requires the built exe to match `Shared.props`, and the next release bump is the release's
decision), so until then only the marker's `serverExe` member and `status` tell the two builds apart. Every
installation of one Windows user therefore has to receive the new helper together; a server an older helper
left behind (`SPT.Server.exe` running from `<root>\server`) is ended by hand.

Commands: `enter --request <file>`, `leave --request <file>`, `stop-server`, `status`, and `watch --file <file>`,
which only the enter starts (step 7).

Request file (written by the client):

```json
{ "schema":1, "direction":"enter", "gamePid":1234,
  "gameExe":"C:\\SPT41\\EscapeFromTarkov.exe", "gameDir":"C:\\SPT41",
  "gameArgs":["-force-gfx-jobs","native","-token=<id>","-config={...}"],
  "profileId":"<id>", "sptRuntimeDir":"C:\\SPT41\\SPT_Runtime",
  "serverMods":[{"guid":"me.sol.sain","folder":"C:\\SPT41\\SPT_Runtime\\user\\mods\\Solarint-SAIN-ServerMod"}],
  "policyHash":"..." }
```

The helper refuses a request it cannot read (exit 1, nothing changed), so the client checks the
same rules before it quits the game (section 7). The rules, in `HelperRequest.TryParse`: schema 1;
`gamePid` positive; `gameExe`, `gameDir` and `sptRuntimeDir` absolute drive paths without `..`
(no UNC or device paths), `gameExe` an `.exe` strictly inside `gameDir`; `gameArgs` strings; for
`enter` a `profileId` of exactly 24 hexadecimal characters (optional for `leave`, the same rule
when given) and a non-empty `serverMods` whose folders are each a direct child of
`<sptRuntimeDir>\user\mods` with a plain, unique folder name. A server mod's `guid` is only required to be
non-blank; the helper uses it as a folder name below `<sptRuntimeDir>\user\patchers` (step 3) and therefore looks
up nothing for a GUID that is not a plain single folder name.

Acknowledgement (review 2026-10-22). The client quits the game as soon as the helper process has started, but a
helper that then fails before it has taken the request over (an unreadable or refused request, a busy mutex)
would leave the player with no game and the reason only in `helper.log`. So the helper confirms in a file, and
the client closes the game only after it has seen that file. Once the request is read and validated and the
helper holds its mutex, and before it waits for the game to exit, the helper writes `<request file>.accepted`
beside the request (`request.json.accepted` for `request.json`):

```json
{"schema":1,"helperPid":4242,"acceptedUtc":"2026-10-22T12:34:56.789Z"}
```

- **Atomic.** The content goes to `<request file>.accepted.<helper pid>.tmp` (flushed to disk, closed) and that
  file is moved onto the final name in one step, so a client that polls for the final name sees no file or the
  complete one, never half of it. The temporary name cannot be the client's own `request.json.tmp`.
  `HelperAcknowledgementFile.Write`; the names and the content are `HelperAcknowledgement` (`SessionLogic.cs`,
  linkable: the client may use it to read the file).
- **Stale files go first.** Before the helper reads the request, and before it takes the mutex, it deletes an
  acknowledgement of that name that an earlier request left (and the temporary files of a helper that died
  while writing one). So no failure path can leave a stale acknowledgement for the client to find: a request the
  helper refuses, a busy mutex and an acknowledgement it cannot write all end without a fresh one. If the stale file
  cannot be removed the helper logs a warning and goes on (the final move replaces it in one step); a client
  should therefore also delete it before it starts the helper and compare `helperPid` with the pid of the helper
  it started.
- **No acknowledgement, no run.** If the acknowledgement cannot be written the helper ends (exit 1, nothing
  changed, the game untouched): the client would never close the game and the helper would wait for it in vain.
- **The client's side** (section 7): it deletes a stale acknowledgement before it starts the helper, then waits for
  the file (looking every 250 ms, at most 15 s, on a coroutine, never blocking the main thread) and only then quits the
  game. The file counts only when it parses, its `helperPid` is the pid of the helper the client started and that
  helper is still running; if the helper process exits first or the time runs out, the game stays running and the
  notice says so. Whenever it reports a failure (a timeout, a helper that stopped, a wait that failed or could not be
  scheduled) the client also deletes the request file: it takes the request back.
- **A request the client takes back** (second pass, 2026-10-22). A helper that is merely slow and has not read the
  request yet (a busy mutex, a slow start) fails to read the deleted file and ends without touching anything. A helper
  that had already read it and acknowledges late (an antivirus scan of its exe, a busy disk) would otherwise wait for
  the game for up to 120 s and switch the session when the player closes the game by hand, unannounced, after the
  client told the player that the game keeps running. So `HelperHost` stamps the request file (last-write time and
  length) before the helper reads it, and the wait for the game looks at the file in every round (about twice a
  second) and once more after the game is gone: when the file is gone, or is another file than the one the helper
  acted on (a new attempt writes a new file, even with the same text), the run ends at once with one Warning
  (`the request was withdrawn (its file is gone or was replaced) while the helper waited for the game ...; nothing was
  changed and the game was not touched`), exit code 1, nothing changed, the game untouched. A file that cannot be
  inspected counts as unchanged: only a request that is really gone or replaced ends the run. A new attempt that
  replaces the file ends the old helper within half a second, so the new helper (which waits up to 5 s for the mutex)
  proceeds. The acknowledgement file is left as it is (the next client and helper delete a stale one first).

`enter`:
1. Wait until `gamePid` has exited (max 120 s; on timeout log and exit 1, never kill the game).
2. Stop a previous arena server of this tree (see `stop-server`). If it belongs to another
   installation the enter fails (step 6 applies).
3. Materialise `<root>\server` (always under the runtime root; refuse any other path; never
   recurse into a reparse point when deleting; delete junctions as links):
   - Server binaries, so that the arena server runs from the tree and `AppContext.BaseDirectory`
     (the folder of the exe, which several server mods resolve their paths from) is the tree: every
     top-level **file** of `<sptRuntimeDir>` except `sptLogger.json` (generated below), `SPT.Launcher*`,
     anything with `Linux` in its name (any case) and `*.so`, copied to the tree root. For SPT 4.1.6 that
     is 44 files, 23 MB: `SPT.Server.exe`, `.dll`, `.runtimeconfig.json`, `.deps.json` and the flat
     libraries of the `deps.json`. Nothing else is needed beside the exe (checked against the server
     host: the working directory decides `IsRunFromInstallationFolder`, `sptLogger.json`, `SPT_Data` and
     `user\mods`; the base directory is used for the satellite assemblies in `SPT_Data\dotnet`, which is a
     junction below; the executing assembly's folder is used for `SPTarkov.Server.Core.dll`, which is
     copied). The copy is incremental: the binaries of the previous tree stay in place and a file is
     copied again only when its length or last-write time (UTC, to the tick) differs from the runtime's
     file, because a copy keeps the source's timestamp; a binary the runtime no longer has is removed.
     Locked files are retried like every other copy. Top-level **folders** of the runtime other than
     `SPT_Data` and `user` are not part of the tree; the helper logs a warning naming them (none exist in
     4.1.6), because a `deps.json` that pointed into one would not find it beside the copied exe. A
     runtime without `SPT.Server.exe` fails the build.
   - `sptLogger.json`: copy of `<sptRuntimeDir>\sptLogger.json` with every `filePattern` value
     prefixed by `arena-` and every string `filePath` value that is not a relative path below `user` replaced by
     `./user/logs/<last segment of the old path>/` (review 2026-10-22, third pass; `HelperJsonText.TryPrefixLogFilePatterns`,
     `IsLogPathUnderUser`, `LogFolderNameOf`). The server combines `filePath` with the file name and resolves a relative
     path against its working directory, which is the tree; an absolute path, a path that climbs out of the tree with
     `..` and a path into the tree's `SPT_Data` folders (junctions into the installation) would make the arena server log
     into the real runtime. So the only paths kept are relative ones whose first segment, after `.` and `..` are resolved
     on the text, is `user` (any case, either separator); a drive, UNC or root-relative path, a drive-relative or stream
     name (a colon), a wildcard, a `..` that climbs out of `user`, a path to any other folder of the tree (`./logs`,
     `./SPT_Data/...`) and a segment Windows would rewrite or refuse (a trailing dot or space, a device name) are moved. The
     new folder name is the last real segment of the old path (`D:\logs\spt\` gives `spt`), `arena` when there is none or
     Windows would not take it as one folder name. An empty `filePath` (SPT itself refuses it) and one that is not a string
     are left alone; the key is judged at any depth, like `filePattern`. A second pass over the result moves nothing. The
     runtime's own file is never changed. What was moved is one Warning of `helper.log` (`sptLogger.json: N logger path(s)
     did not lie below user ... : 'old' -> 'new'`; the list is `HelperTreeResult.LogPathRewrites`). Not judged: the
     content of `filePattern` beyond the prefix.
   - `SPT_Data\`: a directory junction for every subdirectory of `<sptRuntimeDir>\SPT_Data`
     except `configs`; files directly in `SPT_Data` copied; `configs\` copied, then `http.json`
     patched: `port` and `backendPort` set to the arena port, everything else unchanged.
   - the backup config (review 2026-10-23; `HelperJsonText.TryConfineBackupDirectory`, `HelperLayout.IsBackupConfigFile`):
     every file of `configs` that SPT's loader reads as the backup config (extension `.json` or `.jsonc`, name without
     extension a substring of `spt-backup`, both compared exactly as the loader does; `backup.json` in the shipped runtime)
     gets its top-level `directory` replaced by `./user/profiles/backups` in the copy unless it is a relative path below
     `user` by the rule of `sptLogger.json` above (`IsLogPathUnderUser`), and gets the member added when it is missing.
     SPT's backup service writes the copies of the tree's profiles to `<directory>/<date>`, resolved against the working
     directory, deletes the oldest folders there beyond `maxBackups` and restores a profile from the newest one; an
     absolute path, a path that climbs out of the tree and a path into the `SPT_Data` junctions would put the disposable
     profile's copies into the normal server's pool, where that cleanup deletes the normal server's backups and a restore
     may pick an arena copy. `./user/profiles/backups` is the value the shipped `backup.json` carries, not SPT's code
     default: that is the empty string, which resolves to the root of the working directory's drive, so a missing member
     is as unsafe as an absolute path and is added (fail closed; a `directory` that is not a string is replaced the same
     way). A member of that name deeper in the document is not touched; a runtime without any such file is left as it is
     (the helper invents no config file; SPT's own loader decides what such a runtime does). The runtime's own files are
     never changed; every replaced or added value is one entry of one Warning of `helper.log` (`backup config: N backup
     directory value(s) did not lie below user ... : backup.json: 'old' -> 'new'`, `HelperTreeResult.BackupDirectoryRewrites`).
     The installed file carries the shipped value, so nothing is replaced there.
   - the core config (2026-10-25, after the release 0.2.0; `HelperJsonText.TryEnableProfileCleanup`,
     `HelperLayout.IsCoreConfigFile`, the loader's rule as for the backup config, `core.json` in the shipped runtime):
     in the copy, `fixes.removeModItemsFromProfile` and `fixes.removeInvalidTradersFromProfile` are set to `true`.
     The session leaves out the mods the policy excludes, and SPT ships both flags as `false`. With
     `removeModItemsFromProfile: false` an inventory or stash item, a mail attachment or a suit of such a mod, or a
     repeatable quest or trader purchase of such a trader, makes SPT's `ProfileValidatorHelper` throw while the
     server loads the profile, `ProfileMigrationService` marks the profile invalid and the server answers the
     session's game start with `This profile cannot be loaded due to it being invalid or unloadable!`; builds with
     such an item are only logged. With `removeInvalidTradersFromProfile: false` the trader entries (`TradersInfo`) of
     such a trader stay in the profile and SPT only logs them. Reproduced on SPT-GPU-2 on 2026-10-25 with the
     development line before the fix (client and server `64c3b4a`, helper `f98c1f5`; the hotfix branch carries the
     same helper code), WTT-ContentBackport denied and both flags false: the client threw a `NullReferenceException`
     in `TarkovApplication.PrepareGameJob` and closed itself about a minute after its start, the watch stopped the
     arena server, and the player saw no reason (section 10, sixth run). With the flags on, SPT deletes those records
     from the disposable copy instead (a modded weapon the player carries is missing in the session; the real profile
     is never touched). A flag that is not the literal `true` is replaced and a missing flag is appended to `fixes` in
     the file's own style (SPT reads a missing flag as false); `fixProfileBreakingInventoryItemIssues` and everything
     else is left as it is. A core config without a top-level `fixes` object (missing, or not an object) fails the
     build with its name: SPT requires that object and does not start without it, so the normal server could not run
     on that file either. Members of those names outside the top-level `fixes` are not touched; a runtime without a
     core config is left as it is. The runtime's own file is never changed; the changes are one Info line
     of `helper.log` (`core config: SPT's profile clean-up switched on in the tree's copy ... : core.json:
     fixes.removeModItemsFromProfile: false -> true, ...`, `HelperTreeResult.ProfileCleanupChanges`), Info and not
     Warning because most installations carry SPT's values. Not covered by SPT's clean-up and so not by this: quests,
     hideout areas and other profile records of a left-out mod that the validator does not look at.
   - `user\mods\`: emptied, then a copy of every folder in `serverMods` (copies, not junctions,
     so what those mods write beside their own files stays in the disposable tree). A user-allowed
     server mod that resolves its paths from `AppContext.BaseDirectory`, the working directory or its
     assembly location therefore reads and writes the tree, not the real runtime. The exception is
     the `SPT_Data` folders that are junctions: they are the installation's own. Inside a mod, a
     folder that holds only `*.bundle` files (at any depth, at least one) is a junction too: SPT
     serves asset bundles read-only and nothing writes them, while a copy would duplicate
     gigabytes at every enter (WTT-ContentBackport ships about 8 GB of bundles).
   - `user\patchers\<guid>\` (review 2026-10-22): SPT 4.1.6 reads the server prepatch (enum extension) definitions
     of a mod whose metadata sets `HasPrepatcher` from `user\patchers\<ModGuid>`, before it loads any mod, applies
     EVERY folder it finds there, and refuses to load the mods when the folder of such a mod is missing. So for every
     server mod of the request the helper copies `<sptRuntimeDir>\user\patchers\<guid>` into the tree (a copy,
     through the sandbox) when that folder exists, and nothing else from `user\patchers`: the prepatch of a mod the
     policy left out must not be applied. A GUID listed twice is copied once; a GUID that is not a plain single folder
     name is not looked up (`HelperServerMod.PatcherFolderName`); a listed mod without such a folder has no
     prepatcher and the tree gets no `user\patchers` at all. The "server tree ready" line of `helper.log` gives the
     count and the folder names. (Before this, a user-allowed mod with a server prepatcher, e.g. Skills Extended,
     was copied without its definitions and SPT could not load it.)
   - `user\cache\bundleHashCache.json`: copied if it exists (its keys are relative paths and the
     linked bundles are the installation's files), so the server does not hash every bundle again.
   - `user\profiles\<profileId>.json`: fresh copy; `user\profileData\<profileId>\` copied if it
     exists; `user\certs\` and `user\credentials\` copied if they exist.
   - `arena-session.json` marker: `{schema, createdUtc, profileId, port, sourceRuntime, serverExe,
     policyHash, helperVersion}`. `serverExe` is the tree's `SPT.Server.exe`; a marker of an older
     helper has no such member (its server ran the installation's exe). `policyHash` is the hash of the
     policy the request was made with (`request.policyHash`); the server reports it in its ping (section 8),
     which lets the restarted client compare it with its own.
   Before anything is built, the tree root is cleaned: the marker first (a half-cleaned tree must not
   look finished), then everything except the plain files that are server binaries of the runtime being
   copied (folders and links, a stale binary, last session's leftovers).
4. Start `<root>\server\SPT.Server.exe` (the tree's own copy, never the installation's) with
   `WorkingDirectory = <root>\server` (the server resolves data and mods relative to the working
   directory and its DLLs relative to the exe), its own console window, an environment without
   `DOORSTOP_*` variables; write `server.pid` as the very next step after `Process.Start` returns (the
   sandbox that does it is built before the process is started). `server.pid` is the only record that lets
   `stop-server`, `leave` and the next `enter` find the process, so if the write fails after its retries the
   helper ends the process it just started, with the handle it holds, removes a partial pid file and fails the
   enter with a clear Error line (`the arena server (pid N) started but server.pid could not be written (...);
   the server was ended, so no unregistered server stays behind`, or, if it cannot be ended, the pid to end by
   hand); step 6 then restores the normal session. A failure after the pid is written (the port never opens)
   leaves the server registered, and the stop in step 6 ends it. The normal server that `leave` and the
   recovery start writes no pid file and is left running if its port does not open in time: it is the user's
   own server. A helper that is killed in the instant between the start and the write leaves a server that no
   `server.pid` names; `stop-server`, `leave` and the next `enter` find it by its image (see `stop-server`).
   Wait until `<host>:<port>` accepts TCP (host: the `ip` of the tree's `http.json`, a wildcard or `localhost` mapped to
   `127.0.0.1`; an IP literal is probed with a socket of its own address family, so an IPv6 literal such as `::1` works on
   .NET Framework too, where a `TcpClient` made without a family is IPv4-only; a host name is probed through the default
   socket as before; max 180 s) and the system's TCP table names the process that was started as a listener of that port for
   the address the connection reached (review 2026-10-22, third pass; `HelperProcesses.GetListenerProcessIds(port,
   address)`). The lookup is bound to that address: a listener bound to exactly it or to the wildcard of its family counts
   (an IPv6 `[::]` listener too, it can be dual-stack; observed on Windows Server 2025: a dual-stack socket on `[::]` is
   also listed in the IPv4 table as `0.0.0.0`, so the IPv6 table that is read as well for an IPv4 address is a safety net for
   a system that lists it only once, and it also counts a V6-only `[::]` listener, the false accept named in section 11),
   one on another local address of the same port does not
   (`127.0.0.2` beside `127.0.0.1`), and an IPv4-mapped spelling (`::ffff:127.0.0.1`, what a dual-stack client socket
   reports) is the IPv4 address. A server that has exited ends the wait at once. Otherwise: the started process listens:
   ready. Other processes listen, or the table lists no listener at all (it vanished, or is not up yet): keep waiting
   (`port N is held by pid X, not by the server that was started (pid S)`; `port N accepts connections but the system's TCP
   table lists no listener for it`; the first occurrence of each is one Warning, and the timeout names the last). The table
   cannot be read (or the lookup throws): ready only if the helper's own probe found the port free immediately before the
   start, with one Warning that the owner could not be verified; otherwise keep waiting and fail at the timeout with
   `the owner of port N could not be verified: the system's TCP table cannot be read and the port was not verified free just
   before the start`. The proof is a connect plus bind probe on the server's address (`HelperProcesses.IsPortFree(host,
   port)`): used to choose the port, and repeated right before `Process.Start`, because building the tree takes seconds
   between the two (a port that is not free then is a Warning and withdraws the proof). The normal server that `leave` and
   the recovery start has the connect probe that decided to start it as that proof (nothing answered a moment before).
5. Start `gameExe` from `gameDir` with `gameArgs` rewritten: the port inside the `BackendUrl` of
   the `-config=` argument replaced by the arena port, `-arena-session` appended once; environment
   without `DOORSTOP_*` variables (an inherited `DOORSTOP_DISABLE`/`DOORSTOP_INITIALIZED` would
   start the game without BepInEx).
6. If a step after 1 fails and the arena game was not started, restore the normal session as
   `leave` does (the old game is gone; the player must not be left with nothing). Never start a
   second game.
7. Start the session's watchdog (2026-10-23). The helper writes `<root>\watch.json` (the game's pid, exe name and
   start time; the pid of the arena server it started and the marker's `createdUtc`; `sptRuntimeDir`) and starts
   its own exe as `watch --file <root>\watch.json` through `HelperProcesses.Start`, then ends as before. A second
   process on purpose: the enter helper is a child of the old game and holds copies of every handle that game
   made inheritable (sockets, pipes); a process that waited for the whole session would keep them open, the
   watch inherits nothing. A watchdog that cannot be started is a Warning (`no watchdog for this session`) and
   the session runs as before this step. The watch takes no mutex while it waits: it opens the game by pid and
   accepts it only with the recorded exe name and a start time within 2 s of the recorded one (a reused pid, or
   a pid that is gone, counts as a game that has exited), then waits for it to exit, however the game ends
   (EXIT in the main menu, a crash, a kill). Once the game is gone it takes the mutex (waiting up to 180 s: a
   `leave` holds it from its start until the normal game runs) and stops the arena server with the rules of
   `enter` and `leave` (`StopArenaServer(sptRuntimeDir)`: a server of another installation is left alone,
   `OtherInstallation`; `Unverified` and `StillRunning` are logged and left), bound to its own session: when
   `server.pid` names another process than the server this enter started, or no pid file but a marker with
   another `createdUtc` is in place, a newer enter owns the record and nothing is touched (a watchdog whose
   game lingered may wake after the next session began). After a `leave` there is nothing left to stop
   (`NothingToStop`); a mutex that stays busy longer leaves the server to its holder and to the next `enter`,
   `leave` or `stop-server` (a Warning; nothing is killed without the mutex). `helper.log` (the watch logs as
   its own process): `watchdog started (pid W): the arena server (pid S) ends with the game (pid G)` from the
   enter; `watching the Arena game (pid G): the arena server ends with it`, `the Arena game exited; stopping the
   arena server` (no exit code: a process opened by pid yields none on .NET Framework), `arena server after the
   game: <result>` from the watch; a `server.pid` that cannot be read at that moment is a Warning and nothing is
   killed on a guess. One watch process per Arena game; while it runs `maschine-ArenaMode.Session.exe` cannot be
   replaced: `tools/Deploy-ArenaSession.ps1` (and so every build that deploys the helper) stops with its locked-file
   error until the Arena game has ended, and a mirroring tool retries until then (section 9). A watch that is itself
   killed degrades to the behaviour before this step:
   the server lingers until the next command. Before 2026-10-23 only RETURN TO NORMAL SESSION ended the arena
   server, and a game closed by hand left it running beside the normal one.

`leave`: wait for `gamePid` to exit; stop the arena server (a failure to do so is logged and
does not keep the game from starting); read the normal port from
`<sptRuntimeDir>\SPT_Data\configs\http.json`; if nothing listens there, start
`<sptRuntimeDir>\SPT.Server.exe` with `WorkingDirectory = <sptRuntimeDir>` and wait for the port;
start the game with `-arena-session` removed and the `BackendUrl` port set back to the normal port.
The arena tree is kept; the next `enter` replaces it.

`stop-server`: if `server.pid` names a running process whose image is the same file as the exe the
marker says the server was started from and whose start time agrees with `server.pid`, kill it (the
tree is disposable) and delete `server.pid`. That exe is `<root>\server\SPT.Server.exe`, recomputed
from the helper's own session root (the marker's `serverExe` only says that the server was started
from the tree; a kill decision does not rest on a path read from a file), for a marker that has
`serverExe`; and `SPT.Server.exe` of the recorded `sourceRuntime` for a marker without it, a server
started by an older helper, so that a session which outlives a helper update is still stopped once.
The two are never both accepted: a marker with `serverExe` does not accept the installation's exe,
which is also the image of the normal server. Images are compared as files, not as text (a junction or
a SUBST drive gives one file several spellings). A pid that fails these checks is not the server and
is left alone (a reused pid also loses its `server.pid`). `enter` and `leave` additionally leave a
server alone whose `sourceRuntime` is not their own `sptRuntimeDir`.

An arena server that no `server.pid` names (the helper that started it was killed before it wrote the file, or the
file was lost) is found by its image, when no registered server was stopped (no pid file, a pid that is not
running, a pid of another program): a running process whose image is the same file as `<root>\server\SPT.Server.exe`
(the tree copy; only a helper starts it) is such a server. The same ownership rule applies as for a registered one:
the marker must be there, written by this helper (`serverExe`) and name the caller's own installation as
`sourceRuntime` (`stop-server` asks for no installation). Then the process is killed, and the result is `Stopped`.
Without a marker its origin cannot be verified and it is left alone (`Unverified`: `helper.log` names the pid, `enter`
fails, and the process has to be ended by hand), the server of another installation is left alone (`OtherInstallation`, as
above), a process under the marker of an older helper (no `serverExe`: its server ran the installation's exe) is
none of this rule's business, and a process that survives the kill is `StillRunning`. The installation's own
`SPT.Server.exe`, the normal server, is another file and is never taken for an unregistered arena server.

`status`: print a JSON line with tree, marker, pid and port state.

Pure functions (argument rewrite, environment cleaning, `http.json` and `sptLogger.json`
patching, path safety, request parsing, the names and content of the acknowledgement) live in their own file and
are covered by `ArenaMode.Tests`; the client links the request parser and may link the acknowledgement's names.
The file work (junctions, copies, the acknowledgement's atomic write), the runner (processes, `server.pid`) and
`HelperHost` (the order of one run) are linked into the tests too; only the entry point `Program.cs` is not.

## 7. Client (`ArenaMode.Client`)

- `ArenaSessionMode`: `IsArenaSession` (command-line flag), `GateReport` (parsed from the
  AppDomain data), `GateActive`.
- Whitelist admission, evaluated first inside `Compat/ArenaOptionalMods.GetRejectionReason()`:
  - Arena session: refuse with a reason if the gate is not active, no policy can be loaded, the gate reports a
    patcher that ran before it, a patcher the gate let run is one the current policy excludes, a plugin this game
    loaded is one the current policy excludes, the server ping does
    not say `arenaInstance:true`, the server runs a mod the current policy excludes, or the server was built for
    another policy than the gate applied. An internal failure refuses (a process that cannot
    prove it is filtered must not run Arena). Before the review of 2026-10-22 the client evaluated the server's
    mods in a session but only showed the warnings, and the policy hash was written to the marker and the gate
    report but never compared; a policy file that changed while the game restarted (Syncthing mirrors the user
    file) could therefore leave a server running a mod the new policy excludes. The third pass (the gate-policy
    review's appendix) closed the same kind of hole twice more: a session whose policy could not be loaded admitted,
    because every comparison with the policy was skipped, and the plugins that were running were never compared with
    the policy as it reads now. The checks, in this order, each logged once at Warning (check 6 is not logged: it is
    about the connection, not about the policy):
    1. gate inactive: the existing text (`The Arena session could not filter the installed mods (<problem>). ...`);
    2. no usable policy (the shipped default list is missing, unreadable, larger than 256 KB or invalid; a defective
       user file is only ignored, section 3): the normal session's text plus the way back, `Arena is unavailable: its
       mod whitelist could not be loaded (<problem>). Repair the ArenaMode installation; ordinary PMC/Scav raids
       remain available. Return to the normal session.` Everything below compares what is running with the policy
       as the client reads it now, so without one nothing can be judged and the gate's word alone is not trusted
       (before the third pass the session admitted here). Logged as `Arena session refused: ...` beside the loader's
       own `Arena whitelist unavailable: ...` line. Needs no ping;
    3. `patchersRanBeforeGate` of the gate report (section 4) not empty: `The preloader patcher <files> ran before
       the Arena gate could stop it. Allow it in the policy file or remove it, then start the Arena session again.`
       (several files: `The preloader patchers <files> ran before the Arena gate could stop them. Allow them in
       the policy file or remove them, ...`). The gate neutralised what was left of those patchers, but their
       initializers had already run, so the process is not the clean one the session promises. Needs no ping;
    4. a preloader patcher the gate let run (`patchersAllowed` of the gate report, section 4) is one the policy as it
       reads now does not allow (`IsAllowedPatcher`, the gate's own rule; 2026-10-23, external review): `This game
       ran the preloader patcher <file>, which the current policy excludes: the policy changed during the switch.
       Return to the normal session and start the Arena session again.` (several files: `This game ran the
       preloader patchers <files>, which ...`; names sorted, once each, twelve at most, then `and N more`). The
       patcher ran in this process, so a policy that excludes it now excludes this session, and neither check 5
       nor check 7 sees a patcher. A patcher the gate neutralised is not in the list and cannot refuse here, whatever
       the policy says now; one the policy allows now and did not then is a widening, which refuses nothing. A report
       without the member (an older gate) has nothing to judge. Needs no ping;
    5. a plugin this game loaded is excluded: `Chainloader.PluginInfos` evaluated with the policy as it reads now
       (the client side of the same `ArenaWhitelistDecision`; platform and Arena units cannot be excluded) exclude
       at least one unit, of any exclusion reason, `requires-denied` included: `This game loaded <names>, which
       the current policy excludes: the policy changed during the switch. Return to the normal session and start
       the Arena session again.` (names sorted, once each, twelve at most, then `and N more`). The gate removed what
       the policy excluded at the moment the game started; with an unchanged policy the same decision excludes
       nothing, so this fires when a policy file changed during the restart (Syncthing mirrors the user file). The
       decision's warnings are not added to the notes: the gate's report carries them. Needs no ping. Known limit:
       a library that the gate pulled in as the dependency of a plugin which then failed to load (BepInEx skips a
       plugin for a missing dependency or an incompatibility) has no dependant left and reads as excluded; that
       refuses with the same text, which fails closed;
    6. the server is known and is no Arena instance: the existing text (`This game is not connected to the Arena
       session server. ...`);
    7. the ping's `serverMods` evaluated with the policy as the client reads it now (the files through
       `ArenaPolicyFiles`, section 3, so the bytes the gate reads; server side of the same
       `ArenaWhitelistDecision`) exclude at least one unit, of any exclusion reason, `requires-denied` included:
       `The Arena session server runs <names>, which the current policy excludes: the policy changed during the
       switch. Return to the normal session and start the Arena session again.` (names sorted, once each, twelve
       at most, then `and N more`);
    8. the ping's `policyHash` (section 8) and the gate report's `policyHash` are both present (non-blank) and
       differ, compared exactly (ordinal, no case folding): `The Arena session server was built for a different
       policy than this game applies: the policy changed during the switch. Return to the normal session and
       start the Arena session again.` (the Warning in the log carries both hashes). A hash that is absent on
       either side (an older server, a normal server, a marker without a usable hash, a gate report without one)
       is unknown, never a difference.
    Without a ping (not yet answered) there is no server to judge and no hash to compare: 6 to 8 wait for it, and
    the explicit Arena click asks again. The refusal leaves the RETURN TO NORMAL SESSION action.
    The outcome is cached per ping generation and plugin count (menu polling does no I/O); opening the side
    screen and requesting the session start (START ARENA SESSION) first compare the length and last-write time
    of the two policy files of section 3 with the stamps the cached outcome was computed from and evaluate
    again when they differ (a Debug line names the occasion; the known ping is reused, nothing is asked again),
    so a user file edited or mirrored onto the machine while the game runs is noticed at the next screen or
    click, in both session kinds. A change that only widens the policy (the one-line override of section 3
    arriving in a running session) is evaluated the same way and refuses nothing: the session keeps running
    without the mods it did not load until it is started again, and nothing tells the player so (2026-10-22,
    after a run on SPT-GPU-2 showed the stale cache).
  - Normal session: evaluate `Chainloader.PluginInfos`, the preloader patchers and the server's `serverMods` with
    the same policy. A patcher is every `*.dll` below `Paths.PatcherPluginPath` (`BepInEx\patchers`, at any depth,
    the files BepInEx and the gate see) that can be a patcher (below), identified by file name, once per name, and is
    allowed or excluded by `IsAllowedPatcher` exactly as in the gate (`ExclusionReason` tells denied from unlisted). It
    has no dependencies and no version. An excluded patcher counts like an excluded plugin: it refuses Arena, is named
    under "Blocked for Arena" or "Not on the whitelist", and the START ARENA SESSION action stays available (the
    session neutralises it; one that sorts before the gate is then caught by check 3 above). A library that
    sits in the patchers folder without being a patcher (a support DLL that a patcher ships beside itself) is no
    unit: the gate leaves it alone, and it must not block a direct start, with an allow entry for a file that is no
    mod as the only way out. It is told from a patcher without loading it: BepInEx takes a type for a preloader
    patcher only when it has a public static `TargetDLLs` property, so that name is in the metadata (the UTF-8
    string heap) of every patcher, and a DLL whose bytes do not contain it is a library (`MayBePatcher`, a search of
    the file in 64 KB reads; a name that straddles two reads is found). A file the client cannot read counts as a
    patcher: not knowing is no reason to let it pass. A DLL the policy allows is not read at all, and one name in
    two folders is judged by the copy that can be a patcher. If anything is excluded, refuse: `Arena runs only with whitelisted mods. Blocked for
    Arena: <denied names>. Not on the whitelist: <unlisted names>. Start an Arena session to play Arena
    without them; the game restarts.` and show the action **START ARENA SESSION**. "Blocked for Arena"
    names the mods a deny list excludes (the shipped default's or the user's), "Not on the whitelist" the
    mods nothing names (a plugin, a server mod or a patcher; a unit excluded because it requires a denied one,
    `requires-denied:<guid>`, is a deny, so it is named under "Blocked for Arena" with the denied unit, and the notes
    line says `<name>: excluded because it requires <dependency> (denied by ...)`); a part without names is left out,
    so a normal session with only unlisted mods reads as before (`... Not on the whitelist: <names>. Start ...`). Each part is sorted, listed once per GUID
    and capped at twelve names on its own (`and N more`). In blacklist mode only denied mods are excluded
    (the user's, and the default file's, which holds there too), so the first sentence reads `Arena does not
    run with denied mods.` followed by the same `Blocked for Arena: <names>.` part, and the rest is
    unchanged. When no session can be offered (older server, no
    helper, no local SPT server, a remote backend, the server already is an Arena instance, or a
    server the helper cannot switch: no allowed server mod, or one whose folder is not a direct
    child of `<game>\SPT_Runtime\user\mods`) the refusal ends instead with `Remove them and
    restart the game to play Arena.` and shows no action. A missing, unreadable, oversized or invalid default
    whitelist refuses Arena in a normal session (`Arena is unavailable: its mod whitelist could not be loaded
    (<problem>). Repair the ArenaMode installation; ordinary PMC/Scav raids remain available.`, with no session
    action): no code refuses the five denied mods any more, so nothing else keeps them out. An internal failure
    refuses Arena in both session kinds (`Arena could not evaluate its mod whitelist (<message>). ...` in a normal
    session, `Arena could not verify the session state. ...` in an Arena session). The whitelist never fails open;
    ordinary PMC/Scav raids are unaffected.
  - Version warnings never refuse; log them once (Info) and show them as a second line. The policy-override
    notes (section 3) lead that line and are not counted into its cap of four notes.
  - Fika keeps its existing refusal first and gets no session action.
- Arena session menu: ARENA is preselected, PMC and Scav are not selectable, and a notice reads
  `ARENA SESSION: only Arena is available. Profile changes in this session are discarded.` with the
  action **RETURN TO NORMAL SESSION**.
- `ArenaAvailabilityNotice` gets an optional action (label + callback). The action needs two clicks:
  the first changes the label to `CLICK AGAIN TO RESTART THE GAME`, a second click within five
  seconds triggers it.
- `ArenaSessionSwitch.Request(direction, profileId, out error, onFailure)`: writes the request file (game exe and
  root from `BepInEx.Paths`, arguments from `Environment.GetCommandLineArgs()` without argv[0], profile id
  from the `-token=` argument, else from the logged-in profile, and only if it is the 24
  hexadecimal characters the helper accepts, `sptRuntimeDir = <gameDir>\SPT_Runtime`, the allowed
  `serverMods` with folders from the last ping), starts the helper and then waits for its acknowledgement
  (section 6, "Acknowledgement"); the game quits with `Application.Quit()` only once the helper has written
  `<request file>.accepted`. Before anything is written it runs the helper's own request parser
  (`HelperRequest.TryParse`, linked source) on the request text; a request the helper would refuse leaves the game
  running and shows the reason. In order: stale acknowledgement deleted, request file written, helper started.
  `Request` returns true as soon as the helper has started (the game has not quit yet; `HelperPending`); the
  screen then says `Starting the Arena session helper. ...` and offers no second click. The wait
  (`ArenaHelperHandshake`, driven by a coroutine on the plugin object, one call per frame, real time) looks every
  250 ms for at most 15 s: the acknowledgement must parse (`HelperAcknowledgement.TryParse`, a file above 4 KB
  is no acknowledgement), name the pid of the helper that was started (a file of an earlier request or of another
  helper is ignored) and the helper must still be running (a helper that acknowledged and vanished cannot
  switch anything). Then the Info line is logged and the game quits. If the helper process is gone first, or
  the 15 s pass, the game keeps running, one Warning is logged (`Arena session switch (<direction>) not
  completed: ...`; the request file is deleted on every failure, see section 6), `onFailure` gets the message (`The Arena session helper stopped before it accepted the
  request, so the game keeps running. See <helper.log>.` or `... did not accept the request within 15 seconds ...`),
  the screen shows it with the action again and a new attempt is possible. Without the plugin object (nothing
  to wait on) the helper is not started at all; a wait that cannot be scheduled after the start also keeps the
  game running, says so and takes the request back. One Info line per request, written when the helper accepted it.
- Ping parsing: new optional fields `arenaInstance` (bool), `serverMods`
  (`[{guid,name,version,folder,dependencies}]`, `dependencies` an optional array of GUID strings, absent
  or malformed = none) and `policyHash` (a non-blank string, kept exactly as the server wrote it; absent, JSON
  null, blank or of another type = unknown, `ArenaServerClient.PolicyHash` is null); absent fields mean an older
  server: no session offer, normal
  admission continues with the client-side check only. When a screen opens, the ping is
  refreshed with a bounded wait (250 ms) if the answer depends on it; a ping that does not
  arrive in time leaves the fields unknown and the explicit Arena click asks again.

## 8. Server (`ArenaMode.Server`)

- Ping adds `arenaInstance` (`arena-session.json` exists in the working directory),
  `serverMods` (every loaded mod: GUID, name, version, absolute folder, and the GUIDs of its
  `ModDependencies`, sorted) and, on the Arena instance only, `policyHash`: the `policyHash` string of the
  tree's marker, which the helper writes from the request it was started with. Like the marker's
  existence it is read once at startup (parsed with `ArenaMiniJson`, the reader of the other session files) and
  never again, so a marker that is replaced or deleted later does not change the ping of a running server. The
  client compares it with the policy hash of the gate report (section 7) to tell whether the server tree was
  built for the policy that is active now. The string is passed on exactly as written (no trimming, no case
  change). The member is JSON `null` when the marker has no usable hash: it cannot be read (locked, removed
  meanwhile, access denied), is not one JSON object (strict reader: duplicate keys, trailing commas and the
  like count), or its `policyHash` is missing, JSON `null`, not a string, empty or blank. A normal server
  sends no `policyHash` member at all (not even `null`); for a reader, absence and `null` both mean
  "unknown".
- Arena instance: refuse ordinary (non-Arena) local raid starts with a clear error; log one Info
  line at startup: `Arena session server: N mods loaded (<names>)`. The refusal is a normal SPT
  response body `{err: 1000, errmsg, data: null}` (`UnknownScriptExecutionError`), not an
  exception or an HTTP error: the EFT client retries every failure it classifies as default for
  minutes, while it treats this code as expected, aborts the start at once and returns to the
  main menu.
- An Arena instance whose marker gave no usable hash logs one Warning right after the startup line:
  `Arena session server: arena-session.json has no readable policyHash; the client cannot confirm which policy this
  server was built for.` (degraded, not fatal: the ping carries `null`).
- Nothing changes for a normal server apart from the two ping fields (`arenaInstance`, `serverMods`); in
  particular it has no `policyHash` member and logs nothing about it.

## 9. Build, deploy, packaging

- New projects in `ArenaMode.sln`: `ArenaMode.Gate`, `ArenaMode.Session`. While an Arena game runs, its watch
  process (section 6, step 7) is the helper's exe: a build that deploys the helper fails at
  `tools/Deploy-ArenaSession.ps1`'s locked-file check until the game has ended, and a Syncthing copy of the install
  replaces the exe only then. Leave or close the Arena game before deploying the helper.
- Deploy targets: the gate to `<SPT>\BepInEx\patchers\maschine-ArenaMode\`, the helper exe and
  `assets/arena-whitelist.json` to `<SPT>\BepInEx\plugins\maschine-ArenaMode\`. Every new deploy
  target must be overridable the same way the existing ones are (`tools/AirPitChecks/NoDeploy.targets`).
- `tools/Write-ArenaAssets.ps1` and the release packaging include the three new files. The script
  (`Assert-ArenaDefaultWhitelist`) and `tools/Verify-TestRelease.py` (`check_default_whitelist`) also refuse a default
  list that lists or denies a platform or Arena unit (section 3).
- `tools/ArenaSessionHelperNet472Checks/Verify.ps1` runs the helper's socket code (`CanConnect`, the owner lookup) and,
  since 2026-10-23, its mutex (`HelperMutex.Name`, the access list of a mutex it creates under a fresh name) on the
  runtime the helper ships on: it builds a copy of the helper sources and calls the built exe by reflection from Windows
  PowerShell 5.1 (the CLR of .NET Framework 4.x). `ArenaMode.Tests` runs the same sources on net10.0, where a `TcpClient` is
  dual-stack and an IPv6-literal probe works; on .NET Framework it does not unless the socket is made for that family. The
  access list exists only on .NET Framework (net10.0 has no `MutexSecurity` in the runtime), so only this harness sees it.

## 10. Acceptance on SPT-GPU-2

1. Normal start without the flag: the gate is inert; plugin list and Harmony counts equal the
   previous build.
2. Normal session with extra mods: ARENA is disabled with the whitelist reason and the START ARENA
   SESSION action.
3. Enter: the game quits; the arena server on 6970 loads only ArenaMode, SAIN, APBS and ABPS; the
   game starts with only allowed plugins; the ARENA SESSION notice shows; PMC and Scav are not
   selectable; a deathmatch on Factory runs to the end.
4. Leave: the normal server and the full plugin set are back.
5. Killing the game inside a session leaves the normal launch path unaffected.
6. The helper's acknowledgement (review 2026-10-22): after the second click the notice reads `Starting the Arena
   session helper. ...`, the game quits only once `request.json.accepted` exists, and with the helper made to fail
   (a second helper holding the mutex, or an unusable request) the game keeps running, the failure notice with
   the action appears and `helper.log` says why.
7. The session-side policy check: edit the user policy file between the click and the restart so that a server mod
   becomes excluded; the Arena menu of the session then refuses with `... the policy changed during the switch.`
   and RETURN TO NORMAL SESSION works. With the same policy it admits.
8. The patcher unit: a preloader patcher that the policy does not name (a throwaway DLL below `BepInEx\patchers`)
   makes the normal session refuse Arena with START ARENA SESSION; in the session the gate neutralises it (the
   report lists it under `patchersNeutralised`, and under `patchersRanBeforeGate` only when it sorts before the
   gate, which then refuses Arena). A support DLL without a `TargetDLLs` member in the same folder blocks nothing.
9. The late helper (second pass, 2026-10-22): with the helper made slow after it read the request (for example a
   second helper holding the mutex for more than 15 seconds), the client reports the timeout and deletes the request
   file; the helper that acknowledges late logs `the request was withdrawn` and ends, and closing the game by hand
   afterwards starts nothing.
10. The session without a policy (third pass, 2026-10-22): in an Arena session, move the shipped
    `arena-whitelist.json` away before the Arena menu opens (or make it unreadable); ARENA then refuses with `Arena
    is unavailable: its mod whitelist could not be loaded (...). ... Return to the normal session.` and RETURN TO
    NORMAL SESSION works. With the file back, the next Arena click (it asks the server again, so the files are read
    again) admits.
11. The plugins of the session (third pass): between the click and the restart, remove the user `allow` entry of an
    installed plugin that the default list does not name; the session's Arena menu refuses with `This game loaded
    <name>, which the current policy excludes: the policy changed during the switch. ...` and RETURN TO NORMAL
    SESSION works. With the same policy it admits.
12. The policy edited while the session runs (2026-10-22): in a healthy Arena session whose side screen was opened
    once, mirror a user file onto the machine that denies a loaded mod (`{"schema":1,"deny":["me.sol.sain"]}`),
    close and reopen the side screen: it refuses with `This game loaded SAIN, which the current policy excludes:
    the policy changed during the switch. ...`, RETURN TO NORMAL SESSION works, and no new ping was asked for.
    A file that only widens the policy (the one-line override) is re-read the same way (the Debug line) and the
    session stays admitted.
13. The watchdog (2026-10-23): in an Arena session close the game by hand (EXIT in the main menu, or kill the
    process); within a few seconds the arena server is gone (nothing listens on the arena port, 6970 or its
    fallback; `server.pid` is deleted) and `helper.log` ends with `the Arena game exited ... arena server after
    the game: Stopped`, written by the watch process the enter started (`watchdog started (pid W)`); a normal
    start of the game afterwards finds the normal server and runs as before. RETURN TO NORMAL SESSION still
    works (the watch of that game then logs `NothingToStop`).
14. A later deny of an allowed patcher (2026-10-23, external review): in an Arena session let a user file arrive that
    denies a preloader patcher the gate let run (`WTT-ContentBackportPatcher.dll` is on the default list) and open the
    side screen again: `ARENA UNAVAILABLE This game ran the preloader patcher <file>, which the current policy
    excludes: ...` with RETURN TO NORMAL SESSION, and one Warning `Arena session refused: ...` in the client log
    (section 7, check 4). Remove the deny and open the screen again: ARENA is selectable again (a widening refuses
    nothing).

Result on SPT-GPU-2, 2026-10-21 (build `db99587`, deployed to `D:\SPT41`), all five passed:

| Step | Observed |
|---|---|
| 1 | Normal start: 41 plugins loaded, 1198 patched methods; the gate is listed as a loaded patcher and writes no line of its own. Patcher order in the player: BepInEx.Preloader, FixPluginTypesSerialization, LogTools, spt-prepatch, gate, WTT-ContentBackportPatcher. |
| 2 | ARENA disabled with the whitelist reason and START ARENA SESSION; the two-click action works. |
| 3 | Restart to the main menu in 38 s. Gate: WTT patcher switched off, 13 plugins allowed, 28 excluded, 0 warnings; 545 patched methods. Arena server on 6970 ready 4 s after start with exactly ArenaMode, SAIN, APBS and ABPS; the profile copy only lost the PitFireTeam trader. ARENA preselected, PMC/Scav locked. Deathmatch on Factory: 7 bots active on SAIN and Arena layers, no ArenaMode warning; left and returned normally. |
| 4 | RETURN TO NORMAL SESSION: restart in 39 s, arena server stopped, normal server kept running, 41 plugins and 1198 patched methods again. |
| 5 | Second enter, then `taskkill /F` on the game and a normal `gpu-run start`: normal session with 1198 patched methods; `stop-server` then ended the left-over arena server. |

The run above used the default list of 2026-10-21 (SAIN, APBS, ABPS, BigBrain, Waypoints and the tools); since
2026-10-22 the list is the larger one of section 2 and the counts differ.

Second run on SPT-GPU-2, 2026-10-22 (the default list of section 2, before the review changes of this date; build:
HEAD `1713f5f`, the commit that first has the policy-override note the run shows, with no commit after it before the
review changes; the run's report names no hash, and no file with its observations was found, a search of the main
checkout for the probe's name in text files finds nothing, so the table below is the only record; recorded from that
report, not re-observed by the pass that writes it down; items 6 to 11 above were not part of it and are still open):

| Step | Observed |
|---|---|
| Normal session, every installed mod on the default list | Arena deathmatch started directly: no refusal, no warning, no restart. |
| A throwaway probe plugin that is not on the list | ARENA disabled with START ARENA SESSION; the session excluded the probe: 40 plugins allowed, 1 excluded. |
| The one-line override (`{ "schema": 1, "unlisted": "allow" }`) | The normal session allowed Arena with the note `policy override: 1 unlisted mod(s) allowed by your policy file: ArenaWhitelistProbe`; the session loaded the probe: 41 allowed, 0 excluded, with the gate's policy-override suffix. |
| The arena server from freshly copied binaries | 35 s to be ready on its first start, 6 s on the second. |

Follow-up fixed after the run: the refusal named a mod with differently named client and server parts
twice ("DynamicMaps, Dynamic Maps"); the list is now de-duplicated by GUID.

Third run on SPT-GPU-2, 2026-10-22 (build `380f696`: the review fixes of this date plus the policy stamp; the throwaway
probe plugin of the second run installed again; items 2, 3, 4, 12 and the handshake of item 6):

| Step | Observed |
|---|---|
| Normal session with the probe, no user file | ARENA disabled: `Arena runs only with whitelisted mods. Not on the whitelist: ArenaWhitelistProbe. Start an Arena session to play Arena without them; the game restarts.`; START ARENA SESSION with its two clicks. |
| Enter | The game quit only after the helper's acknowledgement; main menu of the session after 48 s; gate `40 plugins allowed, 1 excluded, 0 warnings` (the probe, `not-on-whitelist`); the side screen shows the ARENA SESSION notice, ARENA preselected. |
| The override arrives in the running session | About 60 s after the file was written on the mirroring machine it was on the host; closing and reopening the side screen logged `a policy file changed; the admission is evaluated again`; the session stayed admitted (nothing it runs is excluded). |
| A deny of SAIN arrives | Reopening the side screen refused: `ARENA UNAVAILABLE This game loaded SAIN, which the current policy excludes: the policy changed during the switch. Return to the normal session and start the Arena session again.`, ARENA locked, RETURN TO NORMAL SESSION offered; the Warning `Arena session refused: ...` logged once; no ping request. Before the stamp (the build of the second run) the same file arrived unnoticed: the cached outcome kept admitting until the next ping. |
| Leave | RETURN TO NORMAL SESSION needs its two clicks inside the confirm window (a single click arms and expires); the leave of the previous build took 41 s to the normal main menu, the one of this build was not timed. Afterwards 40 plugins (probe and user file removed meanwhile), ARENA selectable, no note. |

Fourth run on SPT-GPU-2, 2026-10-23 (build `fc46350`, the watchdog of section 6, step 7; the probe plugin installed
again so that the session is offered; item 13 in its three variants):

| Step | Observed |
|---|---|
| Enter | `helper.log`: the enter helper (pid 3456) started the arena server (pid 13888, ready after 26 s: the first start of freshly copied binaries), the game (pid 10984) and, 2 s later, `watchdog started (pid 8904): the arena server (pid 13888) ends with the game (pid 10984)`, then `enter finished`; the watch logged `watching the Arena game (pid 10984)` as its own process; `watch.json` and `server.pid` present. |
| EXIT in the main menu | 0.1 s after the game process ended: `the Arena game exited; stopping the arena server`, `stopping the arena server (pid 13888, ...\session\server\SPT.Server.exe)`, `arena server after the game: Stopped`; `server.pid` deleted. A normal start afterwards found the normal server and ran as before (56 s to the main menu). |
| Kill (`uibridge_quit hard=true`) | Second session (server 15260, game 6548, watch 13648): the same three lines within 0.1 s of the kill, `server.pid` deleted. |
| RETURN TO NORMAL SESSION | Third session (server 15992, game 6484, watch 2680): the leave helper (8308) took the mutex at 13:26:05, saw the game exit after 2.0 s, stopped the arena server itself and started the normal game; the watch saw the exit at 13:26:07.168 and waited; 9 ms after `leave finished` (13:26:10.710) it logged `arena server after the game: NothingToStop`. No double stop, nothing touched that the leave needed. |
| Remote two-click | Two of the four START ARENA SESSION attempts needed a second pair of clicks (the window is 5 s; the first pair after a fresh start did not arm), the user confirmed one pair by hand; not a helper matter. |

Fifth run on SPT-GPU-2, 2026-10-23 (build `015ff7b`, the review fixes of that date: the `Global\` mutex, `patchersAllowed`,
the confined backup config; no probe plugin, `LoadAmmoAnim` unlisted, so the normal session refuses Arena and offers the
session; items 2, 3 and 14):

| Step | Observed |
|---|---|
| Enter | START ARENA SESSION armed at the first pair of clicks. `helper.log`: the enter helper (pid 4600) took the request at 17:51:47Z, the game exited after 2.0 s, the tree was rebuilt (13 server mods, policy `47ee5e5aa6df7eb1`), the arena server (pid 16352) accepted connections after 22.3 s, the game (pid 11544) started with `-arena-session`, `watchdog started (pid 7428)`, `enter finished`; no Warning. The gate: `Arena session: 40 plugins allowed, 1 excluded, 0 warnings`. The tree's `backup.json` is byte-identical to the installed one (its `directory` is the shipped value), so nothing was confined and nothing logged. The side screen: `ARENA SESSION: only Arena is available ...`, ARENA selected. The enter, the watch and the leave below all ran through the shipping build's `Global\` mutex. |
| A deny of an allowed patcher arrives | The user file `{"schema":1,"deny":["WTT-ContentBackportPatcher.dll"]}` written on the VM reached the host after 14 s (Syncthing). BACK, ESCAPE FROM TARKOV: `Arena whitelist: a policy file changed; the admission is evaluated again for the screen` (Debug), then the Warning `Arena session refused: This game ran the preloader patcher WTT-ContentBackportPatcher.dll, which the current policy excludes: the policy changed during the switch. Return to the normal session and start the Arena session again.`; the screen shows `ARENA UNAVAILABLE This game ran the preloader patcher WTT-Co...`, ARENA disabled, RETURN TO NORMAL SESSION offered (item 14). Before this build the screen stayed admitted (the review's reproduction). |
| The deny is removed | The file deleted on the VM was gone on the host after 58 s. BACK, ESCAPE FROM TARKOV: the Debug line again, no refusal, ARENA selected again (a widening refuses nothing). |
| RETURN TO NORMAL SESSION | Armed at the first pair. The leave helper (pid 16248) took the mutex at 17:56:37Z, saw the game exit after 2.0 s, stopped the arena server (pid 16352), found the normal server listening and started the normal game (pid 16304), `leave finished` at 17:56:42.660Z; the watch (7428) had seen the exit at 17:56:39.043Z and logged `arena server after the game: NothingToStop` at 17:56:42.669Z, 9 ms after the leave released the mutex: the `Global\` mutex serialises the two. Normal main menu after 24 s of waiting, 41 plugins, 16 of 16 maschine files identical, normal server pid 1260, no second server; `server.pid` gone (`watch.json` stays, as before), no Warning in `helper.log` for the whole cycle; the user file removed on both machines. |
| Not covered | A backup config outside `user` on a real installation (the installed one carries the shipped value; the confinement is proved offline), two Windows sessions of one account. |

Sixth run on SPT-GPU-2, 2026-10-25 (the profile clean-up of section 6, the core config bullet; the development line,
because the host carried it: first the build before the fix, client and server `64c3b4a`, helper `f98c1f5`, then the
merge of the fix `bfe59be`, which carries `2e1155c`; the review corrections after it do not touch the path a core config
with a `fixes` object takes; Core 1.0.10). Both flags were set to SPT's shipped `false` in the installation's `core.json`
for the two enters and restored to the original bytes afterwards; a user file denied `com.wtt.contentbackport`, and the
profile SPT-GPU-2 holds 11 items of WTT-ContentBackport, among them its primary weapon:

| Step | Observed |
|---|---|
| Before the fix | START ARENA SESSION armed at the first pair; the enter built the tree (12 server mods) and started the arena server and the game. The arena server's log: `Failed to load profile with ID '6ab7db1c64f04b565cf78be9'. The profile will be marked as invalid.` with `InvalidModdedItemException` for the first WTT item. The client threw a `NullReferenceException` in `TarkovApplication.PrepareGameJob` 28 s after its start and closed itself 53 s after its start; the watch stopped the arena server (`Stopped`). Nothing on screen named the reason. |
| With the fix | The enter logged `core config: SPT's profile clean-up switched on in the tree's copy ... : core.json: fixes.removeModItemsFromProfile: false -> true, fixes.removeInvalidTradersFromProfile: false -> true` (Info). The tree's copy carried both flags on, the installation's file still both off. The arena server deleted the 11 WTT items from the copy (`Deleting item id: ... from inventory and insurance`, eleven lines) and the session reached its main menu after 27 s; the gate `39 plugins allowed, 2 excluded, 0 warnings`; the side screen showed the ARENA SESSION notice with ARENA selected; no Arena warning in the client log. |
| Return | RETURN TO NORMAL SESSION armed at the first pair; the leave stopped the arena server and started the normal game, the watch logged `NothingToStop` 13 ms after `leave finished`; no Warning in `helper.log` for the whole cycle. The real profile still holds its 314 items, the 11 WTT items and the WTT primary weapon among them. |

## 11. Changes since the first draft

Sections 5 to 8 above describe the implementation, not the first draft. What changed, and why:

- Section 5: the filter is installed in `Finish()`, not `Initialize()` (installing it earlier loads the
  unpatched `UnityEngine.CoreModule` and no plugin starts; measured, see
  [ARENA-GATE-20261021.md](ARENA-GATE-20261021.md)). The report is provisional until the first scan.
  Neutralised patchers are named in one Info line because Debug never reaches the log during the
  preloader stage, and a later failure says that they stay off.
- Section 6: `status` takes no mutex. A failed `enter` restores the normal session after the old game has
  exited (a player must not be left with no game). `stop-server` compares the server image as a file
  (junction and SUBST spellings differ from the path the game was started from), and `enter`/`leave` leave
  the arena server of another installation alone because the runtime root is shared. The wait for a server
  checks that the process it started holds the port. The request rules are stricter than the first draft
  (24-hex profile id, `serverMods` folders directly below `<sptRuntimeDir>\user\mods`) and the client runs
  the same parser before it quits the game. The tree is kept after `leave`. The server mod copies confine
  what the helper itself and the mods write beside their own files; mods that resolve paths from
  `AppContext.BaseDirectory` were not confined until the exe moved into the tree (last entry below).
- Section 7: the profile id is the `-token=` argument first; the ping is refreshed with a 250 ms bound when a
  screen opens; the refusal text differs when no session can be offered; a whitelist that cannot be loaded
  or evaluated refuses Arena in both session kinds (since 2026-10-22; before, a normal session failed open
  onto the per-mod refusals that no longer exist).
- Section 8: the refusal of an ordinary start is an SPT error body (`err` 1000), not an exception.
- Sections 7 and 8 (2026-10-22, external review): the ping of the Arena instance gains `policyHash`, the hash from
  the marker (read once at startup, `null` when the marker gives none), so that the client can compare it with
  the gate's hash; before, the hash was recorded in the marker and the gate report but never compared in
  admission. Chosen: a normal server has no such member (its ping is unchanged), an unusable hash is `null`
  plus a startup Warning instead of an exception or a refusal on the server side.
- Sections 3, 4 and 7 (2026-10-21, user decision): the user file may carry `"unlisted": "allow"`. Per unit the
  order is platform/arena, then the user `deny`, then the named entries, then `unlisted`. New reason
  `unlisted-allowed`; an unknown `unlisted` value invalidates the whole user file; the policy hash
  covers the mode and the denied names in blacklist mode only (whitelist hashes are unchanged); the
  client refusal and the gate's patcher warning name the denied mods instead of a whitelist.
- Section 6 (2026-10-21, after the GPU run): the arena server runs from its own copy of the server binaries
  in the tree, not from the installation's `SPT.Server.exe`. Why: several server mods resolve paths from
  `AppContext.BaseDirectory`, the folder of the exe, so a server started from the real runtime let them
  write into the real installation although the helper had copied their folders. What changed: `enter`
  copies the top-level files of the runtime (minus `sptLogger.json`, `SPT.Launcher*`, `*Linux*`, `*.so`)
  incrementally into the tree root and starts `<root>\server\SPT.Server.exe`; the marker names it as
  `serverExe`; the identity check of `stop-server`/`enter`/`leave` expects the tree's exe for such a marker
  and the installation's exe only for a marker without `serverExe` (an older helper's session is stopped
  once, a marker of this helper never takes the installation's exe for the arena server); the tree root
  is cleaned except for the binaries that are still current. Evidence (offline, scratch folders only, the
  installation was only read): the helper's own `Enter`, `Leave` and `stop-server` against a stand-in
  server with the same layout (apphost exe, dll, `runtimeconfig.json`, `deps.json`) showed the stand-in's
  `AppContext.BaseDirectory` and image path in the tree, nothing written to the runtime, a second `enter`
  copying no binary and an older helper's session (marker without `serverExe`, server running the
  runtime's exe, which wrote into the runtime as the control) being stopped by `stop-server` and by `enter`;
  the real `SPT.Server.exe` of 4.1.6 booted from a tree built from a scratch copy of the installed
  runtime (44 files, 23 MB copied, four server mods loaded, `/health` healthy, the server log shows the
  Arena instance, the copied runtime unchanged afterwards). Not covered: a GPU-host run of the whole switch
  with this change (section 10 records the run before it).
- Sections 3, 4, 6, 7 and 8 (2026-10-21, review pass): the closure covers server mods. The ping's `serverMods`
  entries carry `dependencies` (the mod's `ModDependencies`) and the client passes them as hard dependencies;
  before, a `deny` (or an omission under a whitelist) of a library that an allowed server mod needs made SPT's
  mod validator drop every mod in the tree, ArenaMode's server mod included. In blacklist mode a `deny` list
  that is not an array of non-blank strings invalidates the user file like an unknown `unlisted` value (a lost
  deny entry would load a mod the user excluded); everything else in the user file stays lenient. Section 6
  states the mixed-helper limit of installations that share one Windows user.
- Sections 1 to 4 and 7 (2026-10-22, user decision): the shipped default list allows every mod that is installed on
  the development hosts (section 2) and carries a **deny list** (`deny`, same syntax as the user file's) with the
  five mods ArenaMode already refused by code. Per unit the order is platform/arena, user `deny`, user `allow`,
  default `deny`, default entries, then `unlisted`; a user `allow` lifts a default deny, a user `deny` always
  wins, and the default denies also hold in blacklist mode. New exclusion reason `denied` (`denied-by-user` stays
  the user's); the closure still pulled a denied hard dependency in, with the warning naming the list that denied it
  (superseded on 2026-10-22, see the last entry).
  A malformed default `deny` invalidates the default file; a GUID both listed and denied there is denied and
  reported. The policy hash gains `default-denied|<name>` lines only when the default file has a deny list, so
  every earlier pinned value is unchanged (the pins use an empty default file). The client refusal splits "Blocked
  for Arena" (denied) from "Not on the whitelist" (unlisted), the blacklist wording became `Arena does not run with
  denied mods. Blocked for Arena: ...`, and the gate's patcher warning and Debug lines name the list that excluded
  the unit. The code-level refusals for the five denied mods are unchanged.
- Sections 1 and 7 (2026-10-22, user decision): the whitelist replaces the per-mod code. Removed: the client
  adapters for MoreBotsAPI, AI Limit, Looting Bots, Remove The Dead, Smoothie, Late To The Party, Classic
  Movement, StatTrack, Deminvincibility, Skills Extended, Pause Continued and EFT Effects Manager / Broken Limbs
  (with their offline harnesses), and the code-level refusals of ORBIT, Adaptive Bot Culling, Questing Bots and
  ISB Special Forces/Aishi (client `ArenaOptionalMods`, `ArenaIsbAdmission`, the server's ISB admission and the
  ping's `admissionFailure` source). `ArenaOptionalMods.GetRejectionReason` is now the whitelist, then the
  failure a server reported in its ping (the client still parses an `admissionFailure` member and shows it, an
  older server may send one; this server sends none), then Continuous Healing, Continuous Load Ammo and Recoil
  Rework Legacy. Consequence: the whitelist is the only guard, so a normal session with a missing, unreadable
  or invalid default list, or a failing evaluation, refuses Arena instead of failing open. Also reverted as foreign-mod
  specific: Arena's `BotSpawnParams` marker `Id_spawn = "arenamode-owned"` (MoreBotsAPI only), the pickup
  markers' camera-checked layer (Smoothie only), the weapon pickup's foreign-cleanup registry (Late To The
  Party only) and `ArenaSkills`' separate Effects Manager contribution.
- Sections 3, 4 and 7 (2026-10-22, external review, `artifacts/session-review-20261022/gate-policy`): deny wins over
  hard dependencies. Before, the closure pulled a unit that a deny list names in as a hard dependency of an allowed
  unit, with a warning. Since the code-level refusals of ORBIT, Questing Bots, Adaptive Bot Culling, ISB Special
  Forces and Aishi are gone, the default deny list is the only guard for them, and one allowed mod that lists such a
  mod as a dependency (or a user `deny` on a library) defeated it. Now a denied unit is never allowed; a unit that
  needs it, directly or through other units, is excluded with `requires-denied:<guid>` and one warning, on the client
  and on the server; unlisted libraries are still pulled in. Consequences: the pull-in warnings with
  `(overrides the ... deny)` are gone, `dependency-of:` occurs under a whitelist only, the excluded dependants count
  as denied ("Blocked for Arena") in the normal-session refusal, and a user who wants a mod that needs a denied
  library has to allow the library. Platform and Arena units stay allowed whatever they name (step 1).
- Section 6 (2026-10-22, external review, `artifacts/session-review-20261022/isolation` and `root`): three helper
  changes. (1) The helper acknowledges a request with `<request file>.accepted` (atomic, after validation and the
  mutex, before it waits for the game) and the client closes the game only after it has seen it; before, the client
  quit the game right after `Process.Start` and a helper that failed at once (an unusable request, a busy mutex)
  left the player at the desktop with the reason only in `helper.log`. The stale-file deletion, the mutex, the
  validation and the acknowledgement run in the fixed order of `HelperHost`, which was split out of `Program.Main`
  so that the order is tested against the real classes. (2) The tree gets `user\patchers\<ModGuid>` for every server
  mod of the request that has one (SPT 4.1.6 loads server prepatch definitions from there, and drops every mod when a
  mod's folder is missing); before, a user-allowed mod with a server prepatcher was copied without it. Only the
  listed mods' folders are copied, because SPT applies every folder it finds. (3) `server.pid` is written right after
  the start, and a failed write ends the just-started server with the handle at hand and fails the enter; before,
  the exception left a running server that no later cleanup could find. Not done at the time: a helper killed in
  the instant between the start and the write, and recognising an unregistered server by its image (an interrupted
  registration); done in the second pass, see the last entry.
- Sections 4 and 5 (2026-10-22, external review, `artifacts/session-review-20261022/gate-policy`, finding 3): the gate
  report names the patchers whose initializer had already run. Before, such a patcher (one that is not allowed and
  sorts before the gate) was only a free-text warning in the report, which nothing could tell from a harmless
  version note, although "not loaded at all" is not true for it: its assembly is loaded and its initializer may have
  registered Harmony patches, events or writes. Now the report has the member `patchersRanBeforeGate` (file names, a
  subset of `patchersNeutralised`, always present, kept by the provisional and every inactive report) and the Info
  summary gets the suffix ` (N patcher(s) ran before the gate)`. Chosen: the gate only reports (the member is data,
  the decision is the reader's); the warning text and the neutralising are unchanged; the list was filled only when the
  gate found its own position in the patcher list, otherwise the existing "position unknown" warning stayed the only
  trace (changed in the second pass, see the last entry). The harness has a world without the early patcher
  (`no-early-patcher`) for the empty case, and nine mutants.
- Sections 6, 7 and 10 (2026-10-22, external review, `artifacts/session-review-20261022/root`, findings 1 to 3 on the
  client side; the answer to each finding is in [ARENA-SESSION-REVIEW-RESPONSE-20261022.md](ARENA-SESSION-REVIEW-RESPONSE-20261022.md)).
  (1) An Arena session now refuses when the server runs a unit the current policy excludes and when the server's
  policy hash (new `policyHash` member of the ping, section 8) is not the gate's: before, the client computed the
  server's exclusions in a session and only turned them into notes, and the hash was never compared. (2) It refuses
  when the gate report's `patchersRanBeforeGate` is not empty, and a normal session counts every preloader patcher
  (`*.dll` below `BepInEx\patchers`, by file name) as a unit of the decision, so a patcher the policy does not name
  blocks a direct start like an unlisted plugin does while the session action stays available. (3) The game quits
  only after the helper's `<request file>.accepted`: before, it quit as soon as `Process.Start` returned and the
  helper had not exited in that instant, which left the player at the desktop when the helper failed a moment
  later. Chosen: a coroutine on the plugin looking every 250 ms for at most 15 s, a helper that is gone or
  silent leaves the game running with the action available again, a file only counts with this helper's pid and a
  running helper, the stale file is deleted first, and a timeout also deletes the request file (a helper that has not read
  it yet then fails to read it). Not done at the time: stopping a helper that had already taken the request over (it
  still waited 120 s for the game and could switch if the player closed it then; the helper could not tell that the
  client had given up), and telling a library in the patchers folder from a patcher without loading it; both done in
  the second pass, see the last entry. The review's files (`artifacts/session-review-20261022/`, the `root` report in
  German with the `gate-policy`, `compat` and `isolation` sub-reports) are in the main checkout's ignored `artifacts`
  folder (`D:\SPT41\Development\ArenaMode\artifacts\session-review-20261022\root\report.md`), not in this branch.
- Sections 3 to 7 and 10 (2026-10-22, second pass: the fixes above were reviewed again, eleven findings; the decision on
  each is in [ARENA-SESSION-REVIEW-RESPONSE-20261022.md](ARENA-SESSION-REVIEW-RESPONSE-20261022.md), "Second pass").
  (1) The policy hash covers the user's denied names in a whitelist as well: since deny wins over hard dependencies a
  deny of an unlisted library changes decisions, and two policies that decide differently shared a hash (a user file
  edited between the click and the restart went unnoticed by the comparison of the hashes). Policies without a user
  deny keep their hash; two new pinned values. (2) A request the client takes back: the client deletes the request
  file on every failure (also when the helper stopped, the wait failed or could not be scheduled), and the helper
  stamps the file and ends its wait for the game when the file is gone or replaced, so a helper that acknowledged
  late can no longer switch the session behind the player's back after "the game keeps running". (3) The gate fails
  closed: a patcher it cannot place is neutralised as `(unidentified) <type name>` and reported, and an unknown gate
  position reports every neutralised patcher with an initializer as having run early; harness scenarios
  `unidentified-patcher` and `gate-unlocated`. (4) An arena server that no `server.pid` names is found by its image and
  stopped under the ownership rule of the registered one (`stop-server`, `leave`, `enter`). (5) A support library in
  `BepInEx\patchers` is no unit in a normal session (a `TargetDLLs` search; a file that cannot be read counts as a
  patcher). (6) The client and the gate read the policy files through one function (`ArenaPolicyFiles`, 256 KB, one
  decoding): before, only the client enforced the limit, and a user file above it locked the Arena session out for good
  through the policy-hash check; harness world `user-oversize`, source pin in `ArenaMode.Tests`. (7) Tests and
  harness mutants for what the first pass left unpinned: the first of several denied dependencies, a stale pid file
  after a failed registration, the "could NOT be ended" outcome. Not done: the observations of section 10 items 6 to 9
  on a GPU host.
- Sections 2, 3, 7 and 10 (2026-10-22, third pass, the gate-policy review's appendix; the client part). (1) An Arena
  session whose policy cannot be loaded now refuses (check 2 of the Arena session), with the normal session's wording
  plus `Return to the normal session.`: before, `ComputeArenaSession` judged the server's mods only `if the policy
  loaded` and otherwise admitted on the gate's word, although a normal session refuses in the same situation (the
  review reproduced it: default file missing, normal session refuses, Arena session admits). (2) The plugins the
  game loaded are evaluated with the policy as it reads now (check 4 then, check 5 since 2026-10-23): the gate filtered with the files of the
  moment the game started and nothing compared the plugin list with the current files, the same hole the second
  pass closed for the server's mods. Chosen: the gate's own decision, any exclusion reason refuses, the wording
  follows the server-mod refusal (`This game loaded <names>, ...`), the checks that need no ping come first (2, 3
  and 4 then; 2 to 5 since 2026-10-23), and the decision's warnings are not added to the notes because the gate's report carries them. Accepted
  limit: a library pulled in for a plugin that then failed to load reads as excluded and refuses (fail closed, the
  same hint). (3) Section 7 said that a missing or unreadable default list "makes the whitelist stand aside" in a
  normal session, which contradicted the code, section 2 and the entry of this section on the same subject (the
  compat review quoted it); it now says what the code does, that the whitelist never fails open. Harness:
  `tools/ArenaSessionClientChecks/SessionPolicyChecks.cs` with fifteen mutants, among them one swap per adjacent pair
  of checks (the order is part of the contract). Not done: the observations of section 10 items 10 and 11 on a GPU
  host.
- Sections 2, 3 and 9 (2026-10-22, external review `artifacts/session-review-20261022/gate-policy`, appendix: code-defined
  units in the default file, and `deny: null`). (1) A `mods` entry of the shipped default file that names a platform or
  Arena unit, or any `com.fika.` GUID, is skipped with a `default whitelist:` diagnostic and the file stays valid (the
  user file's `allow` skips the same entries, without a diagnostic). Before, `AddEntry` replaced the code entry (Mod
  or Tool tier, both sides, the file's versions), and a default `deny` of the same name, which protects code-tier
  entries only, then removed the unit: a shipped file that named `com.SPT.core` or `com.maschine.ArenaMode` in `mods`
  and `deny` left it not allowed. A default `deny` of such a unit was already skipped with a diagnostic; that is
  pinned now, together with the chain. Neither skip changes the policy hash. Chosen: skip, not fail the load, like the
  deny and like the user file; the build scripts are where the mistake is caught. (2) `tools/Write-ArenaAssets.ps1`
  (its checks moved into the functions `Test-ArenaProtectedUnit` and `Assert-ArenaDefaultWhitelist`) and
  `tools/Verify-TestRelease.py` (`check_default_whitelist`) reject a default file that lists or denies such a unit.
  Each holds its own copy of the contract's lists; the tests hand both scripts every unit of `ArenaSessionContract`,
  so a unit added to the contract and not to the scripts fails them. (3) JSON `null` for the user file's `allow`,
  `deny` and `unlisted` is a missing member in both modes. The code already behaved so, `"deny": null` under a
  blacklist included (the review found the spec wording stricter than the code); the spec says it now and tests pin
  it, hash included. Chosen: null is not an unreadable list because it carries no entry that could be lost; the
  default file's `deny` stays strict. Tests: nine new test methods in `ArenaWhitelistTests` (twelve cases, 292 to 304
  in that class), two of which run the real scripts (the PowerShell functions are lifted out of the script with its syntax tree, the Python function is imported;
  they skip without `pwsh` or `python`). Evidence (offline, scratch copies only, the installation was not touched):
  26 mutants of the policy, of the PowerShell functions and of the Python function (guard removed, one unit lost from
  a list, no path reduction, case-sensitive match, silent skip, skip that fails the load, null treated as an error)
  each fail a test; `Write-ArenaAssets.ps1`, run in full against a scratch copy of the tree, accepts the shipped list
  and refuses twelve variants before it writes anything; `verify()` of `Verify-TestRelease.py` on a synthetic
  74-file package accepts the shipped list and refuses the same variants; the versions of both scripts before this
  change accepted a list that names `com.SPT.core` and denies `com.maschine.ArenaMode`. Not covered: a package made by
  `Package-TestRelease.ps1`, a game run.
- Section 6 (2026-10-22, third pass of the helper, `artifacts/session-review-20261022/isolation` finding 3 and its
  hardening notes). Three changes, all in `ArenaMode.Session`. (1) The wait for the server no longer takes a missing owner
  for the server. Before, a connection that succeeded counted as "our server is ready" when the system's TCP table was
  unreadable or listed no listener at all (`owners == null || owners.Count == 0 || owners.Contains(pid)`), and the owner
  lookup covered every local address of the port, not the one the connection reached. Now an empty table keeps the wait
  going, other owners keep it going (as before), and an unreadable table is accepted only if the helper's own probe
  found the port free right before the start (re-probed in `StartArenaServer`, because the tree build sits between
  the choice and the start; the normal server of `leave` has its connect probe as proof), with one Warning that the owner
  could not be verified; the timeout names the reason. The lookup is bound to the address the connection reached
  (`CanConnect(..., out address)`, `GetListenerProcessIds(port, address)`, IPv4-mapped spellings unmapped, wildcard
  listeners counted). `IsPortFree` takes the server's host. Chosen: a failing probe or lookup is "cannot verify", never
  "free"; the dual-stack `[::]` listener counts for an IPv4 connection (the IPv6 table alone cannot tell it from a V6-only
  one; the IPv4 table of Windows Server 2025 lists a dual-stack socket as `0.0.0.0` as well, which would, but the IPv6 read
  stays for systems that list it only once; a false accept would need our server on `[::]` V6-only beside a foreign IPv4
  holder of the same port).
  (2) Writes never follow a link: `WriteAllText` (and so `server.pid`) removes a link at the destination first, like a
  copy, and every file creation or truncation (`WriteAllText`, copies, `helper.log`, the acknowledgement's temporary
  file) goes through `HelperNative.OpenFileForWriting`, which opens with `FILE_FLAG_OPEN_REPARSE_POINT` and refuses a
  file link (a junction or a folder cannot be opened for writing at all and fails with access denied); `helper.log` replaces a link under its name with a plain file and says so in its first line.
  Before, `File.WriteAllText` and `FileMode.Append` followed a link planted at the destination (the review's scratch test
  changed a file through a `server.pid` link). Open: a hard link, which is not a reparse point. (3) `filePath` of the
  copied `sptLogger.json`: a value that is not a relative path below `user` is moved to `./user/logs/<last segment>/`
  (one Warning names the moves); before, only `filePattern` was prefixed and an absolute `filePath` kept the arena
  server logging into the real runtime. Open: the content of `filePattern` beyond the prefix is not judged (a pattern
  with `..` segments could still climb). Tests: 99 new cases in `ArenaSessionHelperTests`/`ArenaSessionHelperProcessTests`
  (the lookup binding with real listeners on `127.0.0.1`, `127.0.0.2` and `::1`; every branch of the wait with an
  injectable lookup (`HelperRunner.ListenerLookup`) and probe (`PortFreeProbe`); links planted before and, through the
  test hooks `HelperSandbox.BeforeWriteOpen` and `HelperLog.BeforeOpen`, between the check and the open; the logger path
  table). Evidence (offline, scratch folders only, the installation was not touched): 27 mutants of the helper (each
  rule of the wait, the probe before the start, the address binding in the runner and in the table read, the unmapping,
  each link rule and each open flag of the four writers, each rule of the path table) were applied one at a time to the
  production source and run against these tests; 26 turn at least one test red. The survivor makes `IsPortFree` connect
  to the loopback address instead of the server's own: equivalent in practice, because the bind that follows on the
  server's address fails for any address with a listener (a socket that opted into address reuse is the only case, and
  none is staged). Four mutants survived the first round: two led to test cases (`..` that climbs out and comes back
  through a folder called `user`, and a root-relative `/user/...` path), one was a redundant check (a colon or wildcard
  anywhere in the path, which `IsSafeSingleName` rejects per segment anyway) and was removed, and one is the equivalent
  survivor above. Not covered: a GPU run, a hard link.
- Section 6 (2026-10-22, review of the helper's third pass; offline only). Four findings, each checked against the code and the
  real file system / sockets before anything was changed. (1) Real wildcard listeners had no test of their own: the only
  wildcard coverage was the pure `ListenerServes` table, so a mutant that keeps that function but compares addresses exactly
  where the rows of the system table are filtered survived every test. Two tests now bind a real `0.0.0.0` listener and a real
  dual-stack `[::]` listener and look the owner up for the address a `CanConnect` probe reached
  (`AnIpv4WildcardListenerIsWhatAnIpv4ConnectionReachesAndAnIpv6OneIsNot`,
  `ADualStackWildcardListenerServesAnIpv4Connection`). One premise of the finding did not hold and is recorded: it said
  that a healthy server on `[::]` is listed in the IPv6 table only, so that dropping the IPv6 read for an IPv4 address would fail every wait. On Windows Server 2025 the IPv4
  table lists a dual-stack `[::]` socket as `0.0.0.0` too (`netstat -ano -p tcp` and `-p tcpv6` both show it; a V6-only socket
  only in the IPv6 table), so that mutant is invisible with a dual-stack listener on this system; it only stops counting a
  V6-only `[::]` listener, the false accept named in the previous entry for section 6. The read stays (older systems may list a
  dual-stack socket once), and it has no mutant. (2) `HelperNative.OpenFileForWriting` was documented to refuse a link, a
  junction and a folder with a `HelperException`. Only a file symbolic link gets that far; a junction and a folder fail in
  `CreateFile` with "access denied" because the open has no backup semantics, so they surface as an
  `UnauthorizedAccessException` (nothing is written in any of the three cases; the sandbox's retry repeats it for about three
  seconds). The folder branch of the attribute check was dead and was removed; the XML summary and section 6 say what
  happens; the test pins each exception type and that the folder stays untouched. The refusal message of a file link still
  says "symbolic link or junction". (3) `CanConnect` probed with `new TcpClient()`: an IPv4-only socket on .NET Framework (the
  helper) and a dual-stack one on net10.0 (the tests), so an `ip` of `::1` in `http.json` could never connect on the shipping
  runtime while its unit tests passed; the user's installed `http.json` has `127.0.0.1`, so nothing is known to be affected.
  An IP literal is now probed with a socket of its own address family, a host name as before (`NewProbeClient`).
  `tools/ArenaSessionHelperNet472Checks` (section 9) checks it on .NET Framework: seven checks, three mutants (the probe
  without the family, wildcard rows matched by equality only, an IPv4 wildcard that serves nothing); the exe built before this
  change fails exactly the IPv6-literal check. (4) The text said that a parent folder that is a link ends the `helper.log`
  write. It does so only when the log's own name is a link as well; a plain log in a folder that is a link is written through
  (only the log's own name is judged). Section 6 says so; a new test pins the write-through, and the old test, which sets up
  both conditions, was renamed `ALogLinkInAFolderThatIsAJunctionIsRefusedNotRedirected`. Evidence: `dotnet test` 2188
  passed (3 more than before), and three hand-made mutants of the production source were each killed by the test named for
  it among the 17 tests run against them: the wildcard filter by equality (the two listener tests), the open with
  backup semantics (the test of `OpenFileForWriting`), a log that refuses a linked folder (the write-through test). Not covered:
  a GPU run (the helper binary changed: every installation of one Windows user receives it together with the client, as before).
- Section 7 (2026-10-22, after the review fixes ran on SPT-GPU-2). The admission outcome was cached per ping generation and
  plugin count, so a policy file changed while the game ran (the user file edited, or mirrored by Syncthing) stayed
  invisible until the next ping: in the Arena session of the GPU run the refusals of checks 5 to 7 (6 to 8 since 2026-10-23) never fired for a file
  that arrived after the first screen. Opening the side screen and requesting the session start now compare the length
  and last-write time of both policy files with the stamps the cached outcome was computed from and evaluate again when
  they differ (`ArenaWhitelistAdmission.InvalidateIfPolicyChanged`, a Debug line names the occasion); menu polling
  still does no I/O, and the known ping is reused. Found by the run, not by the review. Reviewed once more (three
  lenses, each finding checked by a skeptic): the START ARENA SESSION click still built its request from the cached
  outcome (an edit while the screen stayed open travelled into the server tree and was refused after the restart: two
  restarts for nothing; fixed), the harness pinned only the user file's creation (now also its deletion, an edited
  default file, the Arena session kind, the click, and that an unchanged file re-evaluates nothing and pings nothing),
  and the class summary named two triggers. Not changed: a widening change is noticed but produces no note (section 7).
  Evidence: `ArenaSessionClientChecks` 679 assertions, 163 mutants rejected (seven on the stamp: invalidation
  removed, the user file or the default file left out of the stamp, the stamp never recorded, the equality check
  removed, the session kind skipped, the click without the check); the third run of section 10.
- Section 6, step 7, and section 10, item 13 (2026-10-23, user request). A game closed by hand inside an Arena session
  (EXIT, a crash, a kill) left the arena server running beside the normal one; only RETURN TO NORMAL SESSION stopped
  it. The `enter` now writes `watch.json` and starts the helper's own exe as `watch --file`, a second process so
  that nothing the old game made inheritable stays open for the session (the enter helper ends as before). The
  watch opens the game by pid, exe name and start time, waits for it to exit without the mutex, then takes the
  mutex (up to 180 s, because a `leave` holds it while it restarts the game) and stops the arena server with the
  rules of enter and leave, bound to the server its enter started (`server.pid` naming another pid, or a newer
  marker, means a newer session owns the record and nothing is touched); after a `leave` it finds `NothingToStop`,
  a mutex that stays busy leaves the server to its holder (`HelperCommand.Watch`, `HelperWatchFile`,
  `HelperRunner.StartWatchdog`/`LoadWatch`/`StopAfterGameExit`, `HelperHost.WatchArenaGame`). A first draft kept
  the enter process alive instead; its review found the inherited handles and the unbound stop, hence this shape.
  Tests (`ArenaSessionHelperProcessTests`, real stand-in processes): the watch ends a registered server and its pid
  file when the game is killed; it waits for a mutex a leave holds and then finds nothing to stop; a mutex that
  stays busy leaves the server alone; a newer session's record and the server of another installation are left
  alone; the command reads its file, waits for a live game, stops at once for a gone or reused pid, and refuses a
  file it cannot read; the command and the file parse; source pins hold the order (enter: game, watchdog,
  finished; host: the watch command before the mutex, the stop after the wait). Fixture: the runner does not
  start a watchdog (the test assembly is not the helper's exe). SPT-GPU-2 ran the three variants of item 13 the
  same day (section 10, fourth run).
- Sections 4, 5, 6, 7 and 9 (2026-10-23, external review,
  [ARENA-SESSION-REVIEW-RESPONSE-20261023.md](ARENA-SESSION-REVIEW-RESPONSE-20261023.md)). Three findings of a second
  Codex review at `c021c0d`, all three corrected. (1) The tree's copy of `backup.json` kept the runtime's backup
  `directory`; an absolute one, or one that climbs out of the tree, put the disposable profile's backups into the
  normal server's pool, where SPT's retention deletes the normal server's backups and a restore may pick an arena
  copy. In every file SPT's loader reads as the backup config (`.json`/`.jsonc`, name a substring of `spt-backup`) the
  copy now confines the directory to `./user/profiles/backups` unless it lies below `user`, and adds the member when it
  is missing, because SPT's code default is the empty string, the drive root (`HelperJsonText.TryConfineBackupDirectory`,
  `HelperLayout.IsBackupConfigFile`, the rule of the logger paths; `HelperTreeResult.BackupDirectoryRewrites`, one
  Warning names the changes; the installed file carries the shipped value). (2) A preloader patcher the gate let run
  and a later policy denies did not refuse the session: the client judged the loaded plugins, the server's mods and the
  patchers that ran before the gate with the policy of the moment, never the allowed patchers. The gate report lists
  them now (`patchersAllowed`, in every report; `ArenaGatePatchers.Outcome.Allowed`, `ArenaGateRuntime.AllowedPatchers`)
  and the client refuses when the current policy does not allow one of them (section 7, check 4; the old checks 4 to
  7 are 5 to 8). (3) The helper mutex was `Local\`, one per Windows session, while the runtime root is one per user:
  two sessions of one account did not serialise. It is `Global\maschine-ArenaMode.Session.<SID>` now (`HelperMutex` in
  HelperHost.cs, `ArenaSessionContract.HelperMutexNamePrefix`), created with an access list for that user and SYSTEM,
  because the default list comes from the creating token (an elevated administrator's names no user SID). Tests:
  `ArenaSessionHelperTests` (the backup directory in the text, with a missing member and in a built tree, the loader's
  file rule, a malformed file, the mutex name and exclusion, the source pins), `ArenaSessionHelperProcessTests` (the
  runner's Warning), `ArenaGateReportTests` and `ArenaWhitelistTests` (the member, the JSON shape, the contract prefix);
  `tools/ArenaSessionClientChecks` (the refusal and its wording, the order against both neighbours, the cap, a file
  listed twice, blacklist mode, an older report, a neutralised patcher, the normal-session control; eight mutants),
  `tools/ArenaGateChecks` (`patchersAllowed` read from the raw JSON in every scenario that reads a report; five
  mutants, three existing ones re-anchored), `tools/ArenaSessionHelperNet472Checks` (the shipping build's mutex name
  and the access list of a mutex it creates; three mutants, the table can name its file). Negative control: the eleven
  new xunit cases of the first draft fail against the three faults they claim to catch. An adversarial review of the
  change (six lenses, three skeptics per finding, 75 agents) confirmed and this entry's state corrected: the missing
  `directory` member was left alone on the false premise that SPT's default is the private folder; only `backup.json`
  was confined although the loader reads `.jsonc` and other names; six existing client-harness mutants no longer hit
  their named assertion because the new check ran ahead of older checks (it runs after them now, and the swap mutant
  that spans it is re-anchored); the net472 access-list check opened the real name, which an existing helper's mutex
  would have satisfied vacuously (it creates a fresh name); the wording about the default access list held only for an
  elevated creator; stale check numbers, counts and refusal lists in the docs. The final run of the pass
  (`tools/Run-ArenaVerification.ps1`, 2026-10-23): the xunit suite 2 308 tests, 2 307 passed, 1 known skip (the
  installed-APBS clone test); `ArenaSessionClientChecks` 698 assertions, 171 mutants rejected; `ArenaGateChecks` 17 scenarios,
  705 checks, 40 mutants rejected; `ArenaSessionHelperNet472Checks` 9 checks, 6 mutants rejected; the installed
  files unchanged. SPT-GPU-2 ran the pass the same day (section 10, fifth run): enter and leave through the `Global\`
  mutex (the watch waited for the leave's mutex), the refusal of a patcher denied in the running session and its lifting,
  no Warning. The response document records the review's own evidence and what it listed as bounded.
- Section 6 (2026-10-25, after the release 0.2.0, user question): the tree's copy of the core config switches SPT's
  profile clean-up on (`fixes.removeModItemsFromProfile` and `fixes.removeInvalidTradersFromProfile`). SPT ships both
  as false; all development installations had them on, so no run noticed that a session leaving out a content mod
  (items, suits, traders) cannot load a profile that holds that mod's records: the first run of section 10 excluded
  WTT-ContentBackport with the flags on and only recorded the PitFireTeam trader's removal. Reproduced on SPT-GPU-2
  with the build before the fix (section 6, the core config bullet). `HelperJsonText.TryEnableProfileCleanup` (a flag
  not literally true replaced, a missing one appended in the file's style through the shared `AppendMembers`, which
  now takes any object of the document; a core config without a `fixes` object refused), `HelperLayout.IsCoreConfigFile`/`IsConfigFileOf` (the
  loader's file rule, shared with the backup config), `HelperTreeResult.ProfileCleanupChanges`, one Info line of the
  runner. Tests: `ArenaSessionHelperTests` (a reduced copy of SPT's shipped block, values that are not the literal true,
  missing flags in three styles, a missing or non-object `fixes` refused, members of those names elsewhere, a second
  pass, malformed text, the loader's file rule, built trees with `core.json`, `core.jsonc` and `spt-core.json` and the
  runtime's files unchanged, a runtime without a core config or with both flags on, a malformed or `fixes`-less core
  config failing the build) and `ArenaSessionHelperProcessTests` (the runner's Info line, no Warning, no line for a
  runtime without a core config); the general tree fixture's `core.json` now carries both flags on. The JSON scanner
  now ends a `//` comment at a lone CR as well, as SPT's reader does (a review finding; it serves `http.json`,
  `sptLogger.json` and the backup config too). Negative control: with the core config skipped by the build, a
  present flag left as it is, or a missing flag not appended, eight of the new and extended cases fail. An adversarial
  review (three lenses, two skeptics per finding) confirmed missing tests for several core config files and for a
  runtime without one, and listed wording and scanner details; all addressed.
