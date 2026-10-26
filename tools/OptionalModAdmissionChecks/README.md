# Optional mod admission checks

Run `dotnet run --project tools/OptionalModAdmissionChecks -c Release` from the
repository root. This compiles the real `ArenaOptionalMods.cs` with stand-ins for
the whitelist admission, the server ping's admission failure, the shared Core
contract provider, FastGrenadeThrow and PIP capability providers, and Tarkova
Cola's persistence-protection provider. Core owns
the body-replacement adapters. No game assemblies, network requests, installed configuration or
deployment are used.

`pwsh -NoProfile -File tools/OptionalModAdmissionChecks/Verify.ps1` additionally
checks isolated production mutations: dropping or demoting the whitelist, ignoring
or demoting the server rejection, bypassing contract providers, reversing
their priority, and reintroducing a
code-level refusal of a named mod (ORBIT, ISB). Each must fail a behavioral
assertion; compilation failure does not count. Evidence stays in the ignored
`artifacts/optional-mod-admission-checks/`.

Coverage: the order of the admission chain (whitelist, then the server's reported
failure, then Core, FastGrenadeThrow, PIP and Tarkova Cola) and its short-circuiting; exact propagation of
each reason; a verdict that is rechecked rather than cached; and that none of the
mods Arena used to refuse or adapt by code (ORBIT, Questing Bots, Adaptive Bot
Culling, ISB Special Forces, Aishi, MoreBotsAPI, AI Limit, Looting Bots, Remove
The Dead, Smoothie, Late To The Party, Classic Movement, StatTrack,
Deminvincibility, Skills Extended) changes the admission result: since 2026-10-22
the whitelist (shipped default deny list and the user file) owns that decision.
The separately authorized Tarkova Cola integration protects external progression;
its own unavailable-contract rejection is explicitly covered. The current run
contains 60 assertions and eleven behavioral negative controls.

`ProductionAdmissionSource` can point to a temporary mutated production file for
negative controls. These checks establish admission behavior outside Unity, not
actual plugin loading or combined in-raid compatibility. `NativeMenuChecks` owns
the separate production menu/final-start and ordinary-raid integration checks.
