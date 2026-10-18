using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArenaMode.Kits;
using ArenaMode.Session;
using ArenaMode.Shared;
using EFT;
using JsonType;
using SPT.Common.Http;

internal static partial class Program
{
    private static void CheckProtectedEntryAwait()
    {
        var fields = new[] { "_pendingArmToken", "_boundNativeEndIdentity", "<HasPendingArm>k__BackingField" }
            .Select(name => typeof(ArenaServerClient).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)).ToArray();
        var saved = fields.Select(field => field.GetValue(null)).ToArray();
        var issue = TacticalTeamKitEntry.Issue;
        bool failCapture = TacticalTeamKitEntry.FailCapture;
        var post = RequestHandler.Post;
        bool armed = ArenaSession.ArmedForNextRaid, inRaid = InGameStatus.InRaid;
        int epoch = ArenaSession.ArmEpoch;
        var observe = typeof(ArenaLocalRaidStartedPatch).GetMethod("ObserveLocalStart", BindingFlags.Static | BindingFlags.NonPublic);
        void Set(string name, object value) => fields.Single(field => field.Name == name).SetValue(null, value);
        string Begin()
        {
            string token = Guid.NewGuid().ToString("N");
            ArenaSession.ArmedForNextRaid = true; ArenaSession.ArmEpoch = 300;
            Set("_pendingArmToken", token); Set("<HasPendingArm>k__BackingField", true);
            Set("_boundNativeEndIdentity", null); InGameStatus.InRaid = true;
            return token;
        }
        Task<LocalSettings> Run(Task<LocalSettings> native) => (Task<LocalSettings>)observe.Invoke(null, new object[] { native, new EftClientBackendSession(), Request() });
        void Reject(Task task, string why)
        {
            bool refused = false;
            try { task.GetAwaiter().GetResult(); } catch (InvalidOperationException) { refused = true; }
            Assert(refused, why);
        }
        int cancelPosts = 0;
        try
        {
            RequestHandler.Post = (route, body) => { cancelPosts++; return "{\"cancelled\":true}"; };
            string token = Begin();
            var entry = new TaskCompletionSource<bool>(); TacticalTeamKitEntry.Issue = () => entry.Task;
            var task = Run(Task.FromResult(new LocalSettings { serverId = Protocol.BuildNativeEndIdentity("native", token) }));
            Assert(!task.IsCompleted, "LocalGame continuation awaits required kit assets and commit");
            entry.SetResult(true); task.GetAwaiter().GetResult();
            Assert(cancelPosts == 0 && ArenaSession.ArmedForNextRaid, "successful entry preserves protected arm");

            foreach (bool newArena in new[] { false, true })
            {
                token = Begin(); entry = new(); TacticalTeamKitEntry.Issue = () => entry.Task;
                task = Run(Task.FromResult(new LocalSettings { serverId = Protocol.BuildNativeEndIdentity("native", token) }));
                if (newArena) Set("_pendingArmToken", "new-arm-same-epoch");
                else ArenaSession.ArmedForNextRaid = false;
                int before = cancelPosts;
                entry.SetException(new InvalidOperationException("old required loading failed"));
                Reject(task, "old entry failure is propagated");
                Assert(cancelPosts == before && InGameStatus.InRaid && ArenaSession.ArmedForNextRaid == newArena,
                    "old entry failure cannot cancel a newer arena or ordinary raid with unchanged epoch");
            }

            token = Begin(); var nativePending = new TaskCompletionSource<LocalSettings>();
            int issues = 0; TacticalTeamKitEntry.Issue = () => { issues++; return Task.CompletedTask; };
            task = Run(nativePending.Task); ArenaSession.ArmedForNextRaid = false;
            Set("_boundNativeEndIdentity", "new-ordinary-identity");
            nativePending.SetResult(new LocalSettings { serverId = Protocol.BuildNativeEndIdentity("native", token) });
            Reject(task, "old native response cannot resume into ordinary raid");
            Assert(issues == 0 && Equals(fields.Single(f => f.Name == "_boundNativeEndIdentity").GetValue(null), "new-ordinary-identity"),
                "stale native response neither binds an identity nor issues a kit");

            token = Begin(); TacticalTeamKitEntry.Issue = () => Task.FromException(new InvalidOperationException("current entry failed"));
            int prior = cancelPosts;
            task = Run(Task.FromResult(new LocalSettings { serverId = Protocol.BuildNativeEndIdentity("native", token) }));
            Reject(task, "current entry error is propagated");
            Assert(cancelPosts > prior && !ArenaSession.ArmedForNextRaid && !InGameStatus.InRaid,
                "current failed entry cancels its own protected start");

            token = Begin(); nativePending = new(); TacticalTeamKitEntry.FailCapture = true;
            prior = cancelPosts; task = Run(nativePending.Task);
            Assert(!task.IsCompleted && cancelPosts == prior, "capture failure drains dispatched HTTP before cancellation");
            nativePending.SetResult(new LocalSettings { serverId = Protocol.BuildNativeEndIdentity("native", token) });
            Reject(task, "capture fault propagates after native HTTP completion");
            Assert(cancelPosts > prior && !ArenaSession.ArmedForNextRaid, "settled capture failure cancels its own server arm");
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
            TacticalTeamKitEntry.Issue = issue; RequestHandler.Post = post;
            TacticalTeamKitEntry.FailCapture = failCapture;
            ArenaSession.ArmedForNextRaid = armed; ArenaSession.ArmEpoch = epoch; InGameStatus.InRaid = inRaid;
        }
    }
}
