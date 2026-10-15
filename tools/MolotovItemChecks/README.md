# Private Molotov registration checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/MolotovItemChecks/Verify.ps1
```

This executable links production server registration, shared identity, the unchanged
two-RGD-5 server catalog transformation, kit packing/merge and preload code. The
native inventory boundary is modeled; SPT DTOs are real. The separate installed
SPT Core assembly is loaded into an isolated context for actual registration-phase,
override and global flea-unlock checks. No server starts, profile is read or written,
game DLL is built, and no file is deployed.

Since 2026-10-15, the client live-supply path is the sole source of the extra Molotov.
The server must register its private harmless template but must **not** add another
Molotov to kit JSON. Current checks cover zero extra private requests with a valid,
missing or changed private template, retention of two ordinary RGD-5 requests,
authored throwables, idempotence and malformed catalog isolation. Client supply,
native G preference and exact body/round admission are checked separately by
`tools/MolotovChecks` and native game tests.

Registration's `IsAvailable` is true only while the exact successfully registered
object remains under its ID in the original captured item table and all harmless
properties still pass. Removal, lookalike replacement, dangerous mutation,
registration failure and foreign-ID collision revoke availability. The raid arm
response can use this as server ownership evidence; mere client template presence
does not prove that another mod has not occupied the ID.

The item executable runs 1,807 assertions, including the original 1,605 packing
assertions. It additionally rejects twelve individually compiled registration,
availability, native-flea and server-supply mutations. The historical packing
executable runs separately without the private-registration partial extension and
still reports exactly 1,605 assertions; two compiled packing mutations must fail
at the original designated boundaries. Compilation failure never counts as a
successful mutation rejection. Evidence remains under ignored
`artifacts/molotov-item-checks/<run>/`.

The old branch's server-side Molotov request, per-mode supply argument and optional
Molotov packing mutations no longer describe the implementation. Their replacements
explicitly reject duplicate server supply, converting existing RGD-5 requests and
removing one of the two historical frags. No historical packing assertion was
deleted. The empty-items fixture pins the existing server behavior (ordinary frags
only); this integration does not tighten unrelated catalog validation.

These checks do not certify a running SPT server, Harmony hooks, Unity resource
pools, native hands, bottle rendering or profile restoration in a raid. The staged
build helper is separate and must be run only by the integration owner.
