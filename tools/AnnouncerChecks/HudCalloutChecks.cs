using System.Text.RegularExpressions;
using ArenaMode.Audio;
using ArenaMode.Config;
using ArenaMode.Session;

internal static partial class Program
{
    /// <summary>
    /// 2026-10-08 menu polish, item 1: the ANNOUNCER mutator also hides the
    /// announcer's on-screen kill/award callouts. <c>ArenaHud</c> is
    /// Unity-bound (canvas, TMP) and is not linked into this harness, so the
    /// proof has two halves:
    ///   1. behaviour: the one gate the HUD asks, <c>AnnouncerService.
    ///      IsEnabledForLiveMatch()</c>, is the live master switch ANDed with
    ///      the per-mode selection frozen at arm, exactly like the stingers;
    ///   2. routing, by source text (the repo's established pin for Unity-bound
    ///      code, see tools/ArenaTracerChecks): ArenaHud sends
    ///      <c>ArenaMatch.OnCallout</c> through that gate and
    ///      <c>ArenaMatch.OnTextCallout</c> ("WAVE n", the own-grenade taunt)
    ///      around it. Verify-Regression.ps1 mutates ArenaHud.cs through
    ///      ARENA_ANNOUNCER_HUD_SOURCE and must see each pin fail.
    /// Every routing pin reads the HUD with comments removed
    /// (<see cref="StripCSharpComments"/>): a commented-out subscription is a
    /// dropped subscription, not a present one. The subscription pins are exact
    /// lists, not substrings: the HUD must subscribe OnCallout once, to the gated
    /// handler, and no other statement may route it (or anything else) to the
    /// callout element, so an additional ungated subscription or a lambda cannot
    /// slip in beside the right one.
    /// </summary>
    private static void VerifyHudCalloutGate()
    {
        bool master = Settings.AnnouncerEnabled.Value;
        bool selection = ArenaSession.AnnouncerEnabledSelection;
        try
        {
            foreach (bool masterOn in new[] { true, false })
            {
                foreach (bool selectionOn in new[] { true, false })
                {
                    Settings.AnnouncerEnabled.Value = masterOn;
                    ArenaSession.AnnouncerEnabledSelection = selectionOn;
                    Assert(AnnouncerService.IsEnabledForLiveMatch() == (masterOn && selectionOn),
                        $"HUD callout gate is master AND frozen per-mode selection (master={masterOn}, mode={selectionOn})");
                }
            }
        }
        finally
        {
            Settings.AnnouncerEnabled.Value = master;
            ArenaSession.AnnouncerEnabledSelection = selection;
        }

        string hud = ReadHudSource();
        string award = MethodBody(hud, "private static void OnAwardCallout(string label)");
        string text = MethodBody(hud, "private static void OnTextCallout(string label)");

        Assert(award.Contains("Audio.AnnouncerService.IsEnabledForLiveMatch()", StringComparison.Ordinal)
               && award.IndexOf("IsEnabledForLiveMatch", StringComparison.Ordinal)
                  < award.IndexOf("_callout?.Show(label)", StringComparison.Ordinal),
            "ArenaHud shows kill/award callouts only after asking the announcer gate");
        // The whole handler, not a substring: the negated gate is the FIRST
        // statement, it returns, and the callout is the LAST statement. This is
        // what makes an inverted gate (`!` dropped), a neutralised one
        // (`&& false`, `|| true`) or an extra clause in front of the gate fail.
        Assert(AwardHandlerIsExactlyGateReturnShow(award),
            "ArenaHud drops a kill/award callout when the announcer gate is closed");
        Assert(text.Contains("_callout?.Show(label)", StringComparison.Ordinal)
               && !text.Contains("IsEnabledForLiveMatch", StringComparison.Ordinal)
               && !text.Contains("Announcer", StringComparison.Ordinal),
            "HUD text callouts (WAVE n, grenade taunt) never ask the announcer gate");
        string hudCode = StripCSharpComments(hud);
        Assert(hudCode.Contains("match.OnCallout += OnAwardCallout;", StringComparison.Ordinal)
               && hudCode.Contains("_match.OnCallout -= OnAwardCallout;", StringComparison.Ordinal),
            "OnCallout is subscribed to and released from the gated handler");
        Assert(hudCode.Contains("match.OnTextCallout += OnTextCallout;", StringComparison.Ordinal)
               && hudCode.Contains("_match.OnTextCallout -= OnTextCallout;", StringComparison.Ordinal),
            "OnTextCallout is subscribed to and released from the ungated handler");
        // Exact lists: the gated handler is the ONLY subscriber of OnCallout and
        // OnTextCallout keeps its own handler. A second subscription (the pre-
        // cc23559 shape `match.OnCallout += OnTextCallout;`, a lambda, a method
        // group of another class) would show DOUBLE KILL with the announcer off.
        Assert(string.Join("|", EventTargets(hudCode, "OnCallout", "+=")) == "OnAwardCallout"
               && string.Join("|", EventTargets(hudCode, "OnCallout", "-=")) == "OnAwardCallout",
            "ArenaHud subscribes OnCallout exactly once and only to the gated handler");
        Assert(string.Join("|", EventTargets(hudCode, "OnTextCallout", "+=")) == "OnTextCallout"
               && string.Join("|", EventTargets(hudCode, "OnTextCallout", "-=")) == "OnTextCallout",
            "ArenaHud subscribes OnTextCallout exactly once and only to the ungated handler");
        // The callout element is reachable from the two handlers and nowhere else.
        Assert(Regex.Matches(hudCode, @"_callout\s*\??\s*\.\s*Show\s*\(").Count == 2,
            "no HUD statement but the two callout handlers shows the callout element");
        Assert(CountOf(hud, "IsEnabledForLiveMatch()") == 1,
            "exactly one HUD handler asks the announcer gate");
    }

    /// <summary>
    /// The right-hand sides of every <c>Event += X;</c> (or <c>-=</c>) in
    /// <paramref name="code"/> for the event named exactly <paramref name="eventName"/>
    /// (a longer name such as OnTextCallout never matches OnCallout), in order,
    /// whitespace removed.
    /// </summary>
    private static string[] EventTargets(string code, string eventName, string op)
        => Regex.Matches(code, @"(?<![A-Za-z0-9_])" + Regex.Escape(eventName) + @"\s*" + Regex.Escape(op) + @"\s*([^;]+);")
            .Select(match => Regex.Replace(match.Groups[1].Value, @"\s+", string.Empty))
            .ToArray();

    /// <summary>Source text with line and block comments removed and whitespace collapsed.</summary>
    internal static string StripCSharpComments(string source)
    {
        string code = Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(code, @"[ \t]+", " ");
    }

    /// <summary>
    /// True when the handler is exactly
    /// <c>{ if (!Audio.AnnouncerService.IsEnabledForLiveMatch()) return; _callout?.Show(label); }</c>
    /// once comments and whitespace are normalised away.
    /// </summary>
    internal static bool AwardHandlerIsExactlyGateReturnShow(string handler)
    {
        string code = StripCSharpComments(handler);
        return Regex.IsMatch(code,
            @"^\s*private\s+static\s+void\s+OnAwardCallout\s*\(\s*string\s+label\s*\)\s*\{"
            + @"\s*if\s*\(\s*!\s*Audio\s*\.\s*AnnouncerService\s*\.\s*IsEnabledForLiveMatch\s*\(\s*\)\s*\)"
            + @"\s*return\s*;"
            + @"\s*_callout\s*\?\s*\.\s*Show\s*\(\s*label\s*\)\s*;"
            + @"\s*\}\s*$");
    }

    private static int CountOf(string source, string fragment)
    {
        int count = 0;
        for (int index = source.IndexOf(fragment, StringComparison.Ordinal); index >= 0;
             index = source.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>The text from a method signature to the end of its block or expression body.</summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert(start >= 0, "ArenaHud still declares " + signature);
        int open = source.IndexOf('{', start + signature.Length);
        int arrow = source.IndexOf("=>", start + signature.Length, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
            return source.Substring(start, source.IndexOf(';', arrow) - start + 1);
        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source.Substring(start, index - start + 1);
        }
        throw new InvalidOperationException("unterminated method body: " + signature);
    }

    private static string ReadHudSource()
    {
        string overridePath = Environment.GetEnvironmentVariable("ARENA_ANNOUNCER_HUD_SOURCE");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.ReadAllText(overridePath);
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "ArenaMode.Client", "Hud", "ArenaHud.cs"));
    }

    private static string FindRepositoryRoot()
    {
        string overrideRoot = Environment.GetEnvironmentVariable("ARENA_ANNOUNCER_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot)
            && File.Exists(Path.Combine(overrideRoot, "ArenaMode.sln")))
            return Path.GetFullPath(overrideRoot);

        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArenaMode.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("ArenaMode.sln not found above harness output");
    }
}
