using ArenaMode.Hud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>Own summary instead of misleading vanilla raid statistics. No native layout/input writes.</summary>
internal sealed class ArenaCompactMapInfo
{
    private GameObject _root;
    private Transform _screen;
    private TextMeshProUGUI _title, _description;
    private CanvasGroup _nativeGroup;
    private bool _addedGroup, _interactable, _blocksRaycasts;
    private float _alpha;
    internal GameObject Root => _root;

    internal void Show(Transform screen, GameObject nativeInfo, TMP_Text typography)
    {
        Close();
        _screen = screen;
        try
        {
            _root = new GameObject("Arena Compact Map Info", typeof(RectTransform), typeof(Image));
            _root.SetActive(false);
            _root.transform.SetParent(screen, false);
            var image = _root.GetComponent<Image>();
            image.color = ArenaUiTheme.WithAlpha(ArenaUiTheme.Surface, .94f);
            image.raycastTarget = false;
            ArenaUiTheme.Decorate((RectTransform)_root.transform);
            _title = Text("Arena map name", typography, 23f);
            _description = Text("Arena map description", typography, 15f);
            _description.enableWordWrapping = true;
            // One owner hides the original. Customs preview hides OUR summary,
            // never the same native CanvasGroup, so restore leases cannot conflict.
            if (nativeInfo != null)
            {
                _nativeGroup = nativeInfo.GetComponent<CanvasGroup>();
                _addedGroup = _nativeGroup == null;
                if (_addedGroup) _nativeGroup = nativeInfo.AddComponent<CanvasGroup>();
                _alpha = _nativeGroup.alpha;
                _interactable = _nativeGroup.interactable;
                _blocksRaycasts = _nativeGroup.blocksRaycasts;
                _nativeGroup.alpha = 0f; _nativeGroup.interactable = false; _nativeGroup.blocksRaycasts = false;
            }
            Refresh(null, null);
            _root.SetActive(true);
        }
        catch { Close(); throw; }
    }

    internal void Refresh(string name, string description)
    {
        if (_root == null) return;
        ArenaMapPanelLayout.Place((RectTransform)_root.transform, _screen, ArenaMapPanelLayout.InfoTop, ArenaMapPanelLayout.InfoHeight);
        float width = ArenaMapPanelLayout.Width(_screen) - 40f;
        Place(_title.rectTransform, 18f, width, 30f);
        Place(_description.rectTransform, 57f, width, 66f);
        _title.text = string.IsNullOrWhiteSpace(name) ? "SELECT A MAP" : name;
        _description.text = string.IsNullOrWhiteSpace(name) ? "Choose a map. Match rules and mutators apply to your Arena match."
            : ShortDescription(description);
    }

    internal static string ShortDescription(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Configure your Arena match on this map.";
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length <= 160) return text;
        int end = text.LastIndexOf(' ', 156, 40);
        return text.Substring(0, end >= 116 ? end : 156).TrimEnd() + "…";
    }

    internal void Close()
    {
        if (_nativeGroup != null)
        {
            _nativeGroup.alpha = _alpha;
            _nativeGroup.interactable = _interactable;
            _nativeGroup.blocksRaycasts = _blocksRaycasts;
            if (_addedGroup) UnityEngine.Object.DestroyImmediate(_nativeGroup);
        }
        _nativeGroup = null; _addedGroup = false;
        var root = _root; _root = null; _screen = null;
        _title = _description = null;
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
    }

    private TextMeshProUGUI Text(string name, TMP_Text source, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_root.transform, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = source?.font ?? TMP_Settings.defaultFontAsset;
        if (source != null) text.fontSharedMaterial = source.fontSharedMaterial;
        text.fontSize = size; text.fontStyle = FontStyles.Normal;
        text.color = size > 20f ? ArenaUiTheme.Text : ArenaUiTheme.Muted;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false; text.enableWordWrapping = false;
        text.richText = false; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void Place(RectTransform rect, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
