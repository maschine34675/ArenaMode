using ArenaMode.Config;
using ArenaMode.Movement;
using Comfort.Common;
using EFT;
using EFT.Vaulting;
using UnityEngine;

int assertions=0;
void Check(bool condition,string label) { assertions++; if(!condition) throw new Exception("ASSERT: "+label); }
(Player p,MovementContext c,VaultingComponent v) Setup()
{
    ArenaAirVault.Shutdown(); ArenaMovementScope.Active=true; Settings.AirVaultEnabled.Value=true; Time.unscaledTime=1;
    Singleton<GameWorld>.Instance=new(); Singleton<AbstractGame>.Instance=new();
    var settings=new SettingsModel(); Singleton<GlobalConfiguration>.Instance=new() { VaultingSettings=settings };
    var p=new Player(); var c=new MovementContext { _player=p, CurrentState=new JumpPlayerState() };
    p.MovementContext=c; var v=new VaultingComponent { _vaultingContext=c,_vaultingSettings=settings };
    v._vaultingModelDebug.VaultingStatesModel.ClimbState.Allowed=true;
    p.VaultingComponent=v; p.VaultingParameters=v; p.VaultingGameplayRestrictions=new(p,v);
    ArenaMovementScope.Local=p; ArenaAirVault.Enable(); return(p,c,v);
}
void Denied(Action<Player,MovementContext,VaultingComponent> alter,string label)
{
    var(p,c,v)=Setup(); alter(p,c,v); p.ToggleVaulting();
    Check(!c.PlayerAnimator.Climb && !c.PlayerAnimator.Vault && p.VaultNotifications==0,label);
}

var initial=Setup();
initial.p.ToggleVaulting();
Check(initial.c.PlayerAnimator.Climb && !initial.c.PlayerAnimator.Vault,"valid airborne geometry queues native climb");
Check(initial.p.LeftHandRemoved==1 && initial.p.VaultNotifications==1,"native TryVaulting owns successful handoff side effects");
Check(!initial.c.IsGrounded && initial.c.JumpSet,"air request never writes grounded or jump flag");
Check(initial.v.Ticks==2,"manual geometry refreshed before gameplay and native commit");
Check(ArenaAirVault.HasPendingRequest(initial.p),"accepted native mantle takes precedence over double jump");
Check(initial.v.GetVaultingStrategy()==EVaultingStrategy.None,"strategy exception does not survive synchronous request");
Check(initial.v.GetAutoVaultingStrategy()==EVaultingStrategy.None,"automatic vaulting remains native while airborne");
initial.p.ToggleVaulting(); Check(initial.p.VaultNotifications==1,"pending transition cannot submit duplicate mantle");
initial.c.CurrentState=new VaultingMovementState(); initial.c.Vaulting=true; ArenaAirVault.Tick();
Check(!ArenaAirVault.HasPendingRequest(initial.p) && initial.c.PlayerAnimator.Climb,"native entered state keeps ownership of its flag");
ArenaAirVault.Shutdown(); Check(initial.c.PlayerAnimator.Climb,"shutdown never cancels native entered movement");

Denied((p,c,v)=>ArenaMovementScope.Active=false,"ordinary raid never receives airborne strategy");
Denied((p,c,v)=>Settings.AirVaultEnabled.Value=false,"disabled setting preserves native airborne refusal");
Denied((p,c,v)=>p.Ai=true,"bots cannot receive airborne strategy");
Denied((p,c,v)=>p.Alive=false,"dead local cannot request mantle");
Denied((p,c,v)=>p.InputBlocked=true,"blocked UI cannot request mantle");
Denied((p,c,v)=>ArenaMovementScope.Local=new Player(),"replacement local excludes old body");
Denied((p,c,v)=>p.IsVaultingPressed=false,"normal jump without held vault is untouched");
Denied((p,c,v)=>c.CurrentState=new FallDown(),"walk off FallDown is excluded");
Denied((p,c,v)=>((JumpPlayerState)c.CurrentState)._jumpState=JumpPlayerState.EJumpState.Bumbped,"halted jump is excluded");
Denied((p,c,v)=>c.IsGrounded=true,"grounded jump cannot use airborne exception");
Denied((p,c,v)=>p.IsForwardInputDirection=false,"forward input remains required");
Denied((p,c,v)=>c.MovementDirection=new(0,-1,0),"native backward restriction remains");
Denied((p,c,v)=>c.IsInMountedState=true,"mounted state cannot request mantle");
Denied((p,c,v)=>c.IsInPronePose=true,"prone state cannot request mantle");
Denied((p,c,v)=>c.IsStationaryWeaponInHands=true,"stationary weapon cannot request mantle");
Denied((p,c,v)=>c.OverridenState=new MovementState(),"overridden motion cannot request mantle");
Denied((p,c,v)=>c.OverridenControlsState=new JumpPlayerState(),"overridden controls cannot request mantle");
Denied((p,c,v)=>c.Transition=true,"native animator transition restriction remains");
Denied((p,c,v)=>c.Vaulting=true,"existing vault cannot be reentered");
Denied((p,c,v)=>((SettingsModel)v._vaultingSettings).Active=false,"native global vault switch remains authoritative");
Denied((p,c,v)=>v._vaultingModelDebug.ObstacleCalculatorModel.IsTerrain=true,"terrain cannot become mantle obstacle");
Denied((p,c,v)=>v._vaultingModelDebug.StairsCalculatorModel.Blocked=true,"native stairs restriction remains");
Denied((p,c,v)=>v._vaultingModelDebug.VaultingStatesModel.ClimbState.Allowed=false,"no native geometry means no mantle or jump consumption");
Denied((p,c,v)=>v._canClimbByStaminaGetter=()=>false,"native climb stamina remains required");
Denied((p,c,v)=>c.IsSprintEnabled=true,"native sprint climb restriction remains");
Denied((p,c,v)=>p.InventoryController.HasActiveEvents=true,"native inventory operation restriction remains");
Denied((p,c,v)=>p.HandsController=new Meds(),"native meds restriction remains");
Denied((p,c,v)=>c.BlindFire=1,"native blind fire restriction remains");
Denied((p,c,v)=>c.PoseToInt=0,"native pose restriction remains");
Denied((p,c,v)=>p.HandsController=new Player.FirearmController { CurrentHandsOperation=new Player.FirearmController.HandsOperation() },"native weapon operation restriction remains");
Denied((p,c,v)=> { var grenade=new Player.GrenadeHandsController(); grenade.FirearmsAnimator.Animator.Value=(int)FirearmsAnimator.EGrenadeFire.Hold; p.HandsController=grenade; },"native grenade hold restriction remains");

var vault=Setup(); vault.v._vaultingModelDebug.VaultingStatesModel.VaultState.Allowed=true; vault.p.ToggleVaulting();
Check(vault.c.PlayerAnimator.Vault && !vault.c.PlayerAnimator.Climb,"native vault preference is preserved");
Denied((p,c,v)=> { c.IsSprintEnabled=true; v.BehindObstacleRatio=0; v._vaultingModelDebug.VaultingStatesModel.VaultState.Allowed=true; },"native sprint vault restriction remains");

var changed=Setup(); changed.p.InventoryController.HasActiveEvents=true; changed.v.VaultingHeight=.2f;
changed.v.OnTick=()=> { if(changed.v.Ticks==2) changed.v.VaultingHeight=1.2f; };
changed.p.ToggleVaulting();
Check(!changed.c.PlayerAnimator.Climb && changed.p.VaultNotifications==0,"final native geometry rechecks hands restrictions before commit");

var bad=Setup(); bad.v.OnTick=()=>throw new InvalidOperationException("geometry refresh failed"); bad.p.ToggleVaulting();
Check(!bad.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(bad.p),"refresh failure cannot reuse cached strategy");
bad.v.OnTick=null; bad.c.IsGrounded=false;
Check(bad.v.GetVaultingStrategy()==EVaultingStrategy.None,"request lease is cleared after exception");

var stale=Setup(); stale.v.OnTick=()=> { if(stale.v.Ticks==2) ArenaMovementScope.Local=new Player(); }; stale.p.ToggleVaulting();
Check(!stale.c.PlayerAnimator.Climb && stale.p.VaultNotifications==0,"body drift during geometry refresh rejects commit");
var staleWorld=Setup(); staleWorld.v.OnTick=()=> { if(staleWorld.v.Ticks==2) Singleton<GameWorld>.Instance=new(); }; staleWorld.p.ToggleVaulting();
Check(!staleWorld.c.PlayerAnimator.Climb && staleWorld.p.VaultNotifications==0,"world drift during geometry refresh rejects commit");
var staleGame=Setup(); staleGame.v.OnTick=()=> { if(staleGame.v.Ticks==2) Singleton<AbstractGame>.Instance=new(); }; staleGame.p.ToggleVaulting();
Check(!staleGame.c.PlayerAnimator.Climb && staleGame.p.VaultNotifications==0,"game drift during geometry refresh rejects commit");
Denied((p,c,v)=>v.VaultingHeight=float.NaN,"nonfinite native parameters cannot enter animator");

var cancel=Setup(); cancel.p.ToggleVaulting(); cancel.p.IsVaultingPressed=false; ArenaAirVault.Tick();
Check(!cancel.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(cancel.p),"releasing held vault clears unaccepted request");
var off=Setup(); off.p.ToggleVaulting(); Settings.AirVaultEnabled.Value=false; ArenaAirVault.Tick();
Check(!off.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(off.p),"setting disable clears only pending owned flag");
var lost=Setup(); lost.p.ToggleVaulting(); lost.v._vaultingModelDebug.VaultingStatesModel.ClimbState.Allowed=false; ArenaAirVault.Tick();
Check(!lost.c.PlayerAnimator.Climb,"lost geometry clears pending native flag");
var hands=Setup(); hands.p.ToggleVaulting(); hands.p.HandsController=new Meds(); ArenaAirVault.Tick();
Check(!hands.c.PlayerAnimator.Climb,"changed hands invalidate pending mantle");
var pendingWorld=Setup(); pendingWorld.p.ToggleVaulting(); Singleton<GameWorld>.Instance=new(); ArenaAirVault.Tick();
Check(pendingWorld.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(pendingWorld.p),"world drift drops ownership without writing reused body's flag");
var pendingGame=Setup(); pendingGame.p.ToggleVaulting(); Singleton<AbstractGame>.Instance=new(); ArenaAirVault.Tick();
Check(pendingGame.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(pendingGame.p),"game drift drops ownership without writing reused body's flag");
var pendingAnimator=Setup(); pendingAnimator.p.ToggleVaulting(); pendingAnimator.c.PlayerAnimator=new() { Climb=true }; ArenaAirVault.Tick();
Check(pendingAnimator.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(pendingAnimator.p),"replacement animator flag is not owned by pending request");
var pendingHandle=Setup(); pendingHandle.p.ToggleVaulting(); pendingHandle.c.PlayerAnimator.Animator=new(); ArenaAirVault.Tick();
Check(pendingHandle.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(pendingHandle.p),"replacement underlying animator flag is not owned by pending request");
var pendingComponent=Setup(); pendingComponent.p.ToggleVaulting(); pendingComponent.p.VaultingComponent=new VaultingComponent(); ArenaAirVault.Tick();
Check(pendingComponent.c.PlayerAnimator.Climb && !ArenaAirVault.HasPendingRequest(pendingComponent.p),"replacement vault component cannot inherit old pending request");
foreach (var changeGeometry in new Action<VaultingComponent>[] {
    v=>v.VaultingHeight+=.2f, v=>v.VaultingLength+=.2f, v=>v.BehindObstacleRatio+=.2f,
    v=>v.AbsoluteForwardVelocity+=.2f, v=>v.MaxWeightPointPosition=new(4.2f,2,1),
    v=>v.VaultingLength=float.PositiveInfinity })
{
    var drift=Setup(); drift.p.ToggleVaulting(); changeGeometry(drift.v); ArenaAirVault.Tick();
    Check(!drift.c.PlayerAnimator.Climb,"same strategy with changed geometry cannot use stale animator parameters");
}
var stable=Setup(); stable.p.ToggleVaulting(); stable.v.VaultingHeight+=.001f; ArenaAirVault.Tick();
Check(stable.c.PlayerAnimator.Climb,"negligible scan noise preserves pending native request");
var timeout=Setup(); timeout.p.ToggleVaulting(); Time.unscaledTime=2; ArenaAirVault.Tick(); timeout.p.ToggleVaulting();
Check(!timeout.c.PlayerAnimator.Climb && timeout.p.VaultNotifications==1,"animation timeout cancels flag and requires release");
timeout.p.IsVaultingPressed=false; ArenaAirVault.Tick(); timeout.p.IsVaultingPressed=true; timeout.p.ToggleVaulting();
Check(timeout.c.PlayerAnimator.Climb && timeout.p.VaultNotifications==2,"release allows fresh geometry request after timeout");
var blend=Setup(); blend.p.ToggleVaulting(); blend.c.Transition=true; ArenaAirVault.Tick();
Check(blend.c.PlayerAnimator.Climb,"pending native transition is not cancelled by new-request transition gate");

var ordinary=Setup(); ArenaMovementScope.Active=false; ordinary.c.IsGrounded=true; ordinary.c.JumpSet=false;
Check(ordinary.v.TryVaulting() && ordinary.c.PlayerAnimator.Climb,"ordinary grounded native mantle is untouched");
ArenaAirVault.Shutdown();
Console.WriteLine($"ARENA_AIR_VAULT_CONTRACTS_PASSED ({assertions} assertions; actual production with extracted native strategy/request and full native gameplay restrictions)");
