#nullable disable
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        public static bool IsActive { get; set; }
        public static bool ArmedForNextRaid { get; set; }
    }
    internal static class ArenaServerClient
    {
        public static bool HasPendingArm { get; set; }
        public static bool HasPendingEnd { get; set; }
    }
}
namespace ArenaMode.Compat
{
    internal static class PitFireTeamCompat
    {
        public static bool PersistenceSuppressionActive { get; set; }
    }
}
namespace EFT
{
    internal class Player { }
    internal class Profile { }
    internal class BotOwner { }
    internal interface IEftSession { }
}
namespace pitTeam.Shared { internal class PmcKarmaReport { } }
namespace pitTeam.Modules
{
    internal static class PmcKarmaRuntime
    {
        public static int Sent;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void BeginRaid(bool transit = false) { }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Send(pitTeam.Shared.PmcKarmaReport report, EFT.Player player) => Sent++;
        internal static void Capture() => Send(new(), new());
    }
    internal static class FriendlyEncounterPenaltyRuntime
    {
        public static int Sent;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Task SendKillReport(string route, string json, string victimId, long killedAtUnixMs, string raidId)
        { Sent++; return Task.CompletedTask; }
    }
    internal static class GiveGearInsurance
    {
        public static int LocalMutations, Tracked;
        public static FollowerInsuranceRaidReports.Report LastReport;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Cancel(EFT.IEftSession session, EFT.Profile profile, IEnumerable<string> retainedIds)
        { LocalMutations++; LastReport = FollowerInsuranceRaidReports.Prepare("insurance transaction"); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Track(EFT.IEftSession session, EFT.Player owner, EFT.BotOwner recipient, IEnumerable<string> retainedIds) => Tracked++;
    }
    internal static class FollowerInsuranceRaidReports
    {
        public static int Sent, Completed;
        internal sealed class Report
        {
            internal readonly TaskCompletionSource<bool> Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal string Send(string route) { Sent++; Finished.TrySetResult(true); return "ordinary"; }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Report Prepare(string json) => new();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Complete() => Completed++;
    }
}
