using System.Reflection;
using ArenaMode.Combat.Fire;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using UnityEngine;

internal static class Program
{
    private static int _count;
    private static void Check(bool condition, string name) { _count++; if (!condition) throw new Exception("FAILED: " + name); }
    private static bool Near(float a,float b)=>Math.Abs(a-b)<.00001f;
    private sealed record Fixture(GameObject Root, GrenadePrefab Pool, MeshFilter Body, MeshRenderer Renderer,
        Mesh Original, Material Material, MeshRenderer Pin, MeshRenderer AlreadyHidden);
    private static Fixture ItemModel()
    {
        var root = new GameObject("native container"); var pool = root.AddComponent<GrenadePrefab>();
        var hierarchy = new GameObject("native rig"); hierarchy.transform.SetParent(root.transform); pool.Hierarchy = hierarchy.AddComponent<TransformLinks>();
        var anchor = new GameObject("weapon_rgd5"); anchor.transform.SetParent(hierarchy.transform);
        var body = new GameObject("weapon_grenade_rgd5_LOD0"); body.transform.SetParent(anchor.transform);
        var filter = body.AddComponent<MeshFilter>(); var renderer = body.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name="native mesh", bounds=new Bounds { center=new Vector3(.000428f,-.000142f,.04753137f) } }; filter.sharedMesh=mesh;
        var material = new Material(Shader.Find("native")); renderer.sharedMaterials=new[]{material};
        var pin=new GameObject("rgd5_pin_LOD0"); pin.transform.SetParent(anchor.transform); var pinRenderer=pin.AddComponent<MeshRenderer>();
        var hidden=new GameObject("inactive tripwire component"); hidden.transform.SetParent(anchor.transform); var hiddenRenderer=hidden.AddComponent<MeshRenderer>(); hiddenRenderer.enabled=false;
        return new(root,pool,filter,renderer,mesh,material,pinRenderer,hiddenRenderer);
    }
    private static async Task Main(string[] args)
    {
        ArenaMolotovModel.Enable();
        var custom = new Item { TemplateId = ArenaMolotovItem.TemplateId };
        var donor = new Item { TemplateId = ArenaMolotovItem.DonorTemplateId };
        var f = ItemModel(); var task=Task.FromResult(f.Root);
        ArenaSession.IsActive=false; ArenaSession.ArmedForNextRaid=false;
        Check(ReferenceEquals(ArenaMolotovModel.Wrap(task,custom),task),"ordinary request untouched");
        ArenaSession.IsActive=true;
        Check(ReferenceEquals(ArenaMolotovModel.Wrap(task,donor),task),"donor request untouched");
        Check(ReferenceEquals(ArenaMolotovModel.Wrap(task,null),task),"null item untouched");
        Check(ArenaMolotovModel.Wrap(null,custom)==null,"null native task untouched");
        ArenaFireSupply.HasVerifiedTemplate=false;
        Check(ReferenceEquals(ArenaMolotovModel.Wrap(task,custom),task)&&f.Body.sharedMesh==f.Original,"unverified private-ID model untouched");
        ArenaFireSupply.HasVerifiedTemplate=true;
        await ArenaMolotovModel.Wrap(task,custom);
        var owned=f.Body.sharedMesh; var materials=f.Renderer.sharedMaterials;
        Check(owned!=f.Original,"custom body has own geometry");
        Check(f.Original.name=="native mesh" && !f.Original.Destroyed,"shared native mesh untouched");
        Check(materials.Length==3 && materials.All(x=>x!=f.Material),"own materials only");
        Check(!f.Pin.enabled && !f.AlreadyHidden.enabled,"native pins hidden");
        Check(owned.subMeshCount==3,"three original surfaces");
        Check(owned.Vertices.Count<500 && owned.Vertices.Count>300,"bounded model vertex cost");
        Check(owned.Vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"finite model vertices");
        Check(owned.Uv.Count==owned.Vertices.Count,"complete UV stream");
        foreach(var t in owned.Triangles.Values) Check(t.Count%3==0 && t.All(x=>x>=0&&x<owned.Vertices.Count),"valid triangle indices");
        Check(owned.bounds.extents.x<.04f && owned.bounds.extents.z>.1f && owned.bounds.extents.z<.12f,"bottle size sane");
        await ArenaMolotovModel.Wrap(task,custom);
        Check(f.Body.sharedMesh==owned,"decoration idempotent");
        ArenaSession.IsActive=false;
        ArenaMolotovModel.Restore(f.Pool);
        Check(f.Body.sharedMesh==f.Original,"pool mesh restored without active arena");
        Check(f.Renderer.sharedMaterials.Length==1 && f.Renderer.sharedMaterials[0]==f.Material,"pool materials restored");
        Check(f.Pin.enabled && !f.AlreadyHidden.enabled,"original pin visibility restored");
        Check(owned.Destroyed && materials.All(x=>x.Destroyed),"only owned resources destroyed");
        Check(!f.Original.Destroyed && !f.Material.Destroyed,"donor assets survive return");
        ArenaMolotovModel.Restore(f.Pool);
        Check(f.Body.sharedMesh==f.Original,"return idempotent");
        await ArenaMolotovModel.Wrap(task,donor);
        Check(f.Body.sharedMesh==f.Original && f.Pin.enabled,"ordinary donor after custom reuse");
        ArenaSession.ArmedForNextRaid=true;
        await ArenaMolotovModel.Wrap(task,custom);
        Check(f.Body.sharedMesh!=f.Original,"armed icon models decorated");
        InvokeHook("PoolGetPatch",f.Pool);
        Check(f.Body.sharedMesh==f.Original,"get hook repairs skipped return");
        f.Pool.IsInPool=true;
        await ArenaMolotovModel.Wrap(task,custom);
        Check(f.Body.sharedMesh==f.Original,"role model remains untouched");
        f.Pool.IsInPool=false;f.Pool.IsStub=true;
        await ArenaMolotovModel.Wrap(task,custom);
        Check(f.Body.sharedMesh==f.Original,"stub remains untouched");
        f.Pool.IsStub=false;
        var pending = new TaskCompletionSource<GameObject>(); var wrapped = ArenaMolotovModel.Wrap(pending.Task,custom);
        ArenaSession.ArmedForNextRaid=false; pending.SetResult(f.Root); await wrapped;
        Check(f.Body.sharedMesh==f.Original,"late completion after disarm untouched");
        ArenaSession.IsActive=true;
        var err=new InvalidOperationException("native error");
        try { await ArenaMolotovModel.Wrap(Task.FromException<GameObject>(err),custom); Check(false,"native failure preserved"); }
        catch(InvalidOperationException seen) { Check(ReferenceEquals(seen,err),"native failure preserved"); }
        try { await ArenaMolotovModel.Wrap(Task.FromCanceled<GameObject>(new CancellationToken(true)),custom); Check(false,"native cancellation preserved"); }
        catch(TaskCanceledException) { Check(true,"native cancellation preserved"); }
        Check(await ArenaMolotovModel.Wrap(Task.FromResult<GameObject>(null),custom)==null,"native null preserved");
        Shader.Missing=true; await ArenaMolotovModel.Wrap(task,custom); Shader.Missing=false;
        Check(f.Body.sharedMesh==f.Original && f.Renderer.sharedMaterials[0]==f.Material,"presentation failure restores native model");
        Check(!f.Root.GetComponent<ArenaMolotovModelLease>().Active,"failed decoration releases own geometry");
        await ArenaMolotovModel.Wrap(task,custom);
        owned=f.Body.sharedMesh;materials=f.Renderer.sharedMaterials;
        f.Body.FailNextAssignment=true;InvokeHook("PoolReturnPatch",f.Pool);
        Check(f.Body.sharedMesh==owned && !owned.Destroyed,"failed restore does not break native pool hook or destroy bound mesh");
        InvokeHook("PoolGetPatch",f.Pool);
        Check(f.Body.sharedMesh==f.Original && owned.Destroyed && materials.All(x=>x.Destroyed),"pool get retries failed restoration");
        await ArenaMolotovModel.Wrap(task,custom);
        materials=f.Renderer.sharedMaterials;
        var partial=new Material(Shader.Find("partial replacement"));f.Renderer.sharedMaterials=new[]{materials[0],partial,materials[2]};
        InvokeHook("PoolReturnPatch",f.Pool);
        Check(f.Renderer.sharedMaterials[1]==partial && !partial.Destroyed,"foreign material slot preserved");
        Check(f.Renderer.sharedMaterials.All(x=>!materials.Contains(x)) && materials.All(x=>x.Destroyed),"partially replaced material array releases only unbound owned assets");
        f.Renderer.sharedMaterials=new[]{f.Material};
        await ArenaMolotovModel.Wrap(task,custom);
        var replacement=new Mesh();f.Body.sharedMesh=replacement;
        var replacementMat=new Material(Shader.Find("other mod"));f.Renderer.sharedMaterials=new[]{replacementMat};
        InvokeHook("PoolReturnPatch",f.Pool);
        Check(f.Body.sharedMesh==replacement && f.Renderer.sharedMaterials[0]==replacementMat,"later third party replacement preserved");
        f.Body.sharedMesh=f.Original;f.Renderer.sharedMaterials=new[]{f.Material};
        await ArenaMolotovModel.Wrap(task,custom);owned=f.Body.sharedMesh;materials=f.Renderer.sharedMaterials;
        UnityEngine.Object.Destroy(f.Root);
        Check(owned.Destroyed && materials.All(x=>x.Destroyed),"destroy releases owned resources");

        var projectile=new GameObject("projectile");var grenade=projectile.AddComponent<Grenade>();grenade.WeaponSource=donor;
        var world=new GameObject("weapon_rgd5_world(Clone)");world.transform.SetParent(projectile.transform);
        var worldFilter=world.AddComponent<MeshFilter>();var worldRenderer=world.AddComponent<MeshRenderer>();
        var worldOriginal=new Mesh { bounds=new Bounds { center=new Vector3(0,-.392135f,.019277f) } }; worldFilter.sharedMesh=worldOriginal;
        worldRenderer.sharedMaterials=new[]{f.Material};
        ArenaMolotovModel.ApplyThrown(grenade);Check(worldFilter.sharedMesh==worldOriginal,"ordinary thrown donor untouched");
        grenade.WeaponSource=custom;ArenaMolotovModel.ApplyThrown(grenade);
        Check(worldFilter.sharedMesh!=worldOriginal,"custom thrown clone decorated");
        var meshZero=ArenaMolotovModelLease.BottleMesh(new Vector3());
        var dv=worldFilter.sharedMesh.Vertices[0]-meshZero.Vertices[0];
        Check(Near(dv.x,0)&&Near(dv.y,-.392135f)&&Near(dv.z,.019277f-.04753137f),"world baked mesh offset preserved");
        owned=worldFilter.sharedMesh;materials=worldRenderer.sharedMaterials;
        UnityEngine.Object.Destroy(projectile);
        Check(owned.Destroyed && materials.All(x=>x.Destroyed),"projectile destruction frees art");
        _count += NativeContracts.Verify(args[0]);
        Console.WriteLine($"ARENA_MOLOTOV_MODEL_PRODUCTION_PASSED: {_count} assertions and native IL contracts");
    }
    private static void InvokeHook(string name,AssetPoolObject pool)
        => typeof(ArenaMolotovModel).GetNestedType(name,BindingFlags.NonPublic).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{pool});
}
