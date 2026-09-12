using ArenaMode.Hud;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

int checks=0;
void Check(bool ok,string reason) { if(!ok) throw new Exception(reason); checks++; }
var update=typeof(InputManager).GetMethod("Update");
var collect=typeof(InputPreset).GetMethod("UpdateInput");
var dispatch=typeof(InputManager).GetMethod("DispatchInput");
var destroy=typeof(InputManager).GetMethod("OnDestroy");
void BeforeUpdate(InputManager m)=>Harmony.Hooks[update].Prefix.Method.Invoke(null,[m]);
void AfterCollect(InputPreset p,List<ECommand> commands,float[] axes)
    =>Harmony.Hooks[collect].Postfix.Method.Invoke(null,[p,commands,axes]);
InputManager Setup(bool active=true)
{
    ArenaInputBlocker.Shutdown();
    ArenaSession.Active=active;
    ArenaSession.Game=Singleton<AbstractGame>.Instance=new();
    ArenaSession.World=Singleton<GameWorld>.Instance=new();
    InputManager._currentPreset=new(); Input.Held=new bool[3]; Input.Down=new bool[3];
    ArenaInputBlocker.Apply();
    var m=new InputManager(); BeforeUpdate(m); return m;
}
void Collect(InputManager m,params ECommand[] commands)
{
    BeforeUpdate(m);
    if(InputManager._currentPreset._workingKeyCombinations!=null)
    { m._commandsList.Clear(); m._commandsList.AddRange(commands); }
    Array.Fill(m._axesList,1f);
    AfterCollect(InputManager._currentPreset,m._commandsList,m._axesList);
}
(ECommand[] Commands,float[] Axes) Deliver(InputManager m,bool smooth=false,Action during=null)
{
    var commands=smooth?m._emptyCommandsList:m._commandsList;
    object[] args=[m,commands,m._axesList,0L];
    Harmony.Hooks[dispatch].Prefix.Method.Invoke(null,args);
    var result=(commands.ToArray(),m._axesList.ToArray());
    during?.Invoke(); // Models re-entrant native TranslateDelegate work.
    Harmony.Hooks[dispatch].Postfix.Method.Invoke(null,[args[3]]);
    return result;
}
void UiClose(object owner)=>ArenaInputBlocker.Request(owner,false,drainOnRelease:true);

var m=Setup(false); var a=new object();
ArenaInputBlocker.Request(a,true); Collect(m,ECommand.ToggleShooting);
var result=Deliver(m);
Check(result.Commands.Contains(ECommand.ToggleShooting)&&result.Axes.All(x=>x==1)&&!ArenaInputBlocker.Blocked,"ordinary input stays unchanged");

m=Setup(); ArenaInputBlocker.Request(a,true);
Collect(m,ECommand.ToggleShooting,ECommand.EndShooting,ECommand.Escape,ECommand.EndSprint,ECommand.StopTalk,ECommand.ReloadWeapon);
result=Deliver(m);
Check(result.Commands.SequenceEqual(new[]{ECommand.EndShooting,ECommand.Escape,ECommand.EndSprint,ECommand.StopTalk})&&result.Axes.All(x=>x==0),"owner preserves release commands and Escape, blocks actions and axes");
Collect(m,ECommand.ToggleShooting); UiClose(a);
Check(ArenaInputBlocker.Blocked,"close is not immediate input release");
result=Deliver(m);
Check(!result.Commands.Contains(ECommand.ToggleShooting)&&ArenaInputBlocker.Blocked,"pre-close collected batch cannot release");
for(int i=0;i<4;i++)
{
    Input.Held[0]=true; Collect(m,ECommand.ToggleShooting); result=Deliver(m);
    Check(!result.Commands.Contains(ECommand.ToggleShooting)&&ArenaInputBlocker.Blocked,"held pointer keeps close protected");
}
Input.Held[0]=false; Input.Down[0]=true; Collect(m,ECommand.ToggleShooting); Deliver(m);
Check(ArenaInputBlocker.Blocked,"same-frame down and up is not a quiet sample");
Input.Down[0]=false; Collect(m,ECommand.EndShooting,ECommand.Escape);
Deliver(m,true);
Check(ArenaInputBlocker.Blocked,"SuperSmooth empty delivery cannot drain real collection");
result=Deliver(m);
Check(!ArenaInputBlocker.Blocked&&result.Commands.SequenceEqual(new[]{ECommand.EndShooting,ECommand.Escape}),"first released real batch drains only after filtering");
Input.Held[0]=Input.Down[0]=true; Collect(m,ECommand.ToggleShooting);
Check(Deliver(m).Commands.Contains(ECommand.ToggleShooting),"fresh deliberate shooting works after drain");

// Do not confuse an empty real collection with the separate SuperSmooth list.
m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a); Collect(m); Deliver(m);
Check(!ArenaInputBlocker.Blocked,"empty real collection can complete release");

m=Setup(); var b=new object(); ArenaInputBlocker.Request(a,true); ArenaInputBlocker.Request(b,true);
UiClose(a); Collect(m,ECommand.ToggleShooting); Deliver(m);
Check(ArenaInputBlocker.Blocked,"other owners remain authoritative");
ArenaInputBlocker.Request(b,false); Collect(m,ECommand.ToggleShooting); result=Deliver(m);
Check(!result.Commands.Contains(ECommand.ToggleShooting)&&!ArenaInputBlocker.Blocked,"UI drain survives another owner's later non-UI release");
m=Setup(); ArenaInputBlocker.Request(a,true); ArenaInputBlocker.Request(a,false);
Check(!ArenaInputBlocker.Blocked,"non-UI release keeps old immediate behavior");
UiClose(a); UiClose(b); UiClose(null);
Check(!ArenaInputBlocker.Blocked,"unknown repeated and null releases cannot latch input");

m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a); Collect(m);
Input.Held[0]=true; Collect(m,ECommand.ToggleShooting); Deliver(m);
Check(ArenaInputBlocker.Blocked,"later held collection invalidates earlier release sample");
Input.Held[0]=false; Collect(m,ECommand.ToggleShooting);
Deliver(m,false,()=>{ArenaInputBlocker.Request(a,true); UiClose(a);});
Check(ArenaInputBlocker.Blocked,"reentrant reopen and close cannot consume new generation");
Deliver(m);
Check(ArenaInputBlocker.Blocked,"second close requires its own fresh collection");
Collect(m); Deliver(m);
Check(!ArenaInputBlocker.Blocked,"new generation eventually releases");

m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a); Collect(m);
ArenaInputBlocker.Request(a,true); ArenaInputBlocker.Request(a,false); Deliver(m);
Check(ArenaInputBlocker.Blocked,"reopen invalidates a previously ready batch even without UI close flag");
Collect(m); Deliver(m); Check(!ArenaInputBlocker.Blocked,"reopen release recovers on fresh delivery");

m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a); Collect(m);
Deliver(m,false,()=>ArenaInputBlocker.Request(b,true));
Check(ArenaInputBlocker.Blocked,"owner acquired during dispatch stays blocked");
ArenaInputBlocker.Request(b,false); Deliver(m);
Check(ArenaInputBlocker.Blocked,"owner acquired during dispatch invalidates the old release sample");
Collect(m); Deliver(m);
Check(!ArenaInputBlocker.Blocked,"new owner handoff eventually releases");

m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a);
AfterCollect(new InputPreset(),m._commandsList,m._axesList); Deliver(m);
Check(ArenaInputBlocker.Blocked,"foreign preset cannot supply release evidence");
AfterCollect(InputManager._currentPreset,new List<ECommand>(),m._axesList); Deliver(m);
Check(ArenaInputBlocker.Blocked,"foreign list cannot supply release evidence");
AfterCollect(InputManager._currentPreset,m._commandsList,new float[3]); Deliver(m);
Check(ArenaInputBlocker.Blocked,"foreign axes cannot supply release evidence");
InputManager._currentPreset._workingKeyCombinations=null; Collect(m); Deliver(m);
Check(ArenaInputBlocker.Blocked,"uninitialized combinations cannot certify fresh input");
InputManager._currentPreset._workingKeyCombinations=[]; Collect(m); Deliver(m);
Check(!ArenaInputBlocker.Blocked,"initialized collection recovers pending release");

foreach(string change in new[]{"manager","preset","commands","axes","destroy"})
{
    m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a);
    if(change=="manager")m=new();
    if(change=="preset")InputManager._currentPreset=new();
    if(change=="commands")m._commandsList=new();
    if(change=="axes")m._axesList=new float[3];
    if(change=="destroy")Harmony.Hooks[destroy].Prefix.Method.Invoke(null,[m]);
    BeforeUpdate(m); Check(!ArenaInputBlocker.Blocked,"old input release cannot strand replacement: "+change);
    ArenaInputBlocker.Request(a,true); BeforeUpdate(new InputManager());
    Check(ArenaInputBlocker.Blocked,"manager replacement preserves still-open UI owner");
}
m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a);
BeforeUpdate(new InputManager{_isMain=false}); Collect(m); Deliver(m);
Check(!ArenaInputBlocker.Blocked,"foreign non-main manager cannot steal release ownership");

foreach(string change in new[]{"inactive","world","game"})
{
    m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a);
    if(change=="inactive")ArenaSession.Active=false;
    if(change=="world")Singleton<GameWorld>.Instance=new();
    if(change=="game")Singleton<AbstractGame>.Instance=new();
    Collect(m,ECommand.ToggleShooting); result=Deliver(m);
    Check(!ArenaInputBlocker.Blocked&&result.Commands.Contains(ECommand.ToggleShooting),"scope exit cannot leak Arena input blocking: "+change);
}
// No manager observed yet: a pending close must bind the first real collection.
m=Setup(); ArenaInputBlocker.Shutdown(); ArenaInputBlocker.Apply();
ArenaInputBlocker.Request(a,true); UiClose(a); Collect(m); Deliver(m);
Check(!ArenaInputBlocker.Blocked,"first manager observation resolves pending close");
for(int fail=1;fail<=4;fail++)
{
    ArenaInputBlocker.Shutdown(); Harmony.FailAt=fail; ArenaInputBlocker.Apply();
    Check(Harmony.Hooks.Count==0,"partial hook installation rolls back");
    ArenaInputBlocker.Request(a,true); UiClose(a);
    Check(!ArenaInputBlocker.Blocked,"failed installation cannot leave an undrainable latch");
}
ArenaInputBlocker.Shutdown();
Check(Harmony.Hooks.Count==0&&!ArenaInputBlocker.Blocked,"shutdown releases state and hooks");

m=Setup(); ArenaInputBlocker.Request(a,true); UiClose(a);
Harmony.FailUnpatch=true;
ArenaInputBlocker.Shutdown();
Check(!ArenaInputBlocker.Blocked,"failed unpatch still releases movement blocking");
Harmony.FailUnpatch=false; ArenaInputBlocker.Apply();
Check(!ArenaInputBlocker.Blocked,"reinstall cannot inherit a failed cleanup's old close");
ArenaInputBlocker.Request(a,true); UiClose(a); Harmony.FailUnpatch=true; ArenaInputBlocker.Shutdown();
ArenaInputBlocker.Request(b,true); // A surviving UI may still publish its vote.
Collect(m,ECommand.ToggleShooting); result=Deliver(m);
Check(result.Commands.Contains(ECommand.ToggleShooting)&&result.Axes.All(x=>x==1),"remaining detour after failed unpatch is harmless");
Harmony.FailUnpatch=false; ArenaInputBlocker.Shutdown();
Check(Harmony.Hooks.Count==0,"cleanup retry removes remaining detours");
Harmony.FailAt=2; Harmony.FailUnpatch=true;
ArenaInputBlocker.Apply();
Check(!ArenaInputBlocker.Blocked,"partial installation with failed rollback does not throw or block");
Harmony.FailUnpatch=false; ArenaInputBlocker.Shutdown();
Console.WriteLine($"ARENA_INPUT_CHECKS_PASSED ({checks} assertions)");
