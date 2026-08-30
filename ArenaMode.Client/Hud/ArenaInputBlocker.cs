using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ArenaMode.Hud;

/// <summary>
/// Suppresses the game's own input while the arena panel is open, so clicking a
/// toggle does not fire the weapon and dragging the window does not turn the
/// character. The game funnels every bound input through a single
/// InputManager method with the signature (List&lt;ECommand&gt;, float[]);
/// emptying the list and zeroing the axes blocks exactly one delivery, so
/// nothing piles up for later.
///
/// Lifted from the same author's ModProfiler, which has been running this in
/// raids for a while.
/// </summary>
internal static class ArenaInputBlocker
{
    internal static volatile bool Blocked;

    /// <summary>
    /// Who currently wants input blocked. Two panels can be up at once — the
    /// scoreboard over the buy menu — and a plain bool let whichever CLOSED
    /// first hand live game input to the one still open. That is the exact
    /// condition these panels exist to prevent: a click on a kit fires the
    /// weapon and a drag on the window turns the character.
    ///
    /// The bool stays the thing the patch reads; this only decides its value.
    /// </summary>
    private static readonly HashSet<object> Owners = new HashSet<object>();

    /// <summary>Blocked while ANY owner still wants it.</summary>
    internal static void Request(object owner, bool wanted)
    {
        if (owner == null)
            return;

        lock (Owners)
        {
            if (wanted)
                Owners.Add(owner);
            else
                Owners.Remove(owner);

            Blocked = Owners.Count > 0;
        }
    }

    private static bool _attempted;

    /// <summary>
    /// Commands that must ALWAYS pass: they are the release half of a
    /// press/release pair. Swallowing "EndShooting" while the trigger is down
    /// latches the weapon into permanent fire; the same goes for sprint, lean,
    /// breath-hold and interaction. Escape passes too, so the game's own menu
    /// still works while the panel is open.
    /// </summary>
    private static HashSet<int> _passThrough;

    public static void Apply()
    {
        if (_attempted)
            return;

        _attempted = true;
        try
        {
            var type = AccessTools.TypeByName("EFT.InputSystem.InputManager")
                ?? throw new TypeLoadException("EFT.InputSystem.InputManager not found");

            var target = FindDeliverMethod(type);
            BuildPassThrough(target);
            new Harmony(Plugin.PluginGuid + ".inputblock").Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(ArenaInputBlocker), nameof(Prefix))));

            Plugin.ModLogger.LogDebug($"Arena input blocker installed on InputManager.{target.Name}.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                "Arena input blocker unavailable — the panel will free the cursor but not "
                + $"suppress game input: {ex.Message}");
        }
    }

    /// <summary>Matched by SIGNATURE so an SPT rename cannot break it silently.</summary>
    private static MethodInfo FindDeliverMethod(Type type)
    {
        foreach (var method in type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.ReturnType != typeof(void))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length == 2
                && parameters[0].ParameterType.IsGenericType
                && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(List<>)
                && parameters[0].ParameterType.GetGenericArguments()[0].Name == "ECommand"
                && parameters[1].ParameterType == typeof(float[]))
            {
                return method;
            }
        }

        throw new MissingMethodException(
            "no delivery method with signature (List<ECommand>, float[]) on EFT.InputSystem.InputManager");
    }

    /// <summary>
    /// Resolved once off the delivery method's own generic argument, so a
    /// renamed enum type cannot break it.
    /// </summary>
    private static void BuildPassThrough(MethodInfo target)
    {
        var commandType = target.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var pass = new HashSet<int>();

        foreach (var name in Enum.GetNames(commandType))
        {
            if (name.StartsWith("End", StringComparison.Ordinal)
                || name.EndsWith("End", StringComparison.Ordinal)
                || name.StartsWith("Stop", StringComparison.Ordinal)
                || name.StartsWith("Release", StringComparison.Ordinal)
                || name == "Escape")
            {
                pass.Add(Convert.ToInt32(Enum.Parse(commandType, name)));
            }
        }

        _passThrough = pass;
    }

    // Positional injection (__0/__1): binding by parameter NAME would break
    // silently if an update renames them while the signature stays.
    private static void Prefix(IList __0, float[] __1)
    {
        if (!Blocked)
            return;

        if (__0 != null)
        {
            if (_passThrough == null)
            {
                __0.Clear();
            }
            else
            {
                // Keep the release half of every press/release pair; drop the rest.
                for (var i = __0.Count - 1; i >= 0; i--)
                {
                    if (!_passThrough.Contains(Convert.ToInt32(__0[i])))
                        __0.RemoveAt(i);
                }
            }
        }

        if (__1 != null)
            Array.Clear(__1, 0, __1.Length);
    }
}
