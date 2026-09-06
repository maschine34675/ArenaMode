# Tactical PFT goal handoff checks

Run `Verify-Contracts.ps1`, `Verify-Regression.ps1` and
`Verify-Deployment.ps1` from PowerShell. The deployment check rebuilds and installs
Arena's client and optional BigBrain companion; it does not change external mods.

The executable links production goal/context/round guards and the real companion
runtime. EFT and the PFT scoped token are independent test seams, not copied mod
implementations. Its assembly identity deliberately matches the supported PFT API
to exercise reflection. It has no deployment target: **never install the test
`pitFireTeam.dll` into the game**. The real installed PFT binary is checked separately
with Mono.Cecil. Temporary mutation builds are outside production sources.

These tests validate Arena's decisions, not Unity navigation or PFT's entire AI.
See `PFT-GOAL-HANDOFF-REVIEW.md` for the native contract and required GPU playtest.
