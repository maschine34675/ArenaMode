# Log-audit recommendations — implementation, 18 October 2026

This follows [the source audit](LOG-SOURCE-AUDIT-20261018.md) and
[the representative GPU samples](GPU-LOG-AUDIT-20261018.md). It does not silence
unresolved native errors or claim that the underlying model defects are fixed.

## Changes

- **Arena `f64d74f`:** stop match/scoreboard progression and new PFT live-roster
  discovery only after the exact Arena game/world enters native Stopping or
  Stopped. A queued/failed exit remains monitored. Existing roster faults stay
  latched, pending raw creations still drain, and persistence protection stays
  intact. See [the caller review](NATIVE-STOP-FOLLOWUP-20261018.md).
- **Arena `9769c00`:** only the successful exact settings GET loses its routine
  server request line. Completion failures, native 404/exception reporting,
  pushes and all ordinary routes remain visible. No changes to authentication,
  responses, poll frequency, global logging or request execution.
  See [the endpoint boundary](SETTINGS-POLL-LOGGING-20261018.md).
- **Redeploy `aca6e7a`, Core 1.0.8:** expected successful culling replacement,
  camera/cache/marker/freecam details move to Debug. Real recovery failures and
  protection warnings remain. Detailed pipeline measurement is now opt-in through
  the additive `RespawnRequest.EnablePipelineTiming` field; the frozen v1
  observer API is unchanged. Off means no expensive state/reflection counters,
  victims-restore measurement or following-frame coroutine. Actual statistics
  restoration remains unconditional and occurs exactly once.
- **KillcamReplay `00939e5`:** thirteen routine setup/tracking/clone/shader
  emitters move from Info to Debug. Existing bounds remain; lifecycle, missing
  usable barrel data, warnings and errors stay visible. The pre-existing narrow
  synchronous clone logging window was not changed or broadened.
- **Arena `1ffe0b5`:** all four respawn request sites opt into Core timing only
  when `Diagnostics/DeveloperMode` is on. Seven bounded, read-only native-model
  observation hooks add object/body/weapon context under the same setting and
  confirmed Arena ownership. Native logs, exceptions and fallbacks survive.
  See [the hook inventory and limits](NATIVE-MODEL-DIAGNOSTICS-20261018.md).

Arena now needs the matching **Core 1.0.8 or later**. The standalone Redeploy
driver has its own `Diagnostics/EnablePipelineTiming=false` setting. Existing
Arena DeveloperMode remains false by default; no user's saved value is reset.
Switching it off affects subsequent respawn requests, not a measurement already
in progress. No global BepInEx/Harmony filter was changed. A host configured for
`LogLevels=All` still writes Debug messages.

## Independent verification

Implementation leaves were reviewed by the integrating agent, and meaningful
checks rerun independently before deployment:

| Check | Result |
| --- | --- |
| Arena unit suite | 1,167 passed, two existing optional skips |
| Redeploy suite | 183 passed, zero skipped; frozen API, installed consumers, order and disabled-cost controls |
| Killcam handoff | 72 assertions passed |
| Native-stop caller harness | 33 assertions and five compiled defective variants rejected |
| Existing exact-exit coordinator | 81 assertions and eight compiled defective variants rejected |
| Installed PFT compatibility | 340 assertions passed |
| Settings poll | 417 assertions and two compiled defective variants rejected; actual installed SPT Harmony seam exercised in isolation |
| Native model observer | 72 assertions and eight compiled defective variants rejected; actual installed EFT metadata/IL checked |
| Complete UI interaction chain | 81 exit / 738 menu / 52 preview / 695 host-lifecycle assertions; 8 / 5 / 18 compiled defective variants rejected |
| Release binaries | Client/library, Server, Core/standalone Client and Killcam build successfully with deployment disabled |

The clean Arena rebuild retains its four existing nullable client warnings and
two existing server warnings. Core/standalone Client and Killcam builds have no
warnings. The native-model integration's initial obsolete Harmony overload was
fixed and the real paired client build rerun. An initial concurrent diagnostic
harness run collided in shared `obj/bin`; exclusive complete reruns passed.
Neither failed attempt was counted as evidence of success.

The enclosing UI verifier exposed a stale preview test fixture from the earlier
PFT Tactical equipment-choice feature. Its missing boundary and outdated
own-gear expectation are corrected separately; runtime preview code is unchanged.
See [the fixture repair and full verification](PREVIEW-HARNESS-FOLLOWUP-20261018.md).
The test-only repair is committed as `f081c66`; the parent reran the entire
enclosing script successfully after the worker finished, without shared-build
overlap. The native candidate remains `1ffe0b5` because that later commit changes
only fixtures and documentation.

Four-pass integration review checked the exact stop/latch ordering, disabled
measurement cost and unconditional restore, exception preservation and native
request ownership, then rebuilt and reran the production-linked controls.
No changes were made to gameplay difficulty, equipment, mode rules, global
logging settings or unrelated mods.

## Native acceptance

Fresh GPU2 process `4a1afffbdb8243d1a53bff93d05990c3` loaded the reviewed
client, library, Core, standalone driver and Killcam modules; the server was
restarted with the matching server DLL. All six deployed hashes match the
candidate manifest. Factory DM covered two actual enemy-caused deaths with
replay and successful respawn, first with detailed measurement off and then on.
Protected PFT Tactical covered a deterministic native test death, third-person
spectating, restored kit/leader/followers in round two, a further live round,
real firing after the rebuild and normal native disconnect. Neither exit
produced the previous roster/no-contest false alarm.

The final three-stream export contains 10,675 unique records. Successful exact
settings GETs stay quiet while settings synchronization and ordinary request
logging continue. No Warning/Error came from ArenaMode, Redeploy or Killcam's
own logger. This does **not** mean the native log is clean: the new probes
captured jet-material, body disposal, utility-event timeout and clipping-type
context. Some objects were inactive pooled views, others active AI views;
causation is unresolved. Menu preview WeaponPrefab exceptions outside the
confirmed Arena boundary correctly receive no Arena context. Original native
errors remain visible. All pages were consumed; one oversized application Info
payload was clipped by the bridge, but no Warning/Error stack was truncated.

Both protected entry/exit profile comparisons preserve all eight checked
sections and health values (314 items each). All twelve PFT files are
byte-identical; all 224 Arena and eight Redeploy settings match their backups,
and unrelated plugin DLLs/configuration are unchanged. GPU2 is at MainMenu with
test hooks off and the four temporary invitations removed. These are bounded
regression samples, not long-raid or all-map certification.

The dedicated [GPU2 follow-up](GPU-LOG-FOLLOWUP-20261018.md) records final loaded
identities, actual death/replay/respawn and PFT exit behavior, merged client/server/
EFT diagnostics and restoration comparisons. Its observations are separate from
the offline checks above. Raw profiles and stores remain in ignored artifacts;
do not publish them with this report.
