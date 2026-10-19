using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        need(hash == "2f3039c34e2800cc082c908f72b8896dbab88b2a69fba13bdfe90540e0a37a00", "published 1.1.3 fingerprint");
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        need(assembly.Name.Name == "ClassicMovement" && assembly.Name.Version == new Version(1, 0, 0, 0), "published legacy assembly identity");
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "OldTarkovMovement.Plugin");
        need(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin", "published plugin base");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.boogle.classicmovement" && (string)attr.ConstructorArguments[2].Value == "1.1.3", "published plugin metadata");
        var awake = plugin.Methods.Single(m => m.Name == "Awake");
        var targets = new[] { "GetNewState", "ManualUpdate" };
        var names = new[] { "StateReplacer", "SmoothSpeedFix" };
        for (int i = 0; i < names.Length; ++i)
        {
            var type = assembly.MainModule.Types.Single(t => t.FullName == "OldTarkovMovement." + names[i]);
            var method = type.Methods.Single(m => m.Name == "Prefix");
            need(method.IsStatic && method.IsPrivate && method.HasBody && method.ReturnType.FullName == (i == 0 ? "System.Boolean" : "System.Void"), "published patch method shape " + i);
            need(method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(i == 0
                ? new[] { "EFT.MovementContext", "EFT.BaseMovementState&", "EPlayerState", "System.Boolean" }
                : new[] { "EFT.MovementContext", "System.Single" }), "published arguments " + i);
            need(method.CustomAttributes.Any(a => a.AttributeType.FullName == "SPT.Reflection.Patching.Patch" + (i == 0 ? "Prefix" : "Postfix") + "Attribute"), "published native hook kind " + i);
            need(type.Methods.Single(m => m.Name == "GetTargetMethod").Body.Instructions.Any(x => x.OpCode.Code == Code.Ldstr && Equals(x.Operand, targets[i])), "published native target " + i);
            need(awake.Body.Instructions.Any(x => x.OpCode.Code == Code.Newobj && x.Operand is MethodReference ctor && ctor.DeclaringType.FullName == type.FullName), "published startup hook " + i);
        }
        var config = assembly.MainModule.Types.Single(t => t.FullName == "OldTarkovMovement.OldTarkovMovementConfig");
        foreach (var name in new[] { "NostalgiaMode", "NoInertia", "BotsUseOldMovement" })
            need(config.Fields.Any(f => f.Name == "<" + name + ">k__BackingField" && f.IsPrivate && !f.IsStatic && f.FieldType.FullName == "System.Boolean"), "published config storage " + name);
        var factory = assembly.MainModule.Types.Single(t => t.Name == "StateReplacer").Methods.Single(m => m.Name == "Prefix");
        need(factory.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_BotsUseOldMovement"), "published bot state option");
        need(factory.Body.Instructions.Where(i => i.OpCode.Code == Code.Newobj).Select(i => ((MethodReference)i.Operand).DeclaringType.Name)
            .ToHashSet().IsSupersetOf(new[] { "OldIdleState", "OldRunState", "OldJumpState", "OldSprintState", "OldSidestepState", "OldProneIdleState" }), "published replacement state inventory");
        Console.WriteLine("PUBLISHED_CLASSIC_MOVEMENT_SHA256 " + hash);
    }
}
