using System.Reflection;
using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using ContinuousLoadAmmo.Controllers;
using ContinuousLoadAmmo.Patches;
using ContinuousLoadAmmo.Utils;
using Fixture;

internal static class Program
{
 static int checks;
 internal static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("ASSERT: "+name);}
 static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
 static object Invoke(object instance,MethodInfo method,object[] args)
 {
  object state=null,result=null;Exception error=null;bool run=true;var patches=Harmony.GetPatchInfo(method);
  object Hook(MethodInfo hook)
  {
   var p=hook.GetParameters();var hargs=new object[p.Length];
   for(int i=0;i<p.Length;i++){string n=p[i].Name;hargs[i]=n switch {"__instance"=>instance,"__state"=>state,"__result"=>result,"__exception"=>error,_=>args[int.Parse(n[2..])]};}
   object r=hook.Invoke(null,hargs);
   for(int i=0;i<p.Length;i++){if(p[i].Name=="__state")state=hargs[i];if(p[i].Name=="__result")result=hargs[i];}
   return r;
  }
  try
  {
   if(patches!=null)foreach(var p in patches.Prefixes.OrderByDescending(p=>p.priority)){if(p.PatchMethod.ReturnType==typeof(bool)){if(!(bool)Hook(p.PatchMethod))run=false;}else Hook(p.PatchMethod);}
   if(run)result=method.Invoke(instance,args);
   if(patches!=null)foreach(var p in patches.Postfixes.OrderByDescending(p=>p.priority))Hook(p.PatchMethod);
  }
  catch(Exception ex){error=ex.GetBaseException();}
  finally{if(patches!=null)foreach(var p in patches.Finalizers)error=(Exception)Hook(p.PatchMethod);}
  if(error!=null)throw error;return result;
 }
 static void Receipt(MethodInfo target,MethodInfo foreign){Harmony.Bound[target]=new();Harmony.Bound[target].Postfixes.Add(new(){owner="fixture",PatchMethod=foreign});}
 static void Install()
 {
  ArenaContinuousLoadAmmoCompat.Shutdown();Harmony.Bound.Clear();Chainloader.PluginInfos.Clear();
  var type=typeof(ContinuousLoadAmmo.ContinuousLoadAmmo);Chainloader.PluginInfos[ArenaContinuousLoadAmmoContract.PluginGuid]=new(){Instance=new ContinuousLoadAmmo.ContinuousLoadAmmo(),Metadata=type.GetCustomAttribute<BepInPlugin>()};
  Receipt(Method(typeof(GameWorld),"RegisterPlayer"),Method(typeof(RegisterPlayerPatch),"Postfix"));
  foreach(var t in new[]{typeof(Player.PlayerInventoryController.LoadMagazineProcess),typeof(Player.PlayerInventoryController.UnloadMagazineProcess)})Receipt(Method(t,"Start"),Method(typeof(LoadingEndPatches),"Postfix"));
  ArenaContinuousLoadAmmoCompat.Initialize();Check(ArenaContinuousLoadAmmoCompat.GetRejectionReason()==null,"published API fixture admitted");
 }
 static LocalPlayer World(bool arena)
 {
  var world=new GameWorld();var body=new LocalPlayer{GameWorld=world};world.MainPlayer=body;
  var game=new LocalGame{GameWorld=world,LocalPlayer=body,Profile=body.Profile};
  Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=game;Singleton<IBotGame>.Instance=game;
  ArenaSession.IsActive=arena;ArenaSession.ArmedForNextRaid=false;ArenaSession.ActiveGame=game;ArenaSession.ActiveWorld=world;
  ArenaServerClient.HasPendingArm=false;ArenaServerClient.PendingArmToken=null;return body;
 }
 static LoadAmmoController Register(Player p){Hooks.Call(typeof(RegisterPlayerPatch),"Postfix",p.GameWorld,p);return RegisterPlayerPatch.Current;}
 static LocalPlayer Replacement(LocalPlayer old){var p=new LocalPlayer{GameWorld=old.GameWorld,Profile=old.Profile};p.GameWorld.MainPlayer=p;((LocalGame)Singleton<AbstractGame>.Instance).LocalPlayer=p;return p;}
 static Task State(LoadAmmoController c,bool start)=>(Task)Hooks.Call(c,"SetPlayerStateAsync",start);
 static void Dispose(LoadAmmoController c)=>Hooks.Call(c,"Dispose");
 static void Start(LocalPlayer p,Task<IResult> task,bool unload=false)
 {
  object process=unload?new Player.PlayerInventoryController.UnloadMagazineProcess{_inventoryController=p.InventoryController,Task=task}:new Player.PlayerInventoryController.LoadMagazineProcess{_inventoryController=p.InventoryController,Task=task};
  // Native Start owns its native result; foreign Harmony postfix is itself intercepted.
  InvokeNative(process,Method(process.GetType(),"Start"));
 }
 static void InvokeNative(object process,MethodInfo method)
 {
  var result=(Task<IResult>)method.Invoke(process,null);
  foreach(var p in Harmony.GetPatchInfo(method).Postfixes.OrderByDescending(p=>p.priority))
   if(p.PatchMethod.DeclaringType==typeof(ArenaContinuousLoadAmmoCompat))p.PatchMethod.Invoke(null,new object[]{process,result});
   else Invoke(null,p.PatchMethod,new object[]{result});
 }
 static async Task Main(string[] args)
 {
  Hooks.Invoke=Invoke;PublishedMetadata.Verify(args[0],Check);PureScope();Install();
  var ordinary=World(false);var oc=Register(ordinary);Check(oc!=null,"ordinary controller created");await State(oc,true);Check(oc.Starts==1,"ordinary native start retained");
  var ordinaryTask=new TaskCompletionSource<IResult>();Start(ordinary,ordinaryTask.Task);ordinary.InventoryController.Active=false;ordinaryTask.SetResult(new Result());await Task.Delay(25);Check(oc.Ends==1,"ordinary completion broadcast retained");Dispose(oc);Check(RegisterPlayerPatch.Current==null,"ordinary disposal unchanged");
  var a=World(true);var ac=Register(a);Check(ac!=null&&ac.Disposes==0,"Arena initial controller created");int count=RegisterPlayerPatch.Registrations;Check(ReferenceEquals(Register(a),ac)&&RegisterPlayerPatch.Registrations==count,"duplicate registration does not leak controller");
  await State(ac,true);Check(ac.Starts==1&&a.MovementContext.Limit,"Arena original load start preserved");
  var delayed=State(ac,false);await State(ac,true);await delayed;Check(a.MovementContext.Limit&&a.Equips==0,"new same-body load cancels delayed unlock");
  var oldTask=new TaskCompletionSource<IResult>();Start(a,oldTask.Task);var b=Replacement(a);var bc=Register(b);Check(ac.Disposes==1&&ReferenceEquals(RegisterPlayerPatch.Current,bc),"live kit swap disposes before rebinding");
  int staticCount=RegisterPlayerPatch.StaticDisposals;Dispose(ac);Check(RegisterPlayerPatch.StaticDisposals==staticCount&&ReferenceEquals(RegisterPlayerPatch.Current,bc),"late duplicate disposal preserves successor");
  await State(bc,true);oldTask.SetResult(new Result());await Task.Delay(25);Check(ac.Ends==0&&bc.Ends==0&&b.MovementContext.SprintDisabled,"old task cannot notify successor");
  int broadcasts=LoadingEndPatches.Broadcasts;LoadingEndPatches.Broadcast();Check(bc.Ends==0&&b.MovementContext.SprintDisabled,"unrelated global completion cannot notify Arena");
  b.InventoryController.Active=true;var currentTask=new TaskCompletionSource<IResult>();Start(b,currentTask.Task,true);b.InventoryController.Active=false;currentTask.SetResult(new Result());await Task.Delay(30);Check(bc.Ends==1,"current unload reaches exact controller");await Task.Delay(830);Check(!b.MovementContext.Limit&&b.Equips==1,"current completion restores weapon and limit");Check(LoadingEndPatches.Broadcasts==broadcasts+1,"Arena task does not broadcast to ordinary subscribers");
  b.HandsIsEmpty=true;await State(bc,true);var nextEnd=State(bc,false);var c=Replacement(b);var cc=Register(c);await nextEnd;Check(b.Equips==1&&c.Equips==0,"retired delayed restore touches neither body");
  await State(cc,true);var activeEnd=State(cc,false);c.InventoryController.Active=true;await activeEnd;Check(c.MovementContext.Limit,"active load prevents delayed restore");c.InventoryController.Active=false;
  var multi=State(cc,false);MultiSelectInterop.MultiSelectLoadSerializerIsActive=true;await multi;Check(c.MovementContext.Limit,"MultiSelect pending load preserves limit");MultiSelectInterop.MultiSelectLoadSerializerIsActive=false;
  c.HandsIsEmpty=true;await State(cc,true);var worldEnd=State(cc,false);var abandonedTask=new TaskCompletionSource<IResult>();Start(c,abandonedTask.Task);int oldEnds=cc.Ends;var following=World(false);var fc=Register(following);abandonedTask.SetResult(new Result());await Task.Delay(30);Check(cc.Ends==oldEnds,"old task cannot notify following world");await worldEnd;Check(c.Equips==0&&following.Equips==0,"following ordinary world cannot inherit delayed work");Dispose(cc);Check(ReferenceEquals(RegisterPlayerPatch.Current,fc),"old Arena dispose cannot clear ordinary successor");await State(fc,true);Check(fc.Starts==1,"following ordinary start retained");Dispose(fc);
  Initial();await Immediate();await Revocation();Admission();
  Console.WriteLine($"CONTINUOUS_LOAD_AMMO_COMPAT_PASSED ({checks})");
 }
 static void PureScope()
 {
  var s=new ArenaContinuousLoadAmmoScope();object ctl=new(),body=new(),world=new(),inv=new();
  Check(s.BindController(ctl,body,world,inv,world,body,false,true)==null,"ordinary scope is not captured");
  Check(s.BindController(ctl,body,world,inv,new object(),body,true,true)==null,"different physical world is not captured");
  Check(s.BindController(ctl,body,world,inv,world,new object(),true,true)==null,"non-main body is not captured");
  Check(s.BindController(ctl,body,world,inv,world,body,true,false)==null,"bot scope is not captured");
  var lease=s.BindController(ctl,body,world,inv,world,body,true,true);Check(lease!=null,"exact physical scope captured");
  object otherBody=new();Check(s.BindController(ctl,otherBody,world,inv,world,otherBody,true,true)==null,"reused controller body rejected");
  bool Current(object ct,object w,object b,object i,bool alive=true,bool exact=true)=>ArenaContinuousLoadAmmoScope.IsCurrent(lease,ct,w,b,i,alive,exact);
  Check(Current(ctl,world,body,inv),"exact current scope");Check(!Current(new object(),world,body,inv),"controller identity check");Check(!Current(ctl,new object(),body,inv),"world identity check");Check(!Current(ctl,world,new object(),inv),"body identity check");Check(!Current(ctl,world,body,new object()),"inventory identity check");Check(!Current(ctl,world,body,inv,false),"dead current denied");Check(!Current(ctl,world,body,inv,true,false),"unarmed current denied");
  var task1=s.CaptureTask(new object(),lease);var task2=s.CaptureTask(new object(),lease);Check(!ArenaContinuousLoadAmmoScope.IsLatest(task1)&&ArenaContinuousLoadAmmoScope.IsLatest(task2),"old same-body task epoch rejected");
  Check(ArenaContinuousLoadAmmoScope.Retire(lease)&&!ArenaContinuousLoadAmmoScope.Retire(lease),"retire exactly once");Check(!Current(ctl,world,body,inv),"retired lease denied");
  s.Clear();Check(s.Find(ctl)==null&&s.ForInventory(inv)==null,"scope cleanup releases leases");
  var token=Guid.NewGuid().ToString("N");var native=ArenaMode.Shared.Protocol.BuildNativeEndIdentity("native-server", token); // validated wire identity
  bool Raid(bool pair=true,bool running=true,bool pmc=true,string map="factory4_day",string settings="factory4_day",bool active=false,bool armed=true,bool pending=true,string armedMap="factory4_day",string t=null,string id=null)=>ArenaContinuousLoadAmmoScope.OwnsRaid(pair,running,pmc,map,settings,active,armed,pending,armedMap,t??token,id??native);
  Check(Raid(),"confirmed loading admitted");Check(!Raid(pair:false),"foreign game denied");Check(!Raid(running:false),"stopped raid denied");Check(!Raid(pmc:false),"Scav arm denied");Check(!Raid(settings:"woods"),"wrong settings map denied");Check(!Raid(armed:false),"unarmed load denied");Check(!Raid(pending:false),"missing pending arm denied");Check(!Raid(armedMap:"woods"),"wrong armed map denied");Check(!Raid(t:"bad"),"malformed token denied");Check(!Raid(id:"ordinary"),"unconfirmed server identity denied");
 }
 static void Initial()
 {
  var p=World(false);var g=(LocalGame)Singleton<AbstractGame>.Instance;g.Status=GameStatus.Running;g.LocalPlayer=null;Singleton<IBotGame>.Instance=null;
  var token=Guid.NewGuid().ToString("N");g._raidSettings.serverId=ArenaMode.Shared.Protocol.BuildNativeEndIdentity("native-server", token);ArenaSession.ArmedForNextRaid=true;ArenaSession.ArmedMap="factory4_day";ArenaServerClient.HasPendingArm=true;ArenaServerClient.PendingArmToken=token;
  var ctl=Register(p);int n=RegisterPlayerPatch.Registrations;Register(p);Check(RegisterPlayerPatch.Registrations==n,"pre-bot-game local registration captured");Dispose(ctl);
  p=World(false);g=(LocalGame)Singleton<AbstractGame>.Instance;g.Status=GameStatus.Running;g.LocalPlayer=null;g.Profile=new object();Singleton<IBotGame>.Instance=null;g._raidSettings.serverId=ArenaMode.Shared.Protocol.BuildNativeEndIdentity("native-server", token);ArenaSession.ArmedForNextRaid=true;ArenaSession.ArmedMap="factory4_day";ArenaServerClient.HasPendingArm=true;ArenaServerClient.PendingArmToken=token;
  ctl=Register(p);n=RegisterPlayerPatch.Registrations;Register(p);Check(RegisterPlayerPatch.Registrations==n+1,"initial profile object mismatch remains ordinary");Dispose(RegisterPlayerPatch.Current);
 }
 static async Task Immediate()
 {
  var p=World(true);var c=Register(p);await State(c,true);int broadcasts=LoadingEndPatches.Broadcasts;Start(p,Task.FromResult<IResult>(new Result()));await Task.Delay(30);Check(c.Ends==1&&LoadingEndPatches.Broadcasts==broadcasts,"immediate completion captured before foreign broadcast");await Task.Delay(830);Check(p.Equips==1&&!p.MovementContext.Limit,"immediate completion restores current controller");Dispose(c);
 }
 static async Task Revocation()
 {
  var p=World(true);var c=Register(p);await State(c,true);var pending=State(c,false);ArenaContinuousLoadAmmoCompat.Shutdown();ArenaContinuousLoadAmmoCompat.Initialize();await pending;Check(p.Equips==0&&p.MovementContext.Limit,"revoked binding cannot resume after reinitialization");Dispose(c);
 }
 static void Admission()
 {
  var target=Method(typeof(Player.PlayerInventoryController.LoadMagazineProcess),"Start");var info=Harmony.GetPatchInfo(target);var receipt=info.Postfixes.Single(p=>p.PatchMethod.DeclaringType==typeof(LoadingEndPatches));info.Postfixes.Remove(receipt);Check(ArenaContinuousLoadAmmoCompat.GetRejectionReason()!=null,"missing foreign receipt rejects Arena");Install();
  info=Harmony.GetPatchInfo(target);info.ILManipulators.Add(new(){owner="foreign-il",PatchMethod=Method(typeof(RegisterPlayerPatch),"OnDispose")});Check(ArenaContinuousLoadAmmoCompat.GetRejectionReason()!=null,"unknown foreign IL hook rejects Arena");Install();
  info=Harmony.GetPatchInfo(target);info.Postfixes.Single(p=>p.PatchMethod.DeclaringType==typeof(LoadingEndPatches)).priority=Priority.First+1;Check(ArenaContinuousLoadAmmoCompat.GetRejectionReason()!=null,"foreign broadcast before capture rejects Arena");Install();
  var owned=Harmony.GetPatchInfo(Method(typeof(RegisterPlayerPatch),"Postfix"));owned.Prefixes.Clear();Check(ArenaContinuousLoadAmmoCompat.GetRejectionReason()!=null,"missing own receipt rejects Arena");
  ArenaContinuousLoadAmmoCompat.Shutdown();Chainloader.PluginInfos.Clear();Check(ArenaContinuousLoadAmmoCompat.GetRejectionReason()==null,"absent mod does not reject Arena");
 }
}
