# Arena start error checks

Run `pwsh -NoProfile -File tools/ArenaStartErrorChecks/Verify.ps1` from the repository.
It compiles the real `ArenaServerClient` and `ArenaLocalRaidStartedPatch` against
the existing `NativeMenuChecks` game/transport stand-ins and installed Newtonsoft.Json.
No plugin is built or deployed, and no HTTP request reaches a real server.

The checks cover the server refusal's actual reason, absent/non-string reasons,
malformed responses, transport failures, token mismatches, clearing the reason on
success, invalid local settings, profile-protection cancellation and ordinary
raid pass-through. Eleven separately compiled negative controls must fail their
specific behavioral assertions, not a compile error.

The shared `ColaArmChecks` exercises optional baseline transport, accepted-arm
ordering after the exact server acknowledgement/native identity, backend profile,
frozen mode and current epoch. Refused/wrong-token arms never prepare a lease;
failures before/after acceptance retain cancellation and cleanup behavior.
Ordinary PMC/Scav starts and an absent optional adapter stay unchanged. Five new
mutants remove preparation, substitute profile/mode/epoch, or skip failed-arm
cleanup; the original six refusal/error mutants remain unchanged. The adapter's
reflection and persistence are external to this harness; its delegates are only
boundary observations, and the session stand-in mirrors the production EndRaid
call during ClearArmed.

EFT build 40743's `EFT.UI.PreloaderUI.ShowErrorScreen(string, Exception, Action)`
starts with `exception.Message` and appends each inner exception's message. The
harness applies that same traversal to the faulted task returned by Arena's real
prefix. It confirms the context and cause each appear once while retaining the
inner exception. This is not a Unity layout or in-game acceptance test.
