# Private-session reward consent — native acceptance, 2026-10-25

Result: passed on SPT-GPU-2 at 1920×1080, SPT 4.1.6 / EFT 0.16.9.5-40743.
The actual helper transition, private ON/OFF control, one native Deathmatch,
receipt transfer, normal-server outage, grant renewal and clean recovery were
exercised. The test finished in the ordinary MainMenu, backend 6969, with the
original ON preference restored at consent revision 4.

## Final artifacts and isolation

| Artifact | SHA-256 |
| --- | --- |
| ArenaMode.Client.dll | `53AB2A85B43415FE89EF6842DFEFFDF8DC8D02AAB6A7B16DDDEE02EBBA61DC00` |
| ArenaMode.Server.dll | `EA1DEFF0046E0A073BA5DD98BFC0C7BE7620D4CF44E4565C38674BC4A70E55BB` |
| ArenaMode.Session.exe | `A1CF2500C9DE1A824FA1FD8288ED53A5E4BEDFA30A995FAF2002763C3017048D` |

Only the designated SPT-GPU-2 test profile was used. Before restarting any
server, all 50 profile JSON files across development and GPU2 were backed up:
three canonical profiles and 22 existing sync-conflict files per installation.
The client was closed normally for deployment. Exact approved artifacts were
copied only after the corresponding processes stopped, then hashes and loaded
assemblies were checked. The final ordinary game instance `41904719` started
at 23:04:51 UTC; all 16 checked assemblies matched development, host disk and
loaded process. Final normal-server PID 9344 had no second/private SPT server.

## Menu, consent and profile protection

1. Normal Arena admission was already blocked by Manimal-LoadAmmoAnim. Its
   character-selection notice displayed START ARENA SESSION and no reward
   control. No temporary deny policy or test mod was necessary. Private
   character selection likewise had no misplaced reward button.
2. The real two-click helper opened a private backend on 6970. The normal server
   remained running throughout the positive consent changes and combat test.
   A server-owned grant and helper sidecar existed, matched each other and were
   bound to the private marker, selected profile, source runtime and port.
   Evidence checks emitted only booleans/fingerprints, never the bearer or key.
3. The private Arena map card displayed ON with an interactable button. Actual
   UI clicks changed ON → OFF → ON, advancing normal consent revisions 0 → 1 → 2.
   OFF retained the existing origin needed by already-earned receipts. The
   subsequent OFF → ON comparison found the entire ordinary profile identical;
   the earlier helper-entry interval changed only its native Hideout timestamp.
4. After the real match, another UI click set OFF at revision 3. Following the
   outage/restart test, a new helper entry renewed the grant. This fresh OFF
   private tree had no reward origin and an empty outbox. Its map card remained
   writable. The final UI ON click created a fresh enabled origin and revision 4,
   with an identical normal profile before/after the click.
5. There is one full reward card at the top right of the Arena map screen;
   screenshots show the rules, map preview, mutators and navigation unobscured.
   ON, OFF and UNAVAILABLE were observed. No compact reward button remained
   inside the incompatible-mod notice or Mutators.

The initial native attempt exposed a real integration regression: the prepare
request sent `controlId`, but SPT's case-sensitive JSON reader expects
`ControlId`. Preparation failed closed, leaving the older read-only private
flow intact. The corrected client above uses the DTO property name, and the
implementation regression harness now deserializes the actual request body.
All positive tests were repeated with that corrected client.

## Native combat, signed result and transfer

The bounded match used Factory day, Deathmatch, four participants, all opponents
against the player, one scoring round and one kill to win. Five match settings
were temporary in-memory changes (`persist=false`). Godmode, infinite ammo,
safe teleport near an opponent and head tracking made the test repeatable.
An AKS-74 fired 14 observed native shots, causing one headshot kill and a victory.
No kill, result, authorization, receipt or profile was injected.

The independent receipt reader verified:

- Completed / LocalTarget, four actors, one death, one local kill and one win;
- 42,836 ms observed duration and the server-owned 1,000 / 10,000 / 200,000 policy;
- a valid HMAC and identical entry/expected/persisted restoration revisions.

The private outbox contained one signed 11,000-RUB receipt. Actual helper return
while consent was OFF stopped the private server and transferred it: outbox
1 → 0, normal inbox 2 → 3. The two historical receipts were preserved. The
normal profile during private combat changed only its native Hideout timestamp;
inventory cash, payments and reward mail did not change.

## Unavailable control and deliberate restart safety

With the private game in its menu and consent OFF, the normal server was stopped
normally. The private card became UNAVAILABLE and disabled, with instructions
to keep the normal server running and re-enter after restarting it. The game
continued normally. Restarting the normal server invalidated the old process-local
grant; the card remained unavailable after a full refresh interval. Re-entering
through the helper renewed the grant and restored the writable OFF card.

The deliberately concurrent startup also triggered the existing payout safety
hold `another-server-or-unknown-process`: the private server was still running.
The normal profile was completely identical across this OFF startup. The hold
was correctly shown in the reward card and did not prevent restoring consent.

Cleanup therefore used the safe order: return to normal, verify the private
server is gone, close the ordinary game, restart the normal server, then start
the ordinary game. This cleared the deliberate startup hold without editing
any profile, ledger, receipt, enrollment or consent file manually.

## Settlement and cleanup

The final clean startup delivered exactly one new 11,000-RUB reward mail:

| Test profile field | Before | After |
| --- | ---: | ---: |
| Payment ledger entries | 2 | 3 |
| Total recorded rewards | 22,000 | 33,000 |
| Reward messages | 2 | 3 |
| Unclaimed reward cash | 0 | 11,000 |
| Inventory cash | 486,221 | 486,221 |

Before the game logged in, the complete JSON comparison had only four changes:
the System dialogue's `new` / `attachmentsNew` flags, one new message and one
new payment entry. Neither historical receipt was repaid. No cash was claimed.
The whole test-session comparison additionally had the two native
`Health/UpdateTime` and `Hideout/sptUpdateLastRunTimestamp` changes.

Final public status was available, enrolled, ON, writable, revision 4, state
`paid`, zero pending and last payout 11,000. All 48 unrelated profile files
were byte-identical to their backups. Both Arena configuration files had zero
changed or added settings; the original zombie mode returned after restart.
Whitelist policy files stayed absent, and no temporary test mods were added.
The final client has no raid or active test hooks; normal MainMenu is open.

Secret checks read the client log, normal and private SPT logs and helper log
with shared-file access. Private logs initially buffered at zero bytes and were
rechecked after normal helper shutdown (171 KB, then 92 KB). No control bearer
was found in either tested private lifetime. The final scan also checked stored
reward delegation keys, finding none in the nine readable log files. No new
reward exception was found; the deliberate startup hold is explained above.

A second OFF combat raid and a physical click while armed were not performed.
Those fail-closed capture/consent conditions remain covered by the automated
protocol tests; this report does not claim native acceptance of every mode,
resolution, first-time enrollment or expired grant.

## Evidence

Ignored evidence is under `artifacts/reward-private-consent/gpu/`:

- `profile-backup-manifest.json`, `before-development`, `before-gpu2`;
- `corrected-client-deployment.json`, `11-corrected-normal-loaded.json`,
  `12-corrected-enter`, `13-corrected-map`, `14-private-off`, `15-private-on`;
- `16-dm-raid`, `17-bots.json`, `18-native-kill`, `private-native-receipt.json`;
- `20-postraid-off`, `21-normal-offline`, `23-stale-control-refresh`,
  `24-return-receipt-off`, `normal-inbox-receipts-after-transfer.json`;
- `25-fresh-off-enter`, `26-fresh-off-map`, `27-restore-original-on`,
  `grant-renewal-result.json`, `restored-original-on-state.json`;
- `stopped-before-clean-settlement.*`, `after-clean-settlement.*`,
  `final-normal-menu.*`, `final-public-status.json`, `cleanup-result.json`,
  `bearer-log-scan-first-grant-final.json`, `bearer-log-scan-full.json`,
  `29-final-normal-loaded.json`.

Two runner-only misses are retained honestly: a broad ARENA selector matched
START ARENA SESSION before the corrected flow, and the first bot-count check
ran before the asynchronous opponent activations. Three opponents then became
active without changing code, and the bounded combat test passed.