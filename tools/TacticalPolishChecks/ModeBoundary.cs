// Only absent mode services and observable external effects are doubled. The
// methods under test are copied byte-for-byte from the production class by Verify.ps1.
using ArenaMode.Kits;
using ArenaMode.Combat;
using EFT;
namespace ArenaMode.Modes;
public enum ArenaAnnouncerCue { RoundStart, RoundWon, RoundLost, MatchWon, MatchLost, Draw, Clutch, NoContest }
public enum TeamArenaRoundContinuation { RebindNextRound }
public partial class ArenaMatch
{
    // Assist reset behavior is exercised against the full scoreboard in AssistChecks.
    public AssistScoreboardBoundary Scoreboard { get; } = new();
    private readonly ArenaCombatAwards _awards=new();
    private int _multiKillChain;
    private float _lastLocalKillAt;
    // The extracted ReportAnnouncerCue owns this once-per-match terminal latch.
    private bool _noContestAnnounced;
    internal event Action<ArenaAnnouncerCue> OnAnnouncerCue;
    public List<ArenaAnnouncerCue> Cues=[];
    public TacticalMode Mode;
    public ArenaMatch(){OnAnnouncerCue+=Cues.Add;}
    public string KillForTest(){_multiKillChain++;_lastLocalKillAt=UnityEngine.Time.time;return _awards.ReportKill(new ArenaKill{AwardEligible=true,KilledByLocal=true,VictimBodyId=1},null);}
    public int Chain=>_multiKillChain;
}
public sealed class AssistScoreboardBoundary { public void ResetAssists() {} }
public static class ArenaMatchService { public static ArenaMatch Current; }
public partial class TacticalMode
{
    public enum Phase { Staging, Buy, Live, Aftermath, Rebinding, Ended }
    private ArenaMatch _match = new();
    private bool _teamArena,_playerDown,_roundStartPending,_soloCombatReady;
    private bool _rewardTimerExpired;
    private void BeginRewardCombat() { _rewardTimerExpired = false; }
    private KitDefinition _bought,_worn,_starter;
    private Phase _phase=Phase.Buy;
    private float _phaseEndsAt,_squadDeadlineAt;
    private int _squadRequested,_squadEpoch;
    private const float TeamOpponentActivationSeconds=5f;
    private readonly List<BotOwner> _squad=[];
    private TeamArenaRoundContinuation _teamContinuation;
    private SoloTacticalRoundStartPolicy.State _roundStartHold;
    public int RoundNumber {get;private set;}=1;
    public int Money {get;private set;}=1000;
    public int RoundsWon=>Wins; public int RoundsLost=>Losses;
    public int Wins,Losses;
    public Phase CurrentPhase=>_phase;
    public bool IsTeamArena=>_teamArena;
    public int SquadAlive=>_squad.Count(x=>x?.HealthController?.IsAlive==true);
    public bool WaitingForKitSwap=>false;
    public float PhaseRemaining=>Math.Max(0,_phaseEndsAt-UnityEngine.Time.time);
    public KitDefinition Bought=>_bought;
    public int PriceOf(KitDefinition kit)=>kit?.Price??0;
    public bool CanAfford(KitDefinition kit)=>PriceOf(kit)<=Money;
    private bool HoldRoundStartForKitSwap()=>false;
    private void TickTeamStaging(){} private void TickTeamLive(){} private void TickTeamRebinding(){}
    private void EnterTeamRebinding(){} private void EndTeamRaid(){}
    private void EndRound(bool won) {if(won) Wins++; else Losses++; _phase=Phase.Aftermath; _squadEpoch++;}
    public void Prepare(bool down=false, bool team=false)
    {
        _starter=SelectOpeningKit(); _playerDown=down; _teamArena=team;
        _phase=Phase.Buy;_roundStartPending=true;_match.Mode=this;ArenaMatchService.Current=_match;
        KitCatalog.KitProvider=IssueSoloKit;
    }
    public void Start()=>StartRound();
    public IReadOnlyList<ArenaAnnouncerCue> Cues=>_match.Cues;
    public string KillForTest()=>_match.KillForTest();
    public int Chain=>_match.Chain;
    public void EndForTest(){_phase=Phase.Ended;_squadEpoch++;}
    public void SurvivedForTest(){_phase=Phase.Buy;_roundStartPending=true;RoundNumber++;_bought=null;}
    public KitDefinition Issue()=>IssueSoloKit();
    public void LoseStarterForTest(){_bought=_worn=_starter=null;}
}
