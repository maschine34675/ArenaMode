using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        need(hash == "39ae97546f4359dbdfd1f369de3e070f6f17e2db5d21d4ab25d023e20123ba66", "published 4.1.3 DLL fingerprint");
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        need(assembly.Name.Name == "RemoveTheDead" && assembly.Name.Version == new Version(4, 1, 3, 0), "published assembly identity");
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "RemoveTheDead.RTDPlugin");
        need(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin", "published plugin base");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.jbs4bmx.RemoveTheDead"
            && (string)attr.ConstructorArguments[2].Value == "4.1.3", "published GUID case and version");
        need((int)plugin.Fields.Single(f => f.Name == "TarkovVersion").Constant == 40743, "published native build gate");
        var remove = plugin.Methods.Single(m => m.Name == "RemoveBotAndWeapons");
        need(remove.IsPrivate && !remove.IsStatic && remove.ReturnType.FullName == "System.Void"
            && remove.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "EFT.BotOwner" }), "published common seam");
        var manual = plugin.Methods.Single(m => m.Name == "RunRemovalNow");
        var queue = plugin.Methods.Single(m => m.Name == "QueueRemoval");
        var weapons = plugin.Methods.Single(m => m.Name == "RemoveNearbyWeapons");
        var stateAttr = queue.CustomAttributes.Single(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IteratorStateMachineAttribute");
        var stateName = ((TypeReference)stateAttr.ConstructorArguments[0].Value).FullName;
        var state = plugin.NestedTypes.Single(t => t.FullName == stateName);
        var move = state.Methods.Single(m => m.Name == "MoveNext");
        need(Calls(manual, remove) && Calls(move, remove), "published auto and manual share destructive seam");
        need(Calls(plugin.Methods.Single(m => m.Name == "Update"), queue)
            && Calls(plugin.Methods.Single(m => m.Name == "Update"), manual)
            && Calls(plugin.Methods.Single(m => m.Name == "RTDButtonActionDrawer"), manual), "published timer F5 and F12 entry chain");
        need(Calls(remove, weapons) && DestroyCalls(remove) == 1 && DestroyCalls(weapons) == 1,
            "published body and nearby-weapon destruction is behind common seam");
        need(new[] { manual, queue, move, plugin.Methods.Single(m => m.Name == "Update"),
            plugin.Methods.Single(m => m.Name == "RTDButtonActionDrawer") }.All(m => DestroyCalls(m) == 0), "published entries have no direct destroy bypass");
        need(weapons.Body.Instructions.Any(i => i.OpCode.Code == Code.Isinst && i.Operand is TypeReference t && t.FullName == "EFT.InventoryLogic.Weapon"),
            "published nearby cleanup includes every native Weapon loot item");
        Console.WriteLine("PUBLISHED_REMOVE_THE_DEAD_SHA256 " + hash);
    }

    private static bool Calls(MethodDefinition caller, MethodDefinition target)
        => caller.Body.Instructions.Any(i => (i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt)
            && i.Operand is MethodReference method && method.FullName == target.FullName);
    private static int DestroyCalls(MethodDefinition method)
        => method.Body.Instructions.Count(i => (i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt)
            && i.Operand is MethodReference call && call.DeclaringType.FullName == "UnityEngine.Object"
            && (call.Name == "Destroy" || call.Name == "DestroyImmediate"));
}
