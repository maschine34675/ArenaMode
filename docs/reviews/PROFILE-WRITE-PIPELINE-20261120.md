# Profile write pipeline: save proof under transforming mods (2026-11-20)

Contract and proof for `ProfileWriteProof`, `ArenaProfileWriteCapturePatches` and `PapSideFileGuard`. Read this before
changing a save/readback comparison, the profile write capture, or any startup write of a profile file. The user-facing
side (Pack A Punch, the Forge symptom, recovery steps) is in [PACK-A-PUNCH-COMPAT.md](../PACK-A-PUNCH-COMPAT.md).

## Problem

Arena proves every profile save by reading the file back: the text SPT was asked to save (RAM) must equal what is on disk.
Pack A Punch (PaP) 2.x transforms SPT's write: a Harmony prefix on `FileUtil.WriteFileAsync(string, string, ...)` strips
PaP's own data (the hideout area 115, one quest, its fragments) out of the profile text and keeps it in a side file.
So disk never equals RAM, and with PaP loaded in the normal session the readback fails at every site that uses it:

- entry and return of an Arena session (`SaveAndVerify`): the helper dialog says `preflight-save/save-not-confirmed`
  and the server log shows `fieldSample=[disk:...Areas,disk:...Quests]` (Forge issue 3);
- raid end and failed-start restoration (`RequireRestoredPersistence`): `FinalizeAsync` fails before
  `CompleteNativeEndFinalization`, so the Arena end in a normal session never completes;
- the durable-readback checkpoint of the reward lifecycle (`RewardRestorationCheckpoint.Persisted`).

Separately, Arena's own startup writes (enrollment, recovery, payout) write a profile directly. That moves the profile's
mtime forward without PaP's pipeline, and PaP sets a side file that is more than two minutes older than the profile aside
as `.stale` at its next start, which loses the player's PaP data.

## Rule (Part 1: the proof)

A save is confirmed when the comparison would have been confirmed before (`ExactSameProfile(RAM, disk)`), **or** when the
captured write pipeline proves it:

```
Confirms(RAM, disk, proof) = Same(RAM, disk)
                          or (proof.Mode == Pipeline and Same(RAM, proof.Input))
Resolve(id, disk)           = Pipeline only if the sealed record is transformed, subtractive (H1), protected-subtree safe (H2)
                              and Same(record.Output, disk)  -- on the very disk text the caller read
```

`Same` is `RewardRuntimeFiles.ExactSameProfile`: the unpatched, `NoInlining` comparison body of `SameProfile`. Every
profile-versus-profile comparison uses it, so another mod's Harmony postfix on `SameProfile` (Sankese's add-on ignores area
115 there) cannot decide a proof. `SameProfile` remains a wrapper for the Cola state and one diagnostics test.

Without a transforming mod, input equals output, nothing is retained and the proof is Strict: the rule is exactly the old
one. With one, the chain under test is `RAM == input` (the swap and the save happened on this RAM), `input -> output` is a
subtractive projection that leaves the protected subtrees alone, and `output == disk` (SPT really wrote it). Two of the
three links are captured from SPT's own write path, the third is the disk the caller reads itself.

### Capture

`FileUtil.WriteFileAsync(string path, string text, ...)` is `async`, but it encodes the text and calls the `byte[]` overload
before its first `await`, so a prefix of the inner call runs in the sync segment on the same thread. Hence:

| Patch class (Harmony id) | Target | Role |
|---|---|---|
| `ArenaProfileWriteCaptureStringWriteInput` | `WriteFileAsync(string, string, CancellationToken)` | prefix `Priority.First` records the input (`Begin`); finalizer `Priority.Last` closes the pair (`End`) |
| `ArenaProfileWriteCaptureByteWriteOutput` | `WriteFileAsync(string, byte[], CancellationToken)` | prefix `Priority.Last` seals the output (`Seal`) |

Parameters are bound by position (`__0`, `__1`). `TryEnable` verifies priorities through `Harmony.GetPatchInfo` and falls
back to Strict (`pipeline=unavailable`) on any failure. It is independent of the PitFireTeam file patches and their
rollback group.

State is a `[ThreadStatic]` slot (depth, path, input, poisoned flag) and atomically replaced immutable records per profile
id. Only profile paths (`<24 hex>.json` in `user/profiles`, compared by full path) are captured.

- A nested profile write poisons the slot. A `Seal` without a matching `Begin` (a direct `byte[]` writer) installs an
  Opaque record. A `Begin` without `Seal` (a prefix skipped the original, or the JIT inlined past the hook) is detected by the
  finalizer and also makes the record Opaque, with one Debug line per process.
- `Seal` compares input and output without allocation (`Utf8.FromUtf16` into a pooled buffer). Equal means a tiny record and
  nothing retained. Different retains the existing references under a global byte budget (192 MiB, 64 MiB per profile,
  1024 ids); over a limit the record is Opaque.
- Every capture entry point is wrapped in try/catch and never throws into SPT's write path.

### Hardening: provenance is not integrity

"Input equals RAM and output equals disk" proves where the text came from, not that nothing of Arena's was removed. So:

- **H1, subtractive projection.** Every output property exists in the input and is recursively a projection. Arrays are
  ordered subsequences (greedy earliest match, which is optimal for any pairwise predicate). Scalars must be equal, so a
  replaced or nulled value fails. A duplicate property, a parse error or exceeding the visit budget (20 million) fails closed.
- **H2, protected subtrees equal.** The reward ledger block (`RewardProfileDocument.ExtensionKey`), root `info`,
  `characters.pmc.Info` and `characters.scav.Info` must be identical in input and output. PaP touches none of them.

A failure of H1 or H2 is `pipeline=unsafe` and fails closed: an availability problem, never a wrong acceptance.

### Vocabulary (log tokens)

`pipeline`: `none` (hook ready, input equals output; the token is omitted from the warning in this case), `transformed`,
`opaque`, `unsafe`, `unavailable`. `ramInputMatch` and `outputDiskMatch`: `true | false | unknown | n/a`.
Mod names, assembly names, paths, counts of removed items and values never appear.

## Sites

| Site | Rule |
|---|---|
| S1 entry/return, `ArenaRewardReturnListener.SaveAndVerify` via `VerifySavedProfile` | `Confirms` per loaded profile, and `Same(RAM, second RAM snapshot)` |
| S2 raid end and failed start, `ProfileSnapshotService.RequireRestoredPersistence` | `Confirms(serialize(expected), disk)`, and `Same(serialize(expected), serialize(restored))`; the instance logs one `WarnNotConfirmed` on `return-profile-save-not-confirmed` |
| S3 durable-readback checkpoint, `ArenaRewardLifecycle.Persisted` -> `RewardRuntimeCoordinator.RecoverPersisted/FinalizePersisted` -> `RewardRestorationCheckpoint.Persisted` | the checkpoint fingerprint and the snapshot are matched against the disk text or against the pipeline input; `RequireCleanProfile` and `PersistedRevision` stay on the disk text, because later readers hash the file |
| S4 `ObserveRestored` (RAM against RAM) | unchanged |
| S5 startup `Recover`/`RecoverAtStartup` | unchanged and always Strict: nothing was captured in the previous process, so the result is a lost payout, never a wrong payment. Do not add a strip-aware fingerprint; it would copy PaP's rules |

The 3-string `RequireSavedProfile` keeps its strict semantics (pinned by `RewardReturnControlTests`). No new reason code,
no change to the wire contract, the helper's decline codes or the whitelist of return reasons.

### Edge cases and why they fail closed

- **MD5 skip.** SPT caches the profile's MD5 before the write and skips an unchanged profile. No new write happens, so the
  latest sealed record is kept. `Same(RAM, input)` ties the cached hash to the current RAM and `Same(output, disk)` ties it
  to the file; an external writer fails the second.
- **Failed write.** The record is sealed before the write, but disk is unchanged, so `output != disk` (accepted only when the
  output happens to equal the old disk, which is correct). The next skipped save is rejected.
- **Other overwrites** (`File.WriteAllText` and the like): `disk != output`, Strict rejects.
- **Another mod at `Priority.First` or `Priority.Last` on these two methods:** the captured input or output then differs from
  reality, the comparison fails and the entry is declined as without the hook. Documented in the compat page.
- **Hook not ready:** Strict with `pipeline=unavailable`.
- **Memory/CPU:** nothing retained in the no-mod case; one input reference and one output array per transformed profile.

## Rule (Part 2: the side file)

`PapSideFileGuard` runs inside `RewardStartupFiles.WriteAtomic` for the runtime profile only:

1. Discovery (`Configure`, called first in `ArenaRewardStartup.OnLoadAsync` with the injected mod list): the loaded `SptMod`
   whose GUID is `com.kennerz.paphideoutmodule`, major version 2 only, exactly one claimant; the folder of its
   `PaPHideoutServer` assembly. The side file is `<folder>/profiles/<id lowercased>.json`. No scan by assembly name.
2. `Prepare` (before the temp file exists; never throws): the path must be `Canonical(root)/user/profiles/<24 hex>.json`;
   the profile and the side file must both exist (`File.GetLastWriteTimeUtc` of a missing file is 1601 and does not throw,
   so the guards are explicit; a side file is never created or revived); the side file must be fresh, that is
   `profile mtime - side mtime <= 2 min` (PaP's stale test is strictly `> 2:00`).
3. `Carry` (after the staged file is closed, immediately before `File.Move`): set the side file to the staged file's
   `LastWriteTimeUtc`, which the move preserves, so profile and side file end up exactly equal. Never moved backwards, never
   `UtcNow`, never in a `finally`. A crash between carry and move leaves a side file newer than the old profile, which PaP
   counts as fresh.

Content of the side file and `.stale` files are never touched. Up to four profile writes can chain in one startup
(authority recovery, recovery, enrollment, payout store); each carries the side file along with its own mtime.

## Logging

Debug: hook enabled with foreign-prefix counts (no names); one transforming-write fact per process (alias only);
"captured without output"; side-file carry (alias only); unsupported PaP version. Warning once: hook enable failure;
`Carry` failure; the session refusal line with the new tokens; `WarnNotConfirmed`. Nothing is Info.
See [LOGGING.md](../LOGGING.md).

## Tests

- `ProfileWriteProofTests` (including `ProfileWriteProofNativeTests`): real `FileUtil`, real `SaveServer`, a PaP-like prefix
  in both registration orders, MD5 skip, failed write, stale record plus external writer, value changing transform,
  dropped protected subtrees, direct byte writer, skipped string write, throwing prefix, nesting, hook not ready, budget,
  priorities, foreign `SameProfile` postfix, source pin on `VerifySavedProfile`.
- `SessionSaveDiagnosticsTests` (token vocabulary and bounds, byte-identical lines without a pipeline, `WarnNotConfirmed`),
  `ArenaEndPersistenceTests` (end under a transforming pipeline, unmatched output stays refused), `RewardRestorationCheckpointTests`
  (pipeline input accepted, revision bound to the disk text, startup stays pending).
- `PapSideFileGuardTests`: freshness rule, equal times after the move, no revival, no creation, discovery by GUID and major
  version, chained writes, failure isolation, path gate, id casing, order pin.
- Mutants in `tools/OfflineStartupChecks/Mutations.ps1` (each killed by a named test): `pipeline-output-not-compared-with-disk`,
  `pipeline-ram-not-compared-with-input`, `pipeline-projection-check-removed`, `pipeline-protected-subtrees-unchecked`,
  `pipeline-unpaired-begin-keeps-old-record`, `pipeline-nested-write-not-poisoned`,
  `pipeline-ram-compared-with-patchable-equality`, `pap-freshness-inverted`, `pap-carry-after-move`, `pap-carry-uses-now`.
  `tools/Verify-ArenaEndPersistence.ps1` runs both new test classes and requires their families.

Native evidence is recorded separately under `docs/reports/2026-11/`.
