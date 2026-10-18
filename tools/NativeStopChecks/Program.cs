using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var repo = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
string Read(string path) => File.ReadAllText(Path.Combine(repo, path));
string Methods(string path, string owner, params string[] names)
{
    var type = CSharpSyntaxTree.ParseText(Read(path)).GetRoot().DescendantNodes()
        .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == owner);
    return string.Join("\n", names.Select(name => type.Members.OfType<MethodDeclarationSyntax>()
        .Single(m => m.Identifier.ValueText == name).ToFullString()));
}
var roster = Methods("ArenaMode.Client/Compat/PitFireTeamCompat.cs", "PitFireTeamCompat",
    "TryGetLiveFollowers", "GetLiveFollowers", "LatchRuntimeRosterProblem", "Tick");
var match = Methods("ArenaMode.Client/Modes/ArenaMatchService.cs", "ArenaMatchService",
    "Tick", "SynchronizeScoreboard", "SnapshotActor", "NoteScoreboardProblem");
var drain = Methods("ArenaMode.Client/Compat/PitFireTeamRoundLifecycle.cs", "PitFireTeamRoundLifecycle",
    "TickPendingRawPlayerCompletions");
var exit = Read("ArenaMode.Client/Session/ArenaRaidExit.cs");
var doubles = Read("tools/NativeStopChecks/BoundaryDoubles.cs.txt");
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
    .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray();
const string imports = "using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Threading; using EFT; using Comfort.Common; using UnityEngine; using ArenaMode.Session; ";

int Run(string rosterSource, string matchSource)
{
    var sources = new[] { exit, doubles,
        imports + "namespace ArenaMode.Compat { internal static partial class PitFireTeamCompat { " + rosterSource + " } }",
        imports + "namespace ArenaMode.Compat { internal static partial class PitFireTeamRoundLifecycle { " + drain + " } }",
        imports + "namespace ArenaMode.Modes { internal static partial class ArenaMatchService { " + matchSource + " } }" };
    var assembly = CSharpCompilation.Create("NativeStopProbe_" + Guid.NewGuid().ToString("N"),
        sources.Select(s => CSharpSyntaxTree.ParseText(s)), references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using var binary = new MemoryStream();
    var result = assembly.Emit(binary);
    if (!result.Success) throw new Exception("Compilation failed: " + string.Join("\n", result.Diagnostics));
    return (int)Assembly.Load(binary.ToArray()).GetType("NativeStopProbe").GetMethod("Run").Invoke(null, null);
}
string ReplaceOnce(string source, string from, string to)
{
    source = source.Replace("\r\n", "\n");
    if (!source.Contains(from) || source.IndexOf(from, StringComparison.Ordinal) != source.LastIndexOf(from, StringComparison.Ordinal))
        throw new Exception("Missing/ambiguous defect anchor: " + from);
    return source.Replace(from, to);
}
var count = Run(roster, match);
Console.WriteLine($"NATIVE_STOP_CHECKS_PASSED: {count} assertions");
var bad = new[] {
    ("RosterStillPollsDisposal", ReplaceOnce(roster, "if (ArenaRaidExit.NativeStopOwnsCurrentRaid)\n            return true;", "if (false) return true;"), match,
        "stopped disposal must not create a roster fault"),
    ("MatchStillTicksDisposal", roster, ReplaceOnce(match, "if (match == null || ArenaRaidExit.NativeStopOwnsCurrentRaid)", "if (match == null)"),
        "stopped match must not advance or sync"),
    ("SameFrameScoreboardPoll", roster, ReplaceOnce(match, "if (ArenaRaidExit.NativeStopOwnsCurrentRaid || ArenaMatchResult.HasResult(match?.Mode)) return;", "if (ArenaMatchResult.HasResult(match?.Mode)) return;"),
        "same-frame committed Stop must skip scoreboard"),
    ("DropLatchedFault", ReplaceOnce(roster, "if (!string.IsNullOrEmpty(problem))\n            return false;", "if (!string.IsNullOrEmpty(problem) && !ArenaRaidExit.NativeStopOwnsCurrentRaid) return false;"), match,
        "native Stop must preserve an already latched fault"),
    ("SkipPendingDrain", ReplaceOnce(roster, "PitFireTeamRoundLifecycle.TickPendingRawPlayerCompletions();", "if (!ArenaRaidExit.NativeStopOwnsCurrentRaid) PitFireTeamRoundLifecycle.TickPendingRawPlayerCompletions();"), match,
        "pending completion must drain during committed Stop")
};
foreach (var (name, rosterSource, matchSource, expected) in bad)
{
    try { Run(rosterSource, matchSource); throw new Exception("Defect passed: " + name); }
    catch (TargetInvocationException ex) when (ex.InnerException?.Message == expected)
    { Console.WriteLine("Rejected compiled defect: " + name); }
}
Console.WriteLine("NATIVE_STOP_REGRESSION_VERIFIED");
