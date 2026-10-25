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
            Require(route == RewardWire.PrepareControlRoute || route == RewardWire.PrepareEntryRoute,
                "Prepare uses separate authenticated consent and entry routes");
            Require(ArenaMiniJson.TryParse(body, out var raw, out _), "Prepare body parses");
            var request = ArenaMiniJson.AsObject(raw);
            // The SPT router uses System.Text.Json without case-insensitive options.
            // Decode the real submitted bytes with the server's shared DTO contract.
            sentId = route == RewardWire.PrepareControlRoute
                ? System.Text.Json.JsonSerializer.Deserialize<ArenaMode.Shared.RewardControlPrepareRequest>(body).ControlId
                : System.Text.Json.JsonSerializer.Deserialize<ArenaMode.Shared.RewardEntryPrepareRequest>(body).SessionId;
            Require(request.Count == 1 && Guid.TryParseExact(sentId, "N", out _), "Prepare sends only a random control ID, never a credential");
            return Task.FromResult("{\"Accepted\":true,\"Reason\":\"\"}");
        };
        Require(ArenaSessionSwitch.Request("enter", null, out _), "Accepted prepare starts the helper");
        Require(HelperRequest.TryParse(File.ReadAllText(_requestPath), "enter", out var prepared, out _)
            && prepared.RewardControlId == sentId && prepared.OfflineSessionId == sentId && posts == 2,
            "Only approved opaque identities travel to the helper with the required new schema");
        string firstId = sentId;
        ArenaSessionSwitch.ResetRequestLatch();
        Require(ArenaSessionSwitch.Request("enter", null, out _) && sentId != firstId && posts == 4, "Each attempt gets a fresh control identity");

        ReadyToEnter();
        RequestHandler.Post = (route, _) => Task.FromResult(route == RewardWire.PrepareEntryRoute
            ? "{\"Accepted\":true}" : "{\"Accepted\":false}");
        Require(ArenaSessionSwitch.Request("enter", null, out _)
            && HelperRequest.TryParse(File.ReadAllText(_requestPath), "enter", out var entryOnly, out _)
            && entryOnly.OfflineSessionId != null && entryOnly.RewardControlId == null,
            "Entry shutdown remains available when rewards/control are unavailable");

        ReadyToEnter();
        var delayedControl = new TaskCompletionSource<string>();
        RequestHandler.Post = (route, _) => route == RewardWire.PrepareControlRoute ? delayedControl.Task
            : Task.FromResult("{\"Accepted\":true}");
        Require(ArenaSessionSwitch.Request("enter", null, out _)
            && HelperRequest.TryParse(File.ReadAllText(_requestPath), "enter", out var acceptedEntry, out _)
            && acceptedEntry.Schema == ArenaSessionContract.OfflineRequestSchema && acceptedEntry.OfflineSessionId != null,
            "Delayed optional consent cannot hide an accepted offline entry or downgrade its schema");
        delayedControl.SetResult("{\"Accepted\":true}");

        ReadyToEnter();
        RequestHandler.Post = (route, _) => Task.FromResult(route == RewardWire.PrepareControlRoute
            ? "{\"Accepted\":true}" : "{\"Accepted\":false}");
        Require(ArenaSessionSwitch.Request("enter", null, out _)
            && HelperRequest.TryParse(File.ReadAllText(_requestPath), "enter", out var legacy, out _)
            && legacy.OfflineSessionId == null && legacy.RewardControlId != null,
            "Unsupported entry shutdown retains the existing live consent flow");

        foreach (string response in new[] { "{\"Accepted\":false,\"Reason\":\"secret must not be logged\"}", "invalid json", "{\"accepted\":\"true\"}", new string('x', 4097) })
        {
            ReadyToEnter();
            RequestHandler.Post = (_, _) => Task.FromResult(response);
            bool explicitlyUnsupported = response.Contains("\"Accepted\":false");
            Require(ArenaSessionSwitch.Request("enter", null, out _) == explicitlyUnsupported && Launch.Calls == (explicitlyUnsupported ? 1 : 0),
                "Only explicit unavailable entry can use legacy; malformed entry keeps the game open");
            Require(!File.Exists(_requestPath) || !File.ReadAllText(_requestPath).Contains("rewardControlId"), "Unavailable prepare omits control identity");
            Require(!ArenaMode.Plugin.ModLogger.Debug.Any(line => line.Contains("secret must not be logged")), "Prepare rejection details never enter logs");
        }
        ReadyToEnter();
        RequestHandler.Post = (_, _) => throw new InvalidOperationException("secret transport error");
        Require(!ArenaSessionSwitch.Request("enter", null, out _) && Launch.Calls == 0 && UnityEngine.Application.QuitCalls == 0,
            "An uncertain entry transport failure leaves the game open without a legacy downgrade");
        Require(!ArenaMode.Plugin.ModLogger.Debug.Any(line => line.Contains("secret transport error")), "Prepare exception text never enters logs");

        ReadyToEnter();
        var wait = new TaskCompletionSource<ArenaSessionPreparation>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        Require(ArenaSessionSwitch.Request("enter", null, out _) && ArenaSessionSwitch.HelperPending && Launch.Calls == 0,
            "Incomplete prepare keeps the UI alive before helper launch");
        Require(!ArenaSessionSwitch.Request("enter", null, out _), "A pending prepare cannot start another switch");
        _clock += ArenaSessionSwitch.RewardPrepareTimeoutSeconds;
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 0 && !File.Exists(_requestPath) && !ArenaSessionSwitch.HelperPending,
            "Unresolved entry preparation timeout leaves the game open without a legacy fallback");
        wait.SetResult(new ArenaSessionPreparation { RewardControl = true, OfflineEntry = true });
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 0 && !File.Exists(_requestPath), "Late prepare cannot publish or relaunch a stale switch");

        ReadyToEnter();
        wait = new TaskCompletionSource<ArenaSessionPreparation>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        Require(ArenaSessionSwitch.Request("enter", null, out _), "Async prepare starts");
        wait.SetResult(new ArenaSessionPreparation { RewardControl = true, OfflineEntry = true });
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 1 && File.ReadAllText(_requestPath).Contains("rewardControlId")
            && File.ReadAllText(_requestPath).Contains("offlineSessionId"), "Timely prepare attaches both identities");

        ReadyToEnter();
        wait = new TaskCompletionSource<ArenaSessionPreparation>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        Require(ArenaSessionSwitch.Request("enter", null, out _), "Abandoned prepare starts");
        ArenaSessionSwitch.ResetRequestLatch();
        wait.SetResult(new ArenaSessionPreparation { RewardControl = true, OfflineEntry = true });
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(Launch.Calls == 0 && !File.Exists(_requestPath), "Cancelled prepare never writes a helper request");

        ReadyToEnter();
        wait = new TaskCompletionSource<ArenaSessionPreparation>();
        ArenaSessionSwitch.PrepareRewardControl = _ => wait.Task;
        string failure = null;
        Require(ArenaSessionSwitch.Request("enter", null, out _, message => failure = message), "Async switch can report a later launch failure");
        Launch.Succeed = false;
        wait.SetResult(new ArenaSessionPreparation { RewardControl = true, OfflineEntry = true });
        ArenaMode.Plugin.Instance.Coroutines[0].MoveNext();
        Require(failure != null && !ArenaSessionSwitch.HelperPending && UnityEngine.Application.QuitCalls == 0,
            "Failure after preparation keeps the game running and clears the pending state");

        ResetWorld(); ResetLaunch(); ArenaSession(ReportActive); Ping(true, NormalServerMods());
        ArenaSessionSwitch.CommandLineProvider = () => new[] { BepInEx.Paths.ExecutablePath, "-token=0123456789abcdef01234567", "-arena-session" };
        posts = 0;
        ArenaSessionSwitch.PrepareRewardControl = _ => { posts++; return Task.FromResult(new ArenaSessionPreparation()); };
        Require(ArenaSessionSwitch.Request("leave", null, out _) && posts == 0 && !File.ReadAllText(_requestPath).Contains("rewardControlId"),
            "Leaving never prepares or transfers control");

        string offlineId = Guid.NewGuid().ToString("N");
        string markerPath = Path.Combine(_root, "leave-marker.json");
        string source = Path.Combine(_gameDir, "SPT_Runtime");
        var marker = new Dictionary<string, object> { ["offlineSessionId"] = offlineId,
            ["profileId"] = "0123456789abcdef01234567", ["sourceRuntime"] = source };
        File.WriteAllText(markerPath, ArenaMiniJson.Write(marker));
        Require(ArenaSessionSwitch.ReadOfflineSessionIdFromMarker(markerPath, "0123456789abcdef01234567", source) == offlineId,
            "Offline leave reads only the profile/source-bound session ID");
        foreach (string mismatch in new[] { "profileId", "sourceRuntime", "offlineSessionId" })
        {
            var wrong = new Dictionary<string, object>(marker);
            wrong[mismatch] = mismatch == "sourceRuntime" ? Path.Combine(_root, "other") : "wrong";
            File.WriteAllText(markerPath, ArenaMiniJson.Write(wrong));
            bool refused = false;
            try { ArenaSessionSwitch.ReadOfflineSessionIdFromMarker(markerPath, "0123456789abcdef01234567", source); }
            catch (InvalidOperationException) { refused = true; }
            Require(refused, "Changed offline marker cannot downgrade to legacy leave: " + mismatch);
        }
        ArenaSessionSwitch.ResetRequestLatch();
        ArenaSessionSwitch.OfflineSessionIdReader = (_, _) => offlineId;
        Require(ArenaSessionSwitch.Request("leave", null, out _)
            && HelperRequest.TryParse(File.ReadAllText(_requestPath), "leave", out var offlineLeave, out _)
            && offlineLeave.OfflineSessionId == offlineId && offlineLeave.Schema == ArenaSessionContract.OfflineRequestSchema,
            "Offline leave preserves schema3 authority without calling the absent ordinary server");
    }
}
