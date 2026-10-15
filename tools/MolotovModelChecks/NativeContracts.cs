using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NativeContracts
{
    internal static int Verify(string assemblyPath)
    {
        using var module=ModuleDefinition.ReadModule(assemblyPath);
        int checks=0;
        void Require(bool value,string name) { checks++; if(!value)throw new Exception("FAILED: native "+name); }
        TypeDefinition Type(string name)=>module.Types.Single(t=>t.FullName==name);
        MethodDefinition Method(TypeDefinition type,string name,int count)=>type.Methods.Single(m=>m.Name==name&&m.Parameters.Count==count);
        static MethodDefinition AsyncBody(MethodDefinition method)
        {
            var state=method.CustomAttributes.Single(a=>a.AttributeType.Name=="AsyncStateMachineAttribute");
            return ((TypeReference)state.ConstructorArguments[0].Value).Resolve().Methods.Single(m=>m.Name=="MoveNext");
        }
        static bool Calls(MethodDefinition method,string type,string name)=>method.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name);
        var factory=Type("EFT.ObjectsFactory");var asyncCreate=Method(factory,"CreateItemAsync",6);
        Require(asyncCreate.Parameters[0].ParameterType.FullName=="EFT.InventoryLogic.Item" && asyncCreate.ReturnType.FullName=="System.Threading.Tasks.Task`1<UnityEngine.GameObject>","item async ABI");
        Require(Calls(AsyncBody(asyncCreate),"EFT.ObjectsFactory","PopOrCreate"),"item async obtains native instance");
        Require(Calls(AsyncBody(Method(factory,"CreateCleanLootPrefabAsync",2)),"EFT.ObjectsFactory","CreateItemAsync"),"icon loot reaches wrapped factory");
        Require(Calls(AsyncBody(Method(Type("ItemIconCreator"),"RenderModel",2)),"EFT.ObjectsFactory","CreateCleanLootPrefabAsync"),"icon renders native model");
        var pool=Type("EFT.AssetsManager.AssetPoolObject");
        Require(Method(pool,"ReturnToPool",0).IsVirtual && Method(pool,"OnGetFromPool",0).IsVirtual,"pool hook ABI");
        var returnBody=Method(pool,"ReturnToPool",0);
        Require(returnBody.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Push"),"pool return owns push");
        Require(Calls(Method(Type("WeaponPrefab"),"ReturnToPool",0),pool.FullName,"ReturnToPool"),"weapon return reaches base hook");
        Require(Calls(Method(Type("GrenadePrefab"),"ReturnToPool",0),"WeaponPrefab","ReturnToPool"),"grenade return reaches weapon hook");
        Require(Method(pool,"OnCreatePoolRoleModel",0).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="set_IsInPool"),"role model marked pooled");
        var player=Type("EFT.Player");var controller=player.NestedTypes.Single(t=>t.Name=="BaseGrenadeHandsController");
        var create=Method(controller,"CreateAndRegisterGrenade",4);
        Require(create.ReturnType.FullName=="EFT.Grenade","projectile hook ABI");
        Require(Calls(create,"UnityEngine.Object","Instantiate"),"projectile is separate clone");
        Require(Calls(create,"GrenadeFactory","Create"),"projectile keeps native factory");
        return checks;
    }
}
