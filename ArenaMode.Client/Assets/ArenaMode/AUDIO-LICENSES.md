# ArenaMode announcer audio

## Attribution and license

The six recordings in `sounds/` are from **Announcer kill sounds**:

- Voice recording: **Antti Saari**.
- Original audio editing and pack publication: **TripleSnail**.
- Source: <https://opengameart.org/content/announcer-kill-sounds>.
- Original attribution: "Recorded by Antti Saari Edited by TripleSnail".
- License: **Creative Commons Attribution 3.0 Unported (CC BY 3.0)**.
- License URL: <https://creativecommons.org/licenses/by/3.0/>.
- Full license text: [CC-BY-3.0.txt](CC-BY-3.0.txt).
- Source/license checked: 2026-08-31.

These audio assets retain their CC BY 3.0 license independently of the mod's
code. Keep this attribution and the license with redistributed copies. The
license permits redistribution and adaptations subject to its terms, including
attribution, retention of notices, identification of adaptations, and no
additional restrictions on the licensed audio. No endorsement by the original
creators is implied. The assets are supplied without warranties; see the license.

## Files and modifications

For ArenaMode, six of the user-supplied original WAV files were converted to
Ogg Vorbis and renamed to match the existing callout keys. Encoding used
FFmpeg 8.1.2, `libvorbis`, quality `5`. Original sample rates, channel counts,
duration and volume were retained; no trimming, normalization, pitch shifting,
splicing or additional effects were applied. The lossy format conversion changes
the encoded signal. Attribution/source/license metadata was added to each OGG.

| Original file | Bundled file | Existing callout |
| --- | --- | --- |
| DoubleKill.wav | sounds/double_kill.ogg | DOUBLE KILL |
| TripleKill.wav | sounds/triple_kill.ogg | TRIPLE KILL |
| KillingSpree.wav | sounds/killing_spree.ogg | KILLING SPREE |
| Rampage.wav | sounds/rampage.ogg | RAMPAGE |
| Dominating.wav | sounds/dominating.ogg | DOMINATING |
| Godlike.wav | sounds/godlike.ogg | GODLIKE |

The original pack does **not** contain MULTI KILL, MONSTER KILL or UNSTOPPABLE.
Those three callouts still appear in the HUD but have no bundled sound. Other
recordings in the original pack are not included because the current mod does
not emit those labels. Scoring, streak thresholds and callout names are unchanged.

Original WAV SHA-256 checksums, for identifying the conversion inputs:

| Original file | SHA-256 |
| --- | --- |
| DoubleKill.wav | 98B8102E7A63C2D4B03DC96EC89F358AE06799CCF7C0835889DA25BC1EF2CF7E |
| TripleKill.wav | 7A1C2A11B9122A5BAE31B80BD7F5ADA6281E2A1868B71A3F1D43B383725BBE82 |
| KillingSpree.wav | 14DC8580BFF409D90CE2FD030E5A656BF78827DA9633777A3BB5A1B2D99B5FA6 |
| Rampage.wav | 51E9DA4F671186B458DD49321DC2A18AFA2FAD7D958ECE533308520A76ED9C46 |
| Dominating.wav | 3B07A0FC72000E5357218970CED7E4BBB71EB2353C0E8D4371E341CB37B73D43 |
| Godlike.wav | FC6CA171E20BF84DEE0DE82A7BDA1F91C8F1005F7DB12C3D9D84ACB232751B09 |

## Installation and playback

Build and publish output includes the `ArenaMode` directory beside the client
DLL. The local build also copies this directory to `BepInEx/plugins/ArenaMode/`.
Release packages must include the whole directory with these license files,
not just the client DLL or the OGG files alone.

Restart the game after installing or replacing audio. In the F12 configuration,
`Audio / Announcer` enables playback and `Audio / AnnouncerVolume` controls its
volume. Playback also respects Tarkov's Interface volume. Only arena matches
subscribe to the callouts; normal raids do not use this announcer.

Clips load asynchronously at the first arena match. A callout that occurs before
its clip has finished loading is skipped, without queueing a stale announcement.
Once loaded, clips remain available for subsequent arena matches in that process.
Each match rechecks the cache and retries missing or unusable recordings. Invalid
clips encountered during a match can also recover, with at least ten seconds
between retry attempts. A recovery does not replay the missed announcement.

Client logs include match attachment, loading results, and a summary of received,
requested and skipped callouts. Each skip reason is logged once per clip and match.
The first three playback requests, and later changes in output settings, include
the Interface/master mixer levels and listener state. A logged playback request
means that audio was submitted to the native UI sound system, not that it was
audible on the player's device. These diagnostics do not change mixer settings.

The headless lifecycle regression is runnable from the repository root:

```powershell
dotnet run --project tools/AnnouncerChecks -c Release
```

It uses the real AnnouncerService with controlled game/audio stand-ins. It does
not replace an in-game listening test. Playback, mixer balance and subjective
voice quality still require a GPU-capable game client.
