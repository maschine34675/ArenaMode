# Interim ISB admission

Implemented on 2026-10-20 after the [candidate review](COMPATIBILITY-CANDIDATES-20261020.md).
ISB Special Forces and ISB Aishi are blocked **only for Arena** until their
independent persistence and lifecycle paths have dedicated protection.
This is an admission safeguard, not gameplay compatibility with ISB enabled.

## Identity and reason

| Mod | Exact client/server GUID | Reviewed release | Reason |
|---|---|---|---|
| ISB Special Forces | `com.samc137.isbspecialforces` | 2.0.1 | Separate revenge, truce and spawn-progression stores and outer raid-end routes are not protected by Arena's SPT snapshot or MoreBotsAPI adapter. |
| ISB Aishi | `com.samc137.aishi` | 2.0.4, source-only | Anomaly knowledge is stored outside the SPT profile; a charged special defibrillator can veto Arena's death path. |

Presence is sufficient, including unknown/newer versions and missing client
instances. Individual F12 switches do not prove that the foreign routes or
item hooks disappeared. The block uses neither broad name matching nor a
BepInEx incompatibility attribute. Other mods by the same author remain allowed.
The user's foreign configuration, files and plugin lifecycle are unchanged.

The shared policy supplies an English explanation naming the mod, the reason,
the remove-and-restart recovery and the continued availability of ordinary
PMC/Scav raids. Existing native-menu presentation and final admission use it.

## Client and server boundaries

- The client checks the live plugin registry, with soft dependencies for load
  order only. Stale selections are rechecked before server arming and after
  refreshed server/settings data. Closing setup does not remove admission.
- The server independently reads its loaded mod metadata. Its Arena raid-start
  route rejects before stale-snapshot recovery, request replay, preparation or
  snapshot capture. Removing just the client DLL cannot bypass this safeguard.
- The existing ping response advertises an optional `admissionFailure`.
  Server reachability/version remains independent from Arena admission.
  A client with only a server-side ISB installation receives the visible reason
  on explicit Arena selection; subsequent menu queries use that cached value
  without extra HTTP polling.
- Successful discovery clears an earlier server-only restriction when the new
  response has none. A failed connection does not expose a stale mod reason.
  After removing server mods, restart **both server and game**: the client may
  still retain its previous rejection and deliberately does not poll from the
  disabled selection control.
- Ordinary native starts return before this admission path. Recovery, cancel
  and protected end endpoints are not blocked; an existing transaction must
  remain recoverable.

## Verification and limits

Production-linked optional-admission tests cover both exact GUIDs, future and
missing versions, missing instances, late discovery, unrelated author IDs,
server-only propagation and absence/removal. Negative controls deliberately
remove the client ISB branch and server-reason propagation.

The native-menu harness exercises the real transport/admission code with
controlled boundaries: rejection before arming, stale selection, unchanged PMC
and Scav starts, legacy ping responses, refreshed removal and transport failure.
Three server tests exercise both loaded identities before snapshot recovery,
preservation of an existing snapshot and ordinary state, and unrelated content.
An independent adversarial review found no material correctness issues.

These are offline checks; the subsequent native follow-up is recorded below.
No ISB-enabled native raid or Aishi release-DLL equivalence is claimed. The exact
underlying source and published Special Forces DLL evidence is in the candidate
review. No foreign code, download, configuration or executable is included in
the Arena package.

## Native client follow-up and server-test boundary

The [2026-10-20 GPU2 run](GPU-COMPAT-SECOND-20261020.md) temporarily installed the
complete published Special Forces 2.0.1 client and its MoreBotsAPI dependency.
The native character screen displayed the specific restriction, disabled Arena
and retained PMC selection when the Arena model was clicked. The ordinary PMC
map screen remained reachable. Expected missing-ISB-server route errors in this
deliberately client-only setup are not evidence of an Arena adapter failure.
No full raid with ISB was performed.

The complete ISB server was deliberately not installed against the shared
profile collection. Its published DLL has a profile-load router that can migrate
legacy `ISBBerserker` statistics, and a quest-list hook that can change existing
amnesty quest progress outside a raid. Removing the temporary mod would not
reverse saved profile changes. Server-only admission therefore retains its
offline tests; native acceptance needs a separate disposable environment.
This is not a claim that every profile changes or that such a change occurred
during this run.

The broader `EventQuestProfileMigration` found in the release-tag source is
absent from the published DLL and is explicitly **not** used as release evidence.
