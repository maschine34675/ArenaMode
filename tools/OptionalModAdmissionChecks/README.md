# Optional mod admission checks

Run `dotnet run --project tools/OptionalModAdmissionChecks -c Release` from the
repository root. This compiles the real `ArenaOptionalMods.cs` with stand-ins for
the whitelist admission, the server ping's admission failure and the three
remaining optional adapters (Continuous Healing, Continuous Load Ammo, Recoil
Rework Legacy). No game assemblies, network requests, installed configuration or
deployment are used.

`pwsh -NoProfile -File tools/OptionalModAdmissionChecks/Verify.ps1` additionally
checks isolated production mutations: dropping or demoting the whitelist, ignoring
or demoting the server rejection, bypassing each adapter, and reintroducing a
code-level refusal of a named mod (ORBIT, ISB). Each must fail a behavioral
assertion; compilation failure does not count. Evidence stays in the ignored
`artifacts/optional-mod-admission-checks/`.

Coverage: the order of the admission chain (whitelist, then the server's reported
failure, then the three adapters) and its short-circuiting; exact propagation of
each reason; a verdict that is rechecked rather than cached; and that none of the
mods Arena used to refuse or adapt by code (ORBIT, Questing Bots, Adaptive Bot
Culling, ISB Special Forces, Aishi, MoreBotsAPI, AI Limit, Looting Bots, Remove
The Dead, Smoothie, Late To The Party, Classic Movement, StatTrack,
Deminvincibility, Skills Extended) changes the admission result: since 2026-10-22
the whitelist (shipped default deny list and the user file) owns that decision.

`ProductionAdmissionSource` can point to a temporary mutated production file for
negative controls. These checks establish admission behavior outside Unity, not
actual plugin loading or combined in-raid compatibility. `NativeMenuChecks` owns
the separate production menu/final-start and ordinary-raid integration checks.
