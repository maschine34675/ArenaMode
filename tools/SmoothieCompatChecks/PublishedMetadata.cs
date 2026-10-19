using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string file, Action<bool, string> need)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(file);
        need(assembly.Name.Name == "Smoothie" && assembly.Name.Version == new Version(1,0,8,0), "published assembly 1.0.8 identity");
        var plugin = assembly.MainModule.Types.Single(t => t.FullName == "Smoothie.Plugin");
        need(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin", "published plugin base");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == "com.tominocz.smoothie"
            && (string)attr.ConstructorArguments[2].Value == "1.0.8", "published plugin metadata");
        var diagnostics = assembly.MainModule.Types.Single(t => t.FullName == "Smoothie.Diagnostics");
        var target = diagnostics.Methods.Single(m => m.Name == "KeepCleanupPaused");
        need(diagnostics.IsAbstract && diagnostics.IsSealed && target.IsStatic && target.IsAssembly
            && target.ReturnType.FullName == "System.Void" && target.Parameters.Count == 0 && target.HasBody,
            "published GC seam matches checked contract");
        var updates = plugin.Methods.Single(m => m.Name == "Update").Body.Instructions;
        need(updates.Any(i => i.OpCode.Code is Code.Call or Code.Ldftn && i.Operand is MethodReference m && m.FullName == target.FullName),
            "published update invokes exact optional GC seam");
        need(target.Body.Instructions.Any(i => i.Operand is FieldReference f && f.DeclaringType.FullName == "Smoothie.Plugin" && f.Name == "KeepCleanupPaused"),
            "published seam reads separate optional setting");
        var depth = assembly.MainModule.Types.Single(t => t.FullName == "Smoothie.DepthCulling");
        var scan = depth.NestedTypes.Single(t => t.Name.StartsWith("<ScanLevel>"));
        var strings = scan.Methods.Single(m => m.Name == "MoveNext").Body.Instructions
            .Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToHashSet();
        need(strings.Contains("Loot") && strings.Contains("Interactive"), "published scan contains both excluded layer names");
        var candidate = depth.Methods.Single(m => m.Name == "Candidate");
        need(candidate.Body.Instructions.Any(i => i.OpCode.Code == Code.Ldsfld && i.Operand is FieldReference f && f.Name == "_skipLayers"),
            "published renderer candidate consumes exclusion mask");
        Console.WriteLine("PUBLISHED_SMOOTHIE_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
    }
}
