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

        // Gates of the Harmony patches, by source text.
        string patch = ReadCaptionSource("ARENA_CAPTION_PATCH_SOURCE", "ArenaMode.Client", "Session", "ArenaSessionModeCaptionPatch.cs");
        string side = ReadCaptionSource("ARENA_CAPTION_SIDE_SOURCE", "ArenaMode.Client", "Session", "ArenaSideSelectionPatch.cs");
        string plugin = ReadCaptionSource("ARENA_CAPTION_PLUGIN_SOURCE", "ArenaMode.Client", "Plugin.cs");

        string location = ClassBody(patch, "LocationPatch");
        Assert(location.Contains("nameof(MatchMakerSelectionLocationScreen.ShowInternal)", StringComparison.Ordinal)
               && location.Contains("[PatchPostfix]", StringComparison.Ordinal),
            "the location caption is written after ShowInternal, which finishes every Show");
        Assert(Gated(location, "if (ArenaMenuSelection.IsSelectedFor(raidSettings))", "Apply(__instance._subCaption, true)"),
            "location caption is gated on the exact Arena RaidSettings");

        string accept = ClassBody(patch, "AcceptPatch");
        Assert(accept.Contains("typeof(MatchMakerAcceptScreen.MatchmakerAcceptScreenController)", StringComparison.Ordinal)
               && accept.Contains("[PatchPostfix]", StringComparison.Ordinal),
            "the accept caption is written after Show(MatchmakerAcceptScreenController)");
        Assert(Gated(accept, "if (controller != null && ArenaMenuSelection.IsSelectedFor(controller.RaidSettings))", "Apply(__instance._subCaption, true)"),
            "accept caption is gated on the exact Arena RaidSettings");

        string deploying = ClassBody(patch, "DeployingPatch");
        Assert(deploying.Contains("typeof(MatchmakerTimeHasCome.TimeHasComeScreenController)", StringComparison.Ordinal)
               && deploying.Contains("[PatchPostfix]", StringComparison.Ordinal),
            "the deploying caption is written after Show(TimeHasComeScreenController)");
        Assert(deploying.Contains("!controller.IsReconnecting", StringComparison.Ordinal),
            "deploying caption excludes a reconnect to an ordinary raid");
        Assert(Gated(deploying, "(ArenaMenuSelection.IsSelectedFor(controller.RaidSettings) || ArenaSession.ArmedForNextRaid)", "Apply(__instance._subCaption, true)"),
            "deploying caption is gated on the Arena flow");

        foreach (string body in new[] { location, accept, deploying })
            Assert(body.Contains("catch (Exception", StringComparison.Ordinal),
                "a caption patch fails open to the native text instead of throwing");
        string patchCode = string.Join("\n", patch.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert(CountOfText(patch, "Apply(__instance._subCaption, true)") == 3
               && Regex.Matches(patchCode, @"\.text\s*=(?!=)").Count == 2
               && Regex.Matches(patchCode, @"_subCaption\.text").Count == 0,
            "the screens never write the caption directly; only Apply does, and only twice");
        string apply = patch.Substring(patch.IndexOf("internal static void Apply(", StringComparison.Ordinal));
        apply = apply.Substring(0, apply.IndexOf("private sealed class LocationPatch", StringComparison.Ordinal));
        Assert(apply.Contains("NativeText.TryGetValue(caption, out string native)", StringComparison.Ordinal)
               && apply.Contains("caption.text == ArenaSessionModeCaption.ArenaText", StringComparison.Ordinal)
               && apply.Contains("ArenaSessionModeCaption.Resolve(false, native)", StringComparison.Ordinal),
            "only a caption this patch replaced is ever restored");
        string enable = patch.Substring(patch.IndexOf("public static void Enable()", StringComparison.Ordinal));
        enable = enable.Substring(0, enable.IndexOf("}", StringComparison.Ordinal));
        Assert(enable.Contains("new LocationPatch().Enable();", StringComparison.Ordinal)
               && enable.Contains("new AcceptPatch().Enable();", StringComparison.Ordinal)
               && enable.Contains("new DeployingPatch().Enable();", StringComparison.Ordinal),
            "all three caption patches are enabled");
        Assert(plugin.Contains("ArenaSessionModeCaptionPatch.Enable();", StringComparison.Ordinal),
            "Plugin enables the session caption patches");

        // The ARENA toggle on the side screen (the native third toggle): Show and
        // SetSelectedSide both sync the caption, after the PitFireTeam squad-screen
        // early return so a squad-management screen is never touched.
        string showPatch = Slice(side, "private sealed class ShowPatch", "private sealed class ClosePatch");
        string selectPatch = Slice(side, "private sealed class SelectPatch", "private static void SetDescription");
        const string sync = "ArenaSessionModeCaptionPatch.Apply(__instance._subCaption, ArenaMenuSelection.IsSelected);";
        Assert(showPatch.Contains(sync, StringComparison.Ordinal) && selectPatch.Contains(sync, StringComparison.Ordinal),
            "side screen caption follows the ARENA toggle state in Show and SetSelectedSide");
        Assert(selectPatch.IndexOf("[PatchPostfix]", StringComparison.Ordinal) is var postfix && postfix >= 0
               && selectPatch.IndexOf("IsSquadManagementScreen", postfix, StringComparison.Ordinal) is var guard && guard > postfix
               && selectPatch.IndexOf(sync, StringComparison.Ordinal) > guard,
            "the SetSelectedSide caption sync sits behind the squad-management early return");
        Assert(showPatch.IndexOf("IsSquadManagementScreen", showPatch.IndexOf("[PatchPostfix]", StringComparison.Ordinal), StringComparison.Ordinal)
               < showPatch.IndexOf(sync, StringComparison.Ordinal),
            "the Show caption sync sits behind the squad-management early return");
    }

    private static bool Gated(string body, string gate, string apply)
    {
        int gateAt = body.IndexOf(gate, StringComparison.Ordinal);
        int applyAt = body.IndexOf(apply, StringComparison.Ordinal);
        return gateAt >= 0 && applyAt > gateAt;
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
