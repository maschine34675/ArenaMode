using ArenaMode.Hud;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Only the Arena reward card and the Arena side choice own this control.</summary>
internal sealed class ArenaRewardRecoveryView : MonoBehaviour
{
    private static ArenaRewardRecoveryView _side;
    private ArenaRewardRecoveryFlow _flow;
    private GameObject _owner, _buttonObject;
    private RectTransform _sideAnchor;
    private Button _button;
    private TMP_Text _label;
    private DialogWindowContext _dialog;
    private bool _closed;

    internal static ArenaRewardRecoveryView CreateCard(Transform parent, GameObject owner, TMP_Text typography)
    {
        var view = Create(parent, owner, typography);
        var rect = (RectTransform)view.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-140f, -10f);
        rect.sizeDelta = new Vector2(150f, 28f);
        return view;
    }

    internal static void ShowSide(MatchMakerSideSelectionScreen screen)
    {
        HideSide(screen);
        try
        {
            if (_side != null) _side.Close();
            _side = Create(screen.transform, screen.gameObject, screen._sideDescriptionText);
            _side._sideAnchor = screen._randomButton.transform as RectTransform;
            var rect = (RectTransform)_side.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(240f, 28f);
        }
        catch (Exception error)
        {
            if (_side != null) _side.Close();
            _side = null;
            Plugin.ModLogger.LogWarning("Arena reward recovery control unavailable: " + error.GetBaseException().Message);
        }
    }

    internal static void HideSide(MatchMakerSideSelectionScreen screen)
    {
        if (_side == null || !ReferenceEquals(_side._owner, screen.gameObject)) return;
        _side.Close();
        _side = null;
    }

    private static ArenaRewardRecoveryView Create(Transform parent, GameObject owner, TMP_Text typography)
    {
        var root = new GameObject("Arena reward recovery", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var view = root.AddComponent<ArenaRewardRecoveryView>();
        view._owner = owner;
        view._flow = new ArenaRewardRecoveryFlow(Read, Write);
        var buttonObject = new GameObject("Resolve old rewards", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(root.transform, false);
        view._buttonObject = buttonObject;
        var buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = buttonRect.offsetMax = Vector2.zero;
        view._button = buttonObject.GetComponent<Button>();
        view._button.targetGraphic = buttonObject.GetComponent<Image>();
        var labelObject = new GameObject("Recovery label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        view._label = label;
        label.font = typography?.font ?? TMP_Settings.defaultFontAsset;
        if (typography != null) label.fontSharedMaterial = typography.fontSharedMaterial;
        label.fontSize = 12;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.richText = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(5f, 0f);
        label.rectTransform.offsetMax = new Vector2(-5f, 0f);
        ArenaUiTheme.StyleButton(view._button, label);
        buttonObject.GetComponent<Image>().color = Color.clear;
        var navigation = view._button.navigation;
        navigation.mode = Navigation.Mode.None;
        view._button.navigation = navigation;
        view._button.onClick.AddListener(view.OpenReview);
        buttonObject.SetActive(false);
        return view;
    }

    private bool Owns => !_closed && _flow != null && _owner != null
        && _owner.activeInHierarchy && gameObject.activeInHierarchy
        && !ArenaSession.IsActive && !ArenaSession.ArmedForNextRaid;

    private bool BindCurrentProfile()
    {
        if (!Owns) return false;
        var session = Singleton<ClientApplication<IEftSession>>.Instantiated
            ? Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession() : null;
        if (_flow.BindSession(session, session?.Profile?.Id, ArenaSessionMode.IsArenaSession)) CloseDialog();
        return session != null && !string.IsNullOrEmpty(session.Profile?.Id);
    }

    private void LateUpdate()
    {
        if (!BindCurrentProfile())
        {
            if (_buttonObject != null) _buttonObject.SetActive(false);
            CloseDialog();
            _flow?.Reset();
            return;
        }
        if (_sideAnchor != null && transform.parent != null)
        {
            var center = transform.parent.InverseTransformPoint(_sideAnchor.TransformPoint(_sideAnchor.rect.center));
            transform.localPosition = new Vector3(center.x, center.y - 48f, 0f);
        }
        _flow.Tick(Time.unscaledTime);
        _buttonObject.SetActive(_flow.HasEntry);
        _label.text = ArenaRewardRecoveryText.Label(_flow);
        _button.interactable = !_flow.Busy && _dialog == null;
        if (_flow.ReviewReady && _dialog == null) ShowReview();
    }

    private void OpenReview()
    {
        if (!BindCurrentProfile() || _dialog != null) return;
        _flow.OpenReview();
    }

    private void ShowReview()
    {
        if (!Owns || ItemUiContext.Instance == null || ItemUiContext.Instance.MessageWindowActive
            || ItemUiContext.Instance.ScrolledMessageWindowActive) return;
        var reviewedId = _flow.Status.ReviewId;
        bool confirm = _flow.CanConfirm;
        var dialog = ItemUiContext.Instance.ShowMessageWindow(ArenaRewardRecoveryText.Review(_flow.Status),
            confirm ? (Action)(() =>
            {
                // A native callback may outlive the screen or authenticated profile.
                if (BindCurrentProfile()) _flow.Confirm(reviewedId);
            }) : null,
            confirm ? (Action)(() => _flow.CancelReview()) : null,
            caption: confirm ? "CLOSE OLD REWARD CASES?" : "ARENA REWARDS", forceShow: true);
        _dialog = dialog;
        var window = ItemUiContext.Instance._messageWindow;
        var accept = window.AcceptButton;
        var cancel = window._cancelButton;
        string acceptText = accept?.HeaderText, cancelText = cancel?.HeaderText;
        int acceptSize = accept?.HeaderSize ?? 24, cancelSize = cancel?.HeaderSize ?? 24;
        if (confirm)
        {
            accept?.SetHeaderText("Close", acceptSize);
            cancel?.SetHeaderText("Cancel", cancelSize);
        }
        dialog.OnClose += () =>
        {
            // The native message window is shared. Restore only this exact use,
            // including silent screen teardown, before ordinary dialogs can reuse it.
            if (confirm && window != null && ReferenceEquals(window.WindowContext, dialog))
            {
                accept?.SetHeaderText(acceptText, acceptSize);
                cancel?.SetHeaderText(cancelText, cancelSize);
            }
            if (!ReferenceEquals(_dialog, dialog)) return;
            _dialog = null;
            _flow.CancelReview();
        };
    }

    private void CloseDialog()
    {
        var dialog = _dialog;
        _dialog = null;
        _flow?.CancelReview();
        dialog?.CloseSilent();
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        CloseDialog();
        _flow?.Reset();
        if (_button != null) _button.onClick.RemoveAllListeners();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void OnDisable() { CloseDialog(); _flow?.Reset(); }
    private void OnDestroy() { CloseDialog(); _flow?.Reset(); }

    // Invoke native asynchronous transport on the Unity thread before yielding;
    // no delayed Task.Run can accidentally pick a later login's HttpClient.
    private static async Task<RewardRecoveryStatusResponse> Read()
        => JsonConvert.DeserializeObject<RewardRecoveryStatusResponse>(Encoding.UTF8.GetString(
            await RequestHandler.HttpClient.GetAsync(RewardWire.RecoveryStatusRoute).ConfigureAwait(false)));

    private static async Task<RewardRecoveryResponse> Write(RewardRecoveryRequest request)
        => JsonConvert.DeserializeObject<RewardRecoveryResponse>(await RequestHandler.PostJsonAsync(
            RewardWire.RecoveryRequestRoute, JsonConvert.SerializeObject(request)).ConfigureAwait(false));
}
