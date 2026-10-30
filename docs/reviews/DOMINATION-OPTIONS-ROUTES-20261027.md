# Domination: routes, match limits and varied Factory points

## Behavior

- Bots try a bounded set of capture-valid approaches instead of only a radial
  offset and its mirror. The projected destination must be inside the point,
  on the same floor, visible from its center and reachable by a complete route.
  The verified center is the last fallback. Stuck retries retain the total trip
  deadline. Native teleport/cover fallbacks remain disabled.
- Cleanup remembers the destination of the actually owned path. Failed later
  alternatives cannot leave a stale native return target behind; newer foreign
  paths still retain ownership.
- Factory day/night chooses three points per match from its original eight
  surveyed seed candidates. Final repaired floors require bidirectional routes
  to the independent original main-network anchor. Selected points are at least
  12 horizontal metres apart. A different physical triple is chosen when an
  alternative exists; points never move during a match or respawn. All four
  ported maps retain their original anchors. Pickup clearance follows the
  selected runtime positions.
- Domination has separate `ScoreLimit` and `TimeLimitSeconds` preferences,
  defaulting to 200 and 600. Compact menu rows offer Off/100/200/300/500 points
  and Off/5/10/15/20 minutes. F12 additionally accepts 0–1000 points and 0–7200
  seconds. Zero disables that limit; both zero permit endless play. Values are
  frozen when the match starts. TDM preferences and reward exclusion are unchanged.
- Equator's fixed 12:00/00:00 phases read DAY/NIGHT in Arena's map screen.
  ArenaMapPort still owns the actual time selection; ordinary clock maps retain
  their labels. The redundant generic character-selection notice is removed.

## Verification

Offline checks passed: 3,308 unit tests with three environment-gated skips;
all five focused/regression stages passed, including 160 bot assertions and 19
rejected source defects. Both Release client builds passed without errors.
Independent route and menu reviews found no remaining blocker.

Private evidence: `artifacts/domination-options-routes-20261027/`.

GPU-2's ordinary server and test profile completed Factory day (100 points,
five-minute ceiling) and Factory night (points Off, custom 60-second limit)
in one game process. Results were BEAR 44:100 USEC after 135.6 seconds and
BEAR 47:52 USEC after exactly 60 seconds. Both protected ends completed and
returned through ExitStatus to MainMenu without retry or hanging.

The first match used floors (-6.770, 0.031, 10.010),
(-20.756, 0.031, 22.769), (4.730, 0.031, -35.100); the second replaced the first
with (-5.890, 0.031, -27.460). Bot arrivals, captures and contests were observed.
Both complete native end backups match their checkpoint fingerprints; all
observed/persisted/complete witnesses exist. PMC/scav inventories, quests,
skills and PMC XP match each armed baseline and between matches. Before the
first arm, ordinary startup added explicit StackObjectsCount=1 to two existing
inventory items; no item content changed during either protected round.

Native menu captures confirmed compact point/time rows, custom one-minute
display, Equator DAY/NIGHT, restored clock labels on Bay5 and absence of the
redundant generic Arena notice. The existing UIFixes version note remains.
The time-only match exposed a shared HUD label saying "No kill limit";
this is corrected to "No point limit" with a production-linked regression.
The final build passed 487 runtime-model assertions and 913 HUD assertions
with 13 rejected HUD defects. A fresh GPU-2 process loaded matching local/disk
assemblies and completed another 30-second Factory match, confirming the corrected
HUD, normal PMC menus and Equator's PAST/CURR selections. Its full end backup
also matches its checkpoint, and the protected field comparison remains exact.

Final SHA-256 identities (local build, local installation, GPU-2 disk and loaded):

- Client: `C56CD0BF8C4F2CE75AF58FB9663D2B4D6E106A0FE4C3EA1C71806F272243A2BF`
- BigBrain companion: `EBBC88F422AEE64DC5C891251931A4AFEF980C3474DF8A075229A270D3E85057`

The first match recorded arrivals at all three points and one bounded door
timeout; the second recorded captures/contests and no rejected trip. These runs
verify functioning objective movement, not perfect navigation through every
door or every Factory combination.

All 236 previous Arena configuration entries match their initial values on
both installations; the two new entries retain defaults 200/600. Diagnostics
are disabled again. Game and ordinary test server were stopped gracefully.
Only the two client DLLs were deployed; server, Core and map packages are unchanged.

The scope does not add rewards, PitFireTeam squads, new maps, capture-speed
scaling, teleports or global changes to normal PMC/Scav behavior. Factory's
varied layouts still require broader gameplay feedback for competitive balance.
