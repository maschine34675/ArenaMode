using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class PublishedMetadata
{
 internal static void Verify(string path,Action<bool,string> check)
 {
  using var asm=AssemblyDefinition.ReadAssembly(path);
  check(asm.Name.Name=="ozen.ContinuousLoadAmmo"&&asm.Name.Version==new Version(1,2,0,0),"published assembly version");
  var plugin=asm.MainModule.GetType("ContinuousLoadAmmo.ContinuousLoadAmmo");
  var attr=plugin.CustomAttributes.Single(a=>a.AttributeType.Name=="BepInPlugin");
  check((string)attr.ConstructorArguments[0].Value=="com.ozen.continuousloadammo"&&(string)attr.ConstructorArguments[2].Value=="1.2.0","published GUID and plugin version");
  var reg=asm.MainModule.GetType("ContinuousLoadAmmo.Patches.RegisterPlayerPatch");
  var ctl=asm.MainModule.GetType("ContinuousLoadAmmo.Controllers.LoadAmmoController");
  check(reg.Fields.Any(f=>f.Name=="_loadAmmoController"&&f.IsStatic&&f.FieldType.FullName==ctl.FullName),"published static controller slot");
  check(ctl.Fields.Any(f=>f.Name=="_player"&&!f.IsStatic&&f.FieldType.FullName=="EFT.Player"),"published exact body field");
  var register=reg.Methods.Single(m=>m.Name=="Postfix");check(register.IsStatic&&register.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"EFT.GameWorld","EFT.IPlayer"}),"published registration signature");
  foreach(var name in new[]{"Dispose","LoadingEnd","LoadingOutsideInventory"})check(ctl.Methods.Any(m=>m.Name==name&&!m.IsStatic&&m.Parameters.Count==0&&m.ReturnType.FullName=="System.Void"),"published "+name);
  var state=ctl.Methods.Single(m=>m.Name=="SetPlayerStateAsync");check(state.Parameters.Single().ParameterType.FullName=="System.Boolean"&&state.ReturnType.FullName=="System.Threading.Tasks.Task","published delayed state signature");
  var machine=(TypeReference)state.CustomAttributes.Single(a=>a.AttributeType.Name=="AsyncStateMachineAttribute").ConstructorArguments[0].Value;
  var move=machine.Resolve().Methods.Single(m=>m.Name=="MoveNext");
  check(move.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldc_I4&&(int)i.Operand==800),"published 800 ms delay");
  foreach(var name in new[]{"PlayerInventoryController","IsActive"})check(ctl.Properties.Any(p=>p.Name==name),"published "+name+" property");
  check(reg.Methods.Single(m=>m.Name=="OnDispose").Body.Instructions.Any(i=>i.OpCode==OpCodes.Stsfld&&((FieldReference)i.Operand).Name=="_loadAmmoController"),"published static disposal mutation");
  foreach(var t in new[]{"LoadingEndPatches","InventoryScreenClosePatch"})check(asm.MainModule.GetType("ContinuousLoadAmmo.Patches."+t).Events.Any(e=>e.EventType.FullName=="System.Action"),"published identity-free "+t+" event");
  var end=asm.MainModule.GetType("ContinuousLoadAmmo.Patches.LoadingEndPatches").Methods.Single(m=>m.Name=="Postfix");
  check(end.IsStatic&&end.Parameters.Single().ParameterType.FullName=="System.Threading.Tasks.Task`1<Comfort.Common.IResult>","published completion signature");
  check(plugin.Properties.Single(p=>p.Name=="SpeedLimit").PropertyType.FullName=="BepInEx.Configuration.ConfigEntry`1<System.Single>","published speed setting");
  check(asm.MainModule.GetType("ContinuousLoadAmmo.Utils.MultiSelectInterop").Properties.Single(p=>p.Name=="MultiSelectLoadSerializerIsActive").PropertyType.FullName=="System.Boolean","published MultiSelect getter");
 }
}
