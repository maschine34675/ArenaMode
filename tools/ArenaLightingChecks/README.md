# Arena lighting checks

Offline regression harness for the Labs, Factory and Labyrinth Arena lighting
director. It links the production phase policy, runtime director, announcer cue
and tracer palette against small Unity, EFT and BepInEx stand-ins.

The checks cover exact Arena/GameWorld ownership, Labs, both Factory variants
and Labyrinth, effect precedence and fades, Tactical and Rounds timing, configured team colors,
external Blackout/Going Dark suppression, one-light ownership, live disable,
stale-world teardown and duplicate-free cleanup. Source-contract assertions
also reject scene scans, shared-material changes, `RenderSettings` changes and
other mutation of the existing map lighting pipeline.

The runtime technique was informed by the MIT-licensed
[Blackout](https://github.com/Vultify/Blackout) and
[Going Dark](https://github.com/notSENZE/GoingDark) projects. ArenaMode does not
copy their map coordinates, audio, assets or scene-mutation code: it creates a
single additive directional light parented to the exact Arena `GameWorld`.

Run:

```powershell
dotnet run --project tools/ArenaLightingChecks/ArenaLightingChecks.csproj -c Release
```

This proves policy and lifecycle behavior, not rendered brightness. A GPU raid
on Labs, Factory day/night and Labyrinth is still required to tune intensity and
verify the effect through the normal and spectator cameras.
