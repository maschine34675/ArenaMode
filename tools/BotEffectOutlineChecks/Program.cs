using System.Reflection;
using ArenaMode.Effects;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;

static class Program
{
    private static int _checks;
    private static GameWorld _world;
    private static Player _body;
    private static Camera _camera;
    private static SkinnedMeshRenderer _skin;
    private static object Manager => typeof(ArenaBotEffectRenderer).GetField("_current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
    private static void Invoke(string method) => Manager?.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Manager,null);
    private static void Check(bool value,string label){_checks++;if(!value)throw new Exception("ASSERT: "+label);}
    private static CommandBuffer Render(){Camera.Render(_camera);return _camera.Commands.SingleOrDefault();}
    private static void Fresh()
    {
        Invoke("DisposeOwned");
        ArenaSession.IsActive=true;ArenaSession.Exact=true;ArenaSession.ArmEpoch++;
        ArenaMatchService.Current=new();ArenaMatchService.TeamStartAbortPending=false;
        _world=new GameObject("World").AddComponent<GameWorld>(); Singleton<GameWorld>.Instance=_world;
        Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=_world};
        _camera=new GameObject("FPS Camera").AddComponent<Camera>();CameraManager.Instance.Camera=_camera;
        _body=new GameObject("Bot").AddComponent<Player>();
        _body.PlayerBody=new GameObject("Body").AddComponent<PlayerBody>();_body.PlayerBody.transform.SetParent(_body.transform,false);
        _skin=new GameObject("Skin").AddComponent<SkinnedMeshRenderer>();_skin.transform.SetParent(_body.PlayerBody.transform,false);_skin.sharedMesh=new Mesh{subMeshCount=2};
        _body.PlayerBody.Renderers.Add(_skin);_world.Alive[_body.ProfileId]=_body;
        Time.frameCount=10;Time.unscaledTime=10;SystemInfo.Supported=true;AssetBundle.FailLoad=false;CommandBuffer.FailDraw=false;GeometryUtility.InFrustum=true;
    }
    private static int Main()
    {
        try
        {
            Registry(); Rendering(); Invalidations(); Failures();
            Console.WriteLine($"BOT_EFFECT_OUTLINE_BEHAVIOR_PASSED: {_checks} actual registry/renderer/adapter assertions; no rasterization claim");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}
        finally{Invoke("DisposeOwned");}
    }
    private sealed class EqualBody
    {
        public override bool Equals(object obj)=>obj is EqualBody;
        public override int GetHashCode()=>0;
    }
    private static void Registry()
    {
        var book=new ArenaBotEffectLeases<EqualBody>();var a=new EqualBody();var b=new EqualBody();
        var spawn=book.Add(a);var shield=book.Add(a);var other=book.Add(b);
        Check(book.Entries.Count==2,"equal profile-like values cannot merge distinct bodies");
        Check(spawn.Entry.Read(10)==ArenaBotEffectKind.None,"new leases do not render before renewal");
        spawn.Set(ArenaBotEffectKind.Spawn,10);shield.Set(ArenaBotEffectKind.Shield|ArenaBotEffectKind.DoubleDamage,10);
        Check(spawn.Entry.Read(10)==(ArenaBotEffectKind)7,"simultaneous owners coalesce all channels");
        Check(spawn.Entry.Read(12)==(ArenaBotEffectKind)7,"two-frame bounded refresh tolerance");
        Check(spawn.Entry.Read(13)==ArenaBotEffectKind.None,"stale presentation heartbeat cannot persist");
        Check(spawn.Entry.Read(9)==ArenaBotEffectKind.None,"future presentation frame never renders");
        spawn.Set(ArenaBotEffectKind.Spawn,int.MinValue);
        Check(spawn.Entry.Read(int.MaxValue)==ArenaBotEffectKind.None,"frame subtraction cannot overflow into fresh ownership");
        spawn.Set(ArenaBotEffectKind.Spawn,int.MaxValue);
        Check(spawn.Entry.Read(int.MinValue)==ArenaBotEffectKind.None,"wrapped frame cannot borrow future ownership");
        spawn.Set(ArenaBotEffectKind.Spawn,10);shield.Dispose();
        Check(spawn.Entry.Read(10)==ArenaBotEffectKind.Spawn,"releasing pickup owner preserves independent spawn owner");
        var retired=spawn.Entry;book.Retire(retired);spawn.Set(ArenaBotEffectKind.Spawn,11);
        Check(retired.Body==null && retired.Leases.Count==0 && spawn.Entry==null,"retired owner cannot resurrect body references");
        other.Dispose();other.Dispose();book.Clear();Check(book.Entries.Count==0,"all ownership drops idempotently");
    }
    private static void Rendering()
    {
        Fresh();using var spawn=new ArenaSpawnShieldView(_world,_body);spawn.Update();
        var initialModel=_body.PlayerBody;var initialMesh=_skin.sharedMesh;
        var command=Render();
        Check(command!=null && command.Draws.Count==2,"visible skin submits each native submesh");
        Check(command.Draws.All(x=>ReferenceEquals(x,_skin)),"render commands reference actual skin without clones");
        Check(command.Allocations==1 && command.Releases==1 && command.Composites==1,"one bounded mask and composite shared by all effects");
        Check(command.Width==-1 && command.Height==-1,"render size follows camera execution not screen dimensions");
        Check(_camera.depthTextureMode==DepthTextureMode.None,"deferred camera flags remain unchanged");
        using var pickup=new ArenaPowerupIndicator(_world,_body);
        pickup.Update(new(){ShieldSeconds=10,ShieldPoints=220,DamageSeconds=10});command=Render();
        Check(command.Draws.Count==2 && _camera.Commands.Count==1,"simultaneous effects do not duplicate body passes or buffers");
        Check(command.Globals[Shader.PropertyToID("_ArenaEffectFlags")]==7,"combined channel bits reach real command recorder");
        Check(ReferenceEquals(_body.PlayerBody,initialModel)&&ReferenceEquals(_skin.sharedMesh,initialMesh)&&_skin.enabled&&!_skin.forceRenderingOff,"native model mesh and render settings unchanged");
        var second=new GameObject("ReplacementCamera").AddComponent<Camera>();CameraManager.Instance.Camera=second;Camera.Render(second);
        Check(_camera.Commands.Count==0 && second.Commands.Count==1,"camera switch detaches exact previous buffer");
        _camera=second;
        var preview=new GameObject("Preview").AddComponent<Camera>();preview.cameraType=CameraType.Preview;Camera.Render(preview);
        Check(preview.Commands.Count==0,"preview camera never receives effect pass");
        _camera.actualRenderingPath=RenderingPath.Forward;_camera.depthTextureMode=DepthTextureMode.None;Render();
        Check(command.Draws.Count==0,"lost depth admission clears previous draw commands");
        _camera.depthTextureMode=DepthTextureMode.Depth;Render();Check(command.Draws.Count==2,"existing forward depth request can render");
        Time.frameCount+=3;Render();Check(command.Draws.Count==0,"expired heartbeat clears old camera frame");
        spawn.Update();pickup.Update(default);Render();Check(command.Draws.Count==2,"renewal restores only live source");
        foreach(Action hide in new Action[]{()=>_skin.enabled=false,()=>_skin.forceRenderingOff=true,()=>_skin.isVisible=false,
            ()=>_skin.gameObject.activeInHierarchy=false,()=>_skin.shadowCastingMode=ShadowCastingMode.ShadowsOnly,
            ()=>_camera.cullingMask=0,()=>GeometryUtility.InFrustum=false})
        {
            _skin.enabled=true;_skin.forceRenderingOff=false;_skin.isVisible=true;_skin.gameObject.activeInHierarchy=true;_skin.shadowCastingMode=ShadowCastingMode.On;_camera.cullingMask=-1;GeometryUtility.InFrustum=true;
            hide();Render();Check(command.Draws.Count==0,"hidden filtered geometry cannot leave recorded silhouette");
        }
        Invoke("DisposeOwned");
        Check(command.Disposed&&_camera.Commands.Count==0&&Camera.Subscribers==0,"owned buffer and static camera callback fully released");
        Check(Material.Created.All(x=>x.Destroyed)&&AssetBundle.Created.All(x=>x.Unloaded),"owned materials and embedded bundle released");
    }
    private static void Invalidations()
    {
        foreach(Action change in new Action[]{()=>ArenaSession.IsActive=false,()=>ArenaSession.Exact=false,()=>ArenaSession.ArmEpoch++,
            ()=>Singleton<AbstractGame>.Instance=new LocalGame{GameWorld=_world},()=>((LocalGame)Singleton<AbstractGame>.Instance).Status=GameStatus.Stopped,
            ()=>Singleton<GameWorld>.Instance=new GameObject("OtherWorld").AddComponent<GameWorld>(),()=>ArenaMatchService.Current=new(),
            ()=>ArenaMatchService.TeamStartAbortPending=true,()=>((TeamDeathmatchMode)ArenaMatchService.Current.Mode).IsComplete=true,
            ()=>((TeamDeathmatchMode)ArenaMatchService.Current.Mode).AssistScoringOpen=false,
            ()=>_body.ActiveHealthController.IsAlive=false,()=>_body.ActiveHealthController=new(),
            ()=>_world.Alive[_body.ProfileId]=new GameObject("ReplacementBody").AddComponent<Player>(),
            ()=>_body.AIData.BotOwner.BotState=EBotState.NonActive,()=>UnityEngine.Object.Destroy(_body)})
        {
            Fresh();using var spawn=new ArenaSpawnShieldView(_world,_body);spawn.Update();var command=Render();
            Check(command.Draws.Count==2,"invalidation has rendered positive control");change();Invoke("LateUpdate");
            Check(command.Disposed&&_camera.Commands.Count==0&&Manager==null,"context body or health drift retires presentation fully");
        }
        Fresh();using var oldLease=new ArenaSpawnShieldView(_world,_body);oldLease.Update();var oldManager=Manager;Render();
        ArenaMatchService.Current=new();using var newLease=new ArenaSpawnShieldView(_world,_body);newLease.Update();
        Check(!ReferenceEquals(oldManager,Manager)&&Render().Draws.Count==2,"same-world new match cannot inherit retired render lease");
        Fresh();using var first=new ArenaSpawnShieldView(_world,_body);first.Update();Render();_body.ActiveHealthController=new();
        using var next=new ArenaSpawnShieldView(_world,_body);next.Update();Check(Render().Draws.Count==2,"same-body health replacement rebinds current visual ownership");
    }
    private static void Failures()
    {
        Fresh();AssetBundle.FailLoad=true;using var lease=new ArenaSpawnShieldView(_world,_body);lease.Update();Render();
        Check(_camera.Commands.Count==0,"bundle failure produces no unoccluded fallback");
        int warnings=ArenaMode.Plugin.ModLogger.Warnings;
        for(int i=0;i<30;i++){lease.Update();Render();}
        Check(ArenaMode.Plugin.ModLogger.Warnings==warnings,"render fault does not log or retry every frame");
        lease.Dispose();Invoke("LateUpdate");Check(Manager==null&&Camera.Subscribers==0,"failed rendering still releases all ownership");
        Fresh();CommandBuffer.FailDraw=true;using var partial=new ArenaSpawnShieldView(_world,_body);partial.Update();Render();
        Check(_camera.Commands.Count==0 && CommandBuffer.Created.All(x=>x.Disposed)
            && Material.Created.All(x=>x.Destroyed) && AssetBundle.Created.All(x=>x.Unloaded),
            "partial render failure immediately releases buffers materials and bundle");
        Check(Manager!=null,"failed manager retains bounded lease to prevent resource retry");
        partial.Dispose();Invoke("LateUpdate");Check(Manager==null,"failed manager retires after final owner leaves");
        Fresh();ArenaSession.IsActive=false;bool rejected=false;
        try{ArenaBotEffectRenderer.Acquire(_world,_body);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&Manager==null&&Camera.Subscribers==0,"ordinary raid cannot create renderer or hook");
    }
}
