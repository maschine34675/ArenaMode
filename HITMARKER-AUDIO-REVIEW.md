# Example hitmarker sounds

User request: provisionally integrate redistributable example sounds before a
later listening test. Kenney Interface/Impact CC0 assets provide normal hit,
headshot and kill variants. See the packaged AUDIO-LICENSES.md and
hitmarkers/SOURCES.json for the unchanged creator notices, CC0 legal-text link,
original archive/source hashes and conversion details. No subjective listening
approval or in-game audibility is claimed on this GPU-less host.

## Behavior

- F12 > Audio > `HitmarkerSounds` defaults on; `HitmarkerVolume` defaults 0.5,
  range 0-1, live-adjustable independently of Interface and announcer volume.
  Overall Master volume, listener volume/mute and pause still apply.
- Audio uses its own source on the exact native Master mixer root. The existing
  announcer retains its static interface and its own separate instance. Neither
  borrows/stops native UI sources or changes mixer/listener settings.
- The audio subscription is match-owned, independent of HUD/visual marker
  settings. Attach before mode start; cleanup on start failure, raid end, stale
  session recovery, changed match identity and plugin shutdown. Normal raids
  do not subscribe or create sources.
- Audio eligibility is captured in the existing damage-feed Prefix: active
  Arena, current match, exact local attacker body, living different victim,
  direct combat damage, appropriate opposing roster/faction and combat phase.
  Administrative deaths, aborts, pending area and TDM staging are excluded.
  The Postfix additionally requires finite positive health/armor damage.
  Existing visual eligibility and scoring rules remain unchanged.
- The lethal callback can end the match synchronously before the damage
  Postfix. Do not re-test mode completion after that callback: the final kill
  must retain its captured eligibility.
- Aggregate contacts for 40 ms and submit one cue, Kill > Headshot > Hit.
  This is temporal clustering, not an exact projectile/shot identity; near
  simultaneous targets or explosion limbs can also combine. No audio backlog.
  Events older than 150 ms are discarded. A later burst can replace an active
  short effect, including its headshot/kill tail. Announcer speech is unaffected.
- A headshot sound means a direct eligible hit to the head, including a helmet
  impact; kills instead use the kill cue. This is separate from the announcer's
  stricter one-contact lethal Headshot award.
- Pause, disable, zero/nonfinite volume and detach discard pending cues and
  stop only this source. No stale effect resumes after pause/unmute.

## Asset/runtime boundary

Three PCM16 mono WAVs load once at match attach (roughly 40 KB combined). The
parser bounds files to 200 KB, rates to 8-48 kHz and duration to one second,
rejects malformed/duplicate chunks and silence. Missing/bad files produce one
diagnostic per cue/reason per match, never fail or block match startup. Fixes
or replacements are read at the next match. Bundled outputs are 7.12/300/135.08
ms and peak-normalized to 0.5; no live decoding/file reads on damage events.

Managed float PCM survives the native audio-device resets observed with the
announcer. Runtime AudioClip frame/channel/rate/length/load metadata is checked
on each request; invalid clips are recreated and their signal read back once.
All clips are destroyed at detach. Failed requests are never replayed.

## Native source/review evidence

- `../_decompiled/EFT.HealthSystem/ActiveHealthController.cs`: ApplyDamage
  returns total Common HP delta, including overflow, and synchronously invokes
  death callbacks. Prefix must see alive/live before those callbacks.
- `../_decompiled/EFT/Player.cs`: ApplyShot/ApplyDamageInfo normally reject dead
  victims; the audio Prefix independently guards direct health calls.
- Existing `IsAwardEligible`, Tactical `IsOpposingAwardKill` and TDM
  `ShouldBlockMatchDamage` supply mode/physical roster gates, not names.
- `../_decompiled/EFT.UI/GUISounds.cs`, `EFT/ClientApplication.cs`, `BetterAudio.cs`
  and `EFT.Settings.Sound/SoundSettingsController.cs` establish the persistent
  host and independent Master/UI routing. See ANNOUNCER-AWARDS-REVIEW.md.
- Independent adversarial review identified a paused active-tail resumption;
  hitmarker-only source reset and a regression assertion address it.

## Verification and user test

`tools/HitmarkerAudioChecks/Verify.ps1` runs the real PCM parser/service/output
with controlled Unity/context boundaries, then the existing announcer suite
and its fault controls. Checks cover all actual WAVs and malformed/silent files,
device reset, repeated attach, non-Arena/identity drift, cue precedence,
separate announcer/UI channels, bounded diagnostics, expiration, hot volume,
pause, mute and teardown. The native Harmony eligibility boundary has source
review and actual-game-API compile coverage; controlled audio fixtures are not
an in-raid event or rendering test.

Verified delivery (2026-09-11): 54 hitmarker assertions, 142 announcer assertions,
17 existing announcer source faults rejected; actual-API Release build 0 errors
and four pre-existing CS8632 warnings. Feature commit `d9e7598` includes the
parallel Solo Tactical fix `b7678ef` as its parent. Local client SHA256
`0E7AD442F924314B47565AA5755C6766C74FD900ABAAC8AC0FD50ACFF08EDAD6`;
seven audio/provenance files match the committed source and staged output.
Delivery receipt and prior client/license backups:
`artifacts/hitmarker-delivery/992cd14563c8416e9404be81c958707d/`.
Ten protected dependencies and existing audio/draft files stayed unchanged.
No server restart, GPU listening or mirror completion is implied by these checks.

On the gaming PC after game restart (server restart unnecessary): compare body
hit, head hit, kill and a shotgun burst; lower Interface to zero with Master
audible; vary HitmarkerVolume and disable HitmarkerSounds mid-match. Confirm
announcer speech continues during hits and mute/pause do not replay old cues.
Repeat after local respawn and next round, then check an ordinary PMC raid stays
silent. Only the first three play requests per match and a detach summary are
logged; failures are bounded per reason. A request is not proof of audibility.
