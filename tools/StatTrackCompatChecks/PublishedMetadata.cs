using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; } }
    internal static void Verify(string client, string server, string native, Action<bool, string> need)
    {
        string clientHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(client)));
        string serverHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(server)));
        need(clientHash == "837D8E099379A839CB707AD366C1BCF1ABD0D6704EBB5BA4C253322639BBFDC0", "published client fingerprint");
        need(serverHash == "9E46661AEB51593D7FC74967AB6C7A4029B4BE4CC541B2C3084074CD43B0C131", "published server fingerprint");
        using var assembly = AssemblyDefinition.ReadAssembly(client);
        need(assembly.Name.Name == "acidphantasm-stattrack" && assembly.Name.Version == new Version(1, 0, 0, 0), "published legacy client assembly version");
        var types = Types(assembly.MainModule.Types).ToArray();
        var plugin = types.Single(t => t.FullName == "StattrackClient.Plugin");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)metadata.ConstructorArguments[0].Value == "com.acidphantasm.stattrack"
            && (string)metadata.ConstructorArguments[2].Value == "2.1.1", "published plugin metadata");
        var names = new[] { "WeaponOnShotPatch", "PlayerPatch", "PlayerPatch2", "GameWorldPatch" };
        var targets = new[] { "OnShot", "OnBeenKilledByAggressor", "OnHealthApplyDamage", "UnregisterPlayer" };
        var parameters = new[] { new[] { "EFT.InventoryLogic.Weapon" },
            new[] { "EFT.Player", "EFT.IPlayer", "EFT.Ballistics.DamageInfo", "EBodyPart", "EFT.EDamageType" },
            new[] { "EFT.Player", "EBodyPart", "System.Single", "EFT.Ballistics.DamageInfo" }, new[] { "EFT.IPlayer" } };
        for (int i = 0; i < names.Length; ++i)
        {
            var type = types.Single(t => t.FullName == "StattrackClient.Patches." + names[i]);
            var hook = type.Methods.Single(m => m.Name == "PatchPostfix");
            need(hook.IsPrivate && hook.IsStatic && hook.ReturnType.FullName == "System.Void", "published hook shape " + i);
            need(hook.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters[i]), "published hook parameters " + i);
            need(type.Methods.Single(m => m.Name == "GetTargetMethod").Body.Instructions.Any(x => x.OpCode.Code == Code.Ldstr && Equals(x.Operand, targets[i])), "published native target " + i);
            need(hook.CustomAttributes.Any(a => a.AttributeType.FullName == "SPT.Reflection.Patching.PatchPostfixAttribute"), "published postfix attribute " + i);
            need(plugin.Methods.Single(m => m.Name == "Awake").Body.Instructions.Any(x => x.OpCode.Code == Code.Newobj && x.Operand is MethodReference m && m.DeclaringType == type), "published hook startup " + i);
        }
        var callers = new Dictionary<string, HashSet<string>>();
        foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
            if (instruction.Operand is MethodReference target && target.DeclaringType.FullName == "StattrackClient.Utils.JsonFileUtils")
            {
                if (!callers.TryGetValue(target.Name, out var set)) callers[target.Name] = set = new();
                set.Add(method.DeclaringType.Name + "." + method.Name);
            }
        need(callers["TemporaryAddData"].SetEquals(names.Take(3).Select(n => n + ".PatchPostfix")), "published all stat producers accounted");
        need(callers["EndRaidMergeData"].SetEquals(new[] { "GameWorldPatch.PatchPostfix" }), "published single merge producer");
        need(callers["SaveRaidEndInServer"].SetEquals(new[] { "JsonFileUtils.EndRaidMergeData" }), "published merge is only save producer");
        var save = types.Single(t => t.Name.StartsWith("<SaveRaidEndInServer>")).Methods.Single(m => m.Name == "MoveNext");
        var instructions = save.Body.Instructions.ToArray();
        int serialize = Array.FindIndex(instructions, i => i.Operand is MethodReference m && m.Name == "SerializeObject");
        int put = Array.FindIndex(instructions, i => i.Operand is MethodReference m && m.Name == "PutJsonAsync");
        int clear = Array.FindIndex(instructions, i => i.Operand is MethodReference m && m.Name == "Clear");
        need(serialize >= 0 && put > serialize && clear > put, "published ordinary request snapshot precedes await and pending clear");
        need(instructions.Any(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, "/stattrack/save")), "published separate persistent route");
        need(callers["LoadFromServer"].SetEquals(new[] { "InsurancePatch.Postfix", "<Postfix>d__1.MoveNext" }), "published insurance and menu loads remain separate");
        using var serverAssembly = AssemblyDefinition.ReadAssembly(server);
        need(serverAssembly.Name.Version == new Version(2, 1, 1, 0), "published server version");
        var serverTypes = Types(serverAssembly.MainModule.Types).ToArray();
        var losses = serverTypes.SelectMany(t => t.Methods).Where(m => m.HasBody && m.Body.Instructions.Any(i =>
            i.Operand is MethodReference setter && setter.Name == "set_TimesLost")).ToArray();
        need(losses.Length == 1 && losses[0].DeclaringType.Name == "ReplaceIDsPatch" && losses[0].Name == "PostFix", "published only loss producer is server item replacement");
        var replacement = serverTypes.Single(t => t.Name == "ReplaceIDsPatch").Methods.Single(m => m.Name == "GetTargetMethod");
        need(replacement.Body.Instructions.Any(i => i.Operand is TypeReference t && t.FullName == "SPTarkov.Server.Core.Extensions.ItemExtensions")
            && replacement.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, "ReplaceIDs")), "published loss seam is extension not ItemHelper");
        using var gameAssembly = AssemblyDefinition.ReadAssembly(native);
        var gameTypes = Types(gameAssembly.MainModule.Types).ToArray();
        var controller = gameTypes.Single(t => t.FullName == "EFT.Player/SinglePlayerInventoryController");
        need(controller.Methods.Any(m => m.IsConstructor && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(
            new[] { "EFT.Player", "EFT.Profile", "System.Boolean", "System.Boolean" })), "installed native inventory constructor four CLR parameters");
        var localCreate = gameTypes.Single(t => t.FullName == "EFT.LocalPlayer").Methods.Single(m => m.Name == "Create");
        var state = (TypeReference)localCreate.CustomAttributes.Single(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute").ConstructorArguments[0].Value;
        var localMove = gameTypes.Single(t => t.FullName == state.FullName).Methods.Single(m => m.Name == "MoveNext").Body.Instructions.ToArray();
        int mine = Array.FindIndex(localMove, i => i.Operand is MethodReference m && m.Name == "set_IsYourPlayer");
        int inventory = Array.FindIndex(localMove, i => i.Operand is MethodReference m && m.Name == ".ctor" && m.DeclaringType.FullName == controller.FullName);
        int init = Array.FindIndex(localMove, i => i.Operand is MethodReference m && m.Name == "Init" && m.DeclaringType.FullName == "EFT.Player");
        need(mine >= 0 && inventory > mine && init > inventory, "installed constructor captures local body before Init");
        var dispose = gameTypes.Single(t => t.FullName == "EFT.Player").Methods.Single(m => m.Name == "Dispose" && m.Parameters.Count == 0);
        need(dispose.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "EFT.GameWorld" && m.Name == "UnregisterPlayer"), "installed dispose always reaches unregister seam");
        Console.WriteLine("PUBLISHED_STATTRACK_CLIENT_SHA256 " + clientHash);
        Console.WriteLine("PUBLISHED_STATTRACK_SERVER_SHA256 " + serverHash);
    }
}
