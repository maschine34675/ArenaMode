namespace ArenaMode.Session.Preview;

/// <summary>One load and one release; an awaited body Init owns its cleanup until it settles.</summary>
internal sealed class ArenaPreviewLifetimeState
{
    private bool _started;
    private bool _loading;
    private bool _released;

    public bool IsDisposed { get; private set; }

    public bool TryBeginLoad()
    {
        if (_started || IsDisposed) return false;
        _started = _loading = true;
        return true;
    }

    public bool DisposeAndClaimRelease()
    {
        IsDisposed = true;
        return ClaimRelease();
    }

    public bool FinishLoadAndClaimRelease()
    {
        _loading = false;
        return ClaimRelease();
    }

    private bool ClaimRelease()
    {
        if (!IsDisposed || _loading || _released) return false;
        _released = true;
        return true;
    }
}

/// <summary>Exact flow and host identity. A late callback from an earlier attachment cannot restore a new host.</summary>
internal sealed class ArenaPreviewSurfaceState
{
    private object _settings;
    private object _host;
    private long _generation;

    public void Begin(object settings)
    {
        _settings = settings;
        Invalidate();
    }

    public long Attach(object settings, object host, bool selected)
    {
        if (!Matches(settings, selected) || host == null) return 0;
        _host = host;
        return ++_generation;
    }

    public bool Matches(object settings, bool selected)
        => selected && settings != null && ReferenceEquals(settings, _settings);

    public bool IsCurrent(object host, long generation)
        => generation != 0 && generation == _generation && ReferenceEquals(host, _host);

    public void Invalidate()
    {
        _host = null;
        ++_generation;
    }

    public void Clear()
    {
        _settings = null;
        Invalidate();
    }
}
