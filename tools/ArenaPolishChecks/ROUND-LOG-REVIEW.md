# Routine Tactical round outcomes

The inspected 2026-09-06 client log contains eleven protected Tactical victory or
defeat summaries emitted as Warning (19:10:19 through 19:41:17). These are ordinary
game outcomes. `TacticalMode.EndTeamRound` now emits Info when its existing nullable
`won` value contains a result. A null result still emits Warning: readiness,
identity, hostility and lifecycle failures must remain visible. The solo Tactical
round summary already used Info and is unchanged.

Only message severity changes. Round scoring, continuation, rewards, announcer
cues and toasts use the same state and remain untouched. No global logger filter
or exception suppression was added. The client Release build passed; the narrow
branch is reviewed directly rather than mirrored in a new implementation-shaped
test. Existing Tactical regression checks are included in the final integration
run. Live gameplay is not claimed on this host without a GPU.
