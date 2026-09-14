# Announcement progress policy checks

Run `pwsh -NoProfile -File tools/AnnouncementProgressChecks/Verify.ps1` from the
repository root. This isolated console harness compiles the real policy,
snapshot and cue enum without Unity/EFT or any client deployment target.

The scenarios verify countdown restarts/cancellation, threshold crossings and
skipped frames, lead transitions/cooldown, actual live series entry, equipped
final weapons, ladder completion, actual draws and detach/epoch isolation.
Negative controls compile temporary policy copies with one deliberately broken
boundary each; a build error does not qualify as a rejected fault. The script
retains their source and logs under its reported temporary evidence directory
and verifies the production policy/snapshot hashes remain unchanged.

Policy details:

- Prepare is once per logical round. A readiness interruption cancels the
  countdown and permits the current digit to replay on a fresh countdown in
  that same round. Skipped digits are never reconstructed.
- Limits must be present in live progress. First observation is a silent
  baseline; crossings choose the most urgent current warning, consuming skipped
  thresholds too. Zero remaining is never a warning. A limit reappearing after
  being absent also begins with a silent baseline.
- `Lead = null` means irrelevant/0:0 and is silent. If 0:0 has been observed,
  the first scored lead/nonzero tie may speak. Attaching after scoring is silent.
  A three-second simulation-time cooldown keeps the latest net transition from
  the last emitted state; returning to that state needs no repeat announcement.
- Final round and match point require the first actual live observation in an
  epoch; Final Round takes precedence when both apply. Final weapon requires
  the mode's equipped final tier and speaks once
  per ladder cycle, including demotion/re-promotion. Increasing completed cycles
  produces one ladder-complete cue even if the tier has already been cleared;
  jumping multiple cycles produces one current cue, never a backlog.
- Draw is supplied by the mode's real result; equal scores alone cannot create
  one. Epoch changes, phase exits and inactivity cancel queued status; `Reset`
  clears all policy memory at detach.

These checks establish offline presentation behavior. Runtime snapshot adapters,
audio queue behavior and audible native playback need their own verification.
