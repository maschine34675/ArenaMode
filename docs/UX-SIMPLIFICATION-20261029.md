# Concise Arena menus — 2026-10-29

Implemented the [GPU-1 review](UX-REVIEW-20261029.md), preserving the request to
avoid large blocks of explanatory text. This is presentation work; admission,
session ownership, gameplay limits and reward consent are unchanged.

## Changes

- No informational box over an admitted Arena character. Reviewed-version and
  policy advisories remain in the log; real refusals and restart failures remain
  visible. Filterable conflicts retain their confirmed entry action.
- Remove the extra character description and `SEPARATE MATCH` caption. The
  native description object remains intact: selecting PMC/Scav restores native
  text. The private-session strip owns the sole return action and reads
  `ARENA SESSION · FILTERED MODS`; all its original visibility/confirmation guards remain.
- Explain each mode in two short lines. Show RESPAWN and EQUIPMENT once; remove
  the repeated OBJECTIVE and protection rows. Move rules, faction selection and
  the following area panel up 36 reference pixels without shrinking controls.
- Solo Tactical/Zombies use `GAME MODE`; only the actual companion variant
  names PitFireTeam. Domination retains the actual map's objective count.
- Mutators show title and state. Extra help remains on hover, unavailable reasons
  remain inline, and categories read MOVEMENT, INVENTORY, COMBAT, HUD & AUDIO.
- Domination's reward card states once that it has no profile rewards.

## Verification

The full suite passed **3,394 tests**, with three existing environment skips
(installed APBS pipeline and two historical reward-recovery fixtures). All seven
affected harnesses passed: CompactModeUx, CompactMenuUx, CompactSessionUx,
TdmMatchMenu, ArenaSessionClient, NativeSideView and NativeModeSelection.

Old mutation anchors referring to removed UI were updated. The original full
run plus focused corrected runs reject all **174** named session mutants;
the rendered menu checks pass **2,648 assertions**. `ArenaSessionClientChecks`
now also accepts `-Stage Ui` for these focused loops; default `All` retains the
complete suite. No test run deployed to the live installation.

Native acceptance ran on **SPT-GPU-1**, 1920×1080, with the shipping client and
BigBrain companion loaded and verified by hash and MVID:

- Normal main menu has no session strip. Arena selection has no advisory overlay
  despite the installed version differences; switching back to PMC restores its
  original description. The real missing-installation ArenaMapPort dialog remains.
- All six solo modes checked; concise descriptions, independent rules, visible
  N/A reasons and actual ANNOUNCER hover help confirmed. Private main/character
  menus and the post-raid menu each have exactly one return button.
- Factory Deathmatch: one round, no kill limit, two-minute timer, rewards OFF.
  Result shown, native ExitStatus reached, server confirmed full profile snapshot
  restoration and protected raid completion. Result navigation reached the menu.
- Confirmed RETURN TO SPT restarted into a new normal EFT process,
  with no Arena strip. Game and server were then stopped normally.
- All 219 original configuration files restored. All 43 original profile/backup
  files retained; only the played profile's native `Health.UpdateTime` and
  `Hideout.sptUpdateLastRunTimestamp` changed. Native startup added backups.
  No profile was manually repaired or restored. Session closed; no active pointer.

GPU-2 was in use by another compatibility test and was not altered by these native
tests. GPU-1 synchronization was paused for testing and restored afterwards.
PFT companion menus have harness coverage; this is not a new native PFT raid test.
The native run covers Factory, not the absent ported Arena maps.

## Candidate and installation

Client and companion were built together against installed **Redeploy.Core 1.0.16**.
The sibling Core checkout was concurrently developing 1.0.17; the isolated build
used the released DLL reference and its truthful 1.0.16 stamp, without deploying
or bundling that parallel work. Build succeeded; four pre-existing nullable-context
warnings. Tested artifacts carry base commit `5164732b` plus this working-tree diff.

| Payload | SHA-256 |
|---|---|
| Client | `F88A345AA0DA81819873C672929982D9BA3A6718F84F88C7C42C56748232DC1C` |
| BigBrainCompat | `A47F0ACEA0DD1B7ECE6B08CE506AF8FDF4CAAFAE27D45FC2E2EB26BD775F30F8` |

Both validated DLLs installed with the standard Release deployment script on
`D:\SPT41` and GPU-1. Server/helper were not rebuilt for this UI change.
Raw snapshots, build/test logs, profile comparisons and source-bound acceptance
oracles are under ignored `artifacts/ux-menu-20261029/`. Sensitive raw profiles
and helper/session evidence are not part of Git.

The preceding recovery branch was merged into `master`, deleted locally (no
remote branch existed), and pushed as `5164732b`. This UI work is on the separate
`codex/ux-menu-20261029` branch.
