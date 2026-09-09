// Contract surfaces only, not SAIN decision logic. These methods return a
// controlled sentinel so tests can prove the prefix never replaces the result.
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using Reason = SAIN.SAINComponent.Classes.Search.SearchReasonsStruct.EWantToSearchReason;
namespace SAIN.Classes.Bot.Search
{
    public class SearchClass { public SearchDecider Decider { get; set; } }
    public class SearchDecider
    {
        public BotComponent Bot { get; set; }
        private bool shallBeginSearch(Enemy enemy, float timeBeforeSearch, out Reason reason)
        { reason = Reason.Sentinel; return false; }
        private bool ShallContinueSearch(Enemy enemy, float timeBeforeSearch, out Reason reason)
        { reason = Reason.Sentinel; return false; }
    }
}
namespace SAIN.SAINComponent.Classes.Search
{
    public struct SearchReasonsStruct { public enum EWantToSearchReason { Sentinel } }
}
