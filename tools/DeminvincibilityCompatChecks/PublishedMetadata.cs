using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        need(hash == "F34E19DF4257F6D35C202B39599F613C76458FAE8D27912042F37B18CED07C4A", "published 2.2.0 fingerprint");
        using var a = AssemblyDefinition.ReadAssembly(path);
        need(a.Name.Name == "Deminvincibility" && a.Name.Version == new Version(1, 0, 0, 0), "published assembly identity");
        var p = a.MainModule.Types.Single(t => t.FullName == "Deminvincibility.DeminvicibilityPlugin");
        var attr = p.CustomAttributes.Single(x => x.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.hazelify.deminvincibility" && (string)attr.ConstructorArguments[2].Value == "2.2.0", "published metadata");
        var startup = p.Methods.Single(m => m.Name == "ApplyPatches").Body.Instructions.Where(i => i.OpCode.Code == Code.Newobj)
            .Select(i => ((MethodReference)i.Operand).DeclaringType.Name).ToHashSet();
        need(startup.SetEquals(new[] { "Kill", "DestroyBodyPartPatch", "ApplyDamage", "DoFracture" }), "published enabled inventory excludes ChangeHealth and DoBleed");
        string[] names = { "Kill", "DestroyBodyPartPatch", "ApplyDamage", "DoFracture" };
        string[] targets = { "Kill", "DestroyBodyPart", "ApplyDamage", "DoFracture" };
        string[][] parameters = {
            new[] { "EFT.HealthSystem.ActiveHealthController", "EFT.EDamageType", "EFT.Player" },
            new[] { "EFT.HealthSystem.ActiveHealthController", "EFT.Player", "EBodyPart", "EFT.EDamageType" },
            new[] { "EFT.HealthSystem.ActiveHealthController", "EFT.Player", "System.Single&", "EBodyPart", "EFT.Ballistics.DamageInfo" },
            new[] { "EFT.HealthSystem.ActiveHealthController", "EBodyPart", "EFT.Player" }
        };
        for (int n = 0; n < names.Length; n++) {
            var type = a.MainModule.Types.Single(t => t.FullName == "Deminvincibility.Patches." + names[n]);
            var method = type.Methods.Single(m => m.Name == "Prefix");
            need(method.IsStatic && method.HasBody && method.ReturnType.FullName == "System.Boolean", "published bool prefix " + n);
            need(method.Parameters.Select(x => x.ParameterType.FullName).SequenceEqual(parameters[n]), "published parameters " + n);
            need(method.CustomAttributes.Any(x => x.AttributeType.FullName == "SPT.Reflection.Patching.PatchPrefixAttribute"), "published prefix receipt attribute " + n);
            need(type.Methods.Single(m => m.Name == "GetTargetMethod").Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, targets[n])), "published native target " + n);
        }
        var damage = a.MainModule.Types.Single(t => t.Name == "ApplyDamage").Methods.Single(m => m.Name == "Prefix");
        var reads = damage.Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name == "get_Keep1Health").ToArray();
        need(reads.Length == 1 && reads[0].OpCode.Code == Code.Call && reads[0].Next.OpCode.Code == Code.Callvirt
            && reads[0].Next.Operand is MethodReference value && value.Name == "get_Value"
            && value.DeclaringType.FullName == "BepInEx.Configuration.ConfigEntry`1<System.Boolean>", "published unique adjacent clamp read");
        foreach (var getter in new[] { "get_MedicineBool", "get_CustomDamageModeVal" })
            need(damage.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == getter), "published unrelated preference " + getter);
        var kill = a.MainModule.Types.Single(t => t.Name == "Kill").Methods.Single(m => m.Name == "Prefix");
        need(kill.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_SecondChanceProtection"), "published Second Chance read");
        need(kill.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "IsDebouncing"), "published process debounce");
        // Native API and actual event-bearing Kill body: inspect the installed DLL, never instantiate EFT.
        string native = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
        string repo = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repo, "AGENTS.md")) && Directory.GetParent(repo) != null) repo = Directory.GetParent(repo).FullName;
        native = Path.GetFullPath(Path.Combine(repo, "../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"));
        using var eft = AssemblyDefinition.ReadAssembly(native);
        var hc = eft.MainModule.Types.Single(t => t.FullName == "EFT.HealthSystem.ActiveHealthController");
        need(hc.Fields.Any(f => f.Name == "Player" && !f.IsStatic && f.FieldType.FullName == "EFT.Player"), "native controller owner field");
        foreach (var target in targets) need(hc.Methods.Count(m => m.Name == target && !m.IsStatic) == 1, "native unambiguous " + target);
        var nativeKill = hc.Methods.Single(m => m.Name == "Kill");
        need(nativeKill.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_IsAlive"), "native Kill sets life state");
        need(nativeKill.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "DiedEvent"), "native Kill raises death event");
        Console.WriteLine("PUBLISHED_DEMINVINCIBILITY_SHA256 " + hash);
    }
}
