# PitFireTeam release protection checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/PitFireTeam100Checks/Verify.ps1
```

The harness links the production server persistence patches, operation gates,
management scope, persistence window, version policy and client report guard.
It runs independently of the production build and never opens an installed
player profile. Test databases and mutated metadata copies go under ignored
`artifacts/pft-100-contracts/fixtures/`.

Obtain the unmodified published archives from
[1.0.0](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/1.0.0),
[0.10.3](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/beta-0.10.3) and
[0.10.0](https://github.com/pitAlex/SPT-PitFireTeam/releases/tag/beta-0.10.0).
Extract them under `artifacts/pft-100-contracts/release`, `legacy-0.10.3` and
`legacy-0.10.0`, respectively. These third-party binaries are not committed.
`-ReleaseRoot` overrides the modern extracted archive; `-LegacyServer` checks
one specified old server DLL instead of the modern execution fixtures.

The same executable also accepts the exact 1.1.1 and 1.1.2 archives. Run the
updated release separately, retaining the default 1.0.0 and legacy controls:

```powershell
dotnet run --project tools/PitFireTeam100Checks -c Release -- <extracted-1.1.2-archive>
```

For 1.1.2 it additionally checks the internal local-leader property, the exact
account/task tuple cache, private asynchronous initial dispatch and explicit
roster method signatures. Real production Harmony guards execute against the
released companion-protection cache: normal registration/release/raid clearing,
protected suppression returning an empty accepted-ID array, and normal behavior
after protection ends. Mutated release metadata must reject a missing cache
registration method, changed inventory parameter, instance/static drift, and the
old dropped-item result signature. No foreign implementation is bundled.

Reviewed 1.1.2 SHA-256: client
`F0C01D472379ECD3F79D8F799400992093187162E75FB3716A2B6E133891B05D`, server
`D8E1EC74FD5EC85BF599BBE2886F46F73487623FDA10BE97983E328BC93B5BDC`.

The reviewed 1.0.0 archive contains these SHA-256 values:

| File | SHA-256 |
|---|---|
| `pitFireTeam.dll` | `608E6C8E379B7BD66EF00338D3475239CD15F942A082032A3A646D30A361EF4A` |
| `pitFireTeam.Server.dll` | `E30DE975C6796678F5545A16461E0BA66CB2A6F320E15D9E26F8884176307FBB` |
| `LiteDB.dll` | `AE31AC6A93549217B9E8B81497D1EE831658ADE6DF9E1EE20F2838F22DE5E218` |

Coverage:

- Install the actual production Harmony graph against the released 1.0.0 server.
- Execute protected automatic/management/recovery boundaries on deliberately
  unconstructed services, including awaiting the blocked karma result.
- Exercise the real LiteDB implementation: normal writes, protected write/delete/
  wipe, read-only reads, validated initialization, missing databases, a different
  profile, postraid management scopes and preservation of the legacy directory.
- Reject copied DLL metadata with missing or changed transaction/storage methods.
- Retain the 63/64 production-selected legacy contracts against the exact old
  binaries. These are metadata checks: the legacy builds reference a different
  SPT patch assembly, so this does not claim old-runtime Harmony/gameplay testing.
- Confirm absent PFT is not loaded as a side effect of discovery.
- Check 1.0.0 client report and physical round-lifecycle metadata. Execute the
  production report guard against isolated counter fixtures: ordinary control,
  pending arm, solo Arena, local insurance, menu transactions, teardown, the next
  ordinary raid and delayed reports from the old Arena raid.
- Prove async completion/fault lease release, reproduce the PFT mode-semaphore
  cycle, reject a writer-retained negative control, and exclude operations while
  the final snapshot reservation is held.

Expected markers are `PFT_100_CONTRACTS_VERIFIED`,
`PFT_ABSENT_CONTRACT_VERIFIED`, and two `PFT_LEGACY_CONTRACTS_VERIFIED` lines.
An intentionally damaged metadata fixture logs one expected contract rejection.
Successful execution is **offline contract evidence**, not native raid acceptance.

During Arena, lazy database initialization only accepts an existing schema-1
database with a valid migration receipt. Missing, partially initialized or
unmigrated databases fail closed before PFT can cache them or rename legacy
directories. PFT must finish its normal initialization before that profile/mode
enters Arena; the guard does not silently create an empty replacement roster.
