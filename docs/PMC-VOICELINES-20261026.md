# Automatic PMC voicelines and faction voices

Date: 2026-10-26, updated 2026-10-28. Development change after 0.3.0;
verification recorded below.

## Behavior

The single **PMC VOICELINES** mutator defaults on in every Arena mode. Its shared
F12 preference is **Audio / Automatic PMC Voicelines** (`PmcVoicelines`). It adds
native action speech for actual reloads, thrown grenades, empty weapons and
occasional eligible enemy kills. A seven-second shared cooldown, a short pending
cue lifetime and native speaker-busy checks keep speech sparse. Kill confirmation
has a 40% chance and a brief delay; active or pending audible announcer cues take
priority. Muting the announcer does not starve PMC speech. Native speech follows
EFT's speech/audio routing, independently of Arena's announcer volume.

Independent Team DM and Domination rotate among the selected faction's voice banks: USEC
`Usec_1` through `Usec_6`; BEAR `Bear_1`, `Bear_1_Eng`, `Bear_2`, `Bear_2_Eng`,
`Bear_3`. Both Russian and English BEAR voices are intentional. The first choice
is randomized, subsequent matches cycle through that faction's choices for this
game process. One choice stays stable across all bodies in the match. Other
Arena modes keep the player's chosen voice. Since 2026-10-28 the faction voice is
independent of this mutator: switching comments off cancels only Arena's owned
automatic speech and pending cues. Native/manual speech, including the native
Berserk combat call, uses the assigned faction bank. Toggling does not rotate or
reload that bank, and a respawn keeps the same match voice even with comments off.
The original speaker is restored at body retirement/native raid end under the
existing ownership checks; the saved profile faction and customization are never
changed. Appearance and equipment are unchanged.

The native Berserk call itself is unchanged: elite Stress Resistance can trigger
it at low health, including through Arena's MaxSkills boost. The commentary switch
does not suppress native health effects or manually requested phrases. An action
that started while comments were off is not replayed when they are enabled.

The menu retains its bottom edge above native navigation. Its map preview is
40px shorter to preserve readable descriptions with the added mutator.

## Ownership and native seams

- Only an exact confirmed active Arena game/world/match and its current living
  local body may speak. Foreign players, dead bodies, replacement gaps,
  killcam/spectator playback, failed starts and completed matches are excluded.
- Reload detection reads the current native firearm operation, covering ordinary,
  fast and internal reload paths. Grenade speech follows successful creation;
  empty-weapon speech follows the native dry-fire action.
- Kill confirmation consumes Arena's already-classified kill event, including
  exact killer-body identity and friendly-fire/award eligibility.
- Per-match voice resources use a private `IEasyAssets.Retain` token. A stale async
  completion cannot publish into a later raid. No shared voice loader unload is
  called. The exact speaker's original voice, side tag and phrase banks are
  restored only while that assignment is still ours.
- Native `BaseSpeaker.ReplaceVoice` alone is insufficient: it replaces phrase
  banks but leaves `PlayerVoice` and `SideTag` unchanged. All three values must
  agree. No `Profile.Info.Side` or customization record is rewritten.
- Playback uses native `Speaker.Play` with natural priority and health tags. It
  does not add a `Player.Say` call or a second SAIN hearing event. This is not a
  blanket claim that other mods never observe native voice playback.
- Detailed successful/declined cue and assignment messages require Developer
  Mode. Ordinary gameplay does not add Info spam.

Smooth Talker was inspected as a technique reference:
<https://github.com/liquidwarp/SPT-SmoothTalker/tree/265831b4e80ee30314db00ac36586f614a395bab>.
The implementation is independent; no Smooth Talker code, DLL or sound assets
are copied or required. Speech banks are the game's existing resources.

## Who controls bot clothing?

The installed APBS 2.4.1 replaces native appearance generation for newly generated
PMC bots. It selects head, upper body, trousers and matched hands from weighted
faction/tier/season pools. Current local configuration has seasonal appearance on,
modded clothing import off, Blicky mode off and empty clothing blacklists. The
appearance hook checks PMC identity separately from inventory-generation enablement.
Armor, helmets, rigs and weapons are inventory items handled by its separate
inventory generator; faction-specific pools do not guarantee distinct uniforms.

Arena currently changes owned bots' level ranges and forces blue USEC / red BEAR
armbands in Team DM. It does not override their clothing. A future Arena uniform
feature can postprocess the exact generated bot after APBS and before model
preload, preserving matching hands, without changing APBS settings or shared
appearance tables. Saved PFT companions are a separate ownership case.

Evidence: local reference `../_reference/progressivebotsystem-csharp-2.4.1/Patches/SetBotAppearance.cs:43-73`,
`Helpers/BotEquipmentHelper.cs:322-351`, installed APBS config and decompiled
released appearance hook; Arena's `ArenaTeamDeathmatchArmbandPatch` and
`ApbsBotTierCompatibility`. No APBS code was copied.

## Verification

The 2026-10-26 acceptance below records the original coupled switch behavior.
The 2026-10-28 follow-up separates identity from automatic commentary; see
[faction voice verification](FACTION-VOICE-20261028.md) for its current evidence.

The final Release build succeeded against the installed EFT/SPT assemblies;
the build retains four existing CS8632 nullable-annotation warnings. No server,
Core or map changes are part of this feature.

- Unit suite: 3,213 passed, one existing installed-APBS native-pipeline skip.
- Full production-linked voice runtime: 76 assertions and 12 rejected source
  mutations, independently rerun by the parent reviewer.
- TDM menu: 2,690 assertions plus negative controls; map preview: 4,468 assertions
  plus negative controls; announcer: 344 assertions, audio inventory verification
  and full mutation regression checks. Older mutation expectations were updated
  where a new, earlier assertion correctly rejected the same broken code.
- Independent review resolved five lifecycle/audio ownership issues before the
  final build: death/camera cancellation, bounded loading, pre-existing native
  queues, resource lifetime through result holds and reentrant clip replacement.

Both client DLLs were deployed locally and SHA-256 matched the GPU-2 installation
through `gpu-run deploy-wait` (48.3 seconds):

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client | `F902E9B2C39F19B2F1CBA11F1744F8AC339479A2709F5CFBEEFA07E62C81A8ED` |
| ArenaMode.BigBrainCompat | `376A876C7A6EBB81C810FD5A19B652555B224840A35EF55A6AD8602422FF6885` |

Offline evidence is under ignored `artifacts/pmc-voice-20261026/` and
`artifacts/run-arena-verification/`. The latter runner checks that offline tests
do not modify the installed files. Native game acceptance is recorded below;
disk matching alone does not prove what EFT loaded.

GPU-2's fresh process loaded the same local/disk MVIDs:
Client `2172bc78-6e3d-4e3f-8c9d-101d75a8ffd5`, companion
`0a373efc-beda-4d1e-aae2-6a6421647c07`. Shared Core 1.0.15 and UiBridge 0.9.6
were unchanged and also matched. The captured 1920x1080 menu shows all 23 switches
with separate readable descriptions and the original panel bottom boundary.
The first four-player Equator TDM test selected BEAR while the saved profile
remained USEC. Real AK-103 shots followed by a native reload produced
`OnWeaponReload`; speaker inspection confirmed `Bear_1_Eng`, the BEAR side tag
and native clip `bear1_eng_weap_reload_07_l`. This proves the native bank and
action path; it is not a human listening or loudness assessment.
After an actual test death, native player ID 2 was replaced by ID 10 with the
same `Bear_1_Eng` voice and unchanged saved USEC side. Disabling the feature
restored `Usec_3` and the USEC speaker tag. The retired firearm handle was already
destroyed, so its stale-handle response is not proof of an inspectable old speaker.
The native log also confirms `OnGrenade`, `OnOutOfAmmo` and two `EnemyDown`
callouts after real combat actions. An immediate dry-fire follow-up was skipped
by the shared cooldown. Six real shots and a reload while disabled generated no
automatic Arena speech. The reproducible combat steps and filtered log are in
`gpu/kills-run/` beneath the evidence folder.
The next BEAR match advanced to `Bear_2`. With the announcer enabled before raid
entry, native First Blood and Triple Kill requests coincided with rejected
`EnemyDown` cues; Double Kill also played. Exact times are in
`gpu/announcer-enabled-run/results/step-24-log.json`.
A USEC TDM match selected `Usec_4`; a real VSS reload selected
`usec4_weap_reload_02_n`. The subsequent ordinary PMC control retained the
original `Usec_3` voice, USEC speaker tag and USEC profile. Its grenade action
left the native speaker idle without an Arena callout, and the normal extraction
returned through the vanilla result screens.

All 228 Arena setting values were independently compared before and after the
tests and match. Godmode, infinite ammunition/condition preservation, tracking,
held fire and time/weather overrides are off. GPU-2's game and server are stopped,
with no second EFT, server or launcher process. The complete native report is
`artifacts/pmc-voice-20261026/gpu/ACCEPTANCE.md` (local evidence, not packaged).

No PMC-voice warning/error or ArenaMode/Redeploy/Killcam warning/error occurred in
the merged test logs. This is not a claim that all native logs are clean: they
retain map/weather/spawn reconciliation, JetMaterial, a `hash:0` animation event
and clothing-clipping messages. The latter two already occur in
[earlier investigations](LOG-SOURCE-AUDIT-20261018.md) and
[earlier GPU results](GPU-FEEDBACK-20261018.md); the invalid animation event still
lacks an originating animator/stack. They were preserved, not globally suppressed
or declared conclusively unrelated. The pre-existing `reward-import-not-settled`
menu notice was present before the first test raid and no profile data was edited
to clear it. Native playback and chosen clips were inspected; subjective volume,
pronunciation and every possible voice bank were not individually auditioned.
