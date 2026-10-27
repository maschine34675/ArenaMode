using ArenaMode.Session;
using ArenaMode.Shared;

int failures = 0;
void Check(string name, Action action)
{
    try { action(); Console.WriteLine("PASS: " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + " — " + error.Message); }
}
void Require(bool value) { if (!value) throw new InvalidOperationException("assertion failed"); }

Check("polling is read-only", () =>
{
    var f = new Fixture(); f.Load(); f.Flow.Tick(15); f.AnswerRead(); f.Flow.Tick(16);
    Require(f.ReadCount == 2 && f.Writes.Count == 0 && f.Flow.HasEntry && !f.Flow.CanConfirm);
});
Check("fresh review precedes confirmation", () =>
{
    var f = new Fixture(); f.Load(); Require(f.Flow.OpenReview());
    Require(f.ReadCount == 2 && !f.Flow.Confirm("review-1"));
    f.AnswerRead(); f.Flow.Tick(2);
    Require(f.Flow.ReviewReady && f.Flow.CanConfirm && f.Writes.Count == 0);
});
Check("cancellation prevents writes", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); f.Flow.CancelReview();
    Require(!f.Flow.Confirm("review-1") && !f.Flow.ReviewReady && f.Writes.Count == 0);
});
Check("cancel during refresh cannot reopen review", () =>
{
    var f = new Fixture(); f.Load(); f.Flow.OpenReview(); f.Flow.CancelReview();
    f.AnswerRead(); f.Flow.Tick(2);
    Require(!f.Flow.ReviewReady && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
});
Check("confirmation carries explicit waiver only", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); Require(f.Flow.Confirm("review-1"));
    var request = f.Writes.Single();
    Require(request.ReviewId == "review-1" && request.ConfirmWithoutPayment);
    Require(typeof(RewardRecoveryRequest).GetProperties().Length == 2);
});
Check("duplicate confirmation sends one request", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); Require(f.Flow.Confirm("review-1"));
    Require(!f.Flow.Confirm("review-1") && !f.Flow.OpenReview() && f.Writes.Count == 1);
});
Check("wrong reviewed identity cannot submit", () =>
{
    var f = new Fixture(); f.Load(); f.Review();
    Require(!f.Flow.Confirm("other-review") && f.Writes.Count == 0);
});
Check("private session cannot submit", () =>
{
    var f = new Fixture(); f.Load(); var s = Fixture.Status(); s.RequiresNormalSession = true; f.Review(s);
    Require(!f.Flow.CanConfirm && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
    Require(ArenaRewardRecoveryText.Review(s).Contains("Return to normal SPT first."));
});
Check("queued unavailable blocked empty and unversioned cases cannot submit", () =>
{
    foreach (var mutate in new Action<RewardRecoveryStatusResponse>[] {
        s => s.Queued = true, s => s.Available = false, s => s.CanClose = false,
        s => s.Cases.Clear(), s => s.ReviewId = "" })
    {
        var f = new Fixture(); f.Load(); var status = Fixture.Status(); mutate(status); f.Review(status);
        Require(!f.Flow.CanConfirm && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
    }
});
Check("local private session remains read-only despite server response", () =>
{
    var f = new Fixture(); f.Flow.BindSession(f.Session, "profile", privateSession: true); f.Load(); f.Review();
    Require(f.Flow.Status.RequiresNormalSession && !f.Flow.CanConfirm && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
});
Check("unavailable status never claims no old cases", () =>
{
    string text = ArenaRewardRecoveryText.Review(new RewardRecoveryStatusResponse());
    Require(text.Contains("unavailable") && !text.Contains("No reward cases"));
});
Check("profile switch discards pending read", () =>
{
    var f = new Fixture(); f.Flow.Tick(0); var old = f.Reads.Dequeue();
    f.Flow.BindSession(f.Session, "different-profile"); f.Flow.Tick(1);
    old.SetResult(Fixture.Status()); f.AnswerRead(new RewardRecoveryStatusResponse { Available = true }); f.Flow.Tick(2);
    Require(!f.Flow.HasEntry && !f.Flow.ReviewReady && f.Writes.Count == 0);
});
Check("same profile new login invalidates review", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); f.Flow.BindSession(new object(), "profile");
    Require(!f.Flow.CanConfirm && !f.Flow.HasEntry && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
});
Check("same session new profile invalidates review", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); f.Flow.BindSession(f.Session, "different-profile");
    Require(!f.Flow.CanConfirm && !f.Flow.HasEntry && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
});
Check("profile switch discards pending post response", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); f.Flow.Confirm("review-1");
    f.Flow.BindSession(new object(), "other-profile"); f.Flow.Tick(3);
    f.Posts.Dequeue().SetResult(new RewardRecoveryResponse { Accepted = true, Status = Fixture.Status(queued: true) });
    f.AnswerRead(new RewardRecoveryStatusResponse { Available = true }); f.Flow.Tick(4);
    Require(!f.Flow.Status.Queued && !f.Flow.HasEntry && f.Writes.Count == 1);
});
Check("closing prevents late review and callbacks", () =>
{
    var f = new Fixture(); f.Load(); f.Flow.OpenReview(); var old = f.Reads.Dequeue();
    f.Flow.Reset(); old.SetResult(Fixture.Status()); f.Flow.Tick(100);
    Require(!f.Flow.HasEntry && !f.Flow.ReviewReady && !f.Flow.Confirm("review-1") && f.Writes.Count == 0);
});
Check("uncertain post is reconciled without retry", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); f.Flow.Confirm("review-1");
    f.Posts.Dequeue().SetException(new IOException("connection lost after submission")); f.Flow.Tick(3);
    Require(f.Flow.Unconfirmed && f.ReadCount == 3 && f.Writes.Count == 1);
    f.AnswerRead(Fixture.Status(queued: true)); f.Flow.Tick(4);
    Require(f.Flow.Status.Queued && !f.Flow.Unconfirmed && !f.Flow.CanConfirm);
    for (int index = 0; index < 3; index++) { f.Flow.Tick(20 + index * 11); f.AnswerRead(); f.Flow.Tick(21 + index * 11); }
    Require(f.Writes.Count == 1 && !f.Flow.CanConfirm);
});
Check("accepted post still checks server queue", () =>
{
    var f = new Fixture(); f.Load(); f.Review(); f.Flow.Confirm("review-1");
    f.Posts.Dequeue().SetResult(new RewardRecoveryResponse { Accepted = true, Status = Fixture.Status(queued: true) });
    f.Flow.Tick(3); Require(f.Flow.Busy && f.Flow.Status.Queued && f.ReadCount == 3);
    f.AnswerRead(Fixture.Status(queued: true)); f.Flow.Tick(4);
    Require(f.Flow.Status.Queued && !f.Flow.ReviewReady && f.Writes.Count == 1);
});
Check("failed refresh keeps entry but never stale approval", () =>
{
    var f = new Fixture(); f.Load(); f.Flow.OpenReview(); f.Reads.Dequeue().SetException(new IOException()); f.Flow.Tick(2);
    Require(f.Flow.HasEntry && f.Flow.Unconfirmed && !f.Flow.CanConfirm && !f.Flow.Confirm("review-1"));
});
Check("response objects cannot mutate captured review", () =>
{
    var f = new Fixture(); f.Load(); var response = Fixture.Status(); f.Review(response);
    response.ReviewId = "changed"; response.Cases.Clear();
    Require(f.Flow.CanConfirm && f.Flow.Status.Cases.Count == 1 && f.Flow.Status.ReviewId == "review-1");
});
Check("review is concise bounded and treats unknown amounts honestly", () =>
{
    var status = Fixture.Status(); status.Cases[0].Roubles = 1200; status.Cases[0].Experience = 250;
    status.Cases.Add(new RewardRecoveryCaseSummary { Kind = "<size=999>", ShortId = "<size=999>bad\nvalue", AmountKnown = false });
    string text = ArenaRewardRecoveryText.Review(status);
    Require(text.Contains("2 old reward cases") && text.Contains("250 XP") && text.Contains("Amount unknown: 1 case"));
    Require(text.Contains("NO further payout?") && text.Contains("Profile progress stays.") && !text.Contains("<"));
    for (int i = 0; i < 100; i++) status.Cases.Add(new RewardRecoveryCaseSummary { AmountKnown = true, Roubles = long.MaxValue, Experience = long.MaxValue, ShortId = new string('x', 500) });
    text = ArenaRewardRecoveryText.Review(status);
    Require(text.Length < 500 && text.Split('\n').Length <= 7 && !text.Contains(new string('x', 20)));
});
Check("healthy profile does not expose a recovery entry", () =>
{
    var f = new Fixture(); f.Load(new RewardRecoveryStatusResponse { Available = true });
    Require(!f.Flow.HasEntry && !f.Flow.CanConfirm && f.Writes.Count == 0);
});

if (failures != 0) return 1;
Console.WriteLine("REWARD_RECOVERY_UI_CHECKS_PASSED");
return 0;

sealed class Fixture
{
    internal readonly object Session = new();
    internal readonly Queue<TaskCompletionSource<RewardRecoveryStatusResponse>> Reads = new();
    internal readonly Queue<TaskCompletionSource<RewardRecoveryResponse>> Posts = new();
    internal readonly List<RewardRecoveryRequest> Writes = new();
    internal readonly ArenaRewardRecoveryFlow Flow;
    internal int ReadCount;
    internal Fixture()
    {
        Flow = new ArenaRewardRecoveryFlow(() => { ReadCount++; var task = new TaskCompletionSource<RewardRecoveryStatusResponse>(); Reads.Enqueue(task); return task.Task; },
            request => { Writes.Add(request); var task = new TaskCompletionSource<RewardRecoveryResponse>(); Posts.Enqueue(task); return task.Task; });
        Flow.BindSession(Session, "profile");
    }
    internal void Load(RewardRecoveryStatusResponse status = null) { Flow.Tick(0); AnswerRead(status); Flow.Tick(1); }
    internal void AnswerRead(RewardRecoveryStatusResponse status = null) => Reads.Dequeue().SetResult(status ?? Status());
    internal void Review(RewardRecoveryStatusResponse status = null)
    { if (!Flow.OpenReview()) throw new InvalidOperationException("review did not open"); AnswerRead(status); Flow.Tick(2); }
    internal static RewardRecoveryStatusResponse Status(bool queued = false) => new()
    {
        Available = true, CanClose = true, Queued = queued, ReviewId = "review-1",
        Cases = new() { new RewardRecoveryCaseSummary { Kind = "payment", ShortId = "abcdef123456", AmountKnown = true, Roubles = 100, Experience = 20 } }
    };
}
