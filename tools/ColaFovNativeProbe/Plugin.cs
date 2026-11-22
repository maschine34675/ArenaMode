using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using Redeploy.Core.Spawning;
using UnityEngine;

namespace ColaFovNativeProbe;
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
    public UiBridgeActionAttribute(string name) { Name = name; }
    public string Name { get; } public string Description { get; set; } public bool Control { get; set; }
}

// Temporary disposable-profile observer. Never included in a mod package.
[BepInPlugin("com.maschine.ColaFovNativeProbe", "Cola/FOV native observer", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly List<LocalPlayer> Retained = new();
    static readonly List<object> Events = new();
    static Player Previous;
    static Plugin Self;
    static Player Current => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
    static Type Foreign(string guid) => Chainloader.PluginInfos.TryGetValue(guid, out var p) ? p.Instance.GetType() : null;
    static object Read(object target, string name)
    {
        var type = target as Type ?? target?.GetType(); var instance = target is Type ? null : target;
        return type?.GetField(name, All)?.GetValue(instance) ?? type?.GetProperty(name, All)?.GetValue(instance);
    }
    static int Id(object value) => ReferenceEquals(value, null) ? 0 : value is UnityEngine.Object u ? u.GetInstanceID() : RuntimeHelpers.GetHashCode(value);
    static object ComponentState(object value)
    {
        var component = value as Behaviour;
        return new { id = Id(value), body = component != null ? Id(component.gameObject) : 0, enabled = component != null && component.enabled,
            player = Id(Read(value, "Player")), current = component != null && Current != null && ReferenceEquals(component.gameObject, Current.gameObject) };
    }
    static object Snapshot()
    {
        var player = Current; var common = Foreign("com.fontaine.spt-realism-common-lib");
        var fov = Read(Foreign("com.fontaine.fovfix"), "FovController"); var cola = Foreign("com.tarkovacola.server");
        var currentControllers = common?.GetFields(All).Where(f => f.IsStatic && typeof(Behaviour).IsAssignableFrom(f.FieldType) && f.Name != "GameStateInstance" && f.Name != "AudioControllerInstance")
            .ToDictionary(f => f.Name, f => ComponentState(f.GetValue(null)));
        var previousControllers = Previous == null ? Array.Empty<object>() : Previous.GetComponents<Behaviour>()
            .Where(c => c != null && c.GetType().Assembly == common?.Assembly).Select(c => (object)new { type = c.GetType().Name, state = ComponentState(c) }).ToArray();
        var delegates = common?.Assembly.GetTypes().Where(t => t.Namespace == "RealismCommonLib.Events")
            .SelectMany(t => t.GetFields(All).Where(f => f.IsStatic && typeof(Delegate).IsAssignableFrom(f.FieldType)).Select(f => new { type = t, field = f }))
            .Select(x => new { type = x.type.Name, field = x.field.Name, targets = ((Delegate)x.field.GetValue(null))?.GetInvocationList().Select(d => new { method = d.Method.Name, target = Id(d.Target), state = ComponentState(d.Target) }).ToArray() }).ToArray();
        var colaStates = new Dictionary<string, object>();
        foreach (var name in new[] { "QuickRevive", "MuleKick", "ElectricCherry", "Tombstone", "DeathPerception", "PerkService", "ArenaCompat" })
        {
            var t = cola?.Assembly.GetType("TarkovaCola." + name);
            if (t == null) t = cola?.Assembly.GetTypes().FirstOrDefault(x => x.Name == name);
            if (t == null) continue;
            var fields = t.GetFields(All).Where(f => f.IsStatic && (f.FieldType.IsPrimitive || f.FieldType == typeof(string)))
                .ToDictionary(f => f.Name, f => f.GetValue(null));
            colaStates[name] = fields;
        }
        var fc = player?.HandsController as Player.FirearmController;
        return new { frame = Time.frameCount, time = Time.time, current = Id(player), currentObject = player != null ? Id(player.gameObject) : 0,
            previous = Id(Previous), previousRetained = Previous != null, previousAlive = Previous?.HealthController?.IsAlive,
            alive = player?.HealthController?.IsAlive, hands = player?.HandsController?.GetType().Name, weapon = fc?.Item?.TemplateId,
            rounds = fc?.Item?.GetCurrentMagazine()?.Count, aiming = fc?.IsAiming, position = player != null ? player.Position.ToString() : null,
            fovBody = Id(Read(fov, "_player")), fovWeapon = (Read(fov, "CurrentWeapon") as Item)?.TemplateId, fovScope = Read(fov, "CurrentScopeFOV"),
            common = currentControllers, previousControllers, delegates, cola = colaStates, replacing = RespawnApi.IsReplacing };
    }
    void Awake() { Self = this; RespawnApi.Replaced += Replaced; RespawnApi.RaidEnded += Ended; }
    void OnDestroy() { RespawnApi.Replaced -= Replaced; RespawnApi.RaidEnded -= Ended; }
    static void Replaced(Player oldBody, Player newBody, string reason) { Events.Add(new { stage = reason, state = Snapshot() }); }
    static void Ended()
    {
        foreach (var p in Retained) if (p != null && !ReferenceEquals(p, Current)) UnityEngine.Object.Destroy(p.gameObject);
        Retained.Clear(); Previous = null;
    }
    static Player Require()
    {
        var p = Current;
        if (p == null || !p.HealthController.IsAlive || p.Profile.Info.Nickname != "ColaFovTest") throw new InvalidOperationException("Living disposable ColaFovTest profile required");
        return p;
    }
    [UiBridgeAction("observe", Description = "Read only: current and retired body ownership, CommonLib components/events, FOV and Cola transient states.")]
    public static string Observe(bool remember = false)
    {
        if (remember) { Previous = Current; Events.Clear(); }
        var state = Snapshot();
        var folder = Path.Combine(Path.GetTempPath(), "ColaFov-20261122", "observations");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "frame-" + Time.frameCount + ".json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { current = state, events = Events }, Formatting.Indented));
        return JsonConvert.SerializeObject(new { path, frame = Time.frameCount, body = Id(Current), previous = Id(Previous), replacing = RespawnApi.IsReplacing });
    }
    [UiBridgeAction("transition", Control = true, Description = "Disposable profile only: native death or real Core replacement retaining the living outgoing body.")]
    public static string Transition(string kind = "death")
    {
        var p = Require(); var game = Singleton<AbstractGame>.Instance as LocalGame;
        if (game == null || RespawnApi.IsReplacing || Retained.Count >= 3) throw new InvalidOperationException("No idle game or retained-body limit");
        if (kind != "death" && kind != "swap") throw new ArgumentException("death or swap");
        Previous = p; Events.Clear(); Events.Add(new { stage = "before", state = Snapshot() });
        if (kind == "death") p.ActiveHealthController.Kill(EDamageType.Landmine);
        else
        {
            var position = p.Position; Retained.Add((LocalPlayer)p);
            game.StartCoroutine(RespawnEngine.RunRespawn(new RespawnRequest { Game = game, DeadPlayer = (LocalPlayer)p,
                SpawnSelector = new NearPositionSpawnSelector(() => position, () => 3f), HealthPercent = 100f },
                ex => Events.Add(new { stage = "failure", error = ex.ToString() })));
        }
        return JsonConvert.SerializeObject(new { started = true });
    }
    [UiBridgeAction("downedSwap", Control = true, Description = "Disposable perk fixture only: native lethal chest damage must enter Quick Revive downed state, then a real Core live swap. Does not write Cola state.")]
    public static string DownedSwap()
    {
        var player = Require();
        var type = Foreign("com.tarkovacola.server")?.Assembly.GetType("TarkovaCola.Client.QuickRevive");
        if (!(Read(type, "Active") is true)) throw new InvalidOperationException("Drink Quick Revive first");
        player.ActiveHealthController.ApplyDamage(EBodyPart.Chest, 1000f, new DamageInfo { DamageType = EDamageType.Bullet, Damage = 1000f });
        if (!(Read(type, "Downed") is true)) throw new InvalidOperationException("Native damage did not enter downed state");
        return Transition("swap");
    }
    [UiBridgeAction("offering", Control = true, Description = "Disposable profile only: probes the actual native offering queue or authenticated server route with a one-rouble fixture; no production protection field is written.")]
    public static string Offering(bool route = false)
    {
        Require();
        const string json = "[{\"tpl\":\"5449016a4bdc2d6f028b456f\",\"count\":1}]";
        if (route) return SPT.Common.Http.RequestHandler.PostJson("/tarkovacola/offering", new JObject { ["json"] = json }.ToString(Formatting.None));
        var type = Foreign("com.tarkovacola.server")?.Assembly.GetType("TarkovaCola.Client.PerkService");
        var before = Read(type, "_pendingOffering") as string;
        type.GetMethod("RequestOffering", All).Invoke(null, new object[] { json });
        return JsonConvert.SerializeObject(new { before, after = Read(type, "_pendingOffering") });
    }
    [UiBridgeAction("supply", Control = true, Description = "Disposable profile only: reconstructs one unmodified native weapon preset from the test fixture in an empty primary slot, draws through native SetInHands.")]
    public static string Supply(int preset = 0)
    {
        var p = Require(); var factory = Singleton<ItemFactory>.Instance;
        var all = JArray.Parse(File.ReadAllText(Path.Combine(BepInEx.Paths.PluginPath, "ColaFovTestWeapons.json")));
        if (preset < 0 || preset >= all.Count) throw new ArgumentOutOfRangeException(nameof(preset));
        var source = (JArray)all[preset]; var created = new Dictionary<string, Item>();
        foreach (JObject x in source)
        {
            var item = factory.CreateItem(factory.NextId, (string)x["_tpl"], null);
            item.StackObjectsCount = (int?)x["upd"]?["StackObjectsCount"] ?? 1;
            created[(string)x["_id"]] = item;
            p.Profile.Encyclopedia[item.TemplateId] = true;
        }
        Item root = null;
        foreach (JObject x in source)
        {
            var item = created[(string)x["_id"]]; var parentId = (string)x["parentId"];
            if (!created.TryGetValue(parentId ?? "", out var parent)) { root = item; continue; }
            if ((string)x["slotId"] == "cartridges")
            {
                var mag = (Magazine)parent; var result = mag.Cartridges.Add(item, false);
                if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
            }
            else
            {
                var slot = ((CompoundItem)parent).Slots.First(s => s.ID == (string)x["slotId"]);
                var result = slot.AddWithoutRestrictions(item); if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
            }
        }
        var equipmentSlot = p.Inventory.Equipment.Slots.First(s => (s.ID == "FirstPrimaryWeapon" || s.ID == "SecondPrimaryWeapon") && s.ContainedItem == null);
        var added = equipmentSlot.AddWithoutRestrictions(root); if (added.Failed) throw new InvalidOperationException(added.Error.ToString());
        p.SetInHands(root, result => Self.Logger.LogInfo("Native fixture draw: " + result));
        return JsonConvert.SerializeObject(new { supplied = root.TemplateId, slot = equipmentSlot.ID });
    }
}
