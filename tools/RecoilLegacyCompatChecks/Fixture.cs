using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using EFT;
using EFT.Animations.NewRecoil;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

public sealed class Fixture
{
    public Type Plugin,Recoil,Camera,Helper;
    public MethodInfo Init,Calc;
    public Fixture(string version="2.0.0",bool missingCamera=false)
    {
        var asm=AssemblyBuilder.DefineDynamicAssembly(new("SPTRecoilReworkLegacyClient"){Version=new Version(version)},AssemblyBuilderAccess.Run);
        var mod=asm.DefineDynamicModule("fixture");
        var p=mod.DefineType("SPTRecoilRework.Plugin",TypeAttributes.Public,typeof(BaseUnityPlugin));
        p.SetCustomAttribute(new(typeof(BepInPlugin).GetConstructor(new[]{typeof(string),typeof(string),typeof(string)}),
            new object[]{"com.pein.camerarecoilmod","SPTRecoilReworkLegacy",version}));
        Plugin=p.CreateType();
        var spring=mod.DefineType("SPTRecoilRework.Helpers.RecoilSpring",TypeAttributes.Public).CreateType();
        Type Component(string name,bool recoil)
        {
            var b=mod.DefineType("SPTRecoilRework.Components."+name,TypeAttributes.Public,typeof(MonoBehaviour));
            var instance=b.DefineField("_instance",b,FieldAttributes.Private|FieldAttributes.Static);
            Property(b,"Instance",b,instance,true);
            if(recoil)
            {
                b.DefineField("_player",typeof(Player),FieldAttributes.Private);
                var s=b.DefineField("_spring",spring,FieldAttributes.Private);
                Property(b,"RecoilSpring",spring,s,false);
            }
            else foreach(var n in new[]{"SlowShakeSpring","FastShakeSpring","CameraSpring"})
                b.DefineField(n,spring,FieldAttributes.Public);
            Forward(b,"Awake",typeof(void),false,new Type[0],nameof(ForeignBehavior.Awake));
            Forward(b,recoil?"FixedUpdate":"Update",typeof(void),false,new Type[0],nameof(ForeignBehavior.Update));
            return b.CreateType();
        }
        Recoil=Component("RealRecoilComponent",true);
        Camera=Component(missingCamera?"WrongCamera":"CameraOffsetComponent",false);
        var h=mod.DefineType("SPTRecoilRework.Helpers.WeaponHelper",TypeAttributes.Public);
        h.DefineField("CurrentFirearmController",typeof(Player.FirearmController),FieldAttributes.Public|FieldAttributes.Static);
        h.DefineField("CurrentTemplate",typeof(WeaponTemplate),FieldAttributes.Public|FieldAttributes.Static);
        h.DefineField("CurrentRecoilMult",typeof(Vector2),FieldAttributes.Public|FieldAttributes.Static);
        h.DefineField("CurrentRecoilVals",typeof(Vector2),FieldAttributes.Public|FieldAttributes.Static);
        h.DefineField("IsPistolCurrentlyEquipped",typeof(bool),FieldAttributes.Public|FieldAttributes.Static);
        Helper=h.CreateType();
        var init=mod.DefineType("SPTRecoilRework.Patches.PlayerInitPatch",TypeAttributes.Public);
        Forward(init,"PatchPostfix",typeof(void),true,new[]{typeof(Player)},nameof(ForeignBehavior.Init));
        Init=init.CreateType().GetMethod("PatchPostfix");
        var calc=mod.DefineType("SPTRecoilRework.Patches.RecalculateRecoilOnWeaponSwitchPatch",TypeAttributes.Public);
        Forward(calc,"PatchPostfix",typeof(void),true,new[]{typeof(NewRecoilShotEffect),typeof(WeaponTemplate),typeof(GlobalConfiguration.AimingConfiguration),
            typeof(Player.FirearmController),typeof(float),typeof(float),typeof(float),typeof(float)},nameof(ForeignBehavior.Calculate));
        Calc=calc.CreateType().GetMethod("PatchPostfix");
    }
    static void Property(TypeBuilder b,string name,Type t,FieldBuilder f,bool stat)
    {
        var attrs=MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.HideBySig|(stat?MethodAttributes.Static:0);
        var p=b.DefineProperty(name,PropertyAttributes.None,t,null);
        var g=b.DefineMethod("get_"+name,attrs,t,Type.EmptyTypes);var il=g.GetILGenerator();
        if(!stat)il.Emit(OpCodes.Ldarg_0);il.Emit(stat?OpCodes.Ldsfld:OpCodes.Ldfld,f);il.Emit(OpCodes.Ret);p.SetGetMethod(g);
        var s=b.DefineMethod("set_"+name,attrs,typeof(void),new[]{t});il=s.GetILGenerator();
        if(!stat)il.Emit(OpCodes.Ldarg_0);il.Emit(stat?OpCodes.Ldarg_0:OpCodes.Ldarg_1);il.Emit(stat?OpCodes.Stsfld:OpCodes.Stfld,f);il.Emit(OpCodes.Ret);p.SetSetMethod(s);
    }
    static void Forward(TypeBuilder b,string name,Type result,bool stat,Type[] args,string call)
    {
        var m=b.DefineMethod(name,(stat?MethodAttributes.Public|MethodAttributes.Static:MethodAttributes.Private),result,args);
        var il=m.GetILGenerator();
        for(int i=0;i<args.Length+(stat?0:1);i++)il.Emit(OpCodes.Ldarg,i);
        il.Emit(OpCodes.Call,typeof(ForeignBehavior).GetMethod(call));il.Emit(OpCodes.Ret);
    }
    public object Instance(Type t)=>t.GetProperty("Instance").GetValue(null);
    public void SetInstance(Type t,object value)=>t.GetProperty("Instance").SetValue(null,value);
    public object Cache(string name)=>Helper.GetField(name).GetValue(null);
    public void Cache(string name,object value)=>Helper.GetField(name).SetValue(null,value);
    public void Bind()
    {
        new Harmony("PlayerInitPatch").Patch(typeof(Player).GetMethod("Init"),postfix:new HarmonyMethod(Init.DeclaringType,Init.Name));
        new Harmony("RecalculateRecoilOnWeaponSwitchPatch").Patch(typeof(NewRecoilShotEffect).GetMethod("RecalculateRecoilParamsOnChangeWeapon"),
            postfix:new HarmonyMethod(Calc.DeclaringType,Calc.Name));
    }
}
public static class ForeignBehavior
{
    public static Fixture Fixture;
    public static bool FailCamera,FailCalc,StaleCalc;
    public static int Calculations;
    public static void Awake(object value)
    {
        var c=(Component)value;var type=c.GetType();
        if(Fixture.Instance(type) is UnityEngine.Object old && old!=null)
        { UnityEngine.Object.Destroy(c.gameObject);return; }
        Fixture.SetInstance(type,c);
        if(type==Fixture.Recoil)
        {
            type.GetField("_player",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,c.GetComponent<Player>());
            var spring=type.GetProperty("RecoilSpring");spring.SetValue(c,Activator.CreateInstance(spring.PropertyType));
        }
        else
        {
            if(FailCamera)throw new InvalidOperationException("camera init fault");
            foreach(var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)) f.SetValue(c,Activator.CreateInstance(f.FieldType));
        }
    }
    public static void Update(object value) { ((Component)value).GetComponent<Player>().Rotations++; }
    public static void Init(Player player)
    {
        if(!player.IsYourPlayer)return;
        player.gameObject.AddComponent(Fixture.Recoil);player.gameObject.AddComponent(Fixture.Camera);
    }
    public static void Calculate(NewRecoilShotEffect effect,WeaponTemplate t,GlobalConfiguration.AimingConfiguration a,
        Player.FirearmController hands,float x,float y,float z,float m)
    {
        Calculations++;
        var previous=Fixture.Cache("CurrentFirearmController") as Player.FirearmController;
        Fixture.Cache("CurrentRecoilVals",new Vector2(previous?.Template.Value??0,0));
        Fixture.Cache("CurrentFirearmController",hands);Fixture.Cache("CurrentTemplate",t);
        if(StaleCalc)Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer=null;
        if(FailCalc)throw new InvalidOperationException("recalculation fault");
    }
}
