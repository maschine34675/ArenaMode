using ArenaMode.Session;
using ArenaMode.SessionHelper;
using ArenaMode.Shared;
using ArenaMode.Shared.Session;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckRewardPreparation()
    {
        ReadyToEnter();
        string sentId = null;
        int posts = 0;
        RequestHandler.Post = (route, body) =>
        {
            posts++;
            Require(route == RewardWire.PrepareControlRoute, "Prepare uses the authenticated reward-control route");
            Require(ArenaMiniJson.TryParse(body, out var raw, out _), "Prepare body parses");
            var request = ArenaMiniJson.AsObject(raw);
            // The SPT router uses System.Text.Json without case-insensitive options.
            // Decode the real submitted bytes with the server's shared DTO contract.
            sentId = System.Text.Json.JsonSerializer.Deserialize<ArenaMode.Shared.RewardControlPrepareRequest>(body).ControlId;
            Require(request.Count == 1 && Guid.TryParseExact(sentId, "N", out _), "Prepare sends only a random control ID, never a credential");
            return Task.FromResult("{\"Accepted\":true,\"Reason\":\"\"}");
        };
        Require(ArenaSessionSwitch.Request("enter", null, out _), "Accepted prepare starts the helper");
        Require(HelperRequest.TryParse(File.ReadAllText(_requestPath), "enter", out var prepared, out _)
            && prepared.RewardControlId == sentId && posts == 1, "Only the approved control identity travels to the helper");
        string firstId = sentId;
        ArenaSessionSwitch.ResetRequestLatch();
        Require(ArenaSessionSwitch.Request("enter", null, out _) && sentId != firstId && posts == 2, "Each attempt gets a fresh control identity");

        foreach (string response in new[] { "{\"Accepted\":false,\"Reason\":\"secret must not be logged\"}", "invalid json", "{\"accepted\":\"true\"}", new string('x', 4097) })
        {
            ReadyToEnter();
            RequestHandler.Post = (_, _) => Task.FromResult(response);
            Require(ArenaSessionSwitch.Request("enter", null, out _) && Launch.Calls == 1, "Refused or malformed prepare does not block Arena");
            Require(!File.ReadAllText(_requestPath).Contains("rewardControlId"), "Unavailable prepare omits control identity");
            Require(!ArenaMode.Plugin.ModLogger.Debug.Any(line => line.Contains("secret must not be logged")), "Prepare rejection details never enter logs");
        }
        ReadyToEnter();
        RequestHandler.Post = (_, _) => throw new InvalidOperationException("secret transport error");
        Require(ArenaSessionSwitch.Request("enter", null, out _) && Launch.Calls == 1, "Prepare exceptions leave the session usable");
        Require(!ArenaMode.Plugin.ModLogger.Debug.Any(line => line.Contains("secret transport error")), "Prepare exception text never enters logs");

        ReadyToEnter();
        var wait = new TaskCompletionSource<bool>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        Require(ArenaSessionSwitch.Request("enter", null, out _) && ArenaSessionSwitch.HelperPending && Launch.Calls == 0,
            "Incomplete prepare keeps the UI alive before helper launch");
        Require(!ArenaSessionSwitch.Request("enter", null, out _), "A pending prepare cannot start another switch");
        _clock += ArenaSessionSwitch.RewardPrepareTimeoutSeconds;
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 1 && !File.ReadAllText(_requestPath).Contains("rewardControlId"), "Prepare timeout starts a read-only session");
        string timedOutRequest = File.ReadAllText(_requestPath);
        wait.SetResult(true);
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 1 && File.ReadAllText(_requestPath) == timedOutRequest, "Late prepare cannot publish or relaunch a stale switch");

        ReadyToEnter();
        wait = new TaskCompletionSource<bool>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        Require(ArenaSessionSwitch.Request("enter", null, out _), "Async prepare starts");
        wait.SetResult(true);
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 1 && File.ReadAllText(_requestPath).Contains("rewardControlId"), "Timely prepare attaches its control identity");

        ReadyToEnter();
        wait = new TaskCompletionSource<bool>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        Require(ArenaSessionSwitch.Request("enter", null, out _), "Abandoned prepare starts");
        ArenaSessionSwitch.ResetRequestLatch();
        wait.SetResult(true);
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 0 && !File.Exists(_requestPath), "Cancelled prepare never writes a helper request");

        ReadyToEnter();
        wait = new TaskCompletionSource<bool>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        string failure = null;
        Require(ArenaSessionSwitch.Request("enter", null, out _, message => failure = message), "Async switch can report a later launch failure");
        Launch.Succeed = false;
        wait.SetResult(true);
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(failure != null && !ArenaSessionSwitch.HelperPending && UnityEngine.Application.QuitCalls == 0,
            "Failure after preparation keeps the game running and clears the pending state");

        ResetWorld(); ResetLaunch(); ArenaSession(ReportActive); Ping(true, NormalServerMods());
        ArenaSessionSwitch.CommandLineProvider = () => new[] { BepInEx.Paths.ExecutablePath, "-token=0123456789abcdef01234567", "-arena-session" };
        posts = 0;
        ArenaSessionSwitch.PrepareRewardControl = _ => { posts++; return Task.FromResult(true); };
        Require(ArenaSessionSwitch.Request("leave", null, out _) && posts == 0 && !File.ReadAllText(_requestPath).Contains("rewardControlId"),
            "Leaving never prepares or transfers control");
    }
}
