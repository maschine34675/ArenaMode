using ArenaMode.Combat;
using ArenaMode.Modes;
using ArenaMode.Pickups;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

static class Checks
{
    private static int _count;
    private static Player _local, _victim;
    private static MethodInfo _damagePrefix, _deathPrefix;

    static int Main()
    {
        try
        {
            ArenaPowerups.Enable();
            var installed = ModulePatch.Enabled;
            Check(installed.Count == 2, "only health and death patches enabled");
            var hit = installed.Single(x => x.Target.Name == "ApplyDamage");
            Check(hit.Target.DeclaringType == typeof(ActiveHealthController), "one native health seam");
            _damagePrefix = hit.Patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            Check(_damagePrefix.GetCustomAttribute<HarmonyLib.HarmonyPriority>()?.priority == HarmonyLib.Priority.Last,
                "damage prefix runs after ordinary protection prefixes");
            Check(_damagePrefix.GetParameters().Single(p => p.Name == "__runOriginal").ParameterType == typeof(bool), "prefix receives native run-original decision");
            _deathPrefix = installed.Single(x => x.Target.Name == "OnDead").Patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            ArenaPowerups.Enable();
            Check(installed.Count == 2, "enable is idempotent");
            Durations(); Damage(); Scope(); Lifetimes(); JumpBoots(); InfiniteAmmo();
            Console.WriteLine($"ARENA_POWERUP_BEHAVIOR_PASSED: {_count} production policy/adapter assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; }
    }

    private static void Check(bool value, string name) { _count++; if (!value) throw new Exception("ASSERT: " + name); }
    private static void InfiniteAmmo()
    {
        var state=new ArenaPowerupState();Check(state.GrantAmmo(100,100),"ammo duration admitted");
        Near(10,state.Read(100).AmmoSeconds,"ammo duration capped at ten seconds");
        state.GrantAmmo(105,10);Near(10,state.Read(105).AmmoSeconds,"ammo refresh does not stack");
        Near(0,state.Read(115).AmmoSeconds,"ammo expires at exact deadline");
        foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity}) Check(!state.GrantAmmo(1,bad),"invalid ammo duration rejected");
        Fresh();Check(ArenaPowerups.GrantAmmo(_local,10),"local ammo grant");
        ArenaInfiniteAmmo.Enabled=false;Check(!ArenaPowerups.GrantAmmo(_local,10),"missing native ammo patch rejects collection");ArenaInfiniteAmmo.Enabled=true;
        _victim.AIData.IsAI=true;Check(!ArenaPowerups.GrantAmmo(_victim,10),"bot ammo grant refused");
        foreach(Action invalidate in new Action[]{()=>ArenaPickupRuntime.Generation++,()=>ArenaPickupRuntime.Live=false,
            ()=>_local.ActiveHealthController.IsAlive=false,()=>_local.ActiveHealthController=new(){Player=_local},
            ()=>_deathPrefix.Invoke(null,new object[]{_local}),()=>ArenaPowerups.Reset(),()=>ArenaSession.IsActive=false})
        {Fresh();ArenaPowerups.GrantAmmo(_local,10);invalidate();Near(0,ArenaPowerups.Read(_local).AmmoSeconds,"ammo cannot survive scope life or round reset");}
    }
    private static void Near(float expected, float actual, string name)
        => Check(float.IsFinite(actual) && Math.Abs(expected - actual) <= 0.0005f * Math.Max(1f, Math.Abs(expected)), name);
    private static void Fresh()
    {
        Time.time = 100f; ArenaSession.IsActive = true; ArenaPickupRuntime.Live = true;
        ArenaPickupRuntime.Registered.Clear(); ArenaPickupRuntime.Generation++;
        ArenaAdministrativeDeathGate.Reset(); ArenaMatchService.Current = new();
        _local = new Player("local") { Team = 1 }; _victim = new Player("victim") { Team = 2 };
        ArenaPickupRuntime.Registered.Add(_local); ArenaPickupRuntime.Registered.Add(_victim);
        Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = _local };
        Singleton<GameWorld>.Instance.Ever[_local.ProfileId] = _local;
        Singleton<GameWorld>.Instance.Ever[_victim.ProfileId] = _victim;
        ArenaPowerups.Reset();
    }
    private static DamageInfo Info(EDamageType type = EDamageType.Bullet, Player attacker = null)
        => new() { Damage = 777f, DamageType = type, Player = new Bridge { iPlayer = attacker ?? _local }, Weapon = new EFT.InventoryLogic.Weapon() };
    private static float Hit(float damage = 25f, EBodyPart body = EBodyPart.Chest, EDamageType type = EDamageType.Bullet,
        bool run = true, Player attacker = null, Player victim = null)
    {
        var info = Info(type, attacker);
        object[] args = { (victim ?? _victim).ActiveHealthController, body, damage, info, run };
        _damagePrefix.Invoke(null, args);
        Near(777f, ((DamageInfo)args[3]).Damage, "source damage info remains unchanged");
        return (float)args[2];
    }
    private static void Both()
    {
        Check(ArenaPowerups.GrantDamage(_local, 20f), "damage grant admitted");
        Check(ArenaPowerups.GrantShield(_victim, 100f, 20f), "shield grant admitted");
    }
    private static void Durations()
    {
        Fresh(); Both();
        Near(20, ArenaPowerups.Read(_local).DamageSeconds, "damage defaults20");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "explicit smaller shield pool");
        Near(20, ArenaPowerups.Read(_victim).ShieldSeconds, "explicit shorter shield duration");
        Time.time += 7;
        Near(13, ArenaPowerups.Read(_local).DamageSeconds, "scaled game clock duration");
        ArenaPowerups.GrantDamage(_local, 1000); ArenaPowerups.GrantShield(_victim, 1000, 1000);
        Near(20, ArenaPowerups.Read(_local).DamageSeconds, "refresh does not stack duration");
        Near(30, ArenaPowerups.Read(_victim).ShieldSeconds, "shield duration hard cap");
        Near(220, ArenaPowerups.Read(_victim).ShieldPoints, "shield point hard cap");
        Time.time += 19.5f;
        Near(0.5f, ArenaPowerups.Read(_local).DamageSeconds, "active immediately before expiry");
        Time.time += 0.5f;
        Near(0, ArenaPowerups.Read(_local).DamageSeconds, "damage expires at deadline");
        Near(10, ArenaPowerups.Read(_victim).ShieldSeconds, "shield outlasts double damage by ten seconds");
        Time.time += 9.5f;
        Near(.5f, ArenaPowerups.Read(_victim).ShieldSeconds, "shield remains active before its own deadline");
        Time.time += .5f;
        Near(0, ArenaPowerups.Read(_victim).ShieldPoints, "shield expires at deadline");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Check(!ArenaPowerups.GrantDamage(_local, invalid), "invalid damage duration rejected");
            Check(!ArenaPowerups.GrantShield(_victim, 100, invalid), "invalid shield duration rejected");
            Check(!ArenaPowerups.GrantShield(_victim, invalid, 20), "invalid shield points rejected");
        }
        ArenaPowerups.GrantShield(_victim, 10, 5); Near(10, ArenaPowerups.Read(_victim).ShieldPoints, "explicit smaller shield accepted");
        Time.time = float.NaN;
        Check(!ArenaPowerups.GrantDamage(_local, 20), "invalid clock rejects grant");
        Near(0, ArenaPowerups.Read(_victim).ShieldPoints, "invalid clock hides effects");
    }
    private static void JumpBoots()
    {
        Fresh(); Check(ArenaPowerups.GrantJump(_local, 300), "boots grant admitted");
        Near(30, ArenaPowerups.Read(_local).JumpSeconds, "boots duration capped at thirty");
        Time.time += 10; ArenaPowerups.GrantJump(_local, 30);
        Near(30, ArenaPowerups.Read(_local).JumpSeconds, "boots refresh never adds duration");
        Time.time += 29.5f; Near(.5f, ArenaPowerups.Read(_local).JumpSeconds, "boots remain before expiry");
        Time.time += .5f; Near(0, ArenaPowerups.Read(_local).JumpSeconds, "boots expire at deadline");
        foreach(float bad in new[]{0,-1,float.NaN,float.PositiveInfinity})
            Check(!ArenaPowerups.GrantJump(_local,bad), "invalid boots duration rejected");
        Fresh(); _local.AIData.IsAI=true; Check(!ArenaPowerups.GrantJump(_local,30), "bots cannot receive jump boots");
        foreach(var invalidate in new Action[]{()=>ArenaPowerups.Forget(_local),()=>ArenaPowerups.Reset(),
            ()=>ArenaPickupRuntime.Generation++,()=>_local.ActiveHealthController=new(){Player=_local},
            ()=>ArenaPickupRuntime.Registered.Remove(_local),()=>_local.ActiveHealthController.IsAlive=false,
            ()=>ArenaPickupRuntime.Live=false,()=>ArenaSession.IsActive=false})
        {Fresh();ArenaPowerups.GrantJump(_local,30);invalidate();Near(0,ArenaPowerups.Read(_local).JumpSeconds,"boots cannot survive life or scope loss");}
        Fresh();ArenaSession.IsActive=false;Check(!ArenaPowerups.GrantJump(_local,30),"ordinary raid cannot grant boots");
    }
    private static void Damage()
    {
        Fresh(); Near(25, Hit(), "unbuffed impact unchanged");
        ArenaPowerups.GrantDamage(_local, 20);
        Near(50, Hit(), "single double damage application");
        ArenaPowerups.GrantDamage(_local, 20); Near(50, Hit(), "damage recollection does not quadruple");
        ArenaPowerups.GrantShield(_victim, 100, 20);
        Near(0, Hit(), "shield absorbs doubled impact");
        Near(50, ArenaPowerups.Read(_victim).ShieldPoints, "shield spends final doubled impact once");
        Near(10, Hit(30), "depleted shield passes only remainder");
        Near(0, ArenaPowerups.Read(_victim).ShieldSeconds, "depleted shield timer cleared");
        ArenaPowerups.GrantShield(_victim, 100, 20); Near(0, Hit(10), "shield can be refilled");
        ArenaPowerups.GrantShield(_victim, 100, 20); Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "refill resets pool without addition");
        foreach (var body in new[] { EBodyPart.Head, EBodyPart.Chest, EBodyPart.LeftArm })
        foreach (var mult in new[] { 0.25f, 0.5f, 1f, 2f, 4f })
        {
            Fresh(); _victim.ActiveHealthController.DamageMultiplier = mult;
            ArenaPowerups.GrantShield(_victim, 100, 20);
            float factor = body == EBodyPart.Head && mult <= 1 ? 1 : mult;
            Near(Math.Max(0, (80 * factor - 100) / factor), Hit(80, body), "native incoming multiplier and head exemption preserved");
            Near(Math.Max(0, 100 - 80 * factor), ArenaPowerups.Read(_victim).ShieldPoints, "shield uses post-multiplier points");
        }
        Fresh(); Both();
        foreach (var type in Enum.GetValues<EDamageType>())
        {
            if (type is EDamageType.Bullet or EDamageType.Explosion or EDamageType.GrenadeFragment) continue;
            Near(25, Hit(type: type), "non-projectile damage unchanged " + type);
            Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "non-projectile damage does not spend shield " + type);
        }
        Near(25, Hit(type: EDamageType.Bullet | EDamageType.Fall), "combined damage flags fail closed");
        foreach (var type in new[] { EDamageType.Bullet, EDamageType.Explosion, EDamageType.GrenadeFragment })
        {
            Fresh(); Both(); Near(0, Hit(type: type), "admitted projectile type shielded " + type);
            Near(type == EDamageType.Bullet ? 50 : 75, ArenaPowerups.Read(_victim).ShieldPoints, "only direct bullet damage doubled " + type);
        }
        Fresh(); Both(); var info = Info(); info.OverDamageFrom = EBodyPart.LeftArm;
        Near(25, ArenaPowerups.Adjust(_victim.ActiveHealthController, EBodyPart.Chest, 25, info, true), "overflow provenance not boosted or charged twice");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "overflow guard leaves pool");
        foreach (var invalid in new[] { -1f, 0f, float.NaN, float.PositiveInfinity })
        {
            var actual = Hit(invalid);
            Check(actual.Equals(invalid), "invalid damage is preserved");
            Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "invalid damage does not spend");
        }
        Near(float.MaxValue, Hit(float.MaxValue), "double overflow fails open");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "overflow does not spend");
        _victim.ActiveHealthController.DamageMultiplier = float.MaxValue;
        Near(25, Hit(), "native multiplier overflow fails open");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "multiplier overflow does not spend");
        foreach (float mult in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            _victim.ActiveHealthController.DamageMultiplier = mult;
            Near(25, Hit(), "invalid multiplier does not rewrite native input");
            Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "invalid multiplier does not spend");
        }
    }
    private static void Scope()
    {
        Fresh(); Both();
        Near(25, Hit(run: false), "earlier damage veto leaves input unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "earlier damage veto leaves shield intact");
        _victim.ActiveHealthController.DamageCoeff = 0;
        Near(25, Hit(), "native invulnerability leaves input unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "native invulnerability leaves shield intact");
        Near(0, _victim.ActiveHealthController.DamageCoeff, "powerup never changes invulnerability coefficient");
        _victim.ActiveHealthController.DamageCoeff = 1;
        ArenaSession.IsActive = false;
        Near(25, Hit(), "ordinary raid unchanged");
        Check(!ArenaPowerups.GrantShield(_victim, 100, 20), "ordinary raid grants refused");
        ArenaSession.IsActive = true; ArenaPickupRuntime.Live = false;
        Near(25, Hit(), "round break input unchanged");
        ArenaPickupRuntime.Live = true;
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "blocked round call does not debit shield");
        foreach (var mode in new object[] { new object(), null, new TeamDeathmatchMode { Block = true }, new RoundsMode { Block = true } })
        {
            ArenaMatchService.Current.Mode = mode; Near(25, Hit(), "unsupported or protected mode unchanged");
        }
        ArenaMatchService.Current.Mode = new TeamDeathmatchMode(); _victim.Team = _local.Team;
        Near(25, Hit(), "TDM friendly impact unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "TDM friendly impact leaves shield");
        _victim.Team = 2; Near(0, Hit(), "TDM opposing impact admitted");
        Fresh(); Both(); ArenaMatchService.Current.Mode = new RoundsMode { FreeForAll = false };
        var other = new Player("bot3"); ArenaPickupRuntime.Registered.Add(other);
        Singleton<GameWorld>.Instance.Ever[other.ProfileId] = other;
        Near(25, Hit(attacker: other), "all vs player friendly bot damage unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "all vs player friendly damage leaves shield");
        Near(25, Hit(attacker: _victim), "self damage unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "self damage leaves shield");
        using (ArenaAdministrativeDeathGate.Enter(_victim.ProfileId)) Near(25, Hit(), "administrative victim untouched");
        using (ArenaAdministrativeDeathGate.Enter(_local.ProfileId)) Near(25, Hit(), "administrative source untouched");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "admin rejection leaves shield");
        ArenaMatchService.Current.Mode = new RoundsMode();
        _local.AIData.IsAI = _victim.AIData.IsAI = true;
        _local.AIData.BotOwner.Settings.FileSettings.Mind.MUTUAL_IGNORE_FRIENDLY_FIRE = true;
        _victim.AIData.BotOwner.Settings.FileSettings.Mind.MUTUAL_IGNORE_FRIENDLY_FIRE = true;
        Near(25, Hit(), "native mutual AI friendly ignore unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "native mutual AI friendly ignore leaves shield");
        _local.AIData.BotOwner.Settings.FileSettings.Mind.MUTUAL_IGNORE_FRIENDLY_FIRE = false;
        Near(0, Hit(), "one AI flag alone does not suppress hostile impact");
        Fresh(); Both(); var ownerless = Info(); ownerless.Player = null;
        Near(25, ArenaPowerups.Adjust(_victim.ActiveHealthController, EBodyPart.Chest, 25, ownerless, true), "unattributed projectile unchanged");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "unattributed projectile leaves shield");
    }
    private static void Lifetimes()
    {
        Fresh(); Both(); var old = _local; var replacement = new Player(old.ProfileId);
        ArenaPickupRuntime.Registered.Remove(old); ArenaPickupRuntime.Registered.Add(replacement);
        Singleton<GameWorld>.Instance.Ever[old.ProfileId] = replacement;
        Near(25, Hit(), "unregistered old source cannot use successor identity");
        Near(0, ArenaPowerups.Read(replacement).DamageSeconds, "same profile replacement inherits no powerup");
        Fresh(); Both(); _local.ActiveHealthController.IsAlive = false;
        Near(0, Hit(type: EDamageType.Explosion), "dead thrower grenade still meets shield");
        Near(75, ArenaPowerups.Read(_victim).ShieldPoints, "dead thrower grenade has no outgoing bonus");
        Near(0, Hit(), "dead bullet owner still meets shield");
        Near(50, ArenaPowerups.Read(_victim).ShieldPoints, "dead bullet owner has no outgoing bonus");
        Fresh(); Both();
        Near(0, Hit(type: EDamageType.GrenadeFragment), "grenade source rebuilt as live body still meets shield");
        Near(75, ArenaPowerups.Read(_victim).ShieldPoints, "old grenade cannot inherit replacement damage bonus");
        Fresh(); Both(); ArenaMatchService.Current.Mode = new TeamDeathmatchMode(); _local.ActiveHealthController.IsAlive = false;
        ArenaPickupRuntime.Registered.Remove(_local); Singleton<GameWorld>.Instance.Ever.Remove(_local.ProfileId);
        Near(0, Hit(type: EDamageType.Explosion), "TDM retained dead enemy affiliation shields grenade");
        Near(75, ArenaPowerups.Read(_victim).ShieldPoints, "TDM retained grenade has no outgoing bonus");
        foreach (var weapon in new EFT.InventoryLogic.Item[] { new EFT.InventoryLogic.ThrowWeap(), new EFT.InventoryLogic.Weapon { IsGrenadeLauncher = true }, null })
        {
            Fresh(); Both(); var fragment = Info(); fragment.Weapon = weapon;
            Near(0, ArenaPowerups.Adjust(_victim.ActiveHealthController, EBodyPart.Chest, 25, fragment, true), "physical grenade fragment and unknown weapon shielded");
            Near(75, ArenaPowerups.Read(_victim).ShieldPoints, "physical grenade fragment cannot inherit outgoing bonus");
        }
        ArenaPowerupState state = new(); state.GrantDamage(100,20); state.GrantShield(100,100,20);
        Near(40, ArenaPowerupState.Adjust(20, 1, true, null, 100), "shield-free adjustment doubles once");
        Fresh(); Both(); ArenaPickupRuntime.Generation++;
        Near(0, ArenaPowerups.Read(_local).DamageSeconds, "new generation discards old buff");
        Near(0, ArenaPowerups.Read(_victim).ShieldPoints, "new generation discards old shield");
        Fresh(); Both(); _deathPrefix.Invoke(null, new object[] { _local });
        Near(0, ArenaPowerups.Read(_local).DamageSeconds, "native death clears physical life immediately");
        Fresh(); Both(); _victim.ActiveHealthController.IsAlive = false;
        Near(25, Hit(), "dead victim input unchanged");
        Near(0, ArenaPowerups.Read(_victim).ShieldPoints, "dead victim has no readable shield");
        _victim.ActiveHealthController.IsAlive = true;
        Near(0, ArenaPowerups.Read(_victim).ShieldPoints, "read dead life cannot revive a shield");
        Fresh(); Both(); _local.ActiveHealthController = new() { Player = _local };
        Near(0, ArenaPowerups.Read(_local).DamageSeconds, "replacement health controller cannot inherit buff");
        Fresh(); Both(); var foreignHealth = new ActiveHealthController { Player = _victim };
        Near(25, ArenaPowerups.Adjust(foreignHealth, EBodyPart.Chest,25,Info(),true), "non-owned health controller untouched");
        Near(100, ArenaPowerups.Read(_victim).ShieldPoints, "non-owned controller leaves pool");
        ArenaPowerups.Reset(); Near(0, ArenaPowerups.Read(_victim).ShieldPoints, "explicit reset clears pool");
    }
}
