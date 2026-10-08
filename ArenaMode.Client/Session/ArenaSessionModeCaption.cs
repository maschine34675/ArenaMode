using System;
using System.Runtime.CompilerServices;

namespace ArenaMode.Session;

/// <summary>
/// The caption rule for the native matchmaker screens' session-mode label. EFT
/// writes <c>ESessionMode.Pve</c> ("PVE ZONE") above the side, location, accept
/// and deploying screens; inside the Arena flow the user wants "ARENA ZONE".
///
/// Pure on purpose (no Unity, no EFT types) so tools/NativeMenuChecks can
/// compile and mutate it: <see cref="ArenaSessionModeCaptionPatch"/> owns where
/// and when the rule is applied and fails open to the native text.
/// </summary>
internal static class ArenaSessionModeCaption
{
    /// <summary>Replaces the native "PVE ZONE" while an Arena setup flow is on screen.</summary>
    internal const string ArenaText = "ARENA ZONE";

    /// <summary>
    /// The Arena text while the Arena flow is selected, otherwise the native
    /// text unchanged (including null or empty: never invented, never trimmed).
    /// </summary>
    internal static string Resolve(bool arenaSelected, string nativeText)
        => arenaSelected ? ArenaText : nativeText;
}

/// <summary>
/// The set/restore bookkeeping behind the side-screen caption, separated from
/// Unity so tools/NativeMenuChecks executes it with a stand-in label. Only a
/// label this ledger overwrote with the Arena text is ever put back, and only
/// with exactly the text EFT had written there (any language):
/// <list type="bullet">
/// <item>Arena, label not yet Arena: remember the current text, write "ARENA ZONE";</item>
/// <item>Arena, label already "ARENA ZONE": nothing (the remembered text stays);</item>
/// <item>not Arena, nothing remembered: nothing, so an ordinary flow is untouched;</item>
/// <item>not Arena, text remembered: forget it, and write it back only if the
/// label still reads "ARENA ZONE" (a fresh native Show may have rewritten it).</item>
/// </list>
/// <see cref="ArenaSessionModeCaptionPatch"/> instantiates it over
/// <c>TextMeshProUGUI</c>; it may throw whatever <c>read</c>/<c>write</c> throw
/// and the patch catches that and leaves the native text.
/// </summary>
internal sealed class ArenaSessionModeCaptionLedger<TLabel> where TLabel : class
{
    private readonly ConditionalWeakTable<TLabel, string> _native = new ConditionalWeakTable<TLabel, string>();

    internal void Apply(TLabel label, bool arena, Func<TLabel, string> read, Action<TLabel, string> write)
    {
        if (label == null)
            return;
        if (arena)
        {
            string current = read(label);
            if (current == ArenaSessionModeCaption.ArenaText)
                return;
            _native.Remove(label);
            _native.Add(label, current);
            write(label, ArenaSessionModeCaption.Resolve(true, current));
        }
        else if (_native.TryGetValue(label, out string native))
        {
            _native.Remove(label);
            // A fresh native Show may already have rewritten the label.
            if (read(label) == ArenaSessionModeCaption.ArenaText)
                write(label, ArenaSessionModeCaption.Resolve(false, native));
        }
    }
}
