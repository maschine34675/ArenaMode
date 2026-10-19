using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using EFT;
using UnityEngine;
using Version = System.Version;

namespace ArenaMode.Compat;

/// <summary>Only metadata is inspected; no optional assembly is a hard reference.</summary>
internal sealed class ArenaLootingBotsContract
{
    internal const string Guid = "me.skwizzy.lootingbots";
    internal static readonly Version Reviewed = new(1, 8, 0, 0);
    internal MethodInfo Layer, Search, Loot, Cleanup, CorpseQueue, AirdropQueue;
    internal Action<int> ReturnTicket;
    internal PropertyInfo LayerOwner;
    internal FieldInfo FinderOwner, BrainOwner, InventoryOwner, NativeInventory;
    internal Version Version;

    internal static ArenaLootingBotsContract Resolve(Type plugin, string guid, Version version)
    {
        version = Normalize(version);
        if (guid != Guid || version == null || version < Reviewed || plugin?.FullName != "LootingBots.LootingBots"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new NotSupportedException("Looting Bots 1.8.0+ identity is required");
        var assembly = plugin.Assembly;
        var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "Skwizzy.LootingBots" || Normalize(assembly.GetName().Version) != version
            || attr?.GUID != Guid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Looting Bots plugin/assembly metadata disagree");
        var layer = assembly.GetType("LootingBots.LootingLayer", true);
        var brain = assembly.GetType("LootingBots.Components.LootingBrain", true);
        var finder = assembly.GetType("LootingBots.Components.LootFinder", true);
        var inventory = assembly.GetType("LootingBots.Components.LootingInventoryController", true);
        var scheduler = assembly.GetType("LootingBots.Utilities.ScanScheduler", true);
        if (layer.BaseType?.FullName != "DrakiaXYZ.BigBrain.Brains.CustomLayer"
            || !typeof(MonoBehaviour).IsAssignableFrom(brain) || !typeof(MonoBehaviour).IsAssignableFrom(finder))
            throw new MissingMemberException("Looting Bots layer/component inheritance changed");
        var owner = layer.GetProperty("BotOwner", BindingFlags.Public | BindingFlags.Instance);
        if (owner?.PropertyType != typeof(BotOwner) || owner.GetGetMethod() == null)
            throw new MissingMemberException("Looting layer BotOwner changed");
        var result = new ArenaLootingBotsContract
        {
            Version = version, LayerOwner = owner,
            Layer = Method(layer, "IsActive", typeof(bool)),
            Search = Method(finder, "BeginSearch", typeof(void), typeof(int)),
            CorpseQueue = Method(finder, "EnqueuePriorityCorpse", typeof(void), typeof(string)),
            AirdropQueue = MethodWithFlags(finder, "OnAirdropLanded", typeof(void), BindingFlags.NonPublic | BindingFlags.Instance,
                typeof(EFT.Interactive.LootableContainer)),
            ReturnTicket = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>),
                MethodWithFlags(scheduler, "Return", typeof(void), BindingFlags.Public | BindingFlags.Static, typeof(int))),
            Loot = Method(brain, "StartLooting", typeof(void)),
            FinderOwner = Field(finder, "_botOwner", typeof(BotOwner)),
            BrainOwner = Field(brain, "BotOwner", typeof(BotOwner)),
            InventoryOwner = Field(inventory, "_botOwner", typeof(BotOwner)),
            NativeInventory = Field(inventory, "_botInventoryController", typeof(EFT.InventoryLogic.InventoryController))
        };
        // Start/Init remain synchronous setup. A new coroutine/async initializer
        // is a new producer, not evidence that the old guard still covers it.
        Synchronous(Method(brain, "Init", typeof(void), typeof(BotOwner)));
        Synchronous(Method(brain, "Start", typeof(void)));
        Synchronous(Method(finder, "Init", typeof(void), typeof(BotOwner)));
        Method(brain, "StopLooting", typeof(void));
        Method(finder, "StopFindingLoot", typeof(void));
        // 1.8.1-RC starts inventory cleanup independently of layer arbitration.
        // Guard the actual mutation entry, leaving OnSpawnAsync's finally/Init
        // intact. A later API with only half this path must not be admitted.
        bool spawn = brain.GetMethod("OnSpawnAsync", BindingFlags.Public | BindingFlags.Instance) != null;
        bool cleanup = inventory.GetMethod("CleanupInventoryAsync", BindingFlags.Public | BindingFlags.Instance) != null;
        if (spawn != cleanup || (version >= new Version(1, 8, 1, 0) && !spawn))
            throw new MissingMemberException("Looting Bots spawn inventory path changed");
        if (spawn)
        {
            Method(brain, "OnSpawnAsync", typeof(Task));
            result.Cleanup = Method(inventory, "CleanupInventoryAsync", typeof(Task), typeof(CancellationToken));
            Method(inventory, "Init", typeof(void));
        }
        return result;
    }

    private static void Synchronous(MethodInfo method)
    {
        if (method.GetCustomAttribute<AsyncStateMachineAttribute>() != null
            || method.GetCustomAttribute<IteratorStateMachineAttribute>() != null)
            throw new MissingMethodException("Looting Bots initializer became asynchronous: " + method.Name);
    }

    private static MethodInfo Method(Type type, string name, Type returns, params Type[] args)
        => MethodWithFlags(type, name, returns, BindingFlags.Public | BindingFlags.Instance, args);
    private static MethodInfo MethodWithFlags(Type type, string name, Type returns, BindingFlags flags, params Type[] args)
    {
        var method = type.GetMethod(name, flags | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != returns || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static FieldInfo Field(Type type, string name, Type expected)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
        return field;
    }
    internal static Version Normalize(Version v)
        => v == null || v.Build < 0 ? null : new Version(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
}
