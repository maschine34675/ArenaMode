# Optional looting, cleanup and rendering adapters

Implementation date: 2026-10-19. These are Arena-only adapters, not changes to
the optional mods or a blanket certification of every mod combination.

## Implemented behavior

- [Looting Bots](LOOTING-BOTS-COMPAT-20261019.md), reviewed 1.8.0 and the known
  1.8.1 RC startup path: autonomous combatant looting pauses in an exact Arena
  world. This includes allied independent TDM bots and replacement bodies.
  Frozen PFT companions retain command-driven looting; the RC's automatic spawn
  inventory cleanup does not alter their protected entry gear. Acquired search
  tickets are returned and unused corpse/airdrop queues are prevented at source.
- [Remove The Dead](REMOVE-THE-DEAD-COMPAT-20261019.md), reviewed 4.1.3:
  automatic, F5 and F12 removal share one guarded destructive call. The adapter
  protects Arena-retained corpses and nearby weapon pickups without changing the
  foreign timer, queue or saved settings. Its attempt counters may still count
  candidates whose destruction Arena prevented.
- [Smoothie](SMOOTHIE-COMPAT-20261019.md), reviewed 1.0.8: optional GC-pause calls
  yield to Arena's collector policy only in the exact active match. Private
  pickup-marker descendants use an excluded layer only when both current main
  and optic cameras can see it. Pooled weapons, area walls, physics, ragdolls and
  other rendering optimizations remain untouched. Unknown or unavailable visual
  contracts fall back with a warning; this adapter never blocks Arena admission.

All three are soft plugin dependencies and use reflected foreign entry points.
No foreign source or DLL is included in Arena. Initial setup, menu/final-start
admission and shutdown are wired through the existing lifecycle; ordinary PMC
and Scav start paths bypass the Arena admission checks. Looting Bots/RTD contract
failure blocks Arena rather than silently entering an unprotected match. Newer
versions warn and still require the relevant API/identity checks.

## Independent review

The three implementations were cross-reviewed against their pinned published
sources and the installed EFT decompilation. Review corrections included scan
ticket return, preventing retained corpse queues, RC PFT spawn-gear protection,
native interface access, initial inactive markers and delayed optional-plugin
discovery. There is no outstanding defect for the reviewed published contracts.

Forward compatibility remains limited: matching signatures do not prove that a
future release has no additional mutation/cleanup producer. In particular the
RTD call-graph check does not establish arbitrary new delegate/event/reflection
paths. On an identity-read fault Looting Bots deliberately runs the foreign path
and refuses the next Arena admission; it does not claim continuing protection of
an unknown runtime identity.

## Verification and deployment record

Implementation commits: `5540cb8` (RTD), `0782852` (Smoothie), `cdf802f`
(Looting Bots), `144a763` (shared lifecycle/admission/documentation).

Each adapter's linked production checks and compiled negative controls passed
both its implementation run and an independent parent run. Published metadata
was inspected without executing foreign assemblies: stable/RC Looting Bots,
RTD 4.1.3 and Smoothie 1.0.8. This is separate from actual Mono/Harmony and Unity
acceptance; the per-adapter reports describe their exact assertions and limits.

The joined Release build from `144a763` passes with four existing nullable-context
warnings and no errors. It uses the no-deploy target override, including the Core
project reference. The integration verifier confirms that installed client,
companion, Core and server hashes are unchanged by those offline checks.

| Check | Result |
|---|---|
| Looting Bots | 437 assertions, 12 compiled faults rejected, stable/RC metadata |
| Remove The Dead | 209 assertions, 13 compiled faults rejected, release metadata |
| Smoothie | 130 behavior assertions, 18 compiled faults rejected; separate published-metadata run adds eight assertions |
| Native menu/start source harness | 752 assertions |
| Optional admission | 48 assertions, eight compiled faults rejected |
| GC threshold | 35 assertions |
| Pickup regression | 500 behavioral assertions, 30 wiring checks, 41 compiled faults rejected |

Final joined verification evidence:
`artifacts/optional-compat-integration/87c06fd7b45d4d0399dc129621c7d4d6/`.
Existing AI Limit (72 assertions/seven faults) and MoreBotsAPI (93 baseline
assertions) checks were also rerun successfully during this work.

Native test installation uses an exact file/configuration restoration manifest,
not recursive folder deletion. Only the Arena client/companion pair is a permanent
deployment; server, Core and map payloads are outside this change.

Candidate product version: `0.1.0+144a7631340bf2ee2605d10a68bdfc76d49b87c1`.

| Assembly | SHA256 |
|---|---|
| Client | `2e60d3c37b0533087435379c9e4cf5c66a027f4170c0228b6fd1cd3abacc8347` |
| BigBrainCompat | `c6fdd43fab287f618dd47c1df1b1c9d821a1ddb6ccd0e1ad6c57026b2a9b2bd8` |

The pair was deployed to the development installation and GPU2. Native startup
successfully bound all three adapters without their contract warnings. The
first probe was paused when another session occupied the host; testing resumed
only after the user explicitly released GPU2.

On `144a763`, a four-participant Factory Deathmatch demonstrated actual Looting
Bots suppression, intact scan tickets, RTD manual and automatic cleanup attempts
against a retained physical corpse, and suppression of Smoothie's enabled optional
GC writer. A subsequent ordinary PMC raid in the same process demonstrated real
Looting Bots scans, looting/equipment replacement and resumed Smoothie GC ticks.
The ordinary RTD timer ran without a dead candidate; actual ordinary corpse
destruction is not claimed. See [the native matrix](GPU-OPTIONAL-COMPAT-20261019.md).

That raid also exposed a pickup construction failure introduced by hiding the
marker root before native TMP initialization. `2624bef` moves deactivation after
the synchronous text/layer setup, preserves the final inactive marker and adds
a native-lifecycle regression plus complete Debug exception stacks. Independent
review, a parent rerun of 135 behavior assertions/19 compiled faults, the joined
Release build and the full pickup regressions pass. The metadata-inclusive
Smoothie check has 143 assertions. Adapter policy is unchanged.

The corrected client/companion pair is deployed locally and on GPU2, with Core
and server unchanged:

| Assembly | SHA256 on `2624bef` |
|---|---|
| Client | `42de40f1d04670fba861e302360e122b3133ca053ab6d194530bf394675b1e4e` |
| BigBrainCompat | `f732d5856545c9cb1cec8daa69cba347323a9e517567edca42324a81890e10f3` |

The corrected native retest passes: six markers, a completed Smoothie static
scan, all 31 private marker renderers on a camera-visible excluded layer after
rotation, a visible shield label/beacon, and actual local-player damage-pickup
and bot health-pickup collections. The native weapon root retained its original
layer and no parent; the pickup exception did not recur. These are targeted
Factory checks, not exhaustive PFT, RC, optics or long-session acceptance.

One separate `BotFirstAid.CG_ApplyToSelf.method_1` null reference occurred during
native medical cleanup 0.77 seconds after leaving the final raid. This callback
family predates these adapters (see the October 5 Akimbo and bleeding reviews).
No pickup/adapter frame appears in its stack, but the null member and cause are
unproven; it remains an unresolved teardown observation, not a claimed clean log.

Both probe sets restored all 306 captured settings exactly. After regular raid
exit and clean game/server shutdown, the 12 staged payload files and six newly
created foreign configurations were removed across both installations. Recursive
DLL-name checks found no duplicate test DLLs; all eight expected deployed or
protected Client/Companion/Core/Server hashes still match. The original staging
manifest is unchanged. Looting Bots had rewritten its server JSON formatting;
both copies were separately backed up and compared as parsed JSON before removal
(same two original true values). Only empty test directories remain.

Final evidence: `pickup-fix-deployment.json`, `final-config-comparison.json`,
`server-config-normalization-review.json` and `restoration-verification.json`
under `artifacts/gpu-optional-compat-20261019/`. The final joined verification
run is `artifacts/optional-compat-integration/bddedc9511c8466a9d1f7990f0567e50/`;
the earlier corrected-source run is `0e5ce848b511438589ad937626815aea`.
