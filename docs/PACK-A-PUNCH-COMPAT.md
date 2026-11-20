# Pack A Punch compatibility (2026-11-20)

Pack A Punch (PaP, GUID `com.kennerz.paphideoutmodule`) transforms SPT's profile writes. Arena checks every profile save by
reading the file back, so before this change PaP made Arena entries, returns and raid ends fail. This page records the
mechanism, what Arena does about it and what is still not covered. The contract and proof are in
[reviews/PROFILE-WRITE-PIPELINE-20261120.md](reviews/PROFILE-WRITE-PIPELINE-20261120.md). Reviewed against PaP 2.x
(server `PaPHideoutServer`); other major versions are not carried (see Limitations).

## How PaP stores its data

- A Harmony prefix on `FileUtil.WriteFileAsync(string, string, ...)` strips PaP's data out of the profile text before it is
  written: the hideout area 115, one quest and its fragments. The stripped data goes to a side file
  `<PaP server folder>/profiles/<profile id>.json`.
- At the next start PaP restores the data into the loaded profile, but only when the side file is **not older than the
  profile by more than two minutes**. A profile saved without the mod since then could overlap whatever took its place, so an
  older side file is renamed `<id>.json.stale` and PaP logs that it was set aside.
- Consequence for Arena: the file on disk never equals the profile in memory, and anything that writes a profile without
  PaP's pipeline moves the profile's modification time forward.

## What users saw (Forge issue 3, Arena 0.6.0)

- Starting Arena was declined with `preflight-save/save-not-confirmed`; the server log showed
  `fieldSample=[disk:...Areas,disk:...Quests]` in the "Session switch refused" warning.
- With PaP loaded in the normal session the Arena raid end could not finish either (`Protected Arena end failed`,
  `Reward result withheld`): the readback that restores the profile after the raid failed the same way.
- Arena's own startup writes (enrollment, recovery, payout) could leave the side file more than two minutes behind the
  profile, so PaP set its data aside at the next start.

## What Arena does now

**Part 1, save proof.** Arena records what SPT's write path does to the profile text (the text handed to the write and the
bytes that reach the disk) and accepts a save when RAM equals that input and the output equals the disk. It also requires
the output to be a pure removal from the input and the reward ledger and identity blocks to be untouched, so this does not
weaken the readback: without a transforming mod the rule is exactly the old one, and failed, skipped or foreign writes are
still detected. This covers entry and return, the raid end and failed-start restoration, and the reward checkpoint. Startup
recovery stays strict: an uncertain payout is discarded, never paid wrongly.

**Part 2, side file.** When PaP 2.x is loaded and its side file for a profile exists and is still fresh, Arena sets the side
file's modification time to the new profile's time in the same startup write. It changes only that time, never the content,
never a `.stale` file, and never creates or revives a side file.

## Limitations

- **Startup crash window.** A crash during Arena's startup write can still leave the files in a state PaP judges stale. The
  carry happens immediately before the file move to keep that window as small as possible.
- **Another mod at the first or last Harmony priority** on `FileUtil.WriteFileAsync` (string or byte overloads) makes the
  captured input or output differ from reality. The comparison then fails and the entry is declined as it was before
  (`pipeline=transformed` with `outputDiskMatch=false`, or `pipeline=unsafe`).
- **Private server.** The Arena session does not load PaP by default (it is left out by the whitelist), so the private
  server writes profiles unchanged. If you admit PaP into the session through the whitelist, both servers then run it.
- **PaP's own deletion.** PaP removes its side file when it writes a profile that has no PaP data in it
  (`PaPProfileStore.cs:147-152` in the reviewed source). A profile written by something else after that is not carried back.
- **Only PaP major version 2** is carried. For another major version Arena leaves the side file as the mod left it and logs
  one Debug line. The save proof is independent of the version.
- Arena cannot restore PaP data that PaP already set aside before this fix; see Recovery.

## Sankese's add-on (ArenaMode-WeaponPoolFix)

The add-on's patch on `SameProfile` ignored area 115 only (the report also showed `Quests`) and changed the comparison for
everything. Arena's new checks use the unpatched `ExactSameProfile`, so the patch no longer influences any save proof: it is
inert. It should be retired; it is not shipped with Arena.

## Troubleshooting through the log

The warning for a refused session switch, and the raid-end warning ("Restored profile not confirmed after saving:
operation=raid-end|failed-start"), carry these tokens when a pipeline matters:

| Token | Values and meaning |
|---|---|
| `pipeline` | `transformed` (a mod changed the write and it was captured), `opaque` (no usable capture: a direct writer, a skipped write, over a limit), `unsafe` (the change was not a pure removal, or touched Arena's data), `unavailable` (the capture hook is not active; Arena logs one Warning at startup then) |
| `ramInputMatch` | RAM equals the captured input: `true`, `false`, `unknown`, `n/a` |
| `outputDiskMatch` | the captured output equals the file: `true`, `false`, `unknown`, `n/a` |
| `fieldSample` | with a transformed write the samples are prefixed `input:` and `output:` (then `ram:`) instead of `disk:` |

A setup without such a mod prints none of them. `outputDiskMatch=false` means something other than the captured write changed
the file (another writer, a failed write). Debug level adds: the hook enabled with the number of foreign prefixes, one
"transformed before it reaches the disk" fact, and "side file kept fresh".

## Recovery for users hit by 0.6.0

Only if PaP's data is missing after an Arena start. Stop the server first, then in PaP's `profiles/` folder rename
`<profile id>.json.stale` back to `<profile id>.json` and set its modification time to now. This works only if the
profile has not been saved without the mod since: PaP's next strip would otherwise already have written a fresh side file
that holds area 115 only, and renaming the `.stale` file over it would lose that. If in doubt, keep a copy of both files
before changing anything.
