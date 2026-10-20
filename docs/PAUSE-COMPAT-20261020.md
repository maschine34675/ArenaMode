# Pause Continued: scoped Arena activation guard

Date: 2026-10-20. This adapter targets **Pause Continued 1.5.0+**, GUID
`com.rejzbihai.pause`. It is not a complete pause implementation for Arena.
Ordinary PMC/Scav raids and Pause's saved key binding remain unchanged.

## Reason and reviewed package

The published controller stores its original `GameWorld`, game and local player.
Its `Update` toggles a static `IsPaused` flag **before** invoking `Pause`, which
stops scaled time, disables that stored body, pauses effects/audio and deactivates
other players. Replacements and Arena's real-time deadlines do not share a
complete pause/rebind contract. Guarding only `Pause` would be too late: its
static flag would still disable native updates through Pause's other patches.

- [Forge release page](https://sp-mod.com/mod/3170/pause-continued).
- [Matching source](https://github.com/Senyasensey2026/Pause/blob/f647c78cecc51ef9c3b8aeb4e9251a2fad6908a0/source/PauseController.cs).
- Published DLL: `rejzbihai.Pause.dll`, assembly version `1.5.0.0`.
- SHA-256: `4C58ED8A8CE4DF38ED0D87E77FB8A6A28DF8822DED65857874702F1E5ECC3254`.
- The older source tag uses the original `com.netVnum.pause` GUID. It must not
  be mistaken for the continued binary; this adapter does not claim protection
  for that separate original plugin.

The foreign DLL/source is not redistributed. The adapter is original Arena
code using reflected, checked entry points.

## Behavior and identity

One prefix on `Pause.PauseController.Update`:

1. Always retains the original update when already paused, including unpause.
2. Requires the exact current native `LocalGame`/`GameWorld`, the controller
   attached to that world's GameObject and the foreign cached game/world pair.
3. Captures only an exact active Arena or the server-confirmed pending PMC arm,
   with matching selected/native maps and the completed native identity. Loading
   requires the current native Running/profile/body relationship; started raids
   require the same `IBotGame`.
4. Reads Pause's existing key binding and its own edge-triggered input helper.
   With no key press, its update and FOV tracking continue untouched.
5. For an activation request in that protected world, skips the original update
   before it toggles the flag or changes any time/body/audio state.
6. Shows one visible notice on the first blocked request in each physical Arena
   world: “Pause Continued is unavailable during Arena matches. Ordinary raids
   can still be paused.” It logs that limitation once at Info as well.

A weak physical game/world lease retains protection through respawn and terminal
Arena cleanup, while the exact native current pair must still match. It does
not rely on the foreign cached player, which can legitimately be the previous
Arena body. A later ordinary raid with the same map/profile gets no exemption.

There is no per-frame Arena plugin `Tick`, foreign config edit, global unpatch,
time-scale reset, forced unpause, world reinitialization or player mutation.
Shutdown removes only Arena's own prefix. If contract validation or the owned
patch receipt fails, the existing Arena admission path shows a reason; normal
raids remain available.

## Contract and future versions

Validation requires matching plugin/assembly identity and version, checked
instance/static field types, exact method signatures, the native raid settings
field and a readable activation graph. `Pause` must have exactly one caller,
the guarded `Update`; the paused-state setter may be called only by `Update`,
`Awake` and `OnDestroy`, and the latter two must clear it. The update must retain
one key check before its flag setter, one configured-key read and one unpause
call.

Newer versions are attempted only while those contracts still hold, with a
single warning that their runtime behavior has not been reviewed. Versions
older than 1.5.0, changed activation paths or missing hooks reject Arena alone.

## Offline checks and review

Run:

```powershell
pwsh -NoProfile -File tools/PauseCompatChecks/Verify.ps1
```

The harness compiles the three production files directly, uses a dynamic
foreign-plugin fixture and dispatches the installed production prefix. It
checks flag/time mutation, ordinary pause/unpause, existing Arena unpause,
unchanged updates without input, late ordinary raids, stale cached body after
replacement, retained terminal ownership, wrong physical/native identities,
unconfirmed loading and runtime failure admission. It also checks optional
absence, reviewed/forward/invalid versions, hook receipts and partial patch
cleanup.

Separate Mono.Cecil assertions inspect the actual published DLL and installed
native `Assembly-CSharp.dll`; fixtures alone are not used to assert their APIs.
The verification script reruns production before/after its twelve compiled
negative controls, each required to fail its named behavioral assertion rather
than fail compilation.

Implementation self-review covered:

- Expert pass: prefix timing before the global flag, native creation ownership
  and the published input helper.
- Adversarial pass: no-input, already-paused, wrong world/game/controller,
  loading without confirmation, same-map later raid and removed-hook paths.
- Polish pass: one player-visible notice, Debug-only setup, no settings changes
  and no unnecessary update callback.
- Final pass: source-linked assertions, published metadata, mutation rejection
  and unchanged sources across verification.

Independent integration review, production build and native acceptance are
recorded separately by the coordinating task. These offline checks do not prove
native input delivery, FOV/audio behavior or every future Pause version.

Independent source/published-DLL review found no unresolved material issue.
The production client/companion build passed (four existing nullable warnings).
Optional admission passed 114 assertions and twenty negative controls; native
menu checks passed 759 assertions. The reviewed runtime files match the
71-assertion/twelve-negative-control verification snapshots. Native acceptance
is tracked in [the GPU report](GPU-COMPAT-SECOND-20261020.md).

## Native checks still required

With the published continued client installed temporarily, start an Arena,
press its configured pause key and verify one visible reason, unchanged
time-scale, movement, combat and continued respawn. Repeat after a local
replacement and verify no notice spam. Then run an ordinary PMC raid in the
same process and verify actual pause and unpause still freeze/restore the
native game. Preserve the pre-test config and remove only temporary test files.
