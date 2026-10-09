namespace Redeploy.Core.Engine
{
    internal static class RespawnEngineState { internal static bool IsPipelineRunning; }
}
namespace ArenaMode.Spectator
{
    internal static class SpectatorHealthEffects
    { internal static void ClearForDeathReplay(EFT.LocalGame game, EFT.LocalPlayer body) { } }
}
namespace Redeploy.Core.GameAccess
{
    internal static class LocalGameAccessor
    { internal static EFT.PlayerOwner GetPlayerOwner(EFT.LocalGame game) => game.Owner; }
}
namespace ArenaMode.Combat
{
    internal static class RoleClassifier
    {
        internal static string Label(EFT.Player player) => "PMC";
        internal static string DisplayName(EFT.Player player) => player?.Profile?.Nickname;
    }
}
namespace ArenaMode.Modes
{
    public class RoundsMode : IArenaMode
    {
        public bool HasResult, ResultOwnsCurrentRaid;
        public string ResultHeading, ResultDetail, ResultExitStatus;
        public void RequestResultExit() { }
        public void ReportResultExitProblem(string value) { }
        public bool TryBeginScoredEvent() => true;
        public bool TryRecordLocalDeath() => true;
        public string Id => "rounds";
        public string DisplayName => "Rounds";
        public bool IsComplete => false;
        public string HudStatusLine => "";
        public float DeathReplaySeconds => 0;
        public bool HoldRespawn => false;
        public bool ControlsOwnSpawns => false;
        public void OnMatchStart(ArenaMatch match) { }
        public void Tick(float deltaTime) { }
        public void OnKill(Combat.ArenaKill kill) { }
        public void OnLocalDeath() { }
        public void OnLocalRespawn(EFT.Player player) { }
        public void OnMatchEnd() { }
    }
    public class TeamDeathmatchMode : RoundsMode { public void ClassifyKill(Combat.ArenaKill kill) { } }
    public class TacticalMode : RoundsMode { public bool IsTeamArena => false; }
}
