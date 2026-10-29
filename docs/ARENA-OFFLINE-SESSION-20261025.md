# Normal server stopped during a private Arena session

Development after 0.2.0. This extends the native save/shutdown adapter in
[REWARDS-RETURN.md](REWARDS-RETURN.md) and supersedes its keep-normal-running
behavior for supported local sessions.

## Player flow

The installed Session.exe also opens a compact profile picker and starts Arena
without opening normal EFT first. It starts/reuses the local normal server for
fresh preparation and then follows the same schema-3 handover below. See
[direct start](DIRECT-START-20261028.md).

1. START ARENA SESSION prepares a fresh entry operation independently of rewards.
2. After EFT exits, the ordinary server drains its requests and update callbacks,
   saves every loaded profile and verifies disk against RAM. It publishes durable
   completion evidence, then shuts down gracefully.
3. The helper verifies that exact process has exited, copies the saved profile
   into the private tree and starts the private server. Only a confirmed private
   activation and its own listener allow EFT to start.
4. The ordinary server stays stopped during Arena. PROFILE REWARDS remains usable;
   its ON/OFF choice is persisted and signed by the private session. Gameplay,
   snapshot restoration and result validation remain unchanged.
5. RETURN TO NORMAL SESSION ends the private writer and starts normal SPT with its
   original arguments. Before native profile loading, normal SPT imports the final
   preference and signed results, then applies the existing startup payout. EFT
   opens after that completes. No extra manual restart is needed.

This also applies when rewards are OFF or there are no results: stopping the
ordinary server is a session-lifecycle decision. OFF defers pending payouts under
the existing reward policy. A never-enrolled profile can change its preference,
but must return once for startup enrollment before earning in a new private session.

EXIT or a game crash still lets the watcher stop the private server without
automatically reopening EFT. Its durable results and choice are recovered by the
next normal SPT startup. No private profile is copied back.

## Authority and locks

Client/helper requests use schema 3 with an opaque `offlineSessionId`; optional
version-filtering plans remain supported. Old helpers refuse schema 3. Entry
preparation is separate from reward consent. The capability in
`user/arena-rewards/<profileId>/entry.json` is process-local, short lived and bound
to the profile, source runtime, normal PID/start time/arguments, loopback origin,
certificate pin and the exact installed helper. Its secret is never sent to EFT
or through SPT's request-body logger.

The helper holds the original `.startup.lock` from before ordinary shutdown until
the private server has acquired the original `.server.lock`. Private startup does
not acquire the original startup lock, avoiding a deadlock. It writes its activation
proof before the helper releases the startup lock. Ordinary startup acquires both
locks before native profile loading; a concurrent private owner is a fatal startup
refusal, including when an active-pointer file is missing.

The immutable `offline-sessions/<id>/lease.json` binds source/private roots,
profile, marker creation time, port, original server/helper identities, entry
operation and initial consent revision. Each enrolled session gets its own signing
delegation; its control key is distinct. `offline-active.json` indexes the lease;
`activation.json` binds the actual private process. The private marker and exact
lease copy must agree. Private ON/OFF updates use expected revisions and atomic,
read-back-verified HMAC state. Missing authority never falls back to a global or
legacy ON handoff.

After the private writer has exited, normal startup records an immutable
`closed.json` plan with the final signed choice and allowed run/fingerprint list.
It applies consent, copies and verifies each result into the original inbox, and
only then drains the private outbox, records `completion.json` with the closure hash,
and retires the active pointer. A closure alone is a recovery plan, not completed
recovery. Missing pointers are reconstructed from exactly one uncompleted immutable
lease under both locks; contradictory or damaged history is refused. Interrupted
recovery resumes that same plan. Later receipts from an old private session are
not accepted unless the closure already listed their exact fingerprint. Payout
continues through the unchanged approval and permanent-payment journal.

## Failure boundaries

- A lost HTTP reply can succeed only with matching durable save proof and exact
  old-process exit. A crash or absent PID alone is never permission to copy.
- The same authenticated operation can retry after expiry. Competing entry/return
  requests cannot take over its shutdown sequence; Ready publication is serialized.
- Before native update cancellation, a refused save leaves normal SPT usable.
  After cancellation, the existing held Retry/Cancel path preserves RAM. Never
  force-close that server, reset save hashes or substitute a profile from disk.
  Retry is available while that helper operation remains open. Cancel or loss of
  the helper after a held save requires investigation of the retained server; a
  new helper does not take over another helper identity or resume native updates.
- Once entry is accepted, schema 3 cannot downgrade into legacy startup. A timed-out
  preparation leaves EFT open. Slow optional consent preparation does not hide an
  accepted entry capability.
- A failed private startup can return through the proven lease. The helper releases
  the startup lock before starting normal SPT. Listening alone does not prove successful
  recovery: the immutable closure and retired active pointer are required too.
- Before native updates are stopped, a refused entry may explicitly authorize
  reopening EFT against the same normal server. The helper verifies its original
  PID/start time/image and sole local listener again, and refuses any active or
  Ready lease. No private profile copy or server restart occurs on this path.
  Missing/held/unknown replies never authorize it. The helper is started without
  inherited EFT handles; see [restart hardening](ENTRY-RESTART-HARDENING-20261028.md).
- If a new Build fails before replacing the previous private tree, recovery can
  ignore that predecessor's state only with matching original/private leases,
  roots and marker, a valid completed closure, its exact signed final state and
  an empty outbox. The new session keeps its own initial consent and no results;
  mixed or incomplete old/new evidence remains fatal.
- Missing/malformed private state after a positively stopped activated session
  now uses [automatic discard](OFFLINE-STARTUP-RECOVERY-20261028.md), before any
  closure exists. Unknown consent becomes OFF. Missing activation, conflicting
  ordinary authority, multiple unclosed sessions or unknown/live processes remain
  refused. An acknowledged OFF choice never reverts to the original ON state.
- A private tree with unclosed ownership or undelivered results is retained unless
  ordinary startup has durably discarded that exact stopped session. Only ordinary
  startup closes or discards its ownership.
- Several installations of one Windows user share the helper session folder and its kept private tree. A finished
  (closed, completed) session of another installation does not block an entry: another installation's tree is
  only "in the way" while a server really runs from it. An Arena session of another installation that is not
  closed, or whose results were never returned, is refused before this installation's normal server is stopped,
  with a message that names the other installation; nothing is rebuilt over it.
- When an entry fails before the normal server was asked to stop (before the entry capability is opened), the
  helper reopens EFT against the unchanged normal server, with the same proof as for a declined entry, and the
  notice says why Arena did not start (a failure that is not a plain refusal, such as session records that
  are gone, points to `helper.log`). A direct start launches no game of its own in that case.
- Older local installations which never issue an offline entry
  capability retain the schema 1/2 live-server compatibility path. Its consent relay
  and return capabilities are documented in REWARDS-RUNTIME/RETURN. The current
  client blocks automatic session switching against remote servers; the desktop
  direct starter requires local schema-3 support and never falls back to schema 1/2.

Local process/file locks do not coordinate independent machines that synchronize
profile directories. Native save/read-back still refuses conflicting external writes.

## Verification

Synthetic checks cover the actual serialized server/helper wire, profile-independent
entry capability, expiry/replay, native save ordering, lost responses, exclusive
ownership, signed ON/OFF/ON and stale revisions, missing activated evidence,
interrupted closure/import, late receipts, repeated startup payouts, original launch
arguments and refusal of a listener without completed recovery. Existing legacy
session, control and receipt harnesses remain required.

Offline run on 2026-10-25: **3,194 passed, zero failed, one known APBS environment
skip**. It includes 38 server ownership/recovery cases and 34 helper cases. Control
transfer: 194 assertions; client session harness: 748; result transfer: 117, with
both conflicting-result and premature-rebuild mutants rejected. Three focused client
mutants also fail their named behavioral assertions: treating a lost entry reply as
unsupported, hiding accepted entry behind optional consent, and downgrading after a
preparation timeout. `tools/OfflineSessionChecks/Verify.ps1` runs those bounded checks.
The verification
runner confirmed installed payloads unchanged. The complete Release solution,
including the net472 helper, built with deployment disabled (zero errors).

Build/deploy and native acceptance are recorded separately in the
[GPU-2 switch report](GPU-OFFLINE-SESSION-20261025.md); these offline results alone
are not evidence of a completed in-game test.
