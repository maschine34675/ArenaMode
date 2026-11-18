using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NativeContracts
{
    internal static int Verify(string assemblyPath)
    {
        using var module=ModuleDefinition.ReadModule(assemblyPath);
        int checks=0;
        void Require(bool value,string name) {checks++;if(!value)throw new Exception("FAIL: native "+name);}
        TypeDefinition Type(string name)=>module.Types.Single(t=>t.FullName==name);
        MethodDefinition Method(TypeDefinition type,string name,int count)=>type.Methods.Single(m=>m.Name==name&&m.Parameters.Count==count);
        static MethodDefinition AsyncBody(MethodDefinition method)
        {
            var state=method.CustomAttributes.Single(a=>a.AttributeType.Name=="AsyncStateMachineAttribute");
            return ((TypeReference)state.ConstructorArguments[0].Value).Resolve().Methods.Single(m=>m.Name=="MoveNext");
        }
        static bool Calls(MethodDefinition method,string type,string name)=>method.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name);
        var factory=Type("EFT.ObjectsFactory");var create=Method(factory,"CreateItemAsync",6);
        Require(create.Parameters[0].ParameterType.FullName=="EFT.InventoryLogic.Item"&&create.ReturnType.FullName=="System.Threading.Tasks.Task`1<UnityEngine.GameObject>","item async ABI");
        Require(Calls(AsyncBody(create),factory.FullName,"PopOrCreate"),"item async uses native pool");
        Require(Calls(AsyncBody(Method(factory,"CreateCleanLootPrefabAsync",2)),factory.FullName,"CreateItemAsync"),"icon reaches wrapped model");
        Require(Calls(AsyncBody(Method(Type("ItemIconCreator"),"RenderModel",2)),factory.FullName,"CreateCleanLootPrefabAsync"),"native icon render chain");
        var pool=Type("EFT.AssetsManager.AssetPoolObject");
        Require(Method(pool,"ReturnToPool",0).IsVirtual&&Method(pool,"OnGetFromPool",0).IsVirtual,"pool lifecycle ABI");
        Require(Calls(Method(Type("WeaponPrefab"),"ReturnToPool",0),pool.FullName,"ReturnToPool"),"weapon pool base cleanup");
        Require(Method(pool,"OnCreatePoolRoleModel",0).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="set_IsInPool"),"shared role marked pooled");
        var translator=Type("EFT.UsableItemInputTranslator");var translate=Method(translator,"TranslateCommand",1);
        Require(translate.Parameters[0].ParameterType.FullName=="EFT.InputSystem.ECommand"&&translate.ReturnType.Name=="ETranslateResult","native input hook ABI");
        Require(translator.Fields.Any(f=>f.Name=="_controller"&&f.IsPublic),"native translator controller accessible");
        Require(translate.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="ToggleAim")&&translate.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetAim"),"native secondary ADS must be consumed");
        var player=Type("EFT.Player");var hands=player.NestedTypes.Single(t=>t.Name=="ItemHandsController");
        Require(hands.Fields.Any(f=>f.Name=="_player"&&f.IsPublic),"native hands player accessible");
        var usable=player.NestedTypes.Single(t=>t.Name=="UsableItemController");
        Require(Method(usable,"CanInteract",0).IsPublic&&Method(usable,"IsInInteractionStrictCheck",0).IsPublic,"native transition gates accessible");
        var bind=Method(Type("EFT.InventoryLogic.BindResult"),"Run",4);
        Require(Calls(bind,"EFT.InventoryLogic.InventoryController","IsAtBindablePlace")&&Calls(bind,"EFT.InventoryLogic.InventoryController","UnbindItemDirect"),"native binding can evict and requires caller guards");
        Require(bind.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="set_Item"),"native non simulated binding commits synchronously");
        var combo=Type("EFT.InputSystem.ToggleInputKeyCombination");
        var start=Method(combo.NestedTypes.Single(t=>t.Name=="ContinuousIdlingToggleState"),"GetCommand",1);
        Require(start.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="ChangeState"),"continuous press advances state");
        var end=Method(combo.NestedTypes.Single(t=>t.Name=="ContinuousPressedToggleState"),"GetCommandInternal",0);
        Require(end.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="_disableCommand"),"continuous release emits disable command");
        // The production handler deliberately has no end-dependent latch: inspect
        // the installed hold branch itself, rather than making a fake key repeat rule.
        var hold=Method(Type("EFT.InputSystem.InputKeyCombination").NestedTypes.Single(t=>t.Name=="ContinuousPressedState"),"GetCommand",1).Body.Instructions;
        int status=hold.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.Name=="GetKeysStatus");
        int holdValue=(int)Type("EFT.InputSystem.EKeyPress").Fields.Single(f=>f.Name=="Hold").Constant;
        int none=(int)Type("EFT.InputSystem.ECommand").Fields.Single(f=>f.Name=="None").Constant;
        Require(status>=0&&hold[status+1].OpCode==OpCodes.Brfalse_S&&hold[status+2].OpCode==OpCodes.Ldloc_0
            &&hold[status+3].OpCode==OpCodes.Ldc_I4_2&&holdValue==2&&hold[status+4].OpCode==OpCodes.Bne_Un_S,"native hold branch exact");
        Require(hold[status+7].Operand is MethodReference held&&held.Name=="HandleKeys"
            &&hold[status+8].OpCode==OpCodes.Ldc_I4_0&&none==0&&hold[status+9].OpCode==OpCodes.Ret,"held key emits no repeated start");
        return checks;
    }
}
