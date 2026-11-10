using System;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaMode.Session;

/// <summary>The native home screen owns this backdrop and its fade. No native environment, controls or camera is modified.</summary>
internal sealed class ArenaMenuBackdrop : MonoBehaviour
{
    private static ArenaMenuBackdrop _current;
    private static bool _installed, _warned;
    internal const float OverscanAt1080 = 13f;

    internal static bool VisibleFor(MenuScreen.MainMenuScreenController owner) => _current != null &&
        ReferenceEquals(_current._owner, owner) && _current.isActiveAndEnabled && _current._lastSize.x > 0f &&
        _current._lastSize.y > 0f;

    private MenuScreen.MainMenuScreenController _owner;
    private RectTransform _screen, _viewport, _panel, _hall, _barrier, _fence;
    private Vector2 _lastSize, _lastCenter, _cursor;
    private float _artAspect, _unit;

    internal static void Install(Harmony harmony)
    {
        if (_installed || !ArenaSessionMode.GateActive) return;
        var show = AccessTools.DeclaredMethod(typeof(MenuScreen), nameof(MenuScreen.Show),
            new[] { typeof(MenuScreen.MainMenuBaseScreenController) });
        harmony.Patch(show, postfix: new HarmonyMethod(typeof(ArenaMenuBackdrop), nameof(AfterShow)));
        _installed = true;
    }

    // EFT also uses MenuScreen and EEftScreenType.MainMenu for raid pause and reconnect. Only this
    // specific controller displays the ordinary home menu in the positively admitted Arena process.
    private static void AfterShow(MenuScreen __instance, MenuScreen.MainMenuBaseScreenController __0)
    {
        if (!_installed) return;
        ArenaNativeNotices.MenuShown(__instance, __0);
        if (!ArenaSessionMode.GateActive || __0 is not MenuScreen.MainMenuScreenController home || ArenaSession.IsActive)
        {
            RemoveCurrent();
            return;
        }
        if (_current != null && _current._owner == home && _current._screen == __instance.RectTransform) return;
        RemoveCurrent();
        GameObject panel = null;
        try
        {
            var canvas = __instance.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.rootCanvas.transform is not RectTransform viewport) return;
            var hall = ArenaMenuArt.Hall;
            var barrier = ArenaMenuArt.Barrier;
            var fence = ArenaMenuArt.Fence;
            if (hall == null || barrier == null || fence == null)
            {
                ArenaMenuArt.ReleaseMenu();
                return;
            }

            // A child of the actual screen participates in its native close/disable and CanvasGroup
            // fade. A nested canvas isolates the moving graphics from the button canvas rebuilds.
            panel = new GameObject("Arena Menu Backdrop", typeof(RectTransform), typeof(Canvas), typeof(LayoutElement));
            panel.SetActive(false);
            panel.layer = __instance.gameObject.layer;
            panel.transform.SetParent(__instance.transform, false);
            panel.transform.SetAsFirstSibling();
            panel.GetComponent<LayoutElement>().ignoreLayout = true;
            var view = panel.AddComponent<ArenaMenuBackdrop>();
            view._owner = home;
            view._screen = __instance.RectTransform;
            view._viewport = viewport;
            view._panel = (RectTransform)panel.transform;
            view._panel.anchorMin = view._panel.anchorMax = view._panel.pivot = new Vector2(.5f, .5f);
            view._artAspect = (float)hall.width / hall.height;
            view._hall = view.Layer("Hall", hall);
            view._barrier = view.Layer("Barrier", barrier);
            view._fence = view.Layer("Fence", fence);
            view.AddMenuShade();
            view.Layout();
            _current = view;
            panel.SetActive(true);
        }
        catch (Exception error)
        {
            if (panel != null) Destroy(panel);
            _current = null;
            ArenaMenuArt.ReleaseMenu();
            if (!_warned) Plugin.ModLogger?.LogWarning("Arena main-menu background unavailable: " + error.Message);
            _warned = true;
        }
    }

    private RectTransform Layer(string name, Texture2D texture)
    {
        var layer = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        layer.layer = gameObject.layer;
        layer.transform.SetParent(_panel, false);
        var image = layer.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.texture = texture;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(.5f, .5f);
        return image.rectTransform;
    }

    private void AddMenuShade()
    {
        var shade = new GameObject("Menu shade", typeof(RectTransform), typeof(ArenaMenuShade));
        shade.layer = gameObject.layer;
        shade.transform.SetParent(_panel, false);
        var graphic = shade.GetComponent<ArenaMenuShade>();
        graphic.raycastTarget = false;
        graphic.rectTransform.anchorMin = Vector2.zero;
        graphic.rectTransform.anchorMax = Vector2.one;
        graphic.rectTransform.offsetMin = Vector2.zero;
        graphic.rectTransform.offsetMax = Vector2.zero;
    }

    private bool Layout()
    {
        // Convert the entire root canvas to native screen coordinates. The screen itself may be
        // inset or scaled, particularly on wide monitors. All artwork uses one aspect-preserving crop.
        Rect viewport = _viewport.rect;
        Vector2 min = _screen.InverseTransformPoint(_viewport.TransformPoint(viewport.min));
        Vector2 max = _screen.InverseTransformPoint(_viewport.TransformPoint(viewport.max));
        Vector2 size = max - min, center = (max + min) * .5f;
        if (size == _lastSize && center == _lastCenter) return false;
        _lastSize = size;
        _lastCenter = center;
        _panel.localPosition = center;
        _panel.sizeDelta = size;
        _unit = size.y / 1080f;
        float margin = _unit * OverscanAt1080;
        float height = Mathf.Max(size.y + 2f * margin, (size.x + 2f * margin) / _artAspect);
        _hall.sizeDelta = _barrier.sizeDelta = _fence.sizeDelta = new Vector2(height * _artAspect, height);
        return true;
    }

    private void LateUpdate()
    {
        if (_viewport == null || ArenaSession.IsActive || _owner.Closed ||
            !ReferenceEquals(EftScreenManager.Instance.CurrentScreenController, _owner))
        {
            RemoveCurrent();
            return;
        }
        bool resized = Layout();
        Vector2 target = Vector2.zero;
        if (Application.isFocused && Screen.width > 0 && Screen.height > 0)
        {
            Vector3 mouse = Input.mousePosition;
            target = new Vector2(Mathf.Clamp(2f * mouse.x / Screen.width - 1f, -1f, 1f),
                Mathf.Clamp(2f * mouse.y / Screen.height - 1f, -1f, 1f));
        }
        var next = (_cursor - target).sqrMagnitude < .000001f
            ? target : Vector2.Lerp(_cursor, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        if (!resized && _cursor.Equals(next)) return;
        _cursor = next;
        _barrier.anchoredPosition = -_cursor * (5f * _unit);
        _fence.anchoredPosition = -_cursor * (12f * _unit);
    }

    private void OnDisable()
    {
        if (ReferenceEquals(_current, this)) RemoveCurrent(deactivate: false);
    }

    private static void RemoveCurrent(bool deactivate = true)
    {
        var view = _current;
        _current = null;
        if (view == null) return;
        if (deactivate) view.gameObject.SetActive(false);
        Destroy(view.gameObject);
        ArenaMenuArt.ReleaseMenu();
    }

    internal static void Shutdown()
    {
        _installed = false;
        RemoveCurrent();
    }
}

// The hall's bright doorway can sit under the native play button. Grade the middle of the
// lower viewport independently of the moving layers so its text stays legible at each aspect.
internal sealed class ArenaMenuShade : Graphic
{
    private static readonly float[] Columns = { 0f, .34f, .66f, 1f };
    private static readonly float[] Rows = { 0f, .4f, .53f, .7f };
    private static readonly byte[] Opacity = { 142, 162, 78, 0 };

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        for (int y = 0; y < Rows.Length; y++)
        for (int x = 0; x < Columns.Length; x++)
        {
            var alpha = x == 0 || x == Columns.Length - 1 ? (byte)0 : Opacity[y];
            var position = new Vector2(rect.xMin + rect.width * Columns[x], rect.yMin + rect.height * Rows[y]);
            mesh.AddVert(position, new Color32(0, 0, 0, alpha), Vector2.zero);
            if (x == 0 || y == 0) continue;
            int topRight = y * Columns.Length + x, bottomLeft = topRight - Columns.Length - 1;
            mesh.AddTriangle(bottomLeft, topRight - 1, topRight);
            mesh.AddTriangle(bottomLeft, topRight, bottomLeft + 1);
        }
    }
}
