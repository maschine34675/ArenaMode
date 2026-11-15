using Mono.Cecil;
using System.Security.Cryptography;

internal static class NativeContracts
{
    internal static int Verify(string assemblyPath)
    {
        using var module=ModuleDefinition.ReadModule(assemblyPath);
        int checks=0;
        void Require(bool value,string name) {checks++;if(!value)throw new Exception("FAIL: native "+name);}
        TypeDefinition Type(string name)=>module.Types.Single(t=>t.FullName==name);
        MethodDefinition Method(TypeDefinition type,string name,int count)=>type.Methods.Single(m=>m.Name==name&&m.Parameters.Count==count);
        static MethodReference[] Calls(MethodDefinition method)=>method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
        static bool CallsNamed(MethodDefinition method,string name)=>Calls(method).Any(m=>m.Name==name);
        var usable=Type("EFT.Player").NestedTypes.Single(t=>t.Name=="UsableItemController");
        var idle=usable.NestedTypes.Single(t=>t.Name=="Idling"); var aim=Method(idle,"SetAiming",1);
        Require(Calls(aim).Any(m=>m.DeclaringType.Name=="FirearmsAnimator"&&m.Name=="SetFire")&&CallsNamed(aim,"set_IsAiming"),"idle separates animation from ADS");
        Require(CallsNamed(aim,"CanAimInState")&&aim.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="STOP_AIMING_AT"),"idle native state and overlap gates");
        var rangeIdle=Type("EFT.PortableRangeFinderController").NestedTypes.Single(t=>t.Name=="PortableRangeFinderIdling");
        var rangeAim=Method(rangeIdle,"SetAiming",1);
        Require(Calls(rangeAim).Any(m=>m.DeclaringType.Name=="Idling"&&m.Name=="SetAiming")&&CallsNamed(rangeAim,"set_enabled")&&CallsNamed(rangeAim,"SetActive"),"rangefinder ADS separately activates optics");
        var set=Method(Type("FirearmsAnimator"),"SetFire",1); var setCalls=Calls(set);
        Require(setCalls.Length==3&&setCalls[0].Name=="get_Animator"&&setCalls[1].DeclaringType.Name=="AnimationControllerParametersTable"&&setCalls[1].Name=="SetFire"&&setCalls[2].Name=="ResetLeftHand","Fire setter only animation and left hand");
        var table=Type("AnimationControllerParametersTable"); var rawSet=Method(table,"SetFire",2); var get=Method(table,"GetBoolFire",1);
        Require(rawSet.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="BOOL_FIRE")&&Calls(rawSet).Length==1&&Calls(rawSet)[0].Name=="SetBool","direct Fire setter exact flag");
        Require(get.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="BOOL_FIRE")&&Calls(get).Length==1&&Calls(get)[0].Name=="GetBool","Fire getter exact same flag");
        var reset=Method(Type("ObjectInHandsAnimator"),"ResetLeftHand",0);
        Require(CallsNamed(reset,"SetUseLeftHand"),"native setter would erase new left hand interaction");
        var pool=Type("EFT.AssetsManager.AssetPoolObject");
        Require(Method(pool,"ReturnToPool",0).IsVirtual&&Method(pool,"OnGetFromPool",0).IsVirtual,"pool lifecycle methods virtual");
        Require(Calls(Method(Type("WeaponPrefab"),"ReturnToPool",0)).Any(m=>m.DeclaringType.FullName==pool.FullName&&m.Name=="ReturnToPool"),"weapon returns through patched base");
        var weapon=Type("WeaponPrefab"); var put=Method(weapon,"ReturnToPool",0).Body.Instructions.ToList();
        int clear=put.FindIndex(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.Name=="_firearmsAnimator");
        int baseCall=put.FindIndex(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==pool.FullName&&m.Name=="ReturnToPool");
        Require(clear>=0&&clear<baseCall&&CallsNamed(Method(weapon,"ReturnToPool",0),"ResetAnimator"),"override clears wrapper before base so prefix must precede override");
        Require(!weapon.Methods.Any(m=>m.Name=="OnGetFromPool"),"weapon get inherits patched base");
        Require(Method(usable,"CanInteract",0).IsPublic&&Method(usable,"IsInInteractionStrictCheck",0).IsPublic,"native interaction gates accessible");
        Console.WriteLine($"Native contract MVID={module.Mvid}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)))}; assertions={checks}");
        return checks;
    }
}
