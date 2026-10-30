# Direct Arena start

## Usage

Open `BepInEx/plugins/maschine-ArenaMode/maschine-ArenaMode.Session.exe`, select an
initialized SPT profile, and click **Start Arena**. **Desktop shortcut** creates an
`ArenaMode` shortcut. The last selected profile is remembered per installation.
Cancel closes the picker without starting either SPT or EFT.

For a fixed-profile shortcut, use `start --profile <24-character profile ID>`.
No arguments and `start` open the picker. Do not reuse an old `request.json`.

The supported setup is local SPT, using `127.0.0.1` or `::1`, with EFT closed.
Remote servers, proxy/port translations and Fika are refused before handover.
Create new characters through the SPT Launcher first. Broken, wiped and
uninitialized profiles are omitted from the picker.

## Handover

The helper resolves the installation from its own path, verifies the installed
gate/game/server and reads profiles without blocking the normal server's writes
or atomic file replacement. It remembers only a profile ID, never credentials.

It reuses the verified local normal server or starts it briefly, obtains a fresh
preparation grant over certificate-pinned loopback HTTPS, then uses the existing
[offline session transfer](ARENA-OFFLINE-SESSION-20261025.md). The normal server
saves and exits before the private profile is copied. EFT starts only after the
private server owns the session. RETURN TO SPT retains its existing behavior.

The direct path is internal schema 3 with no previous game PID; external wire
requests still require their existing game identity. Existing games, ambiguous
server processes, occupied ports and changed mod policy refuse the start.
Recognized mod version differences remain advisory. No legacy authority fallback
or private-profile copy-back is introduced.

Installations of one Windows user share the helper's session folder. A finished Arena session of another
installation does not block the start; one that is not closed there is refused before preparation, naming that
installation, and nothing is rebuilt over its unreturned results. Only a running Arena server of another
installation is reported as running.

The picker stays responsive while preparation runs. It cannot start a second
handover or close mid-transfer. Failure notices appear after helper locks are
released. A refused direct start does not unexpectedly launch normal EFT.

## Verification

Reproduce automated checks with `pwsh -NoProfile -File tools/Verify-DirectStart.ps1`.
The script builds without deployment, runs the unit suite and .NET Framework
desktop checks, then verifies the existing client/helper switching contracts.

The final unit run passed 3348 tests, with three existing environment-dependent
skips (3351 total). The shipping .NET Framework helper passed eight desktop
checks, including shortcut target, foreign-shortcut preservation and repeat use.
The Release solution built with zero errors. Existing switching checks cover the
750-assertion client baseline, three offline-entry mutations, 174 client-contract
mutations and six helper mutations; results are under the ignored
`artifacts/direct-start-20261028/verification/` folder.

On GPU2 (`C:\SPT41`, Windows user Service), the final helper completed these real
starts and RETURN TO SPT transitions, each reaching MainMenu:

| Start | Private session | Result |
| --- | --- | --- |
| SPT and EFT stopped; explicit profile | `fdbc7896813e45729d7d5a4c6824ff8a` | Arena and normal return passed |
| Normal server already running; explicit profile | `51be0befbcf24c30a1945ee9a6decbec` | Reused the server, Arena and return passed |
| Normal server already running; remembered picker selection | `44d311ee5ca24d84a6337f3a49381a4d` | Arena and return passed |

The picker, actual desktop-shortcut launch and cancellation were also exercised.
The client reported all 16 compared modules matching the installed files.
All three final sessions have activation/completion/closure evidence, and the
original active-session pointer is absent. Full selected-profile comparisons
after the first two returns differ only at the normal SPT Health.UpdateTime and
Hideout.sptUpdateLastRunTimestamp fields. All 238 Arena config values are equal;
BepInEx rewrote only localized comments. No profile was manually repaired or
replaced. These are launch/menu/return tests, not a new gameplay or raid-end test.

An earlier normal server process returned two `ProtocolError` responses to entry
requests, including one attempt with Syncthing paused. Their exact HTTP status
and rejection stage were not captured. The
helper preserved that server and did not copy a profile. After a graceful server
restart with the diagnostic build, the final repeated tests above passed. An
independent decompilation comparison found no functional auth difference in the
old DLL; the earlier process's loaded assembly was not captured. The original
cause remains unproven. New fixed Debug codes distinguish admission, request
parsing and individual authorization failures without logging capabilities or
request contents. Existing native-save diagnostics and protection remain intact.

Only GPU2's SPT41 Syncthing folder was paused for isolation, from 09:57:39 to
10:17:46 UTC, then restored to its original unpaused state. EFT, both SPT servers
and helpers were stopped afterward. User documentation changes were preserved.

Final files deployed locally and on GPU2:

| File | SHA-256 |
| --- | --- |
| `maschine-ArenaMode.Session.exe` | `2D8C7321367A4464A2DF08833A8080F53037EAFADE6214E2738E6B7B12EA9C09` |
| `maschine-ArenaMode.Server.dll` | `19FDE5869D95CEDFD300A090748613C05F3B61CC53873B7E50801680E679052A` |

The local desktop shortcut targets the installed helper with `start`; it carries
no profile token. Detailed native evidence is in the ignored
`artifacts/direct-start-20261028/native/` folder.
