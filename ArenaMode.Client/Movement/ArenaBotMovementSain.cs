using EFT;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Read-only SAIN door guard. SAIN can back away from a door with ordinary Run
/// motion while its own door handler temporarily ignores that door's collider.
/// Native door flags alone cannot prove that this motion is safe to accelerate.
/// </summary>
internal static class ArenaBotMovementSain
{
    private enum BindingState { Uninitialized, Absent, Ready, Unsupported }

    // Metadata only: never retain a bot, component, player, door or raid here.
    private static volatile BindingState _state;
    private static readonly object BindingGate = new();
    private static Type _componentType, _doorType;
    private static PropertyInfo _owner, _player, _door, _doorBot, _interacting;
    private static bool _warned, _watchingAssemblies;

    internal static void Enable()
    {
        if (_state != BindingState.Uninitialized) return;
        lock (BindingGate)
        {
            Bind();
        }
    }

    private static void Bind()
    {
        if (!_watchingAssemblies)
        {
            _watchingAssemblies = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
        }
        if (_state != BindingState.Uninitialized) return;
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(candidate => candidate.GetName().Name == "SAIN").ToArray();
            if (assemblies.Length == 0)
            {
                _state = BindingState.Absent;
                return;
            }
            if (assemblies.Length != 1
                || assemblies[0].GetName().Version != new System.Version(4, 5, 1, 0))
                throw new InvalidOperationException("expected one SAIN 4.5.1 assembly");

            var assembly = assemblies[0];
            var component = assembly.GetType("SAIN.Components.BotComponent", true);
            var componentBase = assembly.GetType("SAIN.Components.BotComponentBase", true);
            var door = assembly.GetType("SAIN.SAINComponent.Classes.Mover.DoorOpener", true);
            var doorBase = assembly.GetType("SAIN.SAINComponent.BotComponentClassBase", true);
            var botBase = assembly.GetType("SAIN.SAINComponent.BotBase", true);
            if (component.BaseType != componentBase || componentBase.BaseType != typeof(MonoBehaviour)
                || door.BaseType != doorBase || doorBase.BaseType != botBase)
                throw new InvalidOperationException("SAIN component/door inheritance changed");

            // The installed 4.5.1 getters are exactly ldarg.0 / ldfld / ret.
            // Validate before invoking any getter from this optional assembly.
            _owner = BindFieldGetter(componentBase, "BotOwner", typeof(BotOwner));
            _player = BindFieldGetter(componentBase, "Player", typeof(Player));
            _door = BindFieldGetter(component, "DoorOpener", door);
            _doorBot = BindFieldGetter(botBase, "Bot", component);
            _interacting = BindFieldGetter(door, "Interacting", typeof(bool));
            _componentType = component;
            _doorType = door;
            _state = BindingState.Ready;
        }
        catch (Exception exception)
        {
            _state = BindingState.Unsupported;
            _componentType = _doorType = null;
            _owner = _player = _door = _doorBot = _interacting = null;
            Warn("SAIN door contract unavailable", exception);
        }
    }

    private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
    {
        // A late-loaded optional mod must not inherit the earlier absence result.
        // Rebind on the next movement call, never touch Unity on a loader thread.
        if (args.LoadedAssembly.GetName().Name == "SAIN")
            lock (BindingGate) _state = BindingState.Uninitialized;
    }

    internal static bool AllowsBoost(BotOwner owner, Player player)
    {
        // The caller owns the exact active Arena/body admission. Also reject
        // stale native pairs here before treating a missing component as vanilla.
        if (owner == null || player == null || !ReferenceEquals(owner.GetPlayer, player))
            return false;
        Enable();
        if (_state == BindingState.Absent) return true;
        if (_state != BindingState.Ready) return false;
        try
        {
            // SAIN creates BotComponent on the native owner's GameObject.
            var component = owner.GetComponent(_componentType);
            if (ReferenceEquals(component, null)) return true;
            if (component == null || component.GetType() != _componentType
                || !ReferenceEquals(_owner.GetValue(component), owner)
                || !ReferenceEquals(_player.GetValue(component), player))
            {
                Warn("SAIN component ownership is unproven", null);
                return false;
            }

            var door = _door.GetValue(component);
            if (door == null || door.GetType() != _doorType
                || !ReferenceEquals(_doorBot.GetValue(door), component))
            {
                Warn("SAIN door ownership is unproven", null);
                return false;
            }
            return !(bool)_interacting.GetValue(door);
        }
        catch (Exception exception)
        {
            Warn("SAIN door state could not be read", exception);
            return false;
        }
    }

    private static PropertyInfo BindFieldGetter(Type declaringType, string name, Type resultType)
    {
        var property = declaringType.GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var getter = property?.GetGetMethod();
        var body = getter?.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (property?.PropertyType != resultType || property.GetIndexParameters().Length != 0
            || getter == null || getter.IsStatic || (getter.IsVirtual && !getter.IsFinal)
            || getter.GetParameters().Length != 0
            || body == null || body.ExceptionHandlingClauses.Count != 0 || body.LocalVariables.Count != 0
            || il == null || il.Length != 7 || il[0] != 0x02 || il[1] != 0x7b || il[6] != 0x2a)
            throw new InvalidOperationException("SAIN getter changed: " + declaringType.FullName + "." + name);
        var field = getter.Module.ResolveField(BitConverter.ToInt32(il, 2));
        if (field.DeclaringType != declaringType || field.IsStatic || field.FieldType != resultType
            || field.Name != "<" + name + ">k__BackingField")
            throw new InvalidOperationException("SAIN backing field changed: " + name);
        return property;
    }

    private static void Warn(string reason, Exception exception)
    {
        if (_warned) return;
        _warned = true;
        Plugin.ModLogger.LogWarning("Arena bot movement: " + reason
            + "; speed bonus withheld, original movement retained"
            + (exception == null ? "." : ": " + exception.GetBaseException().Message));
    }
}
