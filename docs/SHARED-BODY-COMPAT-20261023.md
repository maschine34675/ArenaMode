# Shared replacement-body adapters

Arena now requires Redeploy.Core **1.0.9 or newer**. The general Recoil Rework
Legacy, Continuous Load Ammo and Continuous Healing adapters have moved to Core,
where they also protect standalone Redeploy. Arena no longer installs or ticks a
second copy of these hooks.

`ArenaOptionalMods` preserves its existing admission order: whitelist, cached
server refusal, then Core's optional body-replacement contracts. No whitelist
rules or ordinary PMC/Scav admission rules change.

`ArenaMedicalBodyScope` is the only Arena-specific part of this migration. It
registers exact Arena bodies with Core's medical fallback, retaining weak leases
for delayed callbacks. Unrelated bots in ordinary raids do not receive that
fallback. Shutting down Arena disposes only this registration, never the shared
Core hooks.

Implementation, lifecycle rules and standalone test requirements are documented
in the sibling Redeploy repository's `docs/BODY-REPLACEMENT-COMPAT-20261023.md`.
The three adapter harnesses moved there as well. Arena keeps its admission-chain
verification under `tools/OptionalModAdmissionChecks`.

Earlier dated compatibility/GPU reports describe the previous Arena-owned
implementation. Their gameplay observations remain historical evidence, not
native acceptance of this migration. New in-game checks must load the new Core
and rebuilt consumer together; the version guard rejects an older Core.
Conversely, Core refuses an older Arena binary when it still owns the adapter for
an installed Recoil Rework Legacy, Continuous Load Ammo or Continuous Healing.
Install the rebuilt Arena and matching Core together to avoid duplicate ownership.
