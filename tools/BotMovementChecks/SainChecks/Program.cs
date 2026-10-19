using ArenaMode;
using ArenaMode.Bots;
using ArenaMode.Movement;
using EFT;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

internal static class Program
{
    private static int _assertions;

    private static int Main(string[] args)
    {
        try
        {
            var scenario = args.Length == 0 ? "valid" : args[0];
            if (scenario == "versions")
            {
                RunVersions();
                Console.WriteLine($"BOT_MOVEMENT_SAIN_BEHAVIOR_PASSED: {scenario}, {_assertions} assertions");
                return 0;
            }
            var player = new Player();
            var owner = new BotOwner { GetPlayer = player };
            ArenaBotMovementSain.Enable();
            Check(ArenaBotMovementSain.AllowsBoost(owner, player), "SAIN absent permits native bot");
            Check(!ArenaBotMovementSain.AllowsBoost(null, player), "missing owner rejected");
            Check(!ArenaBotMovementSain.AllowsBoost(owner, null), "missing player rejected");
            Check(!ArenaBotMovementSain.AllowsBoost(owner, new Player()), "stale native player rejected even without SAIN");
            Check(Plugin.ModLogger.Warnings.Count == 0, "absence does not emit compatibility warning");

            // This event occurs after the adapter cached absence. No reflective
            // reset of production state: exercise its real AssemblyLoad hook.
            bool future = scenario is "future" or "future-bad-shape";
            int advisoryCount = future ? 1 : 0;
            var fake = FakeSain.Create(scenario == "bad-version" ? new Version(4, 5, 0, 0)
                    : future ? new Version(4, 6, 0, 0) : new Version(4, 5, 1, 0),
                changedGetter: scenario is "bad-shape" or "future-bad-shape");
            if (scenario is "bad-version" or "bad-shape" or "future-bad-shape")
            {
                Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "unsupported late assembly closes native bonus");
                Check(Plugin.ModLogger.Warnings.Count == 1 + advisoryCount, "contract failure diagnosed once");
                Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "unsupported contract remains closed");
                Check(Plugin.ModLogger.Warnings.Count == 1 + advisoryCount, "unsupported contract does not spam");
            }
            else if (scenario is "valid" or "future")
            {
                RunValid(fake, owner, player, advisoryCount);
            }
            else throw new InvalidOperationException("Unknown scenario " + scenario);

            Console.WriteLine($"BOT_MOVEMENT_SAIN_BEHAVIOR_PASSED: {scenario}, {_assertions} assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private sealed class IdentityAssembly(AssemblyName identity) : Assembly
    {
        public override AssemblyName GetName() => identity;
    }

    private static void RunVersions()
    {
        Assembly Named(string name, Version version) => new IdentityAssembly(new AssemblyName(name) { Version = version });
        foreach (var assembly in new[] { null, Named("SAIN", null), Named("Foreign", new Version(9, 0, 0, 0)),
            Named("SAIN", new Version(4, 5)), Named("SAIN", new Version(4, 5, 0, 99)) })
        {
            bool rejected = false;
            try { ArenaSainVersion.Require(assembly); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "missing, malformed, foreign or older identity rejected");
        }
        foreach (var version in new[] { new Version(4, 5, 1), new Version(4, 5, 1, 0) })
            ArenaSainVersion.Require(Named("SAIN", version));
        Check(Plugin.ModLogger.Warnings.Count == 0, "equivalent baseline identities do not warn");
        foreach (var version in new[] { new Version(4, 5, 1, 1), new Version(4, 5, 2), new Version(4, 6, 0), new Version(5, 0, 0) })
            for (int adapter = 0; adapter < 6; adapter++) ArenaSainVersion.Require(Named("SAIN", version));
        Check(Plugin.ModLogger.Warnings.Count == 1, "future warning is shared across six adapters and repeated binds");
        Check(Plugin.ModLogger.Warnings[0].Contains("tested with 4.5.1")
            && Plugin.ModLogger.Warnings[0].Contains("runtime safety checks"), "future version does not claim compatibility certification");
    }

    private static void RunValid(FakeSain fake, BotOwner owner, Player player, int advisoryCount)
    {
        Check(ArenaBotMovementSain.AllowsBoost(owner, player), "known SAIN without component leaves native bot alone");
        var component = fake.NewComponent();
        owner.Attached = component;
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "uninitialized component has no proven owner");
        fake.Set(component, "BotOwner", owner);
        fake.Set(component, "Player", player);
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "missing door state fails closed");

        var door = fake.NewDoor();
        fake.Set(component, "DoorOpener", door);
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "unbound door fails closed");
        fake.Set(door, "Bot", component);
        Check(ArenaBotMovementSain.AllowsBoost(owner, player), "real reflection chain admits exact idle door");

        fake.Set(door, "Interacting", true);
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "SAIN active door blocks bonus");
        Check((bool)fake.Get(door, "Interacting"), "adapter leaves interaction flag untouched");
        fake.Set(door, "Interacting", false);
        Check(ArenaBotMovementSain.AllowsBoost(owner, player), "fresh flag read allows boost after door finishes");

        fake.Set(component, "BotOwner", new BotOwner { GetPlayer = player });
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "component owner drift rejected");
        fake.Set(component, "BotOwner", owner);
        fake.Set(component, "Player", new Player());
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "component player drift rejected");
        fake.Set(component, "Player", player);
        fake.Set(door, "Bot", fake.NewComponent());
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "door belongs to another component rejected");
        fake.Set(door, "Bot", component);

        component.Destroyed = true;
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "destroyed wrapper is not a missing native component");
        component.Destroyed = false;
        Check(ArenaBotMovementSain.AllowsBoost(owner, player), "temporary uncertainty does not latch off a valid bot");
        owner.Attached = null;
        Check(ArenaBotMovementSain.AllowsBoost(owner, player), "actual missing component allows native boost");
        owner.Attached = component;
        owner.GetPlayer = new Player();
        Check(!ArenaBotMovementSain.AllowsBoost(owner, player), "native body changed after prior valid bind");
        owner.GetPlayer = player;

        Check(ReferenceEquals(fake.Get(component, "BotOwner"), owner), "component owner was never rewritten");
        Check(ReferenceEquals(fake.Get(component, "Player"), player), "component player was never rewritten");
        Check(ReferenceEquals(fake.Get(component, "DoorOpener"), door), "component door was never rewritten");
        Check(ReferenceEquals(fake.Get(door, "Bot"), component), "door ownership was never rewritten");
        Check(Plugin.ModLogger.Warnings.Count == 1 + advisoryCount, "uncertain ownership warns at most once across bots/frames");
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
    }

    private sealed class FakeSain(Type component, Type door)
    {
        internal Component NewComponent() => (Component)Activator.CreateInstance(component);
        internal object NewDoor() => Activator.CreateInstance(door);
        internal void Set(object target, string property, object value) => Field(target.GetType(), property).SetValue(target, value);
        internal object Get(object target, string property) => Field(target.GetType(), property).GetValue(target);

        private static FieldInfo Field(Type type, string property)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField("<" + property + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field;
            }
            throw new InvalidOperationException("Missing fake field " + property);
        }

        internal static FakeSain Create(Version version, bool changedGetter)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SAIN") { Version = version }, AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("SAIN");
            var componentBase = module.DefineType("SAIN.Components.BotComponentBase", TypeAttributes.Public, typeof(MonoBehaviour));
            var component = module.DefineType("SAIN.Components.BotComponent", TypeAttributes.Public, componentBase);
            var botBase = module.DefineType("SAIN.SAINComponent.BotBase", TypeAttributes.Public);
            var doorBase = module.DefineType("SAIN.SAINComponent.BotComponentClassBase", TypeAttributes.Public, botBase);
            var door = module.DefineType("SAIN.SAINComponent.Classes.Mover.DoorOpener", TypeAttributes.Public, doorBase);
            Property(componentBase, "BotOwner", typeof(BotOwner));
            Property(componentBase, "Player", typeof(Player));
            Property(component, "DoorOpener", door);
            Property(botBase, "Bot", component);
            Property(door, "Interacting", typeof(bool), changedGetter);
            componentBase.CreateType();
            botBase.CreateType();
            doorBase.CreateType();
            var doorType = door.CreateType();
            return new FakeSain(component.CreateType(), doorType);
        }

        private static void Property(TypeBuilder type, string name, Type valueType, bool changedGetter = false)
        {
            var field = type.DefineField("<" + name + ">k__BackingField", valueType, FieldAttributes.Private);
            var property = type.DefineProperty(name, PropertyAttributes.None, valueType, Type.EmptyTypes);
            var getter = type.DefineMethod("get_" + name, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                valueType, Type.EmptyTypes);
            var il = getter.GetILGenerator();
            if (changedGetter) il.Emit(OpCodes.Ldc_I4_0);
            else { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); }
            il.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
        }
    }
}
