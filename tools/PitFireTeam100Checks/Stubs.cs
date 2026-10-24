using SPTarkov.Server.Core.Models.Common;
using ArenaMode.Server.Services;

namespace ArenaMode.Server
{
    internal static class ArenaContext
    {
        internal static TestSessions Sessions { get; } = new();
        internal static TestLog Logger { get; } = new();
    }
    internal sealed class TestLog
    {
        public void Debug(string text) { }
        public void Warning(string text) => Console.WriteLine(text);
    }
    internal sealed class TestSessions
    {
        public PitFireTeamPersistenceGuard Protection { get; } = new();
        public ArenaSessionState State { get; set; }
        public bool IsExternalProfileWriteProtected(MongoId id) => Protection.IsProtected(id.ToString());
        public bool AnyExternalProfileWriteProtection() => Protection.AnyProtected();
        public bool IsSoleExternalProfileProtection(MongoId id) => Protection.IsSoleProtectedSession(id.ToString());
        public bool IsGlobalExternalMutationAuthorized() => Protection.IsEveryProtectedSessionAuthorized(PitFireTeamManagementScope.Authorizes);
        public TestStatus GetStatus(MongoId id) => new(State);
    }
    internal record TestStatus(ArenaSessionState state);
}
namespace ArenaMode.Server.Services
{
    internal enum ArenaSessionState { Idle, Armed, InRaid }
}
namespace ArenaMode.Server.Patches
{
    internal static class PitFireTeamFileMutationPatch
    {
        // Legacy file guards have their own production unit tests. This harness
        // executes the new database and service graph without a live SPT host.
        internal static bool ShouldRunOriginal(object fileUtil, string path, string operation) => true;
    }
}
