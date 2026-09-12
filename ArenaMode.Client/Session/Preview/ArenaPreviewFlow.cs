using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArenaMode.Session.Preview;

/// <summary>Transfers exactly one detached mannequin between one native raid setup's screens.</summary>
internal static class ArenaPreviewFlow
{
    private static readonly ArenaPreviewSurfaceState State = new();
    private static ArenaCharacterPreview _preview;
    private static RaidSettings _settings;
    private static Surface _surface;
    private static MatchMakerPlayerPreview _acceptPreview;
    private static MatchmakerTimeHasCome _loadingScreen;
    private static string _localAccountId;
    private static Action<bool> _pendingHostHide;

    public static void KeepFromSide(MatchMakerSideSelectionScreen screen, RaidSettings settings)
    {
        if (!ArenaMenuSelection.IsSelectedFor(settings)) return;
        var preview = ArenaSideSelectionLayout.DetachPreview(screen);
        if (preview == null) return;
        Reset();
        if (!preview.IsAvailable) { preview.Dispose(); return; }
        _preview = preview;
        _settings = settings;
        State.Begin(settings);
        preview.HoldForFlow(() => { if (ReferenceEquals(_preview, preview)) Reset(); });
        // The world is independent of screen activity, including the interval
        // between Close(old) and Show(new). A monitor catches Clear, raid start
        // and application teardown even when no UI host is currently visible.
        preview.SetFlowGuard(() => ReferenceEquals(_preview, preview) && IsSelected,
            () => { if (ReferenceEquals(_preview, preview)) Reset(); },
            () => { if (ReferenceEquals(_preview, preview)) FlushPendingHide(); });
    }

    private static bool IsSelected => _preview?.IsAvailable == true
        && ArenaMenuSelection.IsSelectedFor(_settings) && !ArenaSession.IsActive;

    private static bool Matches(RaidSettings settings)
        => State.Matches(settings, IsSelected);

    public static void ShowCompact(Transform host, RaidSettings settings)
    {
        if (!Matches(settings)) return;
        _acceptPreview = null;
        _loadingScreen = null;
        _localAccountId = null;
        TryAttach(host, null, null, null);
    }

    public static void PrepareAccept(MatchMakerAcceptScreen screen, RaidSettings settings)
    {
        _loadingScreen = null;
        _localAccountId = null;
        _acceptPreview = screen != null && Matches(settings) ? screen._playerModelView : null;
    }

    public static bool TryShowAccept(MatchMakerPlayerPreview preview)
    {
        if (preview == null || !IsSelected || !ReferenceEquals(preview, _acceptPreview) || !preview._isMainCharacter)
            return false;
        return TryAttach(preview.transform, preview._playerVisualImage,
            preview._noVisualImage, preview.UpdateVisual);
    }

    public static void PrepareLoading(MatchmakerTimeHasCome screen,
        MatchmakerTimeHasCome.TimeHasComeScreenController controller)
    {
        _acceptPreview = null;
        bool use = screen != null && controller != null && !controller.IsReconnecting && Matches(controller.RaidSettings);
        _loadingScreen = use ? screen : null;
        _localAccountId = use ? controller.Session.Profile.AccountId : null;
    }

    public static bool IsLocalLoadingPlayer(MatchmakerTimeHasCome screen, RaidPlayer player)
        => IsSelected && ReferenceEquals(screen, _loadingScreen) && player != null
           && !string.IsNullOrEmpty(_localAccountId) && player.AccountId == _localAccountId;

    public static bool TryShowLoading(MatchmakerTimeHasCome screen)
    {
        if (screen == null || !IsSelected || !ReferenceEquals(screen, _loadingScreen)
            || screen._playerModelView == null) return false;
        var image = screen._playerModelView?.GetComponentInParent<CameraImage>();
        var raw = image?.GetComponent<RawImage>();
        if (raw == null) return false;
        return TryAttach(screen.transform, raw, null,
            () => _ = RestoreLoading(screen), screen);
    }

    private static async Task RestoreLoading(MatchmakerTimeHasCome screen)
    {
        try { await screen.ShowPlayerModel(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Native loading preview restore: {ex.Message}"); }
    }

    public static void ShowNativeLoading(MatchmakerTimeHasCome screen)
    {
        if (screen != null && ReferenceEquals(screen, _loadingScreen)
            && _surface != null && ReferenceEquals(_surface.Host, screen.transform)) Suspend();
    }

    private static bool TryAttach(Transform host, RawImage nativeRaw, Image noVisual,
        Action restoreNative, MatchmakerTimeHasCome loading = null)
    {
        if (host == null || !host.gameObject.activeInHierarchy) return false;
        // A host can hide and show again before the next Update. Restore its
        // original flags BEFORE taking the next surface snapshot, including the
        // same-host fast path. A stale queued callback must never undo a new view.
        // This call already passed the exact native request-owner check. An old
        // hide from the same widget must not revoke that new request while its
        // previous surface is being restored. Guard/Close drains still revoke it.
        FlushPendingHide(preserveNativeRequest: true);
        if (_surface != null && ReferenceEquals(_surface.Host, host)) return true;
        try
        {
            Suspend();
            long generation = State.Attach(_settings, host, IsSelected);
            if (generation == 0) return false;
            var surface = new Surface(host, nativeRaw, noVisual, restoreNative, generation);
            _surface = surface; // Cleanup owns all mutations, including a partial attach.
            surface.HideNative();
            _preview.ShowAt(host, nativeRaw?.rectTransform);
            surface.Watch(() => QueueHostHide(host, generation));
            if (loading != null)
            {
                surface.BindDrag(loading, data =>
                {
                    if (State.IsCurrent(host, generation))
                        _preview?.Rotate(data.delta.x, loading._rotator._rotationSpeed);
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena preview host unavailable: {ex.Message}");
            // The caller will execute its native visual path after false.
            Reset(reloadNative: false);
            return false;
        }
    }

    /// <summary>Fallback callbacks only record intent while Unity locks the hierarchy.</summary>
    private static void QueueHostHide(Transform host, long generation)
    {
        if (!State.IsCurrent(host, generation)) return;
        _pendingHostHide = preserveNativeRequest => SuspendHost(host, generation, preserveNativeRequest);
    }

    internal static void FlushPendingHide() => FlushPendingHide(preserveNativeRequest: false);

    private static void FlushPendingHide(bool preserveNativeRequest)
    {
        var pending = _pendingHostHide;
        _pendingHostHide = null;
        Safe(() => pending?.Invoke(preserveNativeRequest));
    }

    /// <summary>Called before native Close disposes widgets or deactivates their ancestors.</summary>
    internal static void BeforeScreenClose(Transform screen)
    {
        // Close can also be called by a subscriber during an ancestor's hide.
        // Its OnDisable notification will be drained later by the guard.
        if (screen == null || !screen.gameObject.activeInHierarchy) return;
        bool OwnsScreen() => screen != null && _surface?.Host != null
            && (_surface.Host == screen || _surface.Host.IsChildOf(screen));
        if (!OwnsScreen()) return;
        FlushPendingHide();
        if (OwnsScreen()) Safe(() => SuspendHost(_surface.Host, _surface.Generation));
    }

    private static void SuspendHost(Transform host, long generation, bool preserveNativeRequest = false)
    {
        if (!State.IsCurrent(host, generation)) return;
        if (!preserveNativeRequest && _acceptPreview != null
            && ReferenceEquals(host, _acceptPreview.transform)) _acceptPreview = null;
        if (!preserveNativeRequest && _loadingScreen != null && ReferenceEquals(host, _loadingScreen.transform))
        {
            _loadingScreen = null;
            _localAccountId = null;
        }
        Suspend();
    }

    private static void Suspend()
    {
        _pendingHostHide = null;
        State.Invalidate();
        var surface = _surface;
        _surface = null;
        Safe(() => _preview?.Suspend());
        Safe(() => surface?.Dispose());
    }

    public static void Reset() => Reset(reloadNative: true);

    private static void Reset(bool reloadNative)
    {
        _pendingHostHide = null;
        var surface = _surface;
        var preview = _preview;
        _preview = null;
        _settings = null;
        _acceptPreview = null;
        _loadingScreen = null;
        _localAccountId = null;
        _surface = null;
        State.Clear();
        // Disable owned camera before restoring the native image. Release of a
        // pending body Init remains deferred to that load's finally block.
        Safe(() => preview?.Dispose());
        Safe(() => surface?.Dispose());
        if (reloadNative) Safe(() => surface?.ReloadNative());
    }

    private static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena preview restore: {ex.Message}"); }
    }

    private sealed class Surface : IDisposable
    {
        public readonly Transform Host;
        public readonly long Generation;
        private readonly RawImage _raw;
        private readonly CameraImage _cameraImage;
        private readonly Image _noVisual;
        private readonly Action _restoreNative;
        private readonly bool _rawActive;
        private readonly bool _rawEnabled;
        private readonly bool _cameraEnabled;
        private readonly bool _placeholderActive;
        private ArenaPreviewHostLifetime _lifetime;
        private Action _onHidden;
        private MatchmakerTimeHasCome _loading;
        private Action<PointerEventData> _onDrag;
        private bool _disposed;

        public Surface(Transform host, RawImage raw, Image noVisual, Action restoreNative, long generation)
        {
            Host = host;
            Generation = generation;
            _raw = raw;
            _noVisual = noVisual;
            _restoreNative = restoreNative;
            if (raw != null)
            {
                _cameraImage = raw.GetComponent<CameraImage>();
                if (_cameraImage == null)
                    throw new InvalidOperationException("Native model image has no CameraImage owner.");
                _rawActive = raw.gameObject.activeSelf;
                _rawEnabled = raw.enabled;
                _cameraEnabled = _cameraImage.enabled;
            }
            _placeholderActive = noVisual != null && noVisual.gameObject.activeSelf;
        }

        public void HideNative()
        {
            if (_cameraImage != null) _cameraImage.enabled = false;
            if (_raw != null) { _raw.gameObject.SetActive(true); _raw.enabled = false; }
            if (_noVisual != null) _noVisual.gameObject.SetActive(false);
        }

        public void Watch(Action onHidden)
        {
            _onHidden = onHidden;
            _lifetime = Host.GetComponent<ArenaPreviewHostLifetime>()
                ?? Host.gameObject.AddComponent<ArenaPreviewHostLifetime>();
            _lifetime.OnHidden = onHidden;
        }

        public void BindDrag(MatchmakerTimeHasCome screen, Action<PointerEventData> onDrag)
        {
            _loading = screen;
            _onDrag = onDrag;
            screen._dragTrigger.onDrag += onDrag;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Safe(() => { if (_lifetime != null && _lifetime.OnHidden == _onHidden) _lifetime.OnHidden = null; });
            Safe(() => { if (_loading != null && _onDrag != null) _loading._dragTrigger.onDrag -= _onDrag; });
            Safe(() => { if (_raw != null) _raw.gameObject.SetActive(_rawActive); });
            Safe(() => { if (_cameraImage != null) _cameraImage.enabled = _cameraEnabled; });
            Safe(() => { if (_raw != null) _raw.enabled = _rawEnabled; });
            Safe(() => { if (_noVisual != null) _noVisual.gameObject.SetActive(_placeholderActive); });
        }

        public void ReloadNative()
        {
            if (Host == null || !Host.gameObject.activeInHierarchy) return;
            // Returning from a substituted visual is a new native request. Its
            // model Show does not recreate the CameraImage texture itself.
            if (_cameraImage != null && _cameraImage.enabled) _cameraImage.RecreateTexture();
            _restoreNative?.Invoke();
        }
    }
}

internal sealed class ArenaPreviewHostLifetime : MonoBehaviour
{
    public Action OnHidden;
    private void OnDisable() => OnHidden?.Invoke();
    private void OnDestroy() => OnHidden?.Invoke();
}

internal sealed class ArenaPreviewFlowGuard : MonoBehaviour
{
    public Func<bool> IsCurrent;
    public Action OnInvalid;
    public Action OnTick;
    private void Update()
    {
        if (IsCurrent?.Invoke() == false) OnInvalid?.Invoke();
        else OnTick?.Invoke();
    }
    private void OnDestroy() => OnInvalid?.Invoke();
}
