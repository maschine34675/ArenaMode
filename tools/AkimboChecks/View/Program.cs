using ArenaMode.Combat.Akimbo;
using EFT;
using EFT.AssetsManager;
using UnityEngine;
using U = UnityEngine.Object;

int checks = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
void Next() { Time.frameCount++; Time.time += .25f; }
void Pose(AkimboView view, Fixture fixture) { Next(); view.Pose(fixture.Camera.transform, new Vector3(-.3f, 0, 0), true, false); }
void Reject(Action action, string reason)
{
    bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
    Need(rejected, reason);
}

// Starting hidden/disabled renderers retain their native state, and a hidden
// original never feeds isVisible=false back into both copies.
var f = new Fixture();
f.Attachment.forceRenderingOff = true;
var nativeObjects = f.NativeObjects();
var view = new AkimboView(f.Hands);
Need(view.RendererCount == 6, "copy exactly three static renderers, omit dynamic ammunition subtree");
Need(view.Matches(f.Hands), "exact source root admitted");
Pose(view, f);
Need(view.Visible && f.Main.forceRenderingOff && f.Slide.forceRenderingOff, "native renderers hidden only while replicas visible");
Need(f.Ammo.VisibilityWrites == 0, "live cartridge renderer never leased or hidden");
var ownRoots = new[] { view.Fireport.parent.gameObject, view.RightFireport.parent.gameObject };
Need(ownRoots.Length == 2 && ownRoots.All(x => x.activeSelf), "two independent owned replica roots");
Need(ownRoots.All(x => x.GetComponent<MeshRenderer>().enabled), "hidden original leaves copies visible");
Need(ownRoots.All(x => !x.transform.Children.Single(c => c.name == "attachment").GetComponent<MeshRenderer>().enabled), "preexisting external hidden state retained in replicas");
Need(nativeObjects.All(x => !x.Destroyed), "construction and pose never destroy native objects");
view.Hide();
Need(!view.Visible && ownRoots.All(x => !x.activeSelf), "hide deactivates both owned roots");
Need(!f.Main.forceRenderingOff && !f.Slide.forceRenderingOff && f.Attachment.forceRenderingOff, "hide restores exact previously visible and hidden states");
int hideWrites = f.Main.VisibilityWrites;
view.Hide(); Need(f.Main.VisibilityWrites == hideWrites, "repeated hide does not touch already released native renderers");
Pose(view, f); f.Slide.enabled = false; Pose(view, f);
Need(ownRoots.All(x => !x.transform.Children.Single(c => c.name == "slide").GetComponent<MeshRenderer>().enabled), "native enabled flag still governs copies");
f.Main.forceRenderingOff = false;
int externallyRestored = f.Main.VisibilityWrites;
view.Dispose();
Need(f.Main.VisibilityWrites == externallyRestored && !f.Main.forceRenderingOff, "dispose preserves external early visibility release");
Need(!f.Slide.forceRenderingOff && !f.Slide.enabled && f.Attachment.forceRenderingOff, "dispose restores visibility without changing native enabled flags");
Need(ownRoots.All(x => x.Destroyed), "dispose destroys both own roots");
Need(nativeObjects.All(x => !x.Destroyed) && !f.SharedMesh.Destroyed && !f.SharedMaterial.Destroyed, "dispose never destroys native graph or shared assets");
Need(!view.Matches(f.Hands) && !view.Visible && view.Fireport == null && view.RightFireport == null, "dispose removes all published view identities");
view.Dispose(); Need(nativeObjects.All(x => !x.Destroyed), "double dispose stays own-only");

// A cartridge returned/reused while firing is intentionally outside ownership.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
var foreignPool = new GameObject("foreign pool");
f.Ammo.gameObject.transform.SetParent(foreignPool.transform, false);
f.Ammo.forceRenderingOff = false; int pooledWrites = f.Ammo.VisibilityWrites;
Pose(view, f);
Need(view.Visible && f.Ammo.VisibilityWrites == pooledWrites && !f.Ammo.forceRenderingOff, "pooled ammo can leave the weapon without further Akimbo writes");
view.Dispose(); Need(f.Ammo.VisibilityWrites == pooledWrites && !f.Ammo.gameObject.Destroyed, "cleanup does not own reused ammo either");

// A regular mod reparent is different: its formerly leased reference cannot be
// touched in a new owner's tree, even on disposal. Other valid leases release.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
f.Attachment.transform.SetParent(foreignPool.transform, false);
f.Attachment.forceRenderingOff = false; int detachedWrites = f.Attachment.VisibilityWrites;
Reject(() => Pose(view, f), "moved static model rejects stale view before another pose");
view.Dispose();
Need(f.Attachment.VisibilityWrites == detachedWrites && !f.Attachment.forceRenderingOff, "reparented renderer is never rewritten during pose or restore");
Need(!f.Main.forceRenderingOff && !f.Slide.forceRenderingOff, "valid original renderers released after neighboring graph drift");
Need(!f.Attachment.gameObject.Destroyed, "drift cleanup leaves transferred native object alive");

// Parent changes within the same weapon also invalidate fixed copied ancestry.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
f.Attachment.transform.SetParent(f.Slide.transform, false);
f.Attachment.forceRenderingOff = false; detachedWrites = f.Attachment.VisibilityWrites;
Reject(() => Pose(view, f), "same-root reparent still invalidates copied graph");
view.Dispose(); Need(f.Attachment.VisibilityWrites == detachedWrites, "same-root drift is not mistaken for owned original ancestry");

// Entire native root can be returned to pool before the controller field changes.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
f.Root.transform.SetParent(foreignPool.transform, false);
foreach (var renderer in new[] { f.Main, f.Slide, f.Attachment }) renderer.forceRenderingOff = false;
var rootWrites = new[] { f.Main.VisibilityWrites, f.Slide.VisibilityWrites, f.Attachment.VisibilityWrites };
Reject(() => Pose(view, f), "same native root reference with changed parent is rejected");
view.Dispose();
Need(rootWrites.SequenceEqual(new[] { f.Main.VisibilityWrites, f.Slide.VisibilityWrites, f.Attachment.VisibilityWrites }), "root pool transfer revokes all renderer writes including nested children");

// Destroyed Unity-style objects compare null; surviving siblings still restore.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
U.Destroy(f.Slide.gameObject);
Reject(() => Pose(view, f), "destroyed child detected without a CLR-null assumption");
view.Dispose(); Need(!f.Main.forceRenderingOff && !f.Attachment.forceRenderingOff, "surviving leases restore after native child destruction");

// Constructor failure cleans every partial replica without owning native assets.
f = new Fixture(); f.Slide.gameObject.AddComponent<SkinnedMeshRenderer>();
nativeObjects = f.NativeObjects(); int createdBefore = GameObject.All.Count;
Reject(() => new AkimboView(f.Hands), "unsupported skinned geometry rejects construction");
Need(GameObject.All.Skip(createdBefore).All(x => x.Destroyed), "partial constructor failure destroys all own roots and descendants");
Need(nativeObjects.All(x => !x.Destroyed) && !f.Main.forceRenderingOff && !f.SharedMesh.Destroyed && !f.SharedMaterial.Destroyed, "constructor failure leaves native objects and shared assets untouched");
f = new Fixture(); Shader.Available = false; createdBefore = GameObject.All.Count;
Reject(() => new AkimboView(f.Hands), "missing flash shader fails closed");
Shader.Available = true;
Need(GameObject.All.Skip(createdBefore).All(x => x.Destroyed) && !f.Main.forceRenderingOff, "shader failure also disposes partial replicas");

// Lost camera destroys only its children; Dispose still returns renderer leases.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
U.Destroy(f.Camera); view.Dispose();
Need(!f.Root.Destroyed && !f.Main.forceRenderingOff && !f.Slide.forceRenderingOff, "camera teardown still restores living native weapon");
// Auto animator: VisualPass happens before EmitEvents. Its new left-shot pose
// must not leak into the previously firing right pistol before confirmation.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
var rightSlide = view.RightFireport.parent.Children.Single(x => x.name == "slide");
var leftSlide = view.Fireport.parent.Children.Single(x => x.name == "slide");
view.Shot(false); view.CaptureAfterFireEvents(true);
Next(); f.Slide.transform.localPosition = new Vector3(0, 0, -.02f);
view.Pose(f.Camera.transform, new Vector3(-.3f, 0, 0), true, true);
Need(rightSlide.localPosition.z == 0 && leftSlide.localPosition.z == 0, "pre-event pose never samples next shot into previous side");
view.Shot(true); view.CaptureAfterFireEvents(true);
view.Pose(f.Camera.transform, new Vector3(-.3f, 0, 0), true, true);
Need(rightSlide.localPosition.z == 0 && leftSlide.localPosition.z == -.02f, "post-event sample belongs only to confirmed firing side");
view.Dispose();
// Native IK targets are world-space and may be blended/clamped. The rendered
// model must follow the actual solved hand, including later body/camera motion.
f = new Fixture(); view = new AkimboView(f.Hands); Pose(view, f);
var localGrip = new Vector3(.03f, -.06f, .02f);
var gripRotation = Quaternion.Euler(7, 11, 4);
var solved = new[] { new GameObject("left solved hand"), new GameObject("right solved hand") };
var roots = new[] { view.Fireport.parent, view.RightFireport.parent };
bool Near(Vector3 a, Vector3 b) => (a.Native - b.Native).Length() < .00001f;
for (int i = 0; i < 2; i++)
{
    var position = f.Root.transform.TransformPoint(localGrip);
    var rotation = f.Root.transform.rotation * gripRotation;
    view.Grip(i, ref position, ref rotation, false);
    solved[i].transform.SetPositionAndRotation(position + new Vector3(.15f, .01f, -.02f), rotation * Quaternion.Euler(0, 5, 0));
    view.FollowHand(i, solved[i].transform);
    Need(Near(roots[i].TransformPoint(localGrip), solved[i].transform.position), "rendered grip follows solved hand, not requested IK target");
    var before = roots[i].position;
    f.Camera.transform.position += new Vector3(.3f, 0, 0);
    Need(Near(roots[i].position, before), "late camera translation cannot drag model away from solved hand");
    solved[i].transform.position += new Vector3(.25f, -.01f, .1f);
    solved[i].transform.rotation *= Quaternion.Euler(0, 15, 0);
    Need(Near(roots[i].TransformPoint(localGrip), solved[i].transform.position), "late strafe and body rotation preserve rendered grip");
}
// Manual arms events can render first in the SAME frame. Pre-IK must still
// regenerate targets rather than feed the previously solved hands back to IK.
f.Camera.transform.position += new Vector3(.2f, 0, 0);
view.BeginIk();
view.Pose(f.Camera.transform, new Vector3(-.3f, 0, 0), true, false);
Need(roots.All(x => x.parent == null), "same-frame pre-IK must detach from earlier manual fire-event presentation");
Need(Near(roots[1].position, f.Root.transform.position), "same-frame pre-IK uses current native idle target rather than previous hand result");
view.Shot(false); Time.frameCount++; Time.time += .01f;
view.Pose(f.Camera.transform, new Vector3(-.3f, 0, 0), true, true);
Need(roots.All(x => x.parent == null), "next pre-IK pose detaches replicas from previous solved hands");
for (int i = 0; i < 2; i++)
{
    var position = Vector3.zero; var rotation = Quaternion.identity;
    view.Grip(i, ref position, ref rotation, true);
    solved[i].transform.SetPositionAndRotation(position, rotation);
    view.FollowHand(i, solved[i].transform);
    Need(Near(roots[i].TransformPoint(localGrip), solved[i].transform.position), "firing preserves independent grip registration");
}
Need(Math.Abs(roots[0].rotation.Native.X - roots[1].rotation.Native.X) > .01f, "right-only kick remains independent after hand registration");
view.Hide(); view.FollowHand(0, solved[0].transform);
Need(!view.Visible && roots.All(x => !x.gameObject.activeSelf), "late hand following cannot resurrect hidden visuals");
view.Dispose();
Need(solved.All(x => !x.Destroyed), "dispose never destroys native solved hands");

// The real aiming view is owned by the same mesh lifetime. Boundary physics
// returns deliberately unordered collisions including the shooter's own body.
Physics.Reset();
f = new Fixture(); nativeObjects = f.NativeObjects(); createdBefore = GameObject.All.Count;
view = new AkimboView(f.Hands); Pose(view, f);
var laserRoots = GameObject.All.Skip(createdBefore).Where(x => x.name.EndsWith("aim laser")).ToArray();
var beams = laserRoots.Select(x => x.GetComponent<LineRenderer>()).ToArray();
Need(laserRoots.Length == 2 && laserRoots.All(x => !x.activeSelf), "new laser lifetime starts hidden until an admitted solved pose");
var wall = new GameObject("wall").AddComponent<Collider>();
var enemy = new GameObject("enemy").AddComponent<Collider>();
var ownCollider = new GameObject("own body").AddComponent<Collider>();
ownCollider.transform.SetParent(f.HandsRoot.transform, false);
var rootCollider = f.HandsRoot.AddComponent<Collider>();
RaycastHit Hit(Collider collider, Vector3 origin, Vector3 direction, float z)
{
    float distance = (z - origin.z) / direction.z;
    return new RaycastHit { collider = collider, point = origin + direction * distance,
        normal = new Vector3(0, 0, -1), distance = distance };
}
Physics.Ray = (origin, direction, length, mask, trigger) => new[] {
    Hit(wall, origin, direction, 5f), Hit(enemy, origin, direction, 10f),
    Hit(ownCollider, origin, direction, .75f), Hit(rootCollider, origin, direction, .8f)
}.Where(x => x.distance >= 0 && x.distance <= length).ToArray();
Vector3 nativeMuzzle = f.Hands.CurrentFireport.Original.position;
Vector3 nativeOrigin = nativeMuzzle - new Vector3(0, 0, .1f);
var aimColor = new Color(.2f, 1f, .16f, 1f);
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => x.activeSelf), "admitted pistol automatically shows both aiming lasers");
Need(Near(beams[0].Points[0], view.RightFireport.position) && Near(beams[1].Points[0], view.Fireport.position), "laser starts follow both visible solved muzzles");
Need(beams.All(x => Math.Abs(x.Points[1].z - 5f) < .0001f), "nearest wall wins over enemy and own body in unsorted collisions");
Need(Physics.Rays.Count == 4 && Physics.Rays.All(x => x.Mask == 7 && x.Trigger == QueryTriggerInteraction.Collide), "bounded ballistic and drawn-segment queries include hit colliders without scene scans");
Need(laserRoots.All(x => x.layer == 0) && beams.All(x => x.useWorldSpace && x.sharedMaterial.Shader.name == "Unlit/Color"), "ordinary world depth-tested laser geometry cannot overlay walls");
Need(laserRoots.All(x => x.transform.Children.Single().gameObject.activeSelf), "surface hits show owned aim points");
Need(laserRoots.All(x => x.transform.Children.Single().GetComponent<Collider>() == null), "aim point never owns an active physics collider");
Need(beams.All(x => x.sharedMaterial.color.g == 1f) && f.SharedMaterial.color.g == 0f, "laser tint touches private material only");

var nonCombatTrigger = new GameObject("non-combat trigger").AddComponent<Collider>();
nonCombatTrigger.isTrigger = true;
enemy.isTrigger = true; enemy.gameObject.layer = 2;
Physics.Ray = (origin, direction, length, mask, trigger) => new[] {
    Hit(nonCombatTrigger, origin, direction, 2f), Hit(enemy, origin, direction, 3f), Hit(wall, origin, direction, 5f)
}.Where(x => x.distance >= 0 && x.distance <= length).ToArray();
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(beams.All(x => Math.Abs(x.Points[1].z - 3f) < .0001f), "non-combat triggers ignored while hit-collider triggers remain aiming targets");
Physics.Ray = (origin, direction, length, mask, trigger) => new[] {
    Hit(wall, origin, direction, 5f),
    new RaycastHit { collider = wall, point = origin + Vector3.forward, normal = Vector3.forward, distance = float.NaN }
}.Where(x => float.IsNaN(x.distance) || x.distance >= 0 && x.distance <= length).ToArray();
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(beams.All(x => Math.Abs(x.Points[1].z - 5f) < .0001f), "non-finite collision distance cannot replace a valid near hit");

// Fresh endpoints every solve, hide the dot on miss, never reuse a prior hit.
Physics.Reset();
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => x.activeSelf && !x.transform.Children.Single().gameObject.activeSelf), "ray miss keeps beams but removes stale aim dots");
Need(Math.Abs(beams[0].Points[1].z - (nativeOrigin.z + 100f)) < .0001f, "laser uses bounded native corrected ray instead of camera center");

// Physical visible muzzle can differ from the corrected native origin. Its
// independent occlusion cast may find an obstruction absent from the shot ray.
Physics.Reset();
Physics.Ray = (origin, direction, length, mask, trigger) => Near(origin, view.RightFireport.position)
    ? new[] { new RaycastHit { collider = wall, distance = .2f, point = origin + direction * .2f, normal = new Vector3(0, 0, -1) } }
    : Array.Empty<RaycastHit>();
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need((beams[0].Points[1] - beams[0].Points[0]).magnitude < .21f, "visible muzzle segment clips before an intervening wall");

Physics.Reset(); Physics.Line = (_, _, _, _) => true;
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => !x.activeSelf), "camera-to-muzzle obstruction hides aim aids instead of showing through cover");
Physics.Reset(); Physics.Line = (from, to, _, _) => Near(from, f.Camera.transform.position) && Near(to, nativeOrigin);
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(!laserRoots[0].activeSelf, "camera to corrected origin wall hides right aiming laser");
Physics.Reset(); Physics.Line = (from, to, _, _) => Near(from, f.Camera.transform.position) && Near(to, view.RightFireport.position);
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(!laserRoots[0].activeSelf, "camera to visible muzzle wall hides right aiming laser");
Physics.Reset(); Physics.Sphere = (_, _, _, _) => true;
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => !x.activeSelf), "muzzle inside geometry hides aim aids");
Physics.Reset(); Physics.Sphere = (at, _, _, _) => Near(at, nativeOrigin);
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(!laserRoots[0].activeSelf, "corrected origin inside geometry hides right laser");
Physics.Reset(); Physics.Sphere = (at, _, _, _) => Near(at, view.RightFireport.position);
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(!laserRoots[0].activeSelf, "visible muzzle inside geometry hides right laser");
Physics.Reset();
Physics.Ray = (origin, direction, length, mask, trigger) => Enumerable.Range(0, 16)
    .Select(_ => Hit(wall, origin, direction, 5f)).ToArray();
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => !x.activeSelf), "full unsorted hit buffer fails closed instead of missing nearer wall");
Physics.Reset();
view.Aim(f.Camera.transform.position, nativeOrigin, new Vector3(float.NaN, 0, 0), nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => !x.activeSelf) && Physics.Rays.Count == 0, "invalid shot vectors never enter physics or retain previous laser");
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
view.Hide();
Need(laserRoots.All(x => !x.activeSelf), "view hide immediately deactivates aiming aids before another callback");
Physics.Reset();
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => !x.activeSelf) && Physics.Rays.Count == 0, "hidden busy or retired view cannot resurrect its aiming aids");
Pose(view, f);
view.Aim(f.Camera.transform.position, nativeOrigin, Vector3.forward, nativeMuzzle, f.HandsRoot.transform, aimColor);
Need(laserRoots.All(x => x.activeSelf), "returning to ready reuses the same aiming aids");
f.Hands.CurrentFireport.Original.SetParent(foreignPool.transform, false);
Reject(() => view.UseFireport(f.Hands.CurrentFireport.Original), "transferred native muzzle cannot drive an old aiming view in the same frame");
var material = beams[0].sharedMaterial;
view.Dispose(); view.Dispose();
Need(laserRoots.All(x => x.Destroyed) && material.Destroyed && nativeObjects.All(x => !x.Destroyed), "laser disposal destroys only owned geometry and private material");

// The aiming helper and actual left shot share production geometry, including
// FOV-adjusted origin and fifty-metre convergence. No ammo/cadence action occurs.
Physics.Reset();
var muzzle = new Vector3(.2f, 1.5f, .6f); var origin0 = muzzle - Vector3.forward * .1f;
var leftMuzzle = muzzle + new Vector3(-.3f, 0, 0);
Need(AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, leftMuzzle, out var shifted, out var aimed), "clear left geometry admitted");
Need(Near(shifted, origin0 + new Vector3(-.3f, 0, 0)), "left shot keeps corrected origin offset");
float toConvergence = (origin0.z + 50f - shifted.z) / aimed.z;
Need(Near(shifted + aimed * toConvergence, origin0 + Vector3.forward * 50f), "left aid and projectile converge on exact native fifty metre ray");
Need(!AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, muzzle + new Vector3(-.7f, 0, 0), out _, out _), "excessive copied-muzzle separation rejects left geometry");
Physics.Line = (from, to, _, _) => Near(from, origin0) && Near(to, origin0 + new Vector3(-.3f, 0, 0));
Need(!AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, leftMuzzle, out _, out _), "wall between native and shifted origin rejects left geometry");
Physics.Reset(); Physics.Line = (from, to, _, _) => Near(from, Vector3.zero);
Need(!AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, leftMuzzle, out _, out _), "camera cover rejects left geometry");
Physics.Reset(); Physics.Line = (from, to, _, _) => Near(to, leftMuzzle);
Need(!AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, leftMuzzle, out _, out _), "shifted origin to muzzle cover rejects left geometry");
Physics.Reset(); Physics.Sphere = (at, _, _, _) => Near(at, leftMuzzle);
Need(!AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, leftMuzzle, out _, out _), "left muzzle occupancy rejects left geometry");
Physics.Reset(); Physics.Sphere = (at, _, _, _) => Near(at, origin0 + new Vector3(-.3f, 0, 0));
Need(!AkimboShotGeometry.TryLeft(Vector3.zero, origin0, Vector3.forward, muzzle, leftMuzzle, out _, out _), "left corrected origin occupancy rejects left geometry");
Physics.Reset();
Console.WriteLine($"AKIMBO_VIEW_CHECKS_PASSED ({checks} assertions)");

sealed class Fixture
{
    internal readonly GameObject HandsRoot = new("native hands"), Root = new("weapon_root"), Camera = new("camera");
    internal readonly Mesh SharedMesh = new();
    internal readonly Material SharedMaterial = new(Shader.Find("native"));
    internal readonly MeshRenderer Main, Slide, Attachment, Ammo;
    internal readonly Player.FirearmController Hands = new();
    internal Fixture()
    {
        Root.transform.SetParent(HandsRoot.transform, false);
        Root.transform.localPosition = new Vector3(.1f, -.2f, .5f);
        Main = Render(Root); Slide = Render(Child("slide", Root.transform));
        Attachment = Render(Child("attachment", Root.transform));
        var ammo = Child("temporary cartridge", Child("patron_in_weapon", Root.transform).transform);
        ammo.AddComponent<AmmoPoolObject>(); Ammo = Render(Child("cartridge mesh", ammo.transform));
        var fireport = Child("fireport", Root.transform);
        fireport.transform.localPosition = new Vector3(0, 0, .15f);
        Hands.HandsHierarchy.Root = Root.transform; Hands.CurrentFireport.Original = fireport.transform;
    }
    private MeshRenderer Render(GameObject go)
    { go.AddComponent<MeshFilter>().sharedMesh = SharedMesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = new[] { SharedMaterial }; return r; }
    private static GameObject Child(string name, Transform parent)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
    internal UnityEngine.Object[] NativeObjects()
    {
        var nodes = GameObject.All.Where(x => ReferenceEquals(x, HandsRoot) || x.transform.IsChildOf(HandsRoot.transform)).ToArray();
        return nodes.Cast<UnityEngine.Object>().Concat(nodes.SelectMany(x => x.Components)).Concat(new UnityEngine.Object[] { SharedMaterial, SharedMesh }).ToArray();
    }
}
