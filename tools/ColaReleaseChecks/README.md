# Tarkova Cola 3.0.1 release contract

Run `pwsh -NoProfile -File tools/ColaReleaseChecks/Verify.ps1 -ReleaseDirectory <folder>`.
The optional folder argument contains only the two official release DLLs extracted
from the upstream 3.0.1 ZIP. Without it, the test uses the current checkout fixture
`artifacts/optional-mod-releases/cola-3.0.1`; missing DLLs fail. This fixture is part
of the verification runner input/cache contract, not a sibling-checkout lookup.
Foreign DLLs are read with Cecil, never loaded or installed.

The checks pin release SHA256/GUID/version, the new guarded reflection contracts,
HTTP closure shape and the actual research catalog emitted in release IL. Production
shared code validates the fingerprint. Separate source-wiring checks pin the Arena
lease hooks. The reroll lifecycle methods are extracted verbatim from the production
adapter and executed with synthetic EFT seams, including actual worker-thread
callbacks. Seven faults must fail their named checks. These are metadata/source
and managed behavior checks, not an executed Unity Harmony or gameplay test.

The focused xunit families `RewardCola*` and `TarkovaCola*` separately exercise real
production delta/settlement/recovery on generated owned profiles, both reviewed
catalogs, old pending plans, no duplicate payout, new research and APEX preservation.
Use the repository no-deploy targets and deployment sentinel when invoking those
families. Parent integration owns full verification and native checks.

3.0.1 preserves existing Arena temporary perk gameplay. New companion perk editing
and machine-editor persistence are withheld only while the protected Arena lease
owns the run. Machine initialization happens once per raid, not per replacement
body. Late Vulture Aid reroll responses are tied to the exact request closure and generated
JSON object plus its exact container. The callback checks only managed lease/body identities on its worker
thread; main-thread application additionally checks the current world, living body
and replacement/finalization state. Each new request gets its own token, so a
completed reroll never reserves its container against later respawns. A failed
replacement that retains the old body still allows the callback's native busy-state
cleanup. If upstream races its separately read JSON/container pair, the mismatched
application is rejected. Other containers and ordinary raid requests are unchanged.

Legacy 2.2.1 receipt bytes, catalog and goals remain supported. A 3.0.1 receipt has
its own catalog and the new goal of 10 for a_bolt. Old completed reward decisions
retain their original goal of 5, even when consumed after an upstream upgrade;
existing higher counters and added perk state never regress. APEX cores/progress,
equipment choices, ordinary statistics and inventory are never imported as rewards.
