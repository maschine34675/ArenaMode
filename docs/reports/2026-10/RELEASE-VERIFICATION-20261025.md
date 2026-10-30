# ArenaMode 0.3.0 and Redeploy 1.0.2 verification

Checked on 2026-10-25 with SPT 4.1.6. This record separates compiled packages,
native gameplay and security scan results.

## Release identities

| Package | Frozen source | ZIP SHA-256 |
| --- | --- | --- |
| ArenaMode 0.3.0 | `27cdb374deab4d38225fa59d17ff7c54bedfd4e3` | `3197f0552bd864b2289e0a091dcee4a42d0331410d2a30cb97095a6e026bae28` |
| Redeploy 1.0.2 | `1c78fb3694912106bb9e43b02020c3f0e37cdaf3` | `2d06882d950a4cc575ca9411062c9f06eebb7c1d876e9ae5e6dcac1421a7198e` |

Both contain the identical Core **1.0.13** library, SHA-256
`b6d226bfe61c7ff4333f7c2c90c35680cc1e5d413a962a104b22dd8ee4f6d9c5`.
Assembly versions, Windows FileVersion/ProductVersion, plugin/server metadata
and both clients' minimum-Core stamps were read from the final binaries.

The old local Redeploy Client and Server were still **1.0.0**. The independently
downloaded GitHub 1.0.1 package was correctly stamped 1.0.1; this was a stale
local deployment. Final Client/Server 1.0.2 and Core 1.0.13 were installed together.
The default Arena policy now records Redeploy Client/Server 1.0.2 as reviewed.

Arena's ZIP contains 74 files: five DLLs, the session-helper EXE, assets and
notices. Redeploy contains exactly three DLLs. Both ZIP roots contain only
`BepInEx/` and `SPT_Runtime/`. Every extracted file was compared with staging;
Core matches across packages. Existing custom kits and ladder were preserved
during local deployment. No temporary test mods were installed.

## Automated checks

- Final Arena suite: **3,197 passed, zero failed, one existing APBS environment skip**.
- Redeploy: **274 passed, zero failed, zero skipped**.
- Shared body coordinator: 19 scenarios, 458 assertions and nine rejected mutants.
- Respawn interop: 47 checks. Arena session client: 748 assertions.
- Admission: 54 assertions and nine rejected mutants, including omission or
  reordering of Tarkova Cola's protection provider.
- Recoil Legacy: baseline and nine mutants; Continuous Load Ammo: 111 assertions
  and 32 mutants; Continuous Healing: 286 assertions and 14 mutants.
- Archive verification covered the exact binary set, default policy, 32 notice
  links, 41 audio provenance entries and exclusion of bundled PitFireTeam files.

An initial long-path harness launch was repeated from a short committed-source
export. The admission harness was missing the newly added Tarkova Cola stand-in;
it was repaired and given behavioral negative controls. The explicit policy
fixture/specification were updated for the newly reviewed Redeploy version.
The final full suite passed after those corrections. No runtime gameplay code
was changed by these test fixes.

## Native checks on GPU2

The initial 0.3.0 candidate used Arena source `7cf41f9` and the final Redeploy/Core
bytes. A real ordinary PMC Factory raid showed the native Redeploy dialog after
death, respawned a fresh body at 440 HP and completed a Gate 3 Run Through.
Redeploy was the Core caller; Arena remained installed but inactive in this raid.
Native ordinary-raid progression added 100 XP. The test profile entered without
a firearm/armor; its own dogtag remained on the old corpse as expected.

The actual Arena-session button saved/stopped normal SPT and started the private
session. A real Factory DM gunfire kill was recorded. Returning through the UI
paid exactly **1,000 RUB by mail and 100 PMC XP**, one additional payment ledger
entry. Compared with the post-PMC profile, all 302 inventory items, Skills and
Quests were preserved. No synthetic result or manual progression write was used.

The stale Redeploy policy notice found in that test prompted the final correction.
All six executable entries in the final Arena package were compared with the
initial candidate: runtime method IL, fields, assembly references and embedded
resources were unchanged. The product/source stamps and external default policy
changed; Redeploy/Core bytes stayed identical.

The final package was synchronized and every one of its 74 deployed files was
hash-checked on both VM and GPU2. A fresh normal/private/return cycle loaded Arena
27cdb374, Redeploy Client/Server 1.0.2 and Core 1.0.13. Redeploy was now included
in the private session and its obsolete version notice disappeared. The empty
return kept XP at 7,961 and the payment ledger at 11: no duplicate payment.
Inventory, Skills and Quests matched the actual final entry state. A knife's
StackObjectsCount normalization had occurred during the preceding ordinary
startup, before private entry, and is not attributed to Arena restoration.

All six Arena/Redeploy/Killcam configuration value sets on VM/GPU2 were restored.
The two unselected profiles on each host stayed byte-identical. Existing host
synchronization copied the selected test profile's legitimate final progress to
both hosts; no manual profile replacement was used. EFT, the normal/private
servers and launcher were stopped. No new Arena/Redeploy/Core error was found;
known loader/Harmony warnings and one ordinary Factory collider warning remain.

These bounded checks do not establish every optional-mod combination, PFT squad,
long-session performance or power-loss recovery case. Earlier HUD/FOV/Cola
evidence remains scoped to its own builds and scenarios. Profiles/configuration
were archived before testing; successful normal-raid and reward progression was
retained rather than rolled back.

## Security scans

Gitleaks 8.30.1 checked all reachable history, frozen source exports and extracted
payloads with default rules. Redeploy history/source and both payloads had zero
findings. Arena history's 16 matches and current source's ten matches were
reviewed individually: deterministic test inputs, public setting names and prose.
Only exact reviewed fingerprints were excluded; no unresolved credential finding
remained. Private ignored evidence and credentials were never packaged.

VirusTotal checked all **eight unique executable hashes**. The seven DLLs have
zero malicious/suspicious detections. The unsigned session helper has **three
malicious labels, zero suspicious, 67 undetected**, one confirmed timeout and
four unsupported results:

- Elastic: `malicious (moderate confidence)`.
- VirIT: `Trojan.Win32.MSIL_Heur.A`.
- McAfeeD: `ti!F312C6469C19`.

[Final helper report](https://www.virustotal.com/gui/file/f312c6469c19adf8a466c37269b9dccf88d7b20f7ab992c762d478c2353db711).
Its packaged bytes match the committed-source build. Review found the expected
local process/session, private-copy and certificate-pinned local control actions,
without a concrete malicious function or package substitution. A separate
Microsoft Defender custom scan completed on an identical final helper copy with
no detection (signatures 1.461.199.0). The three other labels remain **unresolved;
they are not confirmed false positives**. No antivirus setting was disabled and
no binary was changed to evade a detection. Unsupported, timed-out or failed
engines are not clean scans. An antivirus block may prevent session switching.

All individual reports are linked from the GitHub release descriptions. Older
scan results do not attest to changed bytes; the final rebuilt Arena files were
submitted again. The unchanged Redeploy/Core hashes retain their matching scans.

## Publication verification

[ArenaMode 0.3.0](https://github.com/maschine34675/ArenaMode/releases/tag/v0.3.0)
and [Redeploy 1.0.2](https://github.com/maschine34675/Redeploy/releases/tag/v1.0.2)
are published as normal releases, with exactly one installable ZIP each. Annotated
tags identify the frozen source commits above. Each uploaded ZIP was downloaded
again; its SHA-256 matches both the verified local file and GitHub's asset digest.
Every included executable has its exact hash-specific VirusTotal link in the
release description, including the prominently disclosed helper detections.
Older releases were preserved. Subsequent documentation commits do not change
these tagged binaries or their scan provenance.

