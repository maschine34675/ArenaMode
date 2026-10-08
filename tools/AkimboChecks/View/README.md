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
