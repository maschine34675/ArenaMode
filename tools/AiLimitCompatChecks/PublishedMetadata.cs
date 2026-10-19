using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string file, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(file);
        need(assembly.Name.Name == "dvize.AILimit" && assembly.Name.Version == new Version(1, 9, 2, 0), "published assembly 1.9.2 identity");
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "AIlimit.AILimitPlugin");
        need(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin", "published plugin base");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.dvize.ailimit" && (string)attr.ConstructorArguments[2].Value == "1.9.2", "published BepInPlugin metadata");
        var component = assembly.MainModule.Types.Single(t => t.FullName == "AILimit.AILimitComponent");
        need(component.BaseType.FullName == "UnityEngine.MonoBehaviour", "published component base");
        need(!component.Fields.Single(f => f.Name == "disabledBotsLastFrame").IsStatic,
            "published pending disable queue belongs to old component, not a shared static queue");
        need(component.Fields.Single(f => f.Name == "botList").IsStatic
            && component.Fields.Single(f => f.Name == "playerInfoMapping").IsStatic,
            "published tracking lists are static and require lifecycle review");
        need(new[] { "Update", "UpdateBots", "UpdateBotsWithDisabledList" }
            .All(name => !component.Methods.Single(m => m.Name == name).IsStatic),
            "published limiter mutation loops require a live component");
        var enable = component.Methods.Single(m => m.Name == "Enable");
        need(enable.IsPublic && enable.IsStatic && enable.ReturnType.FullName == "System.Void" && enable.Parameters.Count == 0 && enable.HasBody, "published callable entry");
        var patch = assembly.MainModule.Types.Single(t => t.FullName == "AIlimit.NewGamePatch");
        var prefix = patch.Methods.Single(m => m.Name == "PatchPrefix");
        var ops = prefix.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        need(prefix.IsPublic && prefix.IsStatic && prefix.Parameters.Count == 0 && prefix.ReturnType.FullName == "System.Void"
            && ops.Length == 2 && ops[0].OpCode.Code == Code.Call && ((MethodReference)ops[0].Operand).FullName == enable.FullName
            && ops[1].OpCode.Code == Code.Ret, "published start prefix only forwards to Enable");
        var target = patch.Methods.Single(m => m.Name == "GetTargetMethod");
        need(target.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldtoken && i.Operand is TypeReference t && t.FullName == "EFT.GameWorld")
            && target.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldstr && (string)i.Operand == "OnGameStarted"), "published target is GameWorld.OnGameStarted");
        Console.WriteLine("PUBLISHED_AI_LIMIT_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
    }
}
