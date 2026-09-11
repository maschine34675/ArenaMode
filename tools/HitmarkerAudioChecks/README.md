# Hitmarker audio checks

From the repository root:

```powershell
python tools/HitmarkerAudioChecks/verify_assets.py
pwsh -NoProfile -File tools/HitmarkerAudioChecks/Verify.ps1
pwsh -NoProfile -File tools/DoubleJumpChecks/Build-Staged.ps1
```

The first command decodes the actual PCM WAVs and checks signal, format, limits
and provenance/license/output hashes. The second compiles the real service,
PCM parser and shared owned Master output with the announcer's existing Unity
stand-ins, then runs the existing announcer regression/fault checks. The last
compiles the actual client against installed EFT without deploying.

The parser's negative cases corrupt actual bundled WAV copies, paired with
successful parsing/rebuild of all three unmodified WAVs. Native damage/health
and GPU output remain outside this harness. See HITMARKER-AUDIO-REVIEW.md.

`import_assets.py` is explicit asset intake only. Pass the downloaded Freesound
570335 original WAV as its positional argument. It verifies the reviewed source
hash, averages stereo channels, converts to 44.1 kHz mono PCM16 via FFmpeg and
writes identical hit/headshot/kill WAVs plus their source and license hashes.
No normalization, trimming, fades or pitch changes. The unchanged bundled CC0
legal text is retained. No network access; not part of verification/builds.
