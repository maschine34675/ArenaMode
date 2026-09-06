# Persistent round assembly notice

The HUD now places a dark, high-contrast waiting banner below the score panel.
The 30-point heading is distinct from the existing 16-point status line; no
raycast target or input handler is added. It remains visible throughout protected
Tactical staging, including five arrived opponents that are not yet combat ready,
and during team rebuilding. Solo Tactical also shows the notice while its first
opponent is still being generated. It disappears immediately when waiting ends,
without waiting for the 0.2-second text refresh interval.

The production partial `TacticalMode.AssemblyStatus.cs` only reads existing mode
state. No spawning, timer, damage protection, buy phase or round transition is
changed. Native/current phase behavior was checked against `TacticalMode.Tick`,
`TickTeamStaging`, `StartRound` and `RequestSoloSquad`: protected teams enter Live
only after readiness; solo enters Live before its asynchronous first arrival.

Run `pwsh -NoProfile -File tools/ArenaAssemblyChecks/Verify-Regression.ps1`.
The harness links the real notice and banner code and models only remaining
mode fields, native session and UI objects. It exercises persistent full-arrival
waiting, combat/end/normal-raid rejection, same-frame clearing, solo arrival,
and passive UI setup. Three intentionally incorrect source copies must fail
their behavioral assertions; production source is never replaced. No plugin
project is built or deployed by this harness. These are 32 offline assertions,
not a Unity layout or GPU playtest.

Review passes: complete component and lifecycle integration; native phase and
timing review; rejected wrong-phase/normal-raid/premature-ready variants; final
layout and input review. Both text lines fit the 900 by 90 HUD-scaled backdrop,
below the score and above the crosshair/callout region. GPU confirmation remains
required, including different display aspect ratios and protected round rebind.
