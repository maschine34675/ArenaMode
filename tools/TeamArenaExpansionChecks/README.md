# Team Arena expansion checks

Offline checks for protected 5v5 expansion policies and integration contracts.
They exercise best-of-N scoring and spectator policy directly, then inspect the
camera lifecycle, post-death pursuit, raid-leased opponent spawning, exact PFT
rebuild sequence, prepared PMC respawn authorization, and damage guards without
loading EFT or optional mods.

```powershell
dotnet run --project tools/TeamArenaExpansionChecks -c Release
```

## Current announcer contract (2026-09-14)

The old check stopped after 277 assertions because it required the replaced
WARLORD pack's CC wording. Current announcers use ANNOUNCER-SOURCES.json;
historical CC texts must not be treated as the replacement's license.
The asset check retains the original intake classification of 18 active / 28
reserve clips. The original `eventKey` remains empty on all reserve rows; exactly
19 reviewed reserve paths now require their own matching `runtimeEventKey`.
Four alternate result recordings and five CTF clips remain unbound. An independent
allowlist verifies these counts and mappings, six original reviewed lifecycle
labels, the 19 added spoken labels, safe unique paths and nonempty file hashes.
The production catalog is linked directly: it must load exactly the 18 original
plus 19 optional paths, and its 25 lifecycle event mappings must match the
independently declared expected keys. Alternate result basenames cannot replace
the active result recordings.
Provenance fields, HTTPS references and source-fingerprint format must be present.
This verifies records, not source ownership, original-source hash, legal rights
or spoken content. In particular, a structurally valid source fingerprint is
not independently matched to the original supplied recording by this harness.

The thirteen original in-memory controls remain: absent provenance, bad fingerprint/
URL, wrong file hash, path escape, duplicate/missing cue, event/file mismatch,
unreviewed round-won label, active reserve file and reserve event. Eighteen new
manifest controls reject missing/wrong/empty/duplicate runtime mappings, every
alternate result and CTF binding, active overrides, a wrong optional hash or
spoken label, and a changed intake classification. Four loader controls reject
duplicate paths, alternate result substitution, recursive CTF inclusion and a
missing optional clip. These **31 manifest + 4 loader controls** mutate only
in-memory copies; no original manifest or audio file is changed.

The complete current harness passes **830 assertions**, including the existing
installed PFT and SAIN contracts. This is offline evidence; no native listening
or new redistribution license is inferred.

For actual codec/PCM/metadata validation also run the existing, read-only
`python tools/AnnouncerChecks/verify_creator_pack.py`. Codec validation and
deployment verification remain separate from this contract harness.
