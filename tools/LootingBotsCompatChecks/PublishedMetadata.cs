using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        string v = assembly.Name.Version.ToString();
        need(assembly.Name.Name == "Skwizzy.LootingBots" && v is "1.8.0.0" or "1.8.1.0", "published identity " + v);
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "LootingBots.LootingBots");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "me.skwizzy.lootingbots"
            && new Version((string)attr.ConstructorArguments[2].Value).ToString(3) == assembly.Name.Version.ToString(3), "published plugin metadata " + v);
        var layer = assembly.MainModule.Types.Single(t => t.FullName == "LootingBots.LootingLayer");
        var brain = assembly.MainModule.Types.Single(t => t.FullName == "LootingBots.Components.LootingBrain");
        var finder = assembly.MainModule.Types.Single(t => t.FullName == "LootingBots.Components.LootFinder");
        var inventory = assembly.MainModule.Types.Single(t => t.FullName == "LootingBots.Components.LootingInventoryController");
        need(layer.BaseType.FullName == "DrakiaXYZ.BigBrain.Brains.CustomLayer"
            && brain.BaseType.FullName == "UnityEngine.MonoBehaviour" && finder.BaseType.FullName == "UnityEngine.MonoBehaviour", "published layer and component inheritance " + v);
        foreach (var (type, field, expected) in new[] { (brain, "BotOwner", "EFT.BotOwner"), (finder, "_botOwner", "EFT.BotOwner"),
            (inventory, "_botOwner", "EFT.BotOwner"), (inventory, "_botInventoryController", "EFT.InventoryLogic.InventoryController") })
            need(type.Fields.Any(f => !f.IsStatic && f.Name == field && f.FieldType.FullName == expected), "published owner field " + type.Name + "." + field);
        void Method(TypeDefinition type, string name, string result, params string[] args)
            => need(type.Methods.Any(m => m.IsPublic && !m.IsStatic && m.HasBody && !m.HasGenericParameters
                && m.Name == name && m.ReturnType.FullName == result && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args)), "published seam " + name + " " + v);
        Method(layer, "IsActive", "System.Boolean"); Method(finder, "BeginSearch", "System.Void", "System.Int32");
        Method(finder, "EnqueuePriorityCorpse", "System.Void", "System.String");
        need(finder.Methods.Any(m => m.IsPrivate && !m.IsStatic && m.HasBody && m.Name == "OnAirdropLanded"
            && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == "EFT.Interactive.LootableContainer"), "published airdrop queue seam " + v);
        var scheduler = assembly.MainModule.Types.Single(t => t.FullName == "LootingBots.Utilities.ScanScheduler");
        need(scheduler.Methods.Any(m => m.IsPublic && m.IsStatic && m.HasBody && m.Name == "Return"
            && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == "System.Int32"), "published scan ticket return seam " + v);
        Method(brain, "StartLooting", "System.Void"); Method(brain, "StopLooting", "System.Void");
        Method(brain, "Init", "System.Void", "EFT.BotOwner"); Method(brain, "Start", "System.Void");
        Method(finder, "Init", "System.Void", "EFT.BotOwner"); Method(finder, "StopFindingLoot", "System.Void");
        if (v == "1.8.1.0")
        {
            Method(inventory, "CleanupInventoryAsync", "System.Threading.Tasks.Task", "System.Threading.CancellationToken");
            Method(inventory, "Init", "System.Void"); Method(brain, "OnSpawnAsync", "System.Threading.Tasks.Task");
            need(brain.Methods.Single(m => m.Name == "Start").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "OnSpawnAsync"), "published RC starts autonomous spawn cleanup");
            var state = brain.NestedTypes.Single(t => t.Name.StartsWith("<OnSpawnAsync>"));
            need(state.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Any(i => i.Operand is MethodReference m
                && m.Name == "CleanupInventoryAsync" && m.DeclaringType.FullName == inventory.FullName), "published RC cleanup entry is within spawn task");
            need(state.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Any(i => i.Operand is MethodReference m
                && m.Name == "Init" && m.DeclaringType.FullName == inventory.FullName), "published RC spawn still initializes inventory");
            IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
            var callers = assembly.MainModule.Types.SelectMany(Types).SelectMany(t => t.Methods)
                .Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference target
                    && target.Name == "CleanupInventoryAsync" && target.DeclaringType.FullName == inventory.FullName)).ToArray();
            need(callers.Length == 1 && callers[0].DeclaringType == state && callers[0].Name == "MoveNext",
                "published RC cleanup has only spawn caller, no commanded looting path");
        }
        else need(!brain.Methods.Any(m => m.Name == "OnSpawnAsync"), "stable has no RC spawn producer");
        Console.WriteLine("PUBLISHED_LOOTING_BOTS " + v + " SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }
}
