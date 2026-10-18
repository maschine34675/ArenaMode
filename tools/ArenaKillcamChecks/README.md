# Arena killcam / death hold checks

Run from the repository:

```powershell
pwsh -NoProfile -File tools/ArenaKillcamChecks/Verify.ps1
```

The executable links the production `ArenaDeathReplayHold`,
`ArenaReplayWaitPolicy`, `ArenaReplaySelection`, `KillcamCompat`, and
`SpectatorPolicy` sources. It advances the real hold coroutine against narrow
scene/session/time doubles. The installed-mod boundary is represented by public
plugin/controller fixtures with the exact names and member types read by the
**production reflection reader**; the harness does not reimplement its ownership
decision.

Coverage:

- Bounded selection capture and clear, missing dependency and nonfinite input.
- Missing/no recording immediate release; disabled snapshot does not probe the
  plugin; changing a setting after death cannot bypass a captured enabled hold.
- The former KillcamReplay GUID is incompatible, whether alone or beside the
  renamed Killcam; an obsolete camera owner must never count as absent.
- Pending lead-in, playback, and final cleanup; safe skip without a fixed tail.
- Camera and mode holds run concurrently; release requires both owners ready.
- One finite, non-sliding deadline per death, retained across the second wait.
  Unknown ownership, invalid bounds, exceptions, and a busy timeout use the
  existing protected-abort call; they never authorize body replacement.
- Inactive/stopped/StandDown/stale-epoch/stale-match/replaced-or-revived-body
  continuations cancel without probing camera state or aborting a later raid.
- A second wait after kit preload resets its previous permission and rechecks
  camera, mode, and body identity.

The runner also checks production driver injection points: first wait before
protected authorization and ordinary preparation, the same hold's second wait
after preload, cancellation exits before requests, and an unchanged disabled
path without an extra nested coroutine. These are source contracts, not a
simulation of the native respawn engine.

Eight compiled negative controls intentionally release on timeout, ignore the
pending lead-in, accept a replacement body, ignore the mode hold, slide the
deadline, probe while disabled, reuse a previous release or prematurely authorize
protected rebind during camera lead-in. Each must fail its
specific runtime assertion; compile errors do not count. Mutants are written
only to unique ignored artifact directories. Production passes again afterward;
source hashes and per-case logs are retained there.

This does **not** certify Unity coroutine scheduling, Mono reflection against
the installed Killcam DLL, actual replay pixels/audio, camera restoration,
map-menu layout, Redeploy's native body replacement, profile persistence, or
Tactical/PitFireTeam round transitions. Those need native build/review and game
tests. The harness never starts the game, deploys DLLs, or writes profiles.
