using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        need(hash == "0c1d13ae9e2924096d90be0c3b81000a1910ba8c4a2e35646922eb00f53ee0f4", "published 3.1.1 fingerprint");
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        need(assembly.Name.Name == "LateToTheParty-Client" && assembly.Name.Version == new Version(3, 1, 1, 0), "published assembly identity");
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "LateToTheParty.LateToThePartyPlugin");
        need(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin", "published plugin base");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.danw.latetotheparty" && (string)attr.ConstructorArguments[2].Value == "3.1.1", "published plugin metadata");
        var helpers = assembly.MainModule.Types.Single(t => t.FullName == "LateToTheParty.Helpers.Loot.LootDestructionHelpers");
        var discovery = assembly.MainModule.Types.Single(t => t.FullName == "LateToTheParty.Helpers.Loot.LootDiscoveryHelpers");
        var manager = assembly.MainModule.Types.Single(t => t.FullName == "LateToTheParty.Controllers.LootManager");
        MethodDefinition Method(TypeDefinition type, string name, string result, params string[] args)
        {
            var method = type.Methods.Single(m => m.Name == name);
            need(method.IsPublic && !method.HasGenericParameters && method.ReturnType.FullName == result
                && method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args), "published signature " + name);
            return method;
        }
        var start = Method(helpers, "StartDestruction", "System.Void", "EFT.InventoryLogic.Item");
        var destroy = Method(helpers, "DestroyViaLTTP", "System.Void", "EFT.InventoryLogic.Item");
        var eligible = Method(helpers, "IsEligibleForDestruction", "System.Boolean", "EFT.InventoryLogic.Item", "System.Collections.Generic.IEnumerable`1<UnityEngine.Vector3>", "System.Double");
        var found = Method(discovery, "ProcessFoundLooseLootItem", "System.Void", "EFT.Interactive.LootItem", "System.Double");
        need(new[] { start, destroy, eligible, found }.All(m => m.IsStatic), "published static patch methods");
        var calls = Calls(start).ToArray();
        int doorAt = Array.FindIndex(calls, c => c.Name == "openNearbyDoorForLoot");
        int destroyAt = Array.FindIndex(calls, c => c.FullName == destroy.FullName);
        int eventAt = Array.FindIndex(calls, c => c.Name == "Invoke" && c.DeclaringType.FullName.StartsWith("System.Action`1"));
        int confirmAt = Array.FindIndex(calls, c => c.Name == "ConfirmItemDestruction");
        need(doorAt >= 0 && doorAt < destroyAt && destroyAt < eventAt && eventAt < confirmAt, "published door deletion event accounting order");
        need(Calls(destroy).Any(c => c.DeclaringType.FullName == "EFT.InventoryLogic.ItemAddress" && c.Name == "RemoveWithoutRestrictions")
            && Calls(destroy).Any(c => c.DeclaringType.FullName == "EFT.InventoryLogic.IItemOwner" && c.Name == "RaiseRemoveEvent"), "published root and child native deletion");
        var enumerate = manager.Methods.Single(m => m.Name == "FindAndDestroyLoot");
        var state = (TypeReference)enumerate.CustomAttributes.Single(a => a.AttributeType.Name == "IteratorStateMachineAttribute").ConstructorArguments[0].Value;
        var move = manager.NestedTypes.Single(t => t.FullName == state.FullName).Methods.Single(m => m.Name == "MoveNext");
        need(Calls(move).Any(c => c.FullName == start.FullName) && Calls(move).Any(c => c.FullName == found.FullName)
            && Calls(move).Any(c => c.Name == "UpdateLootEligibility"), "published iterator uses guarded discovery eligibility destruction");
        need(Calls(helpers.Methods.Single(m => m.Name == "UpdateLootEligibility")).Any(c => c.FullName == eligible.FullName), "published eligibility delegate chain");
        Console.WriteLine("PUBLISHED_LATE_TO_THE_PARTY_SHA256 " + hash);
    }
    private static IEnumerable<MethodReference> Calls(MethodDefinition method)
        => method.Body.Instructions.Where(i => i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn)
            .Select(i => i.Operand).OfType<MethodReference>();
}
