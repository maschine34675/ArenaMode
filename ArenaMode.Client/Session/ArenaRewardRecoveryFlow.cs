using ArenaMode.Shared;
using System;
using System.Threading.Tasks;

namespace ArenaMode.Session;

/// <summary>Screen-scoped consent. Polling can only read; one explicit review can write once.</summary>
internal sealed class ArenaRewardRecoveryFlow
{
    private readonly Func<Task<RewardRecoveryStatusResponse>> _read;
    private readonly Func<RewardRecoveryRequest, Task<RewardRecoveryResponse>> _write;
    private Task<RewardRecoveryStatusResponse> _reading;
    private Task<RewardRecoveryResponse> _writing;
    private object _session;
    private string _profile, _reviewId;
    private double _nextRead;
    private bool _reviewRequested, _hasResponse, _privateSession;

    internal ArenaRewardRecoveryFlow(Func<Task<RewardRecoveryStatusResponse>> read,
        Func<RewardRecoveryRequest, Task<RewardRecoveryResponse>> write)
    { _read = read; _write = write; }

    internal RewardRecoveryStatusResponse Status { get; private set; } = new RewardRecoveryStatusResponse();
    internal bool Busy => _reading != null || _writing != null;
    internal bool ReviewReady => _reviewRequested && _reviewId != null && !Busy;
    internal bool Unconfirmed { get; private set; }
    internal bool HasResponse => _hasResponse;
    internal bool HasEntry => Status.Cases.Count != 0 || Status.Queued || Status.RequiresNormalSession;
    internal bool CanConfirm => ReviewReady && Status.Available && Status.CanClose
        && !_privateSession && !Status.RequiresNormalSession && !Status.Queued && Status.Cases.Count != 0
        && !string.IsNullOrEmpty(_reviewId) && _reviewId == Status.ReviewId;

    // Backend object AND profile identity matter: a new login can reuse the same profile id.
    internal bool BindSession(object session, string profile, bool privateSession = false)
    {
        if (ReferenceEquals(_session, session) && string.Equals(_profile, profile, StringComparison.Ordinal)
            && _privateSession == privateSession)
            return false;
        Reset();
        _session = session;
        _profile = profile;
        _privateSession = privateSession;
        return true;
    }

    internal void Reset()
    {
        Observe(_reading);
        Observe(_writing);
        _reading = null;
        _writing = null;
        _session = null;
        _profile = null;
        _reviewId = null;
        _reviewRequested = _hasResponse = Unconfirmed = false;
        _privateSession = false;
        _nextRead = 0;
        Status = new RewardRecoveryStatusResponse();
    }

    internal void Tick(double now)
    {
        if (_session == null || string.IsNullOrEmpty(_profile)) return;
        if (_writing != null)
        {
            if (!_writing.IsCompleted) return;
            var task = _writing;
            _writing = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result?.Status != null)
            {
                Status = Copy(task.Result.Status);
                Unconfirmed = !task.Result.Accepted;
            }
            else { Observe(task); Unconfirmed = true; }
            // An uncertain POST is reconciled only by GET, never replayed.
            _nextRead = now;
        }
        if (_reading != null)
        {
            if (!_reading.IsCompleted) return;
            var task = _reading;
            _reading = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
            {
                Status = Copy(task.Result);
                _hasResponse = true;
                if (Status.Queued) Unconfirmed = false;
                if (_reviewRequested) _reviewId = Status.ReviewId ?? "";
            }
            else
            {
                Observe(task);
                CancelReview();
                // Keep a known issue reachable after a failed refresh, but never approve stale data.
                Unconfirmed = true;
            }
            _nextRead = now + 10;
        }
        if (!Busy && !_reviewRequested && now >= _nextRead) StartRead();
    }

    internal bool OpenReview()
    {
        if (Busy || _session == null || string.IsNullOrEmpty(_profile)) return false;
        _reviewRequested = true;
        _reviewId = null;
        StartRead();
        return true;
    }

    internal void CancelReview()
    {
        _reviewRequested = false;
        _reviewId = null;
    }

    internal bool Confirm(string reviewedId)
    {
        if (!CanConfirm || !string.Equals(reviewedId, _reviewId, StringComparison.Ordinal)) return false;
        var request = new RewardRecoveryRequest { ReviewId = _reviewId, ConfirmWithoutPayment = true };
        // Consume the review before invoking transport, including synchronous failures.
        CancelReview();
        Unconfirmed = false;
        try { _writing = _write(request) ?? Task.FromException<RewardRecoveryResponse>(new InvalidOperationException()); }
        catch (Exception error) { _writing = Task.FromException<RewardRecoveryResponse>(error); }
        return true;
    }

    private void StartRead()
    {
        try { _reading = _read() ?? Task.FromException<RewardRecoveryStatusResponse>(new InvalidOperationException()); }
        catch (Exception error) { _reading = Task.FromException<RewardRecoveryStatusResponse>(error); }
    }

    private static void Observe(Task task)
    {
        if (task == null) return;
        if (task.IsFaulted) { var observed = task.Exception; }
        else if (!task.IsCompleted)
            task.ContinueWith(completed => { var observed = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private RewardRecoveryStatusResponse Copy(RewardRecoveryStatusResponse source)
    {
        var result = new RewardRecoveryStatusResponse
        {
            Available = source.Available, CanClose = source.CanClose, Queued = source.Queued,
            RequiresNormalSession = _privateSession || source.RequiresNormalSession, ReviewId = source.ReviewId, Detail = source.Detail
        };
        if (source.Cases != null)
            foreach (var item in source.Cases)
                if (item != null) result.Cases.Add(new RewardRecoveryCaseSummary
                {
                    Kind = item.Kind, ShortId = item.ShortId, AmountKnown = item.AmountKnown,
                    Roubles = item.Roubles, Experience = item.Experience
                });
        return result;
    }
}
