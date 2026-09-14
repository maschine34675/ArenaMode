# Own-grenade text and announcer delivery (2026-09-14)

The user authorized deployment of the complete announcer branch after adding a
text-only mocking own-grenade death message; parallel respawn work is paused.

`NICE THROW, GENIUS!` appears through a separate HUD-only event. No speech key is
submitted, including if a similarly named custom sound exists. Existing score,
killfeed, First Blood, death, respawn and profile rules do not change. The native
kill capture keeps its original self-kill early return. The text only observes
that path and has independent weak per-body deduplication.

## Provenance and immediate damage

The hand-controller `CreateAndRegisterGrenade` prefix observes the actual local
physical thrower and grenade item after successful throw admission, before a
prewarmed grenade can explode. Both normal and quick grenade controllers use the
native base method. Tripwire placement bypasses it. `Grenade.Init` would only
provide a profile ID, which is insufficient across same-profile respawns.

Native grenade blast damage is `GrenadeFragment` with the grenade template as
`SourceId`. Flying physical fragments are `Bullet` damage with `FragmentType`
as their ammunition source. Both retain the original `ThrowWeap` as weapon.
Only that exact registered item, thrower's physical body and current award-round
epoch qualify. Weak origin values retain only scalar IDs/template strings.

An unnested immediate `Player.ApplyDamageInfo` scope must match the lethal event's
exact current local victim/attacker, weapon item, damage type, source and fire
index. The body must actually be dead, and the administrative-death gate must be
clear. No unknown/projectile-only explosion, mine, planted tripwire, enemy grenade,
old-body grenade, delayed damage or later bleed death can reuse a cached attacker.
Raid reset and finalizers clear scopes; the existing round epoch invalidates old
grenades without changing mode lifecycle. No additional death/kill event is sent.

## Verification and deployment boundary

Run `tools/SelfGrenadeTauntChecks/Verify.ps1` for the production-linked native
observer and HUD-only event checks, including compiled negative controls.
Independent review found no blocking defect. Parent verification passed 74
production/text-dispatch assertions, 35 native/source contracts and three
compiled faults (wrong source, old physical body and accidental audio routing),
then reran production successfully with unchanged source and installed binaries.
These boundary doubles and native metadata checks are not a live HUD/raid test.
The native client is built using `tools/DoubleJumpChecks/Build-Staged.ps1`, which
uses the installed Core API without rebuilding or deploying Redeploy.

`tools/AnnouncerDelivery/Deploy-Staged.ps1` accepts only a clean current master
build with matching version/hash and unchanged installed dependencies. It backs
up and copies only the client DLL plus ANNOUNCER-PACK.md, ANNOUNCER-SOURCES.json
and AUDIO-LICENSES.md. The 46 installed OGGs are already byte-identical; no audio,
licence normalization, map assets, user drafts, Core or server are overwritten.
The script verifies all other inventoried plugin DLLs and Arena package files.

The GPU mirror receives the files through the existing synchronization. Local D:
hash checks do not prove arrival on C: or that a fresh game process loaded them.
After restarting EFT, the user should test a lethal own grenade, an enemy grenade
and a later bleed death, then repeat after a respawn. Verify that the mocking text
has no associated sound and the previously added cues and shot awards are audible.
No server restart is needed for this client-only update.
