# Pickup cue

IENBA, **Game Pick Up**, CC0-1.0. Source and licence evidence, original user
excerpt identity, conversion and output hashes are in `SOURCES.json`.

2026-10-08: amplified the converted mono cue by **12 dB** (3.981x sample gain),
without altering playback volume settings. Duration remains 0.600 seconds.
Peak: 26207 / 32768, about -1.94 dBFS. No clipped samples; no compression or
limiter. The existing final 10 ms fade and ending zero sample are retained.

To repeat the gain step, supply the converted WAV identified by
`preGainSha256` to `node tools/PickupAudioChecks/amplify.mjs input.wav output.wav 12`.
The tool refuses gains that would clip, before writing the output.
