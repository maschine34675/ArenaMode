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

`import_assets.py` is explicit asset intake only: it downloads two creator
archives, preserves their complete original license notices and converts just
the three selected originals via FFmpeg. It is not part of verification/builds.
