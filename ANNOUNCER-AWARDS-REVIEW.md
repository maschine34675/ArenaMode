# WARLORD awards and playback

## Rules

- First Blood uses WARLORD. The first eligible combat kill consumes it, including
  a bot's kill; only a local first kill is spoken. Reset at actual Tactical Live
  and Rounds StartRound, once per Endless/Gun Game match, never on respawn,
  solo spawn retries, staging retries or Gun Game ladder wraps.
- Headshot requires the first accepted contact in the target's physical life to
  kill directly with a bullet to the head. Earlier armor-only hits, other
  shooters, environmental damage and multiple contacts/pellets disqualify it.
  Bleed-out cannot inherit a stale headshot. Ordinary scoreboard HS rules and
  all existing scoring remain unchanged.
- Unstoppable adds the missing audio for the existing 20-kill streak.
- Payback refers to the exact enemy body which killed the previous local life.
  Native kill capture precedes the local death event; that event consumes its
  pending aggressor. Later environmental/friendly deaths replace the target with
  none; another actor killing it clears the target. Tactical rounds clear it;
  ordinary Rounds scoring brackets preserve it because those bots stay alive.
  New bodies in the same slot/name/profile never count as that enemy.
- Award-eligible deaths use the actual native Bullet/Blunt/Melee/Explosion/
  GrenadeFragment cause. Environmental/undefined/health-effect deaths (including
  bleeding with an unprovable retained aggressor) cannot consume First Blood or
  nominate Payback. This restriction does not change native scoring attribution.
- Last Man Standing is local-only in protected 5v5: after full Live identity,
  survival, timer and hostility validation, the living local PMC has no surviving
  followers and enemies remain. Once per round, never a spectator announcement.
- Mission Accomplished/Game Over replace the final spoken round result for a
  real protected 5v5 match win/loss. They begin during the six-second aftermath;
  no-contest/start failure/teardown never emits a result. Other modes have no
  finite match end (Gun Game wraps) and therefore do not invent a completion.
  Victory/defeat lighting still receives the equivalent terminal outcome.

One HUD label per kill: First Blood > Payback > existing streak milestone >
multi-kill > Headshot. The audio service lets the current phrase finish, retains
at most one pending award (maximum age four seconds), and prioritizes Last Man
Standing/First Blood/Payback over ordinary kills. A real result discards pending
awards and waits for the active phrase plus 0.1 seconds. A new round or detach
cancels all queued work. Missing/not-yet-loaded clips are diagnosed immediately
and never become delayed announcements. Standard bundled phrases and their
result fit the six-second aftermath; unusually long custom packs can still be
canceled at the hard lifecycle boundary.

## Verified native contract

SPT 4.1 source under `../_decompiled/`:

- `EFT/Player.cs:29961`: ApplyShot rejects dead/ignored targets, processes armor,
  then calls ApplyDamageInfo. ApplyDamageInfo entry counts the accepted contact;
  a shot scope itself does not count a rejected hit.
- `EFT/Player.cs:30020`: player damage calls health damage synchronously.
  Prefix scopes identify the same nested contact; finalizers clear them on both
  normal and exceptional exits without swallowing native exceptions.
- `EFT/Player.cs:30075,30096`: OnBeenKilledByAggressor receives the actual
  lethalDamageType separately from retained LastDamageInfo/LastBodyPart.
- `EFT.HealthSystem/ActiveHealthController.cs:3493,3549`: death callbacks run
  inside damage application, before damage/shot postfixes.
- `EFT.Ballistics/BallisticsCalculator.cs:117`: pellets share FireIndex, so
  neither FireIndex nor a firing event is used as a one-hit identity.

Weak body history does not retain discarded Player objects, and lives do not
reuse IDs after reset. First observation must show maximum aggregate health;
already-wounded or unreadable bodies cannot earn the strict Headshot award even
if healed later. All combat observation is behind Arena activation;
presentation never changes native damage, health, profile or inventory behavior.

## Verification and GPU handoff

Run `dotnet test ArenaMode.Tests -c Release`, `dotnet run --project
tools/AnnouncerChecks -c Release`, and the affected Team Arena, Solo Tactical and
lighting harnesses. Rules are linked production code; transport/audio tests use
controlled time and stand-ins, not an actual Unity device.

On the gaming client: bot-first vs local-first; clean one-hit HS vs prior
armor/body damage and bleed-out; local death/respawn/same-body Payback; no Payback
for a regenerated 5v5 opponent; local-only Last Man Standing; intermediate vs
terminal results; rapid awards followed by a final kill; round 2 after local
respawn; technical no-contest silent. Check ordinary raids remain unaffected.
No GPU runtime success is claimed from this server.

Offline event verification: 135 unit tests, 121 production audio assertions,
766 Team Arena assertions, 32 Solo Tactical and 201 lighting assertions passed.
Twelve controlled source-copy audio mutants fail their intended loading/PCM/
native-route/queue assertions, followed by an unchanged positive run. Two
independent adversarial reviews found and confirmed the fixes for retained
environmental aggressors and initially wounded bodies; no P0-P2 issue remains
in their reviewed event/lifecycle scope. Client Release build has only the four
existing shared-model CS8632 warnings.

## Independent volume follow-up

`Audio / AnnouncerUseUiVolume=false` is the default. `AnnouncerVolume` controls
one owned 2D source directly on the exact Master mixer root. Overall game
volume and listener mute/pause remain effective. Set the switch true explicitly
for the old UI output; there is no automatic fallback when Master resolution
fails. Neither route changes global audio preferences, and the independent route
never stops/borrows native button audio. The PCM cache/rebuild remains unchanged.

Native evidence: `EFT.UI/GUISounds.cs:144-170,418-434` confirms no suitable
independent PlaySound route; commonUiSound also overrides the requested volume.
`EFT/ClientApplication.cs:68-70` locates GUISounds on the persistent application
host. `BetterAudio.cs:931,1036-1038` uses the Master root;
`EFT.Settings.Sound/SoundSettingsController.cs:102-115,137-143` independently
sets MasterVolume and UIVolume. FindMatchingGroups uses substring matching, so
the adapter additionally requires the exact name and owning mixer and rejects
multiple candidates. It never calls ForcePlaySound/SetFloat or changes listeners.

The adapter resets on host/mixer/source drift, mute/disable and detach, updates
owned gain while a phrase plays, and never replays a failed request/watchdog.
Cleanup errors are isolated so they cannot retain a match subscription or skip
spectator/HUD updates. Root-Master rendering and real audibility remain GPU-only:
test Interface=0 with Master>0, then Master=0, AnnouncerVolume=0, pause and
round-2/respawn; verify normal button/raid audio is unchanged.

Final output verification: 142 audio assertions and 17 controlled negative
mutations passed; 135 unit tests and the affected 5v5/lifecycle/loadout/light/
spawn/tracer harnesses passed. Independent audio review has no remaining P0-P2
finding. Release binaries and all 20 audio/license/source-record files match
the installed copies. Detailed hashes and GPU handoff are in
`tools/AnnouncerChecks/GATES.md` (four local gates met, one GPU gate handed off).
