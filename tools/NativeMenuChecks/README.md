# Native Arena menu checks

Run from the repository root:

```powershell
dotnet run --project tools/NativeMenuChecks/NativeMenuChecks.csproj -c Release
```

The harness links the production menu/session code and uses small EFT and SPT
stand-ins. Alongside Arena selection and protected raid-lifecycle behavior, it
checks that the periodic client-settings read uses SPT's authenticated low-level
HTTP client without passing through the noisy `RequestHandler` wrapper. Empty
success responses and real transport failures remain visible in Arena diagnostics.

`Verify-QuietSettingsTransport.ps1` runs a temporary source mutant that restores
`RequestHandler.GetJson` for the periodic read. The matching assertion must fail;
the script then reruns the production checks and verifies that the production
source hash did not change. Actual BepInEx log output still requires a game
restart and a live menu session.
