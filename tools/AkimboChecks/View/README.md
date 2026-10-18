# Akimbo view ownership checks

`dotnet run --project tools/AkimboChecks/View/ViewChecks.csproj`

Links the production `AkimboView.cs` and `AkimboPresentation.cs`; Unity/EFT boundary
doubles do not implement view decisions. Checks static renderer visibility leases,
dynamic ammunition exclusion, external visibility changes, direct/ancestor
reparenting, destroyed native children, partial construction, camera loss, repeated
cleanup, and destruction restricted to owned replicas/materials.
Movement cases use translated/rotated solved hands, independent camera movement,
blended IK targets and a Manual-arms same-frame event-before-IK sequence. They
check model/grip registration and independent recoil after reattachment. Removing
BeginIk invalidation or restoring camera parenting must fail those cases.

This is not a renderer, shader, animation, Unity player-loop or IK acceptance test.
Destruction is immediate in this double; native end-of-frame ordering and the
actual visual pose remain GPU gates.

`-p:ViewSource=<absolute alternate source>` permits negative controls without
changing production. Removing the dynamic AmmoPoolObject exclusion must fail;
removing parent validation must fail; removing renderer restoration must fail.

The same harness now links `AkimboAimLaser` and `AkimboShotGeometry`. Physics
boundary doubles supply unordered wall/body/trigger collisions, camera and
muzzle occlusion, missing hits and buffer overflow; production code chooses the
geometry, lifetime and closest valid endpoint. `Check-LaserMutants.ps1` verifies
17 behavioral faults and separately compiles the production `AkimboNativeAim`
against installed EFT/Unity assemblies. Its native API fault restores the old
forbidden `Player.transform` access and must fail with CS0619. This extra compile
contract catches API drift that the view doubles cannot see.

Run the complete regression with `pwsh -NoProfile -File
tools/AkimboChecks/Verify.ps1`. Laser evidence manifests and individual fault
logs are saved under `artifacts/akimbo-laser-checks/<run>/`.
