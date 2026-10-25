using ArenaMode.Config;
using ArenaMode.Effects;
using ArenaMode.Session;
using Comfort.Common;
using EFT;

int checks = 0;
Singleton<AbstractGame>.Instance = new AbstractGame();
Singleton<GameWorld>.Instance = new GameWorld();
ArenaSession.ExactGame = Singleton<AbstractGame>.Instance;
ArenaSession.ExactWorld = Singleton<GameWorld>.Instance;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
void Fallback(string context)
{
    Check(ArenaTeamPalette.Friendly == ArenaTracerColor.Green, context + ": custom friendly retained");
    Check(ArenaTeamPalette.Opponent == ArenaTracerColor.Orange, context + ": custom opponent retained");
    Check(ArenaTeamPalette.FriendlyUi.g > ArenaTeamPalette.FriendlyUi.r, context + ": friendly UI green");
    Check(ArenaTeamPalette.OpponentUi.r > ArenaTeamPalette.OpponentUi.g, context + ": opponent UI orange");
}

// Neither a pending choice nor stale choice may leak into ordinary raids or other modes.
foreach (bool? selected in new bool?[] { null, true, false })
{
    ArenaTeamSideSelection.SelectedUsec = selected;
    ArenaSession.IsActive = false;
    ArenaSession.ActiveMode = "teamdeathmatch";
    Fallback("inactive " + selected);
    foreach (string mode in new[] { "tactical", "zombie", "gungame", "deathmatch", "", null })
    {
        ArenaSession.IsActive = true;
        ArenaSession.ActiveMode = mode;
        Fallback("non-TDM " + mode + "/" + selected);
    }
}

ArenaSession.IsActive = true;
ArenaSession.ActiveMode = "teamdeathmatch";
ArenaTeamSideSelection.SelectedUsec = null;
Fallback("TDM without captured side");

foreach (bool usec in new[] { true, false, true })
{
    ArenaTeamSideSelection.SelectedUsec = usec;
    Check(ArenaTeamPalette.Friendly == (usec ? ArenaTracerColor.Blue : ArenaTracerColor.Red), "friendly HDR faction");
    Check(ArenaTeamPalette.Opponent == (usec ? ArenaTracerColor.Red : ArenaTracerColor.Blue), "opponent HDR faction");
    Check(ArenaTeamPalette.FriendlyHex == (usec ? "#7FA7FF" : "#FF6B6B"), "friendly UI faction");
    Check(ArenaTeamPalette.OpponentHex == (usec ? "#FF6B6B" : "#7FA7FF"), "opponent UI faction");
    Check(ArenaTeamPalette.FriendlyHex != ArenaTeamPalette.OpponentHex, "teams remain distinct");
    Check(Settings.FriendlyTracerColor.Value == ArenaTracerColor.Green
        && Settings.OpponentTracerColor.Value == ArenaTracerColor.Orange, "saved choices are not overwritten");
}
Check(ArenaTeamPalette.UsecRgb == 0x7FA7FF && ArenaTeamPalette.BearRgb == 0xFF6B6B,
    "monitor faction constants match displayed faction colors");
// A missed end callback may leave IsActive/choice set while a different world loads.
Singleton<GameWorld>.Instance = new GameWorld();
Fallback("different current world");
Singleton<GameWorld>.Instance = (GameWorld)ArenaSession.ExactWorld;
Singleton<AbstractGame>.Instance = new AbstractGame();
Fallback("different current game");
Singleton<AbstractGame>.Instance = (AbstractGame)ArenaSession.ExactGame;
Singleton<GameWorld>.Instance = null;
Fallback("missing current world");
Singleton<GameWorld>.Instance = (GameWorld)ArenaSession.ExactWorld;
Singleton<AbstractGame>.Instance = null;
Fallback("missing current game");
Singleton<AbstractGame>.Instance = (AbstractGame)ArenaSession.ExactGame;
ArenaTeamSideSelection.SelectedUsec = null;
Fallback("clear after BEAR or USEC match");
ArenaSession.IsActive = false;
ArenaSession.ActiveMode = null;
Settings.FriendlyTracerColor = null;
Settings.OpponentTracerColor = null;
Check(ArenaTeamPalette.Friendly == ArenaTracerColor.Cyan && ArenaTeamPalette.Opponent == ArenaTracerColor.Red,
    "uninitialized config has existing safe defaults");

Console.WriteLine($"TDMPALETTECHECKS_PASSED: {checks} assertions");
