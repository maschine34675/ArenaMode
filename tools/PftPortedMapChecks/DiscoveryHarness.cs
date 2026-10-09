using ArenaMode.Areas;
using ArenaMode.Compat;
using ArenaMode.Modes;
using EFT;

static class DiscoveryHarness
{
    static int count;
    static void Check(bool value,string label) {count++;if(!value)throw new InvalidOperationException(label);}
    static void Initial()
    {
        ArenaAreaRuntime.CurrentArea=new();ArenaAreaRuntime.IsReady=false;
        ArenaMatchService.Current=null;PitFireTeamCompat._currentLocalPlayer=null;
        PitFireTeamCompat._currentLocalProfileId=null;PitFireTeamCompat.CurrentBodiesByAccountId.Clear();
    }
    internal static int Run()
    {
        Initial();Check(PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"initial bounded Tick waits before mode binds its leader");
        ArenaAreaRuntime.IsReady=true;
        Check(PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"ready area still waits until deferred match is actually created");
        ArenaMatchService.Current=new();
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"created match must prove its own leader");
        Initial();ArenaAreaRuntime.CurrentArea=null;
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"full-map startup keeps existing strict discovery");
        Initial();PitFireTeamCompat._currentLocalPlayer=new Player();
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"any prior leader reference retains validation");
        PitFireTeamCompat._currentLocalPlayer.Destroyed=true;
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"destroyed Unity leader is not an unbound initial leader");
        Initial();PitFireTeamCompat._currentLocalProfileId="old-leader";
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"missing formerly bound leader cannot hide behind deferred startup");
        Initial();PitFireTeamCompat.CurrentBodiesByAccountId.Add("selected",new BotOwner());
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"bound follower history retains validation after leader loss");
        Initial();ArenaAreaRuntime.IsReady=true;ArenaMatchService.Current=new();
        PitFireTeamCompat._currentLocalPlayer=new Player();PitFireTeamCompat._currentLocalProfileId="leader";
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"live match inspects physical identities");
        ArenaMatchService.Current=null;PitFireTeamCompat._currentLocalPlayer=null;
        Check(!PitFireTeamCompat.ShouldDeferInitialRosterDiscovery(),"lost live match and leader still fail closed through bind history");

        var repo=Directory.GetCurrentDirectory();while(!File.Exists(Path.Combine(repo,"AGENTS.md")))repo=Directory.GetParent(repo)?.FullName??throw new Exception("repo");
        var source=File.ReadAllText(Path.Combine(repo,"ArenaMode.Client/Compat/PitFireTeamCompat.cs"));
        int begin=source.IndexOf("public static bool TryGetLiveFollowers(",StringComparison.Ordinal);
        int end=source.IndexOf("public static IReadOnlyList<BotOwner> GetLiveFollowers()",begin,StringComparison.Ordinal);
        var discovery=source[begin..end];
        int deferred=discovery.IndexOf("if (ShouldDeferInitialRosterDiscovery())",StringComparison.Ordinal);
        Check(deferred>discovery.IndexOf("if (!string.IsNullOrEmpty(problem))",StringComparison.Ordinal)
            && deferred>discovery.IndexOf("if (!LiveTeamArena ||",StringComparison.Ordinal)
            && deferred<discovery.IndexOf("the exact local PMC body was not available",StringComparison.Ordinal),
            "central discovery defers initial bind after existing error and contract gates, before physical latch");
        Initial();ArenaAreaRuntime.CurrentArea=null;
        return count;
    }
}
