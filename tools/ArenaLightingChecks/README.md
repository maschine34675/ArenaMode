# Arena lighting checks

Offline regression harness for the Arena lighting director and short native
deploy countdown. It links the production phase policy, runtime director,
announcer cue, tracer palette and deploy patches against small Unity, EFT,
Harmony and BepInEx stand-ins.

The checks cover exact Arena/GameWorld ownership on Labs, both Factory variants,
Labyrinth, Customs and ported Equator, Bay5 and AirPit, effect precedence and fades,
Tactical and Rounds timing, configured team colors,
external Blackout/Going Dark suppression, one-light ownership, live disable,
stale-world teardown and duplicate-free cleanup. Source-contract assertions
also reject scene scans, shared-material changes, `RenderSettings` changes and
other mutation of the existing map lighting pipeline.

The deploy checks execute the linked production prefixes/postfixes with a native
configuration stand-in: one second while armed, restoration on completion and
teardown, heal-first restoration after an aborted load, ordinary-raid/disabled
isolation, and preservation of already shorter countdowns.

The runtime technique was informed by the MIT-licensed
[Blackout](https://github.com/Vultify/Blackout) and
[Going Dark](https://github.com/notSENZE/GoingDark) projects. ArenaMode does not
copy their map coordinates, audio, assets or scene-mutation code: it creates a
single additive directional light parented to the exact Arena `GameWorld`.

Run:

```powershell
dotnet run --project tools/ArenaLightingChecks/ArenaLightingChecks.csproj -c Release
```

This proves policy and lifecycle behavior, not rendered brightness or native
audio fade timing. GPU raids are still required to verify the effect through
normal and spectator cameras and the shortened pre-raid countdown.
