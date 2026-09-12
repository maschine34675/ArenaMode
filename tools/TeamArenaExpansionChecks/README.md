# Team Arena expansion checks

Offline checks for protected 5v5 expansion policies and integration contracts.
They exercise best-of-N scoring and spectator policy directly, then inspect the
camera lifecycle, post-death pursuit, raid-leased opponent spawning, exact PFT
rebuild sequence, prepared PMC respawn authorization, and damage guards without
loading EFT or optional mods.

```powershell
dotnet run --project tools/TeamArenaExpansionChecks -c Release
```

## Current announcer contract (2026-09-12)

The old check stopped after 277 assertions because it required the replaced
WARLORD pack's CC wording. Current announcers use ANNOUNCER-SOURCES.json;
historical CC texts must not be treated as the replacement's license.
The revised asset check retains all production cue mappings and requires the
current 18 active / 28 reserve manifest, six reviewed lifecycle labels, safe
unique file paths, matching nonempty file hashes and no reserve event bindings.
Provenance fields, HTTPS references and source-fingerprint format must be present.
This verifies records, not source ownership, original-source hash, legal rights
or spoken content. In particular, a structurally valid source fingerprint is
not independently matched to the original supplied recording by this harness.

Thirteen in-memory negative controls reject absent provenance, bad fingerprint/
URL, wrong file hash, path escape, duplicate/missing cue, event/file mismatch,
unreviewed round-won label, active reserve file and reserve event. No original
manifest/audio file is changed; the original passes again afterward.
Root and independent complete runs: **774 assertions**, including later installed
PFT/SAIN contracts that the old early failure had prevented from running.

For actual codec/PCM/metadata validation also run the existing, read-only
`python tools/AnnouncerChecks/verify_creator_pack.py`. The 2026-09-12 root run
passed all **46 clips** and the optional deployment comparison matched **61 asset
files**. No native listening or new redistribution license is inferred.
