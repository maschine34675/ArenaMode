# Own-hand-grenade HUD text checks

Run `pwsh -NoProfile -File tools/SelfGrenadeTauntChecks/Verify.ps1` from the repo.
`-SptPath` overrides the installation inferred from the Development directory.

The executable compiles the complete production `ArenaSelfGrenadeTaunt`, real
projectile epoch and physical-body histories, and real administrative-death
guard. Native boundary doubles expose only required game identity/damage types
and hook registration. The actual `ArenaMatch.OnTextCallout`,
`ReportTextCallout`, and `RaiseSafe` declarations are extracted from current
production source and compiled, with observable unrelated score/audio routes.
The complete match, HUD and announcer are not replicated in this small harness.

Scenarios cover native grenade blast and physical-fragment damage sources,
registered hand throws, same-profile old bodies, old rounds, enemy/unregistered
or planted grenades, GL/mine/bleed/non-grenade damage, immediate damage/death
correlation, required death, nested scopes, finalizer exception preservation,
resets, administrative retirement, duplicate suppression, HUD consumer failure,
and text dispatch with no score/death/audio side effects. Three compiled faults
must fail targeted checks: wrong source, old body, and dispatch to combat audio.

The separate native verifier reads installed Assembly-CSharp metadata/IL using
Mono.Cecil and checks actual source wiring. It proves the hook signature and
owner path, tripwire separation, native damage types, and event hookup/teardown.
It is not Unity/Harmony runtime or raid proof. No native client/server build,
deployment, process launch, or installed file mutation occurs. The verifier
hash-binds source and installed Arena client/server/Core and keeps full build,
run and metadata logs in ignored `artifacts/self-grenade-taunt/`.
