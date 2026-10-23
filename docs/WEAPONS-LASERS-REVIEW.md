# Automatic Arena weapon builds and visible lasers

This change set contains two separately reviewed features. Weapon variants retain
the twenty existing kit concepts and Gun Game progression, while adding weighted
compatible attachments. Laser changes fill the missing protected Tactical team
scope and add an Arena-only configurable visible intensity minimum.

## Review findings addressed

The independent weapon review found that the client preloads kit JSON while the
server session is still Armed. Generating only at native raid start would therefore
leave the new builds unused. Catalogues now freeze under the exact session lock
on the first valid Armed request and survive the native start unchanged.

The review also rejected treating arbitrary mod database category nodes as native
item-filter classes. Only verified native category identities may use ancestry;
unknown filters remain exact-template-only. Complete candidate validation checks
the actual parent slots, required parts, exclusion filters and both conflict
directions. Four stock covers have a narrow, atomic rail-and-optic exception;
custom cover state or attached children prevent that replacement.

The typed server-data test exposed a further integration issue: ordinary JSON
options cannot preserve SPT's MongoId filter/conflict values. The production
adapter now uses the initialized native SPT JSON options; the test constructs a
local instance of the same converter set without modifying global state. A stock
M4 seed also exhausted candidate attempts on conflicting optics with many mounting
paths. Those known conflicts are now rejected before weighted selection, with
the complete final validator retained.

The laser review checked native creation/rendering order, exact current Tactical
body identities, pooled objects, exception restoration and deferred point lights.
No open P0-P2 findings remained. BrightLasers' reviewed material writes do not
overwrite the submitted per-draw Arena colors. The proven previous omission was
Tactical 5v5, because team laser coloring had only admitted Team Deathmatch.

## Verification

The weapon oracle passed 22,982 production-linked assertions, its intentional
incompatible-slot mutation, and all five actual server adapter/lifecycle tests
with no skips. Each of four fixed seeds equipped all twenty shipped and installed
kits with an optic (80/80 per catalogue). In the shipped fallback Gun Game pool,
the first seed increased optic-equipped builds from 31 to 113 of 139; 82 weapon
models improved across four seeds. The live server-generated pool can differ.
Unsupported rails and already invalid authored builds remain safe fallbacks.

The laser oracle passed 1,191 replay assertions and rejected all thirteen targeted
mutations. Independent reviewers found no remaining P0-P2 issues after the fixes.
The feature-specific native contracts, negative controls and limitations are documented in
`WEAPON-VARIANTS-REVIEW.md` and `LASER-BRIGHTNESS-REVIEW.md`.

`tools/WeaponLaserIntegrationChecks/Verify.ps1` serializes both feature checks,
the server/catalogue test suite and existing tracer, TDM and Tactical regression
harnesses. `Verify-Deployment.ps1` builds the server and optional companion, then
the client last, and compares built/installed SHA-256 values including Redeploy.Core.
(`Verify-Deployment.ps1` and `ExternalFiles.ps1` were retired on 2026-10-23: their
baseline oracle `.unlazy/weapons-lasers/external-before.json` no longer matched the
install; `Verify.ps1` remains and no longer deploys during its test run.)

Both integration and deployment oracles passed on 2026-09-06. The existing
server/catalogue suite and the tracer, Give up, TDM, Tactical expansion and PFT
goal-handoff regression harnesses all passed. The Release build/deployed pairs
were compared again after the combined run:

| Assembly | Matching SHA-256 |
| --- | --- |
| ArenaMode.Client | FEB7B382E3753AC632460A230F680E72BDA273E4F783B0BAB79880C13BA01045 |
| ArenaMode.Server | 7820730881BFC03323760FD470EC47BC57200DB4332EBFDBF9681A8DC40A810B |
| ArenaMode.BigBrainCompat | 13A3A294895562CAD5443B681CC01DB9E4FDE3507D140AB054546D4C543FD432 |
| Redeploy.Core | 3973761FDE09BBDBBD924AA893F3D351ECA50EFE14B93183A6E1C8CE947BAA8C |

Feature commits: `f6cfcc6` (lasers) and `cace78e` (weapon variants). This report
and the shared verification scripts form a separate verification commit.

An initial read-only baseline covered 158 external mod/config/catalogue files.
During implementation, before deployment, the BrightLasers client/server DLLs and
their two LICENSE files were independently removed from the live installation.
No agent removed or restored them. Their original hashes and observed absence
were recorded separately; the remaining 154 files matched the initial baseline
both immediately before and after deployment. This is not a claim that the live
installation stayed wholly unchanged throughout the task.

## GPU handoff

Restart both server and game. Check the generated kit sights, mounts and handling,
Gun Game transitions/redraws, and visible team lasers before and after a Tactical
round rebind. `Effects/VisibleLaserBrightness` defaults to 2; 1 disables Arena's
addition. With BrightLasers present, an already larger intensity is retained
instead of multiplied again. Ordinary raids, IR and flashlights stay outside the
effect. Actual Unity appearance, aiming/eye relief, and gameplay remain untested
because this host has no GPU.
