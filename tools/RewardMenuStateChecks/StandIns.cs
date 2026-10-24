using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityEngine
{
    internal static class Time { public static float unscaledTime; }
}

namespace ArenaMode.Session
{
    internal static class ArenaSession { internal static bool ArmedForNextRaid, IsActive; }
}

namespace SPT.Common.Http
{
    internal sealed class PendingRequest
    {
        internal string Route, Body;
        internal readonly TaskCompletionSource<string> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete(object value) => Reply.SetResult(Newtonsoft.Json.JsonConvert.SerializeObject(value));
    }

    internal static class RequestHandler
    {
        internal static readonly FakeHttpClient HttpClient = new();
        internal static readonly ConcurrentQueue<PendingRequest> Gets = new(), Posts = new();
        internal static string PostJson(string route, string body)
        {
            var request = new PendingRequest { Route = route, Body = body };
            Posts.Enqueue(request);
            return request.Reply.Task.GetAwaiter().GetResult();
        }
        internal static PendingRequest WaitFor(ConcurrentQueue<PendingRequest> requests, int index)
        {
            if (!SpinWait.SpinUntil(() => requests.Count > index, 5000)) throw new Exception("Transport request was not started");
            return requests.ToArray()[index];
        }
    }

    internal sealed class FakeHttpClient
    {
        internal async Task<byte[]> GetAsync(string route)
        {
            var request = new PendingRequest { Route = route };
            RequestHandler.Gets.Enqueue(request);
            return Encoding.UTF8.GetBytes(await request.Reply.Task);
        }
    }
}
