using ArenaMode.Bots;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SAIN.Components;
using System.Reflection;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
bool Throws<T>(Action action) where T : Exception
{ try { action(); return false; } catch(Exception ex) { return ex.GetBaseException() is T; } }
bool Prefix(MethodInfo target, object instance)
{
    if (!Harmony.Hooks.TryGetValue(target, out var hook)) return true;
    var value=hook.Prefix.method.Invoke(null,[instance]);
    return value is not bool allowed || allowed;
}
BotOwner Setup(bool active=true)
{
    ArenaBotLifecycleScope.Clear();
    ArenaSession.IsActive=active; ArenaSession.ArmedForNextRaid=false;
    ArenaSession.World=Singleton<GameWorld>.Instance=new();
    ArenaSession.Game=Singleton<AbstractGame>.Instance=new();
    var owner=new BotOwner { GetPlayer=new() };
    owner.ItemTaker=new() {_owner=owner};
    Singleton<GameWorld>.Instance.Players[owner.GetPlayer.ProfileId]=owner.GetPlayer;
    return owner;
}
BotItemTaker.CG_PickupAction Pending(BotOwner owner)
{
    var action=new BotItemTaker.CG_PickupAction {owner=owner.GetPlayer,botItemTaker_0=owner.ItemTaker};
    owner.GetPlayer.MovementContext.PickupAction=action.method_0;
    return action;
}
var dispose=typeof(BotOwner).GetMethod("Dispose");
var disable=typeof(BotComponent).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic);
var original=Setup(); Pending(original); original.Dispose();
Check(Throws<ArgumentException>(()=>original.GetPlayer.MovementContext.PickupAction()),"pickup failure fixture positive control");
Check(Throws<NullReferenceException>(()=>disable.Invoke(new BotComponent(),null)),"uninitialized component positive control");
Harmony.Reject=true; ArenaPickupTeardownSafety.Enable();
Check(Harmony.Hooks.Count==0,"failed pickup installation rolled back");
Harmony.Reject=false; ArenaPickupTeardownSafety.Enable();
Check(Harmony.Hooks.ContainsKey(dispose),"pickup disposal hook installed");

foreach (string scenario in new[] {"owned", "ordinary", "preactive", "foreign-body", "foreign-taker", "different-owner", "different-method", "multicast", "foreign-action", "retired", "new-world", "missing-context"})
{
    var owner=Setup(scenario!="ordinary"); var action=Pending(owner);
    if(scenario=="preactive") owner.BotState=EBotState.PreActive;
    if(scenario=="foreign-body") action.owner=new();
    if(scenario=="foreign-taker") owner.ItemTaker=new() {_owner=owner};
    if(scenario=="different-owner") action.botItemTaker_0._owner=new();
    if(scenario=="different-method") owner.GetPlayer.MovementContext.PickupAction=action.OtherAction;
    if(scenario=="multicast") owner.GetPlayer.MovementContext.PickupAction+=()=>{};
    if(scenario=="foreign-action") owner.GetPlayer.MovementContext.PickupAction=()=>{};
    if(scenario is "retired" or "new-world")
    {
        Check(ArenaBotLifecycleScope.Owns(owner),"stamp owned body before ending");
        ArenaSession.IsActive=false;
        if(scenario=="new-world") Singleton<GameWorld>.Instance=new();
    }
    if(scenario=="missing-context") owner.GetPlayer.MovementContext=null;
    var before=owner.GetPlayer.MovementContext?.PickupAction;
    Prefix(dispose,owner);
    Check(owner.Disposals==0 && action.Calls==0,"prefix never runs disposal or pickup");
    bool cancel=scenario is "owned" or "retired";
    Check(cancel ? owner.GetPlayer.MovementContext.PickupAction==null : owner.GetPlayer.MovementContext?.PickupAction==before,
        "exact pending pickup cancellation: "+scenario);
    owner.Dispose();
    Check(owner.Disposals==(scenario=="preactive"?0:1),"original disposal remains native");
    if(cancel) { owner.GetPlayer.MovementContext.PickupAction?.Invoke(); Check(action.Calls==0,"retired animator has no stale transaction"); }
}
Check(!ArenaSainDisableSafety.TryBind(null),"missing dependency fails open");
Harmony.Reject=true;
Check(!ArenaSainDisableSafety.TryBind(typeof(BotComponent).Assembly) && Harmony.Hooks.ContainsKey(dispose),"optional hook failure preserves pickup guard");
Harmony.Reject=false;
Check(ArenaSainDisableSafety.TryBind(typeof(BotComponent).Assembly),"reviewed two-call contract bound");
var getter=typeof(BotComponent).GetProperty("BotActivation").GetMethod;
var activate=getter.ReturnType.GetMethod("SetActive");
var stop=typeof(UnityEngine.MonoBehaviour).GetMethod("StopAllCoroutines");
Check(!ArenaSainDisableSafety.MatchesDisableBody(typeof(BotComponent).GetMethod("ChangedDisable",BindingFlags.Instance|BindingFlags.NonPublic),getter,activate,stop),"changed disable body rejected");
foreach(string scenario in new[]{"uninitialized","initialized","ordinary","assigned-other-owner","no-native-owner","retired","new-world","live-failure"})
{
    var owner=Setup(scenario!="ordinary"); var component=new BotComponent();
    if(scenario!="no-native-owner") component.Components[typeof(BotOwner)]=owner;
    if(scenario=="assigned-other-owner") component.BotOwner=new();
    if(scenario is "initialized" or "live-failure") component.BotActivation=new() {Throw=scenario=="live-failure"};
    if(scenario is "retired" or "new-world")
    {
        Check(ArenaBotLifecycleScope.Owns(owner),"stamp disable lifetime"); ArenaSession.IsActive=false;
        if(scenario=="new-world") Singleton<GameWorld>.Instance=new();
    }
    bool run=Prefix(disable,component); bool guarded=scenario is "uninitialized" or "retired";
    Check(run!=guarded,"disable original admission: "+scenario);
    Check(component.CoroutineStops==(guarded?1:0),"only guarded missing activation retains coroutine cleanup");
    if(scenario=="initialized") { disable.Invoke(component,null); Check(component.BotActivation.Calls==1&&!component.BotActivation.LastValue&&component.CoroutineStops==1,"live activation runs complete original"); }
    if(scenario=="live-failure") Check(Throws<InvalidOperationException>(()=>disable.Invoke(component,null)),"live original failure never masked");
}
ArenaSainDisableSafety.Shutdown(); ArenaPickupTeardownSafety.Shutdown();
Check(Harmony.Hooks.Count==0,"shutdown removes only own detours");
Console.WriteLine($"ARENA_EXIT_CHECKS_PASSED ({checks} assertions)");
