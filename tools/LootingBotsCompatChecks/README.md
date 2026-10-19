# Looting Bots compatibility checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/LootingBotsCompatChecks/Verify.ps1
```

The isolated .NET 10 harness compiles the production adapter, scope, reflection
contract and protocol. It uses small EFT/BepInEx/Harmony doubles and generated
foreign assembly fixtures. No game, foreign plugin or server is started. No
solution build, deployment, F12 setting or profile is changed.

Covered behavior:

- Ordinary PMC/Scav, Hideout, missing plugin, malformed/unconfirmed arm,
  mismatched world/map/controller/body/inventory and a following same-map normal
  raid retain their original methods.
- A server-confirmed loading world is protected before Arena becomes active;
  current active worlds and replacement bodies retain protection.
- Layer arbitration, direct searches, actual loot admission and both priority
  queues are guarded. A skipped search returns its exact scheduler ticket once.
- PFT Tactical/Zombie companion commands retain their original entries, including
  replacement bodies before profile rebind. TDM never inherits that exemption.
  The RC autonomous spawn cleanup is blocked for PFT entry gear too.
- Missing/changed contracts, asynchronous initializer drift, hook removal,
  partial installation, native field drift, identity/scheduler exceptions and
  failed unpatching do not produce a false safe admission.
- Removing the integration revokes guards before unpatching, never removes
  another Harmony owner's patches, and leaves no raid/body lease behind.
- DM/TDM never read the allocating PFT roster getter.

`Verify.ps1` runs the production checks before and after twelve separately
compiled mutation controls. A mutant must compile and fail its named behavioral
assertion; a compile failure is not accepted. Only a complete successful run
prints `LOOTING_BOTS_COMPAT_VERIFIED`. Evidence and mutant sources stay below
ignored `artifacts/looting-bots-checks/<run-id>/`.

If the audited stable and RC DLLs exist under the ignored audit directory, the
same run also inspects them with Mono.Cecil without executing them. Their
identity, hook signatures, owner fields, RC spawn call path and unique cleanup
caller are checked and SHA256 values are printed. Alternatively pass explicit
paths with `-PublishedAssembly`. A missing optional binary does not invalidate
the stand-in tests; the final output states the number actually inspected.

The harness exercises installed production prefixes, not Mono/Harmony's real
detour engine, Unity callback scheduling or asynchronous foreign transactions.
Native first-spawn, repeat-raid and commanded-PFT testing remains separate.
