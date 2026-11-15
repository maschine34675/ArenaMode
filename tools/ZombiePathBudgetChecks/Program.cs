using System.Reflection;
using System.Reflection.Emit;
using ArenaMode.Bots;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

int assertions=0;
void Check(bool condition,string message){assertions++;if(!condition)throw new InvalidOperationException("FAIL: "+message);}
bool Always(object b,object t)=>true;
foreach(int population in new[]{32,96})
{
    var policy=new ZombiePathBudgetPolicy();var bodies=Enumerable.Range(0,population).Select(_=>new object()).ToArray();
    var target=new object();var count=new int[population];var last=new int[population];
    for(int frame=1;frame<=population*2;frame++)
    {
        int grants=0;
        for(int i=0;i<population;i++) if(policy.TryAcquire(bodies[i],target,frame,Always))
        {
            grants++;count[i]++;Check(last[i]==0||frame-last[i]<=population/2+1,"FIFO preserves fair bounded waits in stable native order");last[i]=frame;
        }
        Check(grants<=2&&policy.FrameJobs==grants,"mixed callers never exceed two planning jobs per frame");
    }
    Check(count.All(n=>n>=3),"FIFO preserves fair bounded waits in stable native order");
}
var p=new ZombiePathBudgetPolicy();object a=new(),b=new(),c=new(),d=new(),e=new(),t=new();
Check(p.TryAcquire(a,t,1,Always)&&!p.TryAcquire(a,t,1,Always),"same body cannot replay a consumed permit in one frame");
Check(!p.TryAcquire(a,new object(),1,Always),"target change cannot replay same frame permit");
p.Reset();p.TryAcquire(a,t,1,Always);p.TryAcquire(b,t,1,Always);p.TryAcquire(c,t,1,Always);p.TryAcquire(d,t,1,Always);
Check(!p.TryAcquire(e,t,2,Always)&&!p.TryAcquire(e,t,3,Always)&&p.TryAcquire(e,t,4,Always),"abandoned pending heads expire after two missed frames");
p.Reset();p.TryAcquire(a,t,1,Always);p.TryAcquire(b,t,1,Always);p.TryAcquire(c,t,1,Always);p.TryAcquire(d,t,1,Always);
Check(p.TryAcquire(d,t,2,(body,target)=>!ReferenceEquals(body,c)),"dead or changed target head cannot block live caller");
p.Reset();p.TryAcquire(a,t,1,Always);p.TryAcquire(b,t,1,Always);p.TryAcquire(c,t,1,Always);p.TryAcquire(d,t,1,Always);
bool throwSafe=false;try{throwSafe=p.TryAcquire(d,t,2,(body,target)=>ReferenceEquals(body,c)?throw new Exception("stale"):true);}catch{}
Check(throwSafe,"throwing stale ticket is evicted without poisoning other callers");
p.Reset();for(int i=0;i<150;i++)p.TryAcquire(new object(),t,1,Always);
Check(p.Retained==128&&p.Pending<=128,"queue and retained identities have an explicit memory bound");
Check(p.TryAcquire(a,t,0,Always)&&p.FrameJobs==1&&p.Retained==1,"backward frame starts fresh bounded lifetime");

void Setup()
{
    ZombiePathBudgetRuntime.Reset();Time.frameCount=1;Time.time=10;ArenaSession.IsActive=true;
    Singleton<GameWorld>.Instance=new();ArenaMatchService.Current=new(){Mode=new object()};
    ArenaMode.Plugin.ModLogger.Warnings=0;
}
BotOwner Bot()
{
    var owner=new BotOwner{World=Singleton<GameWorld>.Instance,Mode=ArenaMatchService.Current.Mode};
    var target=new Player();target.AIData.Player=target;owner.Memory.GoalEnemy.Person=target;
    return owner;
}
MoveToEnemyZigZag Node(BotOwner owner)=>new(){_owner=owner};
bool Drive(MoveToEnemyZigZag node)
{
    var type=typeof(ZombiePathBudgetRuntime).GetNestedType("ArenaZombiePathBudgetApproachPatch",BindingFlags.NonPublic);
    bool allowed=(bool)type.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{node});
    if(allowed)node.NotMovingCheck();return allowed;
}
var fallback=typeof(ZombieMeleePatch).GetMethod("FallbackRunToEnemyUpdate",BindingFlags.Static|BindingFlags.NonPublic);
bool Melee(BotMeleeWeaponData m)=>(bool)fallback.Invoke(null,new object[]{m});
Setup();var first=Node(Bot());var second=Node(Bot());var third=Node(Bot());
Check(Drive(first)&&Drive(second)&&!Drive(third),"actual approach prefix shares the two job budget");
Check(third._nextCheck==0&&third._lastEnemyId==-1&&third.NativeCalls==0,"denied approach leaves native timers target and path untouched");
Check(first.NestedPaths==8&&first.NativeCalls==1,"one admitted job retains all native nested searches and fallbacks");
Time.frameCount++;Check(Drive(third)&&third.NativeCalls==1,"denied approach retries and gets its fair permit");
Setup();Drive(Node(Bot()));Drive(Node(Bot()));var meleeOwner=Bot();var melee=meleeOwner.WeaponManager.Melee;
melee.ShallEndRun=true;var originalPath=new Vector3[3];meleeOwner.Mover.Path=originalPath;
Check(Melee(melee)&&melee.Hits==1&&meleeOwner.Steering.Calls>0&&meleeOwner.SprintCalls>0,"deferred melee still attempts attack steering and sprint");
Check(!melee.ShallEndRun&&melee.Routes==0&&melee._runPathCheck==0&&ReferenceEquals(originalPath,meleeOwner.Mover.Path),"deferred melee is no path failure and preserves retry clock existing path");
Time.frameCount++;Check(Melee(melee)&&melee.Routes==1&&ReferenceEquals(meleeOwner.Mover.Path,melee.NativePath),"next frame executes the real native route rather than fabricating success");
Setup();meleeOwner=Bot();melee=meleeOwner.WeaponManager.Melee;melee.RouteResult=false;
Check(!Melee(melee)&&melee.ShallEndRun&&melee.Routes==1,"granted native route failure retains native failure semantics");
Setup();meleeOwner=Bot();melee=meleeOwner.WeaponManager.Melee;meleeOwner.Mover.HasPathAndNoComplete=true;melee._runPathCheck=100;
Check(Melee(melee)&&melee.Routes==0&&ZombiePathBudgetRuntime.FrameJobs==0,"not yet due melee does not consume planning work");
Setup();Drive(Node(Bot()));Drive(Node(Bot()));var foreign=Bot();foreign.Owned=false;
Check(Drive(Node(foreign))&&ZombiePathBudgetRuntime.FrameJobs==2,"unowned and foreign bodies remain native without consuming or resetting budget");
ArenaSession.IsActive=false;Check(Drive(Node(Bot()))&&ZombiePathBudgetRuntime.FrameJobs==2,"ordinary raids keep native planning unchanged");
Setup();Drive(Node(Bot()));Drive(Node(Bot()));var old=Bot();Drive(Node(old));
Singleton<GameWorld>.Instance=new();ArenaMatchService.Current.Mode=new object();
Check(Drive(Node(Bot()))&&ZombiePathBudgetRuntime.FrameJobs==1&&ZombiePathBudgetRuntime.Pending==0,"world and match transition clears stale tickets");
ZombiePathBudgetRuntime.Reset();Check(ZombiePathBudgetRuntime.PeakJobs==0&&ZombiePathBudgetRuntime.Pending==0&&ZombiePathBudgetRuntime.ApproachJobs==0,"raid reset clears queue usage and diagnostics");
Setup();var throwing=Bot();throwing.ThrowOwnership=true;
Check(Drive(Node(throwing))&&Drive(Node(throwing))&&ArenaMode.Plugin.ModLogger.Warnings==1,"budget identity exception fails open with bounded warning");
Check(!ZombiePathBudgetRuntime.ShouldDeferMelee(throwing.WeaponManager.Melee),"budget identity exception cannot demote healthy reverse patch");
Setup();Drive(Node(Bot()));Drive(Node(Bot()));var brokenHead=Bot();Drive(Node(brokenHead));var liveTail=Node(Bot());Drive(liveTail);brokenHead.ThrowOwnership=true;Time.frameCount++;
Check(Drive(liveTail),"throwing stale runtime ticket cannot abort another approach");

// Exercise actual RunOriginal fallback after a native delegate throws *after*
// it already performed route work. The second path may not reuse its permit.
Setup();melee=Bot().WeaponManager.Melee;
void Set(string name,object value)=>typeof(ZombieMeleePatch).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value);
Set("_reversePatchDead",false);Set("_reversePatchAttempted",true);
Set("_reversePatchedOriginal",(Func<BotMeleeWeaponData,bool>)(m=>{Melee(m);throw new Exception("native after planning");}));
typeof(ZombieMeleePatch).GetMethod("RunOriginal",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{melee});
Check(melee.Routes==1&&ZombiePathBudgetRuntime.FrameJobs==1,"reverse failure cannot replay expensive work through same frame fallback");

Func<BotMeleeWeaponData,bool,bool> EmitGuarded(bool mutateLabels=false,bool missing=false,bool duplicate=false)
{
    var dynamic=new DynamicMethod("guardedNativeFixture",typeof(bool),new[]{typeof(BotMeleeWeaponData),typeof(bool)},typeof(Program).Module,true);
    var il=dynamic.GetILGenerator();var path=il.DeclareLocal(typeof(Vector3[]));var anchor=il.DefineLabel();var finish=il.DefineLabel();
    var code=new List<CodeInstruction>();
    void Add(OpCode op,object arg=null)=>code.Add(new(op,arg));
    var owner=AccessTools.Field(typeof(BotData),"_owner"); // native IL names the declaring base, not its reflected derived type
    void Enemy(){Add(OpCodes.Ldarg_0);Add(OpCodes.Ldfld,owner);Add(OpCodes.Ldfld,AccessTools.Field(typeof(BotOwner),"Memory"));Add(OpCodes.Ldfld,AccessTools.Field(typeof(BotMemory),"GoalEnemy"));}
    Add(OpCodes.Ldarg_0);Add(OpCodes.Ldc_I4_0);Add(OpCodes.Stfld,AccessTools.Field(typeof(BotMeleeWeaponData),"ShallEndRun"));
    Add(OpCodes.Ldarg_1);Add(OpCodes.Brfalse,anchor);
    Add(OpCodes.Ldarg_0);Enemy();Add(OpCodes.Callvirt,AccessTools.Method(typeof(BotMeleeWeaponData),"TryHit"));Add(OpCodes.Pop);
    Add(OpCodes.Ldarg_0);Add(OpCodes.Ldc_R4,.1f);Add(OpCodes.Callvirt,AccessTools.Method(typeof(BotMeleeWeaponData),"ResetHitTime"));
    Add(OpCodes.Ldarg_0);code[^1].labels.Add(anchor);Add(OpCodes.Ldfld,owner);
    Add(OpCodes.Callvirt,AccessTools.PropertyGetter(typeof(BotOwner),"Mover"));
    Add(OpCodes.Callvirt,AccessTools.PropertyGetter(typeof(BotMover),"HasPathAndNoComplete"));Add(OpCodes.Pop);
    for(int i=0;i<2;i++)
    {
        Add(OpCodes.Ldarg_0);Enemy();Add(OpCodes.Ldloca,path);Add(OpCodes.Callvirt,AccessTools.Method(typeof(BotMeleeWeaponData),"CanRunToEnemyToHit"));Add(OpCodes.Pop);
        if(i==0)Add(OpCodes.Br,finish);
    }
    Add(OpCodes.Ldc_I4_1);code[^1].labels.Add(finish);Add(OpCodes.Ret);
    if(missing)code.RemoveAt(code.FindIndex(x=>Equals(x.operand,AccessTools.Method(typeof(BotMeleeWeaponData),"ResetHitTime"))));
    if(duplicate)code.Add(new(OpCodes.Callvirt,AccessTools.PropertyGetter(typeof(BotMover),"HasPathAndNoComplete")));
    var patched=ZombiePathBudgetTranspiler.Apply(code,il).ToList();
    foreach(var ins in patched)
    {
        foreach(var label in ins.labels)il.MarkLabel(label);
        switch(ins.operand)
        {
            case null:il.Emit(ins.opcode);break;case FieldInfo field:il.Emit(ins.opcode,field);break;case MethodInfo method:il.Emit(ins.opcode,method);break;
            case Label label:il.Emit(ins.opcode,label);break;case LocalBuilder local:il.Emit(ins.opcode,local);break;case float f:il.Emit(ins.opcode,f);break;
            default:throw new Exception("unsupported fixture operand");
        }
    }
    return (Func<BotMeleeWeaponData,bool,bool>)dynamic.CreateDelegate(typeof(Func<BotMeleeWeaponData,bool,bool>));
}
Func<BotMeleeWeaponData,bool,bool> guarded=null;try{guarded=EmitGuarded();}catch{}
Check(guarded!=null,"emitted reverse guard preserves attack and covers hit-not-due branch labels");
foreach(bool hitDue in new[]{true,false})
{
    Setup();Drive(Node(Bot()));Drive(Node(Bot()));melee=Bot().WeaponManager.Melee;melee.ShallEndRun=true;
    Check(guarded(melee,hitDue)&&melee.Hits==(hitDue?1:0)&&melee.Routes==0&&!melee.ShallEndRun,"emitted reverse guard preserves attack and covers hit-not-due branch labels");
    Time.frameCount++;Check(guarded(melee,false)&&melee.Routes==1,"emitted reverse guard resumes native planning on fair grant");
}
foreach(var variant in new[]{1,2})
{
    bool failed=false;try{EmitGuarded(missing:variant==1,duplicate:variant==2);}catch(InvalidOperationException){failed=true;}
    Check(failed,"native missing or ambiguous IL anchor refuses partial rewrite");
}
ZombieMeleePatch.EnablePatch();
Check(SPT.Reflection.Patching.ModulePatch.Enabled.Contains("ArenaZombiePathBudgetApproachPatch"),"existing melee setup installs separate approach budget patch");
Console.WriteLine($"ZOMBIE_PATH_BUDGET_CHECKS_PASSED {assertions} assertions");
