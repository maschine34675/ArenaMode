using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string path,Action<bool,string> need)
    {
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        need(hash=="EF8C2C77797001A9888CBD6871D83CA72A631C56430028292544E2A66076B516","published client fingerprint");
        using var asm=AssemblyDefinition.ReadAssembly(path);
        need(asm.Name.Name=="SPTRecoilReworkLegacyClient"&&asm.Name.Version==new Version(2,0,0,0),"published assembly identity");
        var plugin=asm.MainModule.Types.Single(t=>t.FullName=="SPTRecoilRework.Plugin");
        var attr=plugin.CustomAttributes.Single(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value=="com.pein.camerarecoilmod"&&(string)attr.ConstructorArguments[2].Value=="2.0.0","published metadata");
        foreach(var name in new[]{"RealRecoilComponent","CameraOffsetComponent"})
        {
            var t=asm.MainModule.Types.Single(t=>t.Name==name);
            need(t.BaseType.FullName=="UnityEngine.MonoBehaviour","published component base "+name);
            var awake=t.Methods.Single(m=>m.Name=="Awake");
            need(awake.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="get_gameObject")
                &&awake.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Destroy"&&m.DeclaringType.FullName=="UnityEngine.Object"),"published destructive singleton "+name);
            need(t.Properties.Single(p=>p.Name=="Instance").SetMethod.IsStatic,"published singleton writable "+name);
        }
        var init=asm.MainModule.Types.Single(t=>t.Name=="PlayerInitPatch").Methods.Single(m=>m.Name=="PatchPostfix");
        var components=init.Body.Instructions.Where(i=>i.Operand is GenericInstanceMethod m&&m.Name=="AddComponent")
            .Select(i=>((GenericInstanceMethod)i.Operand).GenericArguments.Single().Name).ToArray();
        need(components.SequenceEqual(new[]{"RealRecoilComponent","CameraOffsetComponent"}),"published initializer adds both synchronously");
        need(init.Parameters.Count==1&&init.Parameters[0].ParameterType.FullName=="EFT.Player","published initializer signature");
        var recalc=asm.MainModule.Types.Single(t=>t.Name=="RecalculateRecoilOnWeaponSwitchPatch").Methods.Single(m=>m.Name=="PatchPostfix");
        need(recalc.Parameters.Count==8&&recalc.Parameters[3].ParameterType.FullName=="EFT.Player/FirearmController","published incoming controller argument");
        var reads=recalc.Body.Instructions.ToList();
        int read=reads.FindIndex(i=>i.OpCode.Code==Code.Ldsfld&&i.Operand is FieldReference f&&f.Name=="CurrentFirearmController");
        int write=reads.FindIndex(i=>i.OpCode.Code==Code.Stsfld&&i.Operand is FieldReference f&&f.Name=="CurrentFirearmController");
        need(read>=0&&write>read,"published previous-controller bug established");
        Console.WriteLine("PUBLISHED_RECOIL_LEGACY_SHA256 "+hash);
    }
}
