using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> need)
    {
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        need(hash == "26635e3fed8efb77d351b6e0825997735e7378c8f124c4b413c13dc909a3eaaf", "published 1.6.3 fingerprint");
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        need(assembly.Name.Name == "ContinuousHealing" && assembly.Name.Version == new Version(1, 6, 3, 0), "published assembly identity");
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "ContinuousHealing.CH_Plugin");
        need(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin", "published plugin base");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.lacyway.ch"
            && (string)attr.ConstructorArguments[2].Value == "1.6.3", "published plugin metadata");
        var names = new[] { "CH_EndHeal_Patch", "CH_StartHeal_Patch", "CH_CancelHeal_Patch" };
        var nativeNames = new[] { "OnEffectRemoved", "Spawn", "CancelApplyingItem" };
        var awake = plugin.Methods.Single(m => m.Name == "Awake");
        for (int index = 0; index < names.Length; ++index)
        {
            var type = assembly.MainModule.Types.Single(t => t.FullName == "ContinuousHealing.Patches." + names[index]);
            var method = type.Methods.Single(m => m.Name == "Prefix");
            need(method.IsStatic && method.IsPublic && method.HasBody && method.ReturnType.FullName == (index == 0 ? "System.Boolean" : "System.Void"), "published prefix callable " + index);
            need(method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(index == 0
                ? new[] { "EFT.Player/MedsController/MedsInHandsOperation", "EFT.HealthSystem.IHealthEffect" }
                : new[] { "EFT.Player" }), "published prefix parameters " + index);
            need(method.CustomAttributes.Any(a => a.AttributeType.FullName == "SPT.Reflection.Patching.PatchPrefixAttribute"), "published prefix attribute " + index);
            need(type.Methods.Single(m => m.Name == "GetTargetMethod").Body.Instructions
                .Any(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, nativeNames[index])), "published native target " + index);
            need(awake.Body.Instructions.Any(i => i.OpCode.Code == Code.Newobj && i.Operand is MethodReference ctor
                && ctor.DeclaringType.FullName == type.FullName), "published startup installs patch " + index);
        }
        var end = assembly.MainModule.Types.Single(t => t.Name == names[0]).Methods.Single(m => m.Name == "Prefix");
        var il = end.Body.Instructions;
        int cancel = il.Select((i, n) => (i, n)).First(x => x.i.OpCode.Code == Code.Ldsfld && x.i.Operand is FieldReference f && f.Name == "CancelRequested").n;
        int local = il.Select((i, n) => (i, n)).First(x => x.i.Operand is MethodReference m && m.Name == "get_IsYourPlayer").n;
        int finish = il.Select((i, n) => (i, n)).First(x => x.i.Operand is MethodReference m && m.Name == "Finish").n;
        int med = il.Select((i, n) => (i, n)).First(x => x.i.OpCode.Code == Code.Isinst && x.i.Operand is TypeReference t && t.Name == "IMedEffect").n;
        need(cancel < finish && finish < local && med < local, "published cancellation and non-med handling precede local-player check");
        Console.WriteLine("PUBLISHED_CONTINUOUS_HEALING_SHA256 " + hash);
    }
}
