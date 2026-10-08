using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ArenaMode.Session;

// 2026-10-08 menu polish, item 6: "PVE ZONE" becomes "ARENA ZONE" on the Arena
// setup screens. The pure rule (ArenaSessionModeCaption.Resolve) is compiled and
// executed; the Harmony patches are Unity/EFT-bound, so their gates are pinned by
// source text, the repo's established proof for such code. Verify-
// SessionModeCaption.ps1 mutates each source through the ARENA_CAPTION_* variables
// (and the pure helper through ProductionSessionModeCaptionSource) and must see
// the named assertion fail.
internal static partial class Program
{
    private static void CheckSessionModeCaption()
    {
        // Pure rule.
        Assert(ArenaSessionModeCaption.ArenaText == "ARENA ZONE", "the Arena caption is exactly ARENA ZONE");
        Assert(ArenaSessionModeCaption.Resolve(true, "PVE ZONE") == "ARENA ZONE"
               && ArenaSessionModeCaption.Resolve(true, null) == "ARENA ZONE"
               && ArenaSessionModeCaption.Resolve(true, "") == "ARENA ZONE",
            "Resolve returns the Arena caption while Arena is selected");
        foreach (string native in new[] { "PVE ZONE", "PVP ZONE", "", null, " pve zone ", "ZONE" })
            Assert(ArenaSessionModeCaption.Resolve(false, native) == native,
                "Resolve leaves the native text unchanged outside Arena: " + (native ?? "<null>"));

        // The set/restore state machine behind the side-screen caption, executed.
        CheckCaptionLedger();

        // Gates of the Harmony patches, by source text. EVERY pin below reads the
        // sources with comments removed (NormalizeCode): a statement that was
        // commented out is a statement that is gone, so a half-disabled caption
        // feature (an Enable() line, the Plugin call, a side-screen sync) cannot
        // end the run green on the strength of its own commented-out text.
        string patchCode = NormalizeCode(ReadCaptionSource("ARENA_CAPTION_PATCH_SOURCE", "ArenaMode.Client", "Session", "ArenaSessionModeCaptionPatch.cs"));
        string sideCode = NormalizeCode(ReadCaptionSource("ARENA_CAPTION_SIDE_SOURCE", "ArenaMode.Client", "Session", "ArenaSideSelectionPatch.cs"));
        string pluginCode = NormalizeCode(ReadCaptionSource("ARENA_CAPTION_PLUGIN_SOURCE", "ArenaMode.Client", "Plugin.cs"));

        string location = ClassBody(patchCode, "LocationPatch");
        Assert(location.Contains("nameof(MatchMakerSelectionLocationScreen.ShowInternal)", StringComparison.Ordinal)
               && location.Contains("[PatchPostfix]", StringComparison.Ordinal),
            "the location caption is written after ShowInternal, which finishes every Show");
        Assert(HasGuardedApply(location, "ArenaMenuSelection.IsSelectedFor(raidSettings)"),
            "location caption is gated on the exact Arena RaidSettings");

        string accept = ClassBody(patchCode, "AcceptPatch");
        Assert(accept.Contains("typeof(MatchMakerAcceptScreen.MatchmakerAcceptScreenController)", StringComparison.Ordinal)
               && accept.Contains("[PatchPostfix]", StringComparison.Ordinal),
            "the accept caption is written after Show(MatchmakerAcceptScreenController)");
        Assert(HasGuardedApply(accept, "controller != null && ArenaMenuSelection.IsSelectedFor(controller.RaidSettings)"),
            "accept caption is gated on the exact Arena RaidSettings");

        string deploying = ClassBody(patchCode, "DeployingPatch");
        Assert(deploying.Contains("typeof(MatchmakerTimeHasCome.TimeHasComeScreenController)", StringComparison.Ordinal)
               && deploying.Contains("[PatchPostfix]", StringComparison.Ordinal),
            "the deploying caption is written after Show(TimeHasComeScreenController)");
        Assert(deploying.Contains("!controller.IsReconnecting", StringComparison.Ordinal),
            "deploying caption excludes a reconnect to an ordinary raid");
        // The WHOLE condition, as one statement: a substring pin would accept a
        // negated group (`!(A || B)`) or a changed operator (`... || (A || B)`).
        Assert(HasGuardedApply(deploying,
                "controller != null && !controller.IsReconnecting && (ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid)"),
            "deploying caption is gated on the Arena flow");

        foreach (string body in new[] { location, accept, deploying })
            Assert(body.Contains("catch (Exception", StringComparison.Ordinal),
                "a caption patch fails open to the native text instead of throwing");
        Assert(CountOfText(patchCode, "Apply(__instance._subCaption, true)") == 3
               && Regex.Matches(patchCode, @"\.text\s*=(?!=)").Count == 1
               && Regex.Matches(patchCode, @"_subCaption\.text").Count == 0,
            "the screens never write the caption directly; only Apply does, and only once");
        // Apply is a thin marshal: the rules are the ledger's, executed above. What
        // must hold here is that every caption goes through the ONE static ledger
        // (a fresh ledger per call would forget what it replaced).
        string apply = patchCode.Substring(patchCode.IndexOf("internal static void Apply(", StringComparison.Ordinal));
        apply = apply.Substring(0, apply.IndexOf("private sealed class LocationPatch", StringComparison.Ordinal));
        Assert(Regex.Matches(patchCode, @"ArenaSessionModeCaptionLedger<").Count == 1
               && patchCode.Contains("private static readonly ArenaSessionModeCaptionLedger<TextMeshProUGUI> Ledger = new();", StringComparison.Ordinal)
               && apply.Contains("Ledger.Apply(caption, arena, label => label.text, (label, text) => label.text = text);", StringComparison.Ordinal)
               && apply.Contains("if (caption == null)", StringComparison.Ordinal),
            "every caption goes through the one static ledger, which restores only what it replaced");
        // Apply is the cosmetic call that the side screen's ShowPatch.Postfix and
        // SelectPatch.Postfix (ArenaSideSelectionPatch) make inside their own work:
        // the first sits in a catch that clears the Arena selection and hides the
        // ARENA toggle, the second has no try at all. So an exception must never
        // leave Apply. Pinned as: a try that holds the ledger call, closed by a plain
        // catch (Exception) (no when filter, no narrower type), and no throw
        // anywhere in the method.
        Match applyTry = Regex.Match(apply, @"\btry\s*\{");
        Match applyCatch = Regex.Match(apply, @"\bcatch\s*\(\s*Exception(\s+\w+)?\s*\)\s*\{");
        int applyLedger = apply.IndexOf("Ledger.Apply(", StringComparison.Ordinal);
        Assert(applyTry.Success && applyCatch.Success && applyLedger >= 0
               && applyTry.Index < applyLedger && applyLedger < applyCatch.Index,
            "Apply holds its ledger call in a try with a catch (Exception) and never throws");
        Assert(!Regex.IsMatch(apply, @"\bthrow\b"),
            "Apply never rethrows what its catch swallowed");
        string enable = patchCode.Substring(patchCode.IndexOf("public static void Enable()", StringComparison.Ordinal));
        enable = enable.Substring(0, enable.IndexOf("}", StringComparison.Ordinal));
        Assert(enable.Contains("new LocationPatch().Enable();", StringComparison.Ordinal)
               && enable.Contains("new AcceptPatch().Enable();", StringComparison.Ordinal)
               && enable.Contains("new DeployingPatch().Enable();", StringComparison.Ordinal),
            "all three caption patches are enabled");
        Assert(pluginCode.Contains("ArenaSessionModeCaptionPatch.Enable();", StringComparison.Ordinal),
            "Plugin enables the session caption patches");

        // The ARENA toggle on the side screen (the native third toggle): Show and
        // SetSelectedSide both sync the caption, after the PitFireTeam squad-screen
        // early return so a squad-management screen is never touched.
        string showPatch = Slice(sideCode, "private sealed class ShowPatch", "private sealed class ClosePatch");
        string selectPatch = Slice(sideCode, "private sealed class SelectPatch", "private static void SetDescription");
        const string sync = "ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);";
        Assert(showPatch.Contains(sync, StringComparison.Ordinal) && selectPatch.Contains(sync, StringComparison.Ordinal),
            "side screen caption follows the ARENA toggle state in Show and SetSelectedSide");
        // The sync is an UNGUARDED statement, pinned by its neighbour: a guard such
        // as if (ArenaMenuSelection.IsSelected) Apply(..., IsSelected) keeps every
        // substring above and silently drops the restore half (switching ARENA off
        // would leave "ARENA ZONE" on the ordinary side screen). In SetSelectedSide
        // it directly follows the description statement; in Show it directly
        // follows the closing brace of the if (IsSelected) { ... } block, i.e. it
        // runs at the level of the try body, not inside that block.
        Assert(selectPatch.Contains("if (ArenaMenuSelection.IsSelected) SetDescription(__instance); " + sync, StringComparison.Ordinal)
               && showPatch.Contains("SetDescription(__instance); } " + sync, StringComparison.Ordinal),
            "the side screen caption sync is an unguarded statement, so switching ARENA off restores the native text");
        Assert(selectPatch.IndexOf("[PatchPostfix]", StringComparison.Ordinal) is var postfix && postfix >= 0
               && selectPatch.IndexOf("IsSquadManagementScreen", postfix, StringComparison.Ordinal) is var guard && guard > postfix
               && selectPatch.IndexOf(sync, StringComparison.Ordinal) > guard,
            "the SetSelectedSide caption sync sits behind the squad-management early return");
        Assert(showPatch.IndexOf("IsSquadManagementScreen", showPatch.IndexOf("[PatchPostfix]", StringComparison.Ordinal), StringComparison.Ordinal)
               < showPatch.IndexOf(sync, StringComparison.Ordinal),
            "the Show caption sync sits behind the squad-management early return");
    }

    /// <summary>
    /// True when the patch body holds exactly the statement
    /// <c>if (condition) Apply(__instance._subCaption, true);</c>, comments removed
    /// and whitespace collapsed. The condition is compared as ONE string, so a
    /// negated group, a swapped operator or an extra clause fails the pin.
    /// </summary>
    private static bool HasGuardedApply(string body, string condition)
        => NormalizeCode(body).Contains("if (" + condition + ") Apply(__instance._subCaption, true);", StringComparison.Ordinal);

    private static string NormalizeCode(string source)
    {
        string code = Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(code, @"\s+", " ").Trim();
    }

    /// <summary>A stand-in for the TMP label: text plus how often it was written.</summary>
    private sealed class FakeCaptionLabel
    {
        internal string Text;
        internal int Writes;
        internal FakeCaptionLabel(string text) { Text = text; }
    }

    private static void LedgerApply(ArenaSessionModeCaptionLedger<FakeCaptionLabel> ledger, FakeCaptionLabel label, bool arena)
        => ledger.Apply(label, arena, current => current.Text, (target, text) => { target.Text = text; target.Writes++; });

    /// <summary>Applies and reports whether it completed without throwing.</summary>
    private static bool LedgerApplyCompletes(ArenaSessionModeCaptionLedger<FakeCaptionLabel> ledger, FakeCaptionLabel label, bool arena)
    {
        try { LedgerApply(ledger, label, arena); return true; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Executes the set/restore state machine behind the side-screen caption with
    /// a stand-in label (the production ledger is compiled into this harness).
    /// The order is deliberate: each Verify-SessionModeCaption.ps1 mutant of the
    /// ledger trips exactly the assertion it names, and the first failure aborts.
    /// </summary>
    private static void CheckCaptionLedger()
    {
        // Ordinary flows ask for "not Arena" on every side selection and must find
        // the native label untouched, whatever it says.
        foreach (string native in new[] { "PVE ZONE", "PVP ZONE", "", null, "ZONA PVE" })
        {
            var ordinary = new ArenaSessionModeCaptionLedger<FakeCaptionLabel>();
            var untouched = new FakeCaptionLabel(native);
            LedgerApply(ordinary, untouched, false);
            LedgerApply(ordinary, untouched, false);
            Assert(untouched.Text == native && untouched.Writes == 0,
                "an ordinary flow's caption is never touched: " + (native ?? "<null>"));
        }

        // Arena on writes ARENA ZONE once; asking again changes nothing.
        var ledger = new ArenaSessionModeCaptionLedger<FakeCaptionLabel>();
        var label = new FakeCaptionLabel("PVE ZONE");
        LedgerApply(ledger, label, true);
        Assert(label.Text == "ARENA ZONE" && label.Writes == 1, "Arena writes ARENA ZONE over the native text");
        LedgerApply(ledger, label, true);
        Assert(label.Text == "ARENA ZONE" && label.Writes == 1,
            "a second Arena apply writes nothing and keeps the remembered native text");

        // Off puts back exactly what EFT had written there, in any language, and
        // only once.
        LedgerApply(ledger, label, false);
        Assert(label.Text == "PVE ZONE" && label.Writes == 2,
            "switching Arena off restores exactly the native text EFT wrote");
        LedgerApply(ledger, label, false);
        Assert(label.Text == "PVE ZONE" && label.Writes == 2, "a restored caption is not restored again");
        foreach (string native in new[] { "ZONA PVE", "", null, "PVE ZONE (live)" })
        {
            var localized = new FakeCaptionLabel(native);
            LedgerApply(ledger, localized, true);
            LedgerApply(ledger, localized, false);
            Assert(localized.Text == native && localized.Writes == 2,
                "off restores the exact native text: " + (native ?? "<null>"));
        }

        // A native Show that rewrote the label while Arena was on is never clobbered
        // by the later off, and the ledger has forgotten the stale text by then.
        var rewritten = new FakeCaptionLabel("PVE ZONE");
        LedgerApply(ledger, rewritten, true);
        rewritten.Text = "PVE ZONE (fresh)";
        int writes = rewritten.Writes;
        LedgerApply(ledger, rewritten, false);
        Assert(rewritten.Text == "PVE ZONE (fresh)" && rewritten.Writes == writes,
            "a fresh native rewrite is never clobbered by the restore");
        rewritten.Text = "ARENA ZONE";
        LedgerApply(ledger, rewritten, false);
        Assert(rewritten.Text == "ARENA ZONE" && rewritten.Writes == writes,
            "a restore forgets the remembered text, so it cannot be applied twice");

        // Arming again after a native rewrite remembers the NEW native text.
        var rearmed = new FakeCaptionLabel("PVE ZONE");
        LedgerApply(ledger, rearmed, true);
        rearmed.Text = "PVE ZONE v2";
        Assert(LedgerApplyCompletes(ledger, rearmed, true) && rearmed.Text == "ARENA ZONE",
            "arming again after a native rewrite completes and shows ARENA ZONE");
        LedgerApply(ledger, rearmed, false);
        Assert(rearmed.Text == "PVE ZONE v2", "arming again after a native rewrite remembers the new native text");

        // Each label has its own memory; null labels are ignored.
        var left = new FakeCaptionLabel("PVE LEFT");
        var right = new FakeCaptionLabel("PVE RIGHT");
        LedgerApply(ledger, left, true);
        LedgerApply(ledger, right, true);
        LedgerApply(ledger, left, false);
        Assert(left.Text == "PVE LEFT" && right.Text == "ARENA ZONE",
            "a restore touches only its own label");
        LedgerApply(ledger, right, false);
        Assert(right.Text == "PVE RIGHT", "each label restores its own native text");
        Assert(LedgerApplyCompletes(ledger, null, true) && LedgerApplyCompletes(ledger, null, false),
            "a missing label is ignored without throwing");
    }

    private static string ClassBody(string source, string className)
    {
        int start = source.IndexOf("private sealed class " + className, StringComparison.Ordinal);
        Assert(start >= 0, "caption patch class exists: " + className);
        int next = source.IndexOf("private sealed class ", start + 10, StringComparison.Ordinal);
        return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
    }

    private static string Slice(string source, string from, string to)
    {
        int start = source.IndexOf(from, StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf(to, start, StringComparison.Ordinal);
        Assert(start >= 0 && end > start, "source section exists: " + from);
        return source.Substring(start, end - start);
    }

    private static int CountOfText(string source, string fragment)
    {
        int count = 0;
        for (int index = source.IndexOf(fragment, StringComparison.Ordinal); index >= 0;
             index = source.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string ReadCaptionSource(string overrideVariable, params string[] relativePath)
    {
        string overridePath = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.ReadAllText(overridePath);
        return File.ReadAllText(Path.Combine(new[] { CaptionRepositoryRoot() }.Concat(relativePath).ToArray()));
    }

    private static string CaptionRepositoryRoot()
    {
        string overrideRoot = Environment.GetEnvironmentVariable("ARENA_CAPTION_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot) && File.Exists(Path.Combine(overrideRoot, "ArenaMode.sln")))
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
