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
var ownRoots = f.Camera.transform.Children.Select(x => x.gameObject).ToArray();
Need(ownRoots.Length == 2 && ownRoots.All(x => x.activeSelf), "two own replica roots under camera");
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
