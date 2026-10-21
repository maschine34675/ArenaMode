# Arena starter startup timing investigation

Status: investigation only, 2026-10-21; production baseline `d30b6db` on
`feature/arena-start-prototype`. No starter, game or server implementation was
changed or deployed. A temporary diagnostic executable called the unchanged
starter checks on SPT-GPU-2 without starting EFT or SPT.Server.

## Findings

The shared-content check dominates: **75.45 seconds of an 80.96-second full
verification**, or **93.2%**. A normal restart hashes the shared assets once,
not twice. Larger transient read buffers are the most promising first change:
the diagnostic large-file sample needed about 41% less time with sequential
1 MiB reads while checking exactly the same SHA-256 digests.

That percentage is a sample result, not a measured reduction in total startup
time. No optimized full verification or subsequent game launch was tested.

## Native measurement

The helper ran locally on SPT-GPU-2, against source `C:\SPT41` and the existing
diagnostic runtime `C:\SPT41-Arena`, on SPT 4.1.6. It acquired the existing
anchor/session lease, rejected pending rebuilds/updates and held both readonly
handles for each shared asset until all comparisons completed. It used the
original compiled `ManagedEnvironment.Inspect`, `EnvironmentCopy.Verify`,
`ModUpdates.Inspect` and `DirectGame.ValidatePatch` implementations.

The clock is `Stopwatch`; phase boundaries inside verification are its existing
synchronous progress callbacks. There is no WPF dispatcher delay in this probe.
Protective before/after hashing and anchor resolution are outside the measured
functions. The source-version files and selected protected files were therefore
already read before verification. The OS cache was not flushed or controlled;
this is not a cold-start test.

| Operation | Seconds | Scope |
|---|---:|---|
| GUI inspection backend | 1.304 | `ManagedEnvironment.Inspect`, without WPF/rendering; existing runtime, native source bindings and mod-package inspection |
| Full runtime verification | **80.955** | Breakdown below; not additive with its subphases |
| Source-version bindings | 0.130 | 178 files, 127,716,316 bytes |
| Shared assets | **75.445** | 8,239 files, 57,371,504,296 bytes / 53.43 GiB |
| Full runtime directory walk | 0.391 | Through discovery of 10,636 runtime file paths |
| Runtime path/private ownership checks | 0.238 | Includes non-catalogued mutable files |
| Catalog integrity pass | 4.717 | 2,135 private files / 4,359,073,953 catalogued bytes; shared files skip rehashing |
| Other verification setup/final checks | 0.033 | Remainder, including profile boundary and callback gaps; displayed subphases are rounded |
| Mod source capture after verification | 0.112 | `Inspect` with no delta; does **not** include `Apply` package JSON/PE validation |
| Native patch reconstruction/check | 0.223 | Original backup + delta reconstructed and compared with installed DLL |

Verification consumed 52.453 seconds of process CPU time. Wall-clock time alone
cannot separate storage, OS cache, scheduling and hashing overhead. It does not
justify attributing the delay solely to the disk or CPU.

The integrity progress counter covers all 10,374 catalogued files but only
rehashes private files. Its large count is not evidence of a second shared-asset
hash pass.

## Read/hash comparison

After full verification, the helper read the eight largest shared files plus
the first 256 files below 64 KiB in ordinal path order: **264 files and
7,023,287,084 bytes / 6.54 GiB**. This is deliberately a large-file sample, not a
representative sample of all file sizes. Every computed digest had to match
the manifest; all shared-file leases remained held.

Order: original, 64 KiB, 1 MiB, four workers, four workers, 1 MiB, 64 KiB,
original. All alternatives used incremental SHA-256 on the same readonly stream
type; four workers used 1 MiB buffers. They did not replace production code.

| Hash strategy | First run (s) | Return run (s) | Mean (s) | Mean process CPU (s) |
|---|---:|---:|---:|---:|
| Current `SharedAssets.Hash` | 6.010 | 5.811 | **5.910** | 5.781 |
| Sequential, 64 KiB reads | 3.593 | 3.570 | **3.581** | 3.578 |
| Sequential, 1 MiB reads | 3.466 | 3.465 | **3.465** | 3.508 |
| Four workers, 1 MiB reads | 2.863 | 2.790 | **2.827** | 3.797 |

Sequential 1 MiB used 41.4% less wall time than the current helper on this sample.
Four workers saved a further 18.4% against sequential 1 MiB, with more process
CPU time. Comparing four workers only against the original would conflate
buffering and concurrency changes.

Repeated reads benefit from cache, but even the initial sample is not guaranteed
to be fully cached: the preceding 53 GiB verification may evict earlier files.
The reversed order provides a limited consistency check, not a cold/warm cache
study or a hardware-independent speedup guarantee. The sample also excludes
most of the production path, identity and lease-management work.

## Actual repeated work and priorities

1. **Improve the shared-content hash reader first.** Try one reusable, bounded
   64 KiB or 1 MiB scratch buffer while preserving the exact hashes, file identity
   checks, stream position contract and lifetime of both readonly handles.
   Do not retain a large buffer per stream: there are more than 16,000 retained
   handles. Measure the whole verification and actual GUI launch afterward.
2. **Reuse the first-install asset lease.** `Prepare` currently hashes assets and
   disposes its lease before the immediate `RunCore` verifies them again. Carry
   the continuously held lease into startup, following the existing rebuild
   mechanism. This avoids a real second 53 GiB pass on first creation; it does
   not remove work from the already-existing runtime measured here.
3. **Consider bounded parallel hashing only after the sequential change.** Its
   additional sample gain was smaller and its CPU cost higher. Full-catalog
   testing must cover limited concurrency, error cleanup and slower storage.
4. **Treat metadata deduplication as secondary.** `Ensure` and `RuntimeHost.Run`
   both resolve/validate the owned manifest, and private catalog files are opened
   for ownership and then again for hashing. A single validated start context
   under the anchor lease and combined private-file checks may reduce this work.
   The measured full walk plus path checks were only 0.63 seconds; do not remove
   the full scan that detects extra DLLs, reparse points and mutable hardlinks.

Do not reuse stale GUI results at launch: there is no continuous protection
between inspection and the start click. Mod update capture/rollback checks and
fresh source bindings protect real change windows. Native patch reconstruction
is only 0.22 seconds here and is not worth weakening for this optimization.

Keeping shared-file leases between game sessions could avoid another hash pass,
but would also keep normal SPT/asset updates locked until explicit release or
launcher exit. That would be a separate product decision. A size/timestamp-only
cache does not provide the same content validation.

Relevant code: `tools/ArenaLauncher/LauncherOperations.cs`,
`tools/ArenaStart/ManagedEnvironment.cs`, `RuntimeHost.cs`, `EnvironmentCopy.cs`,
`SharedAssets.cs`, `ModUpdates.cs`, `BaseRebuild.cs` and `DirectGame.cs`.

## Relationship to the earlier game starts

The [earlier acceptance run](GPU-ARENA-RECOMMENDED-MODS-20261021.md) recorded
189 and 116 seconds for the complete `gpu-run start --via arena` recipe. That
includes display/UIA checks, startup and main-menu/profile/status verification;
it is not solely time spent in the starter's hash checks.

The earlier process evidence gives server creation to EFT creation as 27.370
and 3.612 seconds. EFT creation to the main menu is only approximately 26-28
seconds from rounded UiBridge timings. First launch also installed 84 new and
three changed package files; the server spent roughly 24 seconds between config
import and modloader startup, versus roughly 0.4 seconds on the next run. The
cause of that difference is not established. Today's 80.955-second verification
is an independent measurement, not an exact reconstruction of either launch.

## Preservation and evidence

Result: **`STARTUP_TIMING_PASS`**, exit code 0; all 8,239 shared asset leases
retained through comparison; all sample hashes matched. Before/after comparison
confirmed **580 selected files unchanged**, covering source/private BepInEx
configs, profiles, SPT settings, native DLL/backup/delta and private manifest/
account receipt. This is a bounded set, not a byte-for-byte audit of both full
installations. The helper never calls game/server launch functions, and its
wrapper found no native game/server/launcher process before or after the run.

The only runtime write requested by the measured functions was the native patch
check's unique temporary scratch file, removed by its original cleanup. Probe
binaries/output were staged outside both installations in
`C:\Temp\ArenaStartTiming-20261021`. The helper and all processes it started
exited normally; temporary diagnostics remain in that directory (optional
cleanup was rejected by the execution environment). No mod, profile or
configuration was updated; no new gameplay acceptance is claimed.

Local ignored evidence: `artifacts/startup-timing-20261021/` contains the
diagnostic source/project, build, launcher script and `results/r1.json`,
`r1.jsonl`, `finished.json`. Raw earlier game/server logs remain ignored and
must not be published because they can contain native account tokens.

- Helper EXE SHA-256:
  `66F9D1B705C3C071A727681CE9A62D88459C99931AF48013D56C8502B431D4A2`
- Timing JSON SHA-256:
  `9F96A4BB150BBBFCF1DE5A93215CAAF7B234ACD1CC718A2093865568B3F9C88E`
- Native run: 2026-10-21, approximately 08:27-08:29 UTC.

Independent review checked the production call graph, diagnostic safety and
the cache/sample/measurement boundaries. The next implementation still needs
its own isolation regression checks and complete native startup comparison.
