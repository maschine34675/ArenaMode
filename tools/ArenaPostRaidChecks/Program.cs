using ArenaMode.Session;
using EFT;
using EFT.UI.SessionEnd;
using EFT.UI.Screens;
using Mono.Cecil;
using Redeploy.Core.GameAccess;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception("ASSERTION_FAILED: " + message); checks++; }
LocalGame game = null;
GameWorld world = null;
RaidSettings settings = null;
SessionResultShowOperation Fresh()
{
    game = new(); world = new(); settings = new();
    ArenaSession.Game = game; ArenaSession.World = world;
    ArenaSession.IsActive = ArenaSession.LastRaidWasArena = true;
    ArenaSession.ArmedForNextRaid = LocalGameAccessor.Throw = false; ArenaSession.ArmEpoch++;
    ArenaPostRaidScreens.CaptureEndedRaid(game, world);
    ArenaSession.IsActive = false;
    ArenaPostRaidScreens.BeginResult(game.Settings, settings, game.ProfileId);
    var op = new SessionResultShowOperation { _raidSettings = settings };
    ArenaPostRaidScreens.Bind(op);
    return op;
}
IBaseScreenController<EEftScreenType>[] Sequence(SessionResultShowOperation op)
    => [op._killListScreenController, op._statisticsScreenController, op._experienceScreenController, op._healthTreatmentScreenController];
var operation = Fresh();
Check(ArenaPostRaidScreens.Owns(operation), "exact result bound");
var screen = new SessionResultExitStatus();
ArenaPostRaidScreens.HideWarning(screen, operation._exitStatusScreenController);
Check(!screen._warningPanel.active, "owned warning hidden");
Check(!ArenaPostRaidScreens.ProceedFromStatistics(operation) && operation.HealthCalls == 1, "XP uses native health continuation");
var original = Sequence(operation); var filtered = original;
ArenaPostRaidScreens.FilterHealthSequence(ref filtered);
Check(filtered.Length == 3 && filtered[0] == original[0] && filtered[1] == original[1] && filtered[2] == original[3], "native main-menu history omits XP only");
Check(original.Length == 4 && original[2] == operation._experienceScreenController && !ReferenceEquals(original, filtered), "caller array untouched");
ArenaPostRaidScreens.FilterHealthSequence(ref filtered);
Check(filtered.Length == 3, "filter idempotent");
ArenaPostRaidScreens.Bind(operation); Check(ArenaPostRaidScreens.Owns(operation), "same-operation ReInit retained");
screen._warningPanel.SetActive(true); // Native Show rebuilds the warning on Back/ReInit.
ArenaPostRaidScreens.HideWarning(screen, operation._exitStatusScreenController);
Check(!screen._warningPanel.active, "reshown result warning hidden again");
var foreign = new SessionResultShowOperation { _raidSettings = settings };
ArenaPostRaidScreens.Bind(foreign); Check(!ArenaPostRaidScreens.Owns(foreign) && ArenaPostRaidScreens.Owns(operation), "second operation cannot steal result");
screen = new(); ArenaPostRaidScreens.HideWarning(screen, foreign._exitStatusScreenController); Check(screen._warningPanel.active, "foreign warning unchanged");
Check(ArenaPostRaidScreens.ProceedFromStatistics(foreign) && foreign.HealthCalls == 0, "foreign XP unchanged");
ArenaPostRaidScreens.Complete(foreign); Check(ArenaPostRaidScreens.Owns(operation), "foreign completion cannot clear owner");
ArenaPostRaidScreens.Complete(operation); Check(!ArenaPostRaidScreens.Owns(operation), "completion clears owner");
ArenaPostRaidScreens.Bind(operation); Check(!ArenaPostRaidScreens.Owns(operation), "completed context cannot rebind");

Action[] exclusions = [() => ArenaSession.IsActive = true, () => ArenaSession.LastRaidWasArena = false,
    () => ArenaSession.ArmedForNextRaid = true, () => ArenaSession.ArmEpoch++, () => operation._raidSettings = new(),
    () => operation._matchProfileId = "other", () => operation._session.Profile.Id = "other"];
for (int i = 0; i < exclusions.Length; i++)
{
    operation = Fresh(); exclusions[i](); screen = new();
    ArenaPostRaidScreens.HideWarning(screen, operation._exitStatusScreenController);
    Check(screen._warningPanel.active, "invalid scope warning unchanged " + i);
    Check(ArenaPostRaidScreens.ProceedFromStatistics(operation) && operation.HealthCalls == 0, "invalid scope XP unchanged " + i);
    original = Sequence(operation); filtered = original; ArenaPostRaidScreens.FilterHealthSequence(ref filtered);
    Check(ReferenceEquals(original, filtered), "invalid scope history unchanged " + i);
}
for (int i = 0; i < 4; i++)
{
    operation = Fresh(); original = Sequence(operation); original[i] = new Controller(); filtered = original;
    ArenaPostRaidScreens.FilterHealthSequence(ref filtered); Check(ReferenceEquals(original, filtered), "foreign sequence retained " + i);
}
operation = Fresh(); original = [operation._experienceScreenController, operation._healthTreatmentScreenController]; filtered = original;
ArenaPostRaidScreens.FilterHealthSequence(ref filtered); Check(ReferenceEquals(original, filtered), "short foreign sequence retained");
foreach (int fault in Enumerable.Range(0, 6))
{
    operation = Fresh();
    switch (fault)
    {
        case 0: ArenaPostRaidScreens.BeginResult(new LocalRaidSettings(), settings, "pmc"); break;
        case 1: ArenaPostRaidScreens.BeginResult(game.Settings, settings, "wrong"); break;
        case 2: settings.Side = ESideType.Savage; ArenaPostRaidScreens.BeginResult(game.Settings, settings, "pmc"); break;
        case 3: settings.Local = false; ArenaPostRaidScreens.BeginResult(game.Settings, settings, "pmc"); break;
        case 4: ArenaSession.IsActive = true; ArenaPostRaidScreens.CaptureEndedRaid(new LocalGame(), world); ArenaSession.IsActive = false; break;
        case 5: ArenaSession.IsActive = true; LocalGameAccessor.Throw = true; ArenaPostRaidScreens.CaptureEndedRaid(game, world); ArenaSession.IsActive = false; break;
    }
    ArenaPostRaidScreens.Bind(operation); Check(!ArenaPostRaidScreens.Owns(operation), "wrong native raid capture rejected " + fault);
}

var repo = Path.GetFullPath(args[0]);
// The wiring order is significant: the exact pair is cleared immediately after.
var lifecycle = File.ReadAllText(Path.Combine(repo, "ArenaMode.Client/Session/ArenaSession.cs"));
var end = lifecycle[lifecycle.IndexOf("private static void OnRaidEnded()", StringComparison.Ordinal)..];
int captureIndex = end.IndexOf("ArenaPostRaidScreens.CaptureEndedRaid(_startedGame, _startedWorld)", StringComparison.Ordinal);
Check(captureIndex >= 0 && captureIndex < end.IndexOf("_startedWorld = null", StringComparison.Ordinal), "capture precedes native identity clear");
using var assembly = AssemblyDefinition.ReadAssembly(Path.GetFullPath(Path.Combine(repo, "../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")));
var types = assembly.MainModule.Types;
IEnumerable<MethodReference> Calls(MethodDefinition m) => m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>();
var nativeResult = types.Single(t => t.FullName == "EFT.SessionResultShowOperation");
var next = nativeResult.Methods.Single(m => m.Name == "ProceedFromStatistics");
Check(Calls(next).Any(m => m.Name == "get_ExperienceScreenController"), "native statistics path contains XP");
Check(Calls(nativeResult.Methods.Single(m => m.Name == "get_ExperienceScreenController")).Any(m => m.Name == "ShowHealthTreatmentScreen"), "native XP continuation is treatment");
var health = Calls(nativeResult.Methods.Single(m => m.Name == "ShowHealthTreatmentScreen")).ToArray();
Check(health.Any(m => m.Name == "GoToMainMenu") && health.Any(m => m.Name == "ShowScreen"), "native treatment preserves both branches");
var nativeScreen = types.Single(t => t.FullName == "EFT.UI.SessionEnd.SessionResultExitStatus");
Check(nativeScreen.Fields.Single(f => f.Name == "_warningPanel").FieldType.FullName == "UnityEngine.GameObject", "native warning target verified");
var wrapper = nativeScreen.Methods.Single(m => m.Name == "Show" && m.Parameters.Count == 1);
Check(Calls(wrapper).Any(m => m.Name == "Show" && m.Parameters.Count == 7), "native controller Show reaches warning writer");
var app = types.Single(t => t.FullName == "EFT.TarkovApplication");
Check(app.Fields.Single(f => f.Name == "_localRaidSettings").FieldType.FullName == "EFT.LocalRaidSettings", "native field injection contract");
var moveNext = app.NestedTypes.Single(t => t.Name.Contains("OnGameEnd") && t.Methods.Any(m => m.Name == "MoveNext")).Methods.Single(m => m.Name == "MoveNext");
var calls = Calls(moveNext).Select(m => m.Name).ToArray();
Check(Array.IndexOf(calls, "TryUnloadGame") < Array.IndexOf(calls, "ShowSessionResult"), "entry capture precedes unload and results");
var sequenceState = nativeResult.NestedTypes.Single(t => t.Name.Contains("TryGoToMainMenu") && t.Methods.Any(m => m.Name == "MoveNext"));
var sequenceCalls = Calls(sequenceState.Methods.Single(m => m.Name == "MoveNext")).Select(m => m.Name).ToArray();
Check(sequenceCalls.Contains("get_ExperienceScreenController") && sequenceCalls.Contains("ShowScreenSequence")
    && sequenceCalls.Contains("ShowMessageWindow") && sequenceCalls.Contains("GoToMainMenu"), "native main-menu history and treatment prompt verified");
Console.WriteLine($"ARENA_POSTRAID_PRODUCTION_CHECKS_PASSED: {checks} assertions/contracts");
