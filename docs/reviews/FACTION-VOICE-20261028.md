# Faction voice independent of automatic comments

Date: 2026-10-28. Follow-up to [PMC voicelines](PMC-VOICELINES-20261026.md).

## Behavior

Team Deathmatch and Domination assign a voice from the selected faction even
when **PMC VOICELINES** is off before entry. The selected bank remains the same
across toggles and respawns. It is used by native/manual speech as well as Arena
comments when enabled. The switch controls only Arena's additional reload,
grenade, empty-weapon and kill comments. Other modes keep the profile voice.

This also covers EFT's native Berserk combat phrase. Arena's default elite skill
boost can enable the Stress Resistance bonus; low health can then trigger
`Say(OnFight, demand: true)`. This native call and all gameplay effects remain
unchanged. No global Player.Say/BaseSpeaker.Play interception was added.

`ArenaPmcVoice.CurrentBody` owns the unchanged exact game/world/match/current
living body checks. Speech-event admission still requires the commentary setting;
voice preparation and assignment do not. While disabled, Tick clears Arena's
pending cue and cancels only its exactly owned audio clip, retaining the faction
bank. It continues observing reload state so enabling midway does not replay an
earlier reload. Native playing/queued speech and announcer priority are preserved.

The existing lifetime boundaries remain: retired bodies restore their original
speaker, invalid gameplay states do not receive new assignments, result holds
retain asset resources until native raid end, and stale asynchronous loads cannot
publish into a later raid. Voice name, faction tag and phrase banks restore only
while their complete assignment is still owned by Arena. Profile side, voice
customization and saved progression are never rewritten.

The existing asynchronous loading window remains: at the very first raid-ready
frame the original voice may still be active until the faction bundle is ready
and the native speaker is idle. The next eligible Tick assigns the faction bank;
the change does not interrupt native speech to force an immediate swap.

## Offline verification

- Full unit suite: 3,348 passed, three existing environment skips, zero failures;
  the verification runner confirmed the live installation stayed unchanged.
- Complete production-linked voice harness: 126 assertions, 16 rejected source
  mutants. Coverage includes OFF at entry in both modes/both factions, native
  playing and queued speech, OFF across asynchronous loading and body replacement,
  exact restoration and disabled event hooks before a between-frame toggle.
- The harness now links the real shared mode policy, repairing its stale fixture
  dependency introduced with Domination.
- Both client assemblies built against the installed game APIs. Initial full
  compilation reported the existing nullable-context warnings in
  `ClientSettingsModels.cs`; no compile errors.
- Independent adversarial review: no unresolved findings. A missing test for
  disabled hooks followed by enabling before Tick was added before final checks.

## Candidate and native acceptance

Both client DLLs were deployed locally with the repository deployment tool.
This change does not modify the server, Core or foreign mods. A separate session
updated Redeploy/Core on the test hosts; native acceptance records the actual
loaded versions independently of this Arena candidate.

| Artifact | MVID | SHA-256 |
| --- | --- | --- |
| Client | `b653b7d5-0e4e-4971-a667-c131273f65c7` | `44FA377ED63E0FACEFFC889DA1623A0E0C56724E3AB2BF46C7AB294CDE286CA1` |
| BigBrain companion | `4e479129-95ee-4092-a2ab-cee4b28b1251` | `7DFF35655AD7C8B49E8826F785139D05AE732F07F115A2475C01013694842659` |

GPU2 became occupied by another session after the initial availability check;
its running process was not restarted or modified. The candidate was synchronized
there on disk only. Native verification used the free GPU1, whose fresh process
loaded both candidate MVIDs above. It also loaded the independently deployed
Redeploy.Core 1.0.16. All four raids entered with comments OFF and used native F1
input to select the following clips:

| Mode / side | Selected bank | Native clip |
| --- | --- | --- |
| Team DM / BEAR | `Bear_1_Eng` | `bear1_eng_mutter_03_q` |
| Team DM / USEC | `Usec_6` | `usec6_mutter_04_n` |
| Domination / USEC | `Usec_1` | `usec1_mutter_03_q` |
| Domination / BEAR | `Bear_2` | `bear2_mutter8_q` |

The saved profile remained USEC. TDM BEAR also covered real shots plus reload
while OFF, an ON reload positive control, OFF again without restoring the voice,
an empty-weapon trigger and a native grenade throw. No additional Arena comments
were recorded while OFF. A real death/respawn changed player ID 2 to 35 and kept
`Bear_1_Eng`/BEAR with no pending Arena cue. Native injury breathing used the
BEAR bank too. A documented low-health test hook did not produce an observed
Berserk `OnFight` event: the specific Berserk trigger is code-verified, not
claimed as directly reproduced in this run. Clip selection was inspected;
subjective listening and volume were not tested.

Native verification is recorded in the task's ignored evidence directory,
`artifacts/faction-voice-20261028/gpu/`; the source/hash manifest is one level
above it. The complete run is summarized in that directory's `ACCEPTANCE.md`.

An ordinary Factory PMC control restored `Usec_3`/USEC and selected
`usec3_mutter_14_n` through F1; Arena's voice/body/playback state was null.
The player extracted through native Gate 3. All 230 Arena configuration values
matched the fresh pre-test baseline after restoration.

The merged client/server/EFT warning export contained 423 entries, including
native JetMaterial, map and startup warnings. No warning/error named
ArenaPmcVoice, ArenaMode, Redeploy or Killcam in that export. This is a scoped
check, not a claim that all installed mods or native game logs are error-free.

GPU1's final menu readback showed godmode, infinite ammunition/condition
preservation, tracking, held fire and weather overrides off. The final process
check confirmed game and server stopped with no second EFT/server/launcher.
GPU2's other session was left untouched. No production build or source changes
were made after the candidate whose hashes appear above.
