using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        need(assembly.Name.Name == "SkillsExtended.Client.Skills"
            && assembly.Name.Version == new Version(3, 1, 4, 0), "published assembly identity");
        var module = assembly.MainModule;
        var plugin = module.Types.Single(t => t.FullName == "SkillsExtended.SkillsExtendedPlugin");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.cj.skillsextended"
            && (string)attr.ConstructorArguments[2].Value == "3.1.4", "published BepInPlugin metadata");
        var runtime = module.Types.Single(t => t.FullName == "SkillsExtended.Skills.Signals.SignalsRuntime");
        need(runtime.BaseType.FullName == "UnityEngine.MonoBehaviour", "published runtime identity");
        var boot = runtime.Methods.Single(m => m.Name == "Boot");
        need(boot.IsPublic && boot.IsStatic && boot.Parameters.Count == 1
            && boot.Parameters[0].ParameterType.FullName == "EFT.GameWorld"
            && boot.ReturnType.FullName == "System.Threading.Tasks.Task", "published Boot carries exact world");
        need(runtime.Properties.Single(p => p.Name == "World").PropertyType.FullName == "EFT.GameWorld", "published runtime world getter");
        var reward = runtime.Methods.Single(m => m.Name == "ApplyXp");
        need(reward.IsPrivate && !reward.IsStatic && reward.Parameters.Count == 0
            && reward.ReturnType.FullName == "System.Void", "published reward entry signature");
        var ledger = module.Types.Single(t => t.FullName == "SkillsExtended.Skills.Signals.SignalsXpLedger");
        var reserve = ledger.Methods.Single(m => m.Name == "Reserve");
        need(reserve.IsPublic && reserve.IsStatic && reserve.ReturnType.FullName == "System.Single"
            && reserve.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Single" }),
            "published receipt reservation signature");
        var start = module.Types.Single(t => t.FullName == "SkillsExtended.Skills.Shared.Patches.OnGameStartedPatch");
        var started = start.Methods.Single(m => m.Name == "Postfix");
        need(started.IsPrivate && started.IsStatic && started.Parameters.Single().ParameterType.FullName == "EFT.GameWorld"
            && started.CustomAttributes.Any(a => a.AttributeType.FullName == "SPT.Reflection.Patching.PatchPostfixAttribute"),
            "published foreign world postfix");
        need(start.Fields.Single(f => f.Name == "Player").FieldType.FullName == "EFT.Player", "published static event body");
        var callbacks = new[] { "ApplyMedicalXp", "ApplyNatoRifleXp", "ApplyEasternRifleXp" }
            .Select(n => start.Methods.Single(m => m.Name == n)).ToArray();
        need(callbacks.All(m => m.IsStatic && m.IsPrivate && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1),
            "published event callback signatures");
        need(callbacks[0].Parameters[0].ParameterType.FullName == "EFT.HealthSystem.IHealthEffect"
            && callbacks.Skip(1).All(m => m.Parameters[0].ParameterType.FullName == "EFT.Mastering"),
            "published exact native event arguments");
        var registrations = started.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldftn)
            .Select(i => ((MethodReference)i.Operand).FullName).ToArray();
        need(registrations.Order().SequenceEqual(callbacks.Select(m => m.FullName).Order()), "published three exact subscribed callbacks");
        need(started.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "FixDoors"),
            "published original has unrelated door fix to retain");
        var callers = AllTypes(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody
            && m.Body.Instructions.Any(i => i.Operand is MethodReference target && target.FullName == reserve.FullName)).ToArray();
        need(callers.Length == 1 && callers[0] == reward, "published only reward entry reserves external receipt");
        var methods = AllTypes(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
        var bodyField = start.Fields.Single(f => f.Name == "Player");
        var bodyReaders = methods.Where(m => m.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName == bodyField.FullName)).ToArray();
        need(bodyReaders.All(m => m == started || callbacks.Contains(m)), "published event body has no extra consumers");
        need(methods.Where(m => m.DeclaringType != ledger).All(m => m.Body.Instructions.All(i =>
            i.Operand is not MemberReference member || member.DeclaringType?.FullName != ledger.FullName
                || m == reward && member.FullName == reserve.FullName)),
            "published all receipt accesses fit runtime contract");
        need(methods.All(m => m.Body.Instructions.All(i => i.Operand is not MethodReference reference
            || !callbacks.Any(c => c.FullName == reference.FullName)
            || m == started && i.OpCode.Code == Code.Ldftn)),
            "published no callback registration outside guarded start");
        var eventCalls = started.Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name.StartsWith("add_"))
            .Select(i => (MethodReference)i.Operand).ToArray();
        need(eventCalls.Length == 3 && eventCalls.Count(m => m.Name == "add_EffectStartedEvent") == 1
            && eventCalls.Count(m => m.Name == "add_OnMasteringExperienceChanged") == 2,
            "published exact three native event subscriptions");
        var instructions = reward.Body.Instructions;
        int receiptAt = instructions.Select((i, at) => (i, at)).Single(x => x.i.Operand is MethodReference m && m.FullName == reserve.FullName).at;
        need(receiptAt >= 0 && instructions.Skip(receiptAt + 1).Any(i => i.Operand is MethodReference m && m.Name == "ExecuteSkill"),
            "published external write precedes skill execution");
        need(ledger.Methods.Single(m => m.Name == "get_FilePath").Body.Instructions
            .Any(i => i.OpCode.Code == Code.Ldstr && (string)i.Operand == "skills-extended-signals-xp.json"),
            "published journal path outside profile snapshot");
        Console.WriteLine("PUBLISHED_SKILLS_EXTENDED_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    internal static void VerifyNative(string path, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var types = AllTypes(assembly.MainModule.Types).ToArray();
        var world = types.Single(t => t.FullName == "EFT.GameWorld");
        need(world.Methods.Count(m => m.Name == "OnGameStarted" && !m.IsStatic && m.Parameters.Count == 0
            && m.ReturnType.FullName == "System.Void") == 1, "native GameWorld start contract");
        var player = types.Single(t => t.FullName == "EFT.Player");
        need(player.Properties.Single(p => p.Name == "Skills").PropertyType.FullName == "EFT.SkillManager"
            && player.Properties.Single(p => p.Name == "ActiveHealthController").PropertyType.FullName == "EFT.HealthSystem.ActiveHealthController",
            "native exact-body event owners");
        var health = types.Single(t => t.FullName == "EFT.HealthSystem.BaseHealthController`1");
        var healthEvent = health.Events.Single(e => e.Name == "EffectStartedEvent");
        need(healthEvent.EventType.FullName == "System.Action`1<EFT.HealthSystem.IHealthEffect>"
            && healthEvent.AddMethod.IsPublic && healthEvent.RemoveMethod.IsPublic, "native medical event supports exact delegate detach");
        var skills = types.Single(t => t.FullName == "EFT.SkillManager");
        var mastery = skills.Events.Single(e => e.Name == "OnMasteringExperienceChanged");
        need(mastery.EventType.FullName == "System.Action`1<EFT.Mastering>"
            && mastery.AddMethod.IsPublic && mastery.RemoveMethod.IsPublic, "native mastery event supports exact delegate detach");
        Console.WriteLine("NATIVE_ASSEMBLY_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }
}
