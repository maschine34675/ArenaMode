// External Unity/TMP surfaces only. Production view and immutable state are
// compiled unchanged; this exercises ownership/layout arithmetic, not rendering.
namespace UnityEngine
{
    public class Object
    {
        public static T Instantiate<T>(T original, Transform parent, bool world) where T:Component
        {
            var result = new GameObject("cloned native row",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));
            result.GetComponent<UnityEngine.UI.Button>().targetGraphic = result.GetComponent<UnityEngine.UI.Image>();
            result.transform.SetParent(parent,false);
            var label=new GameObject("native label",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI));
            label.transform.SetParent(result.transform,false);
            return (T)(Component)result.GetComponent<UnityEngine.UI.Button>();
        }
        public static void Destroy(GameObject go) { go.SetActive(false); go.transform.SetParent(null,false); }
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform=>gameObject.transform;
        public string name { get=>gameObject.name; set=>gameObject.name=value; }
        public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool inactive) where T:Component=>gameObject.GetComponentsInChildren<T>(inactive).FirstOrDefault();
    }
    public class Transform:Component
    {
        public Transform parent;
        public readonly List<Transform> Children=new();
        public Vector3 localScale;
        public void SetParent(Transform target,bool world) { parent?.Children.Remove(this);parent=target;parent?.Children.Add(this); }
        public void SetAsLastSibling(){}
        public void SetAsFirstSibling(){}
    }
    public class RectTransform:Transform { public Vector2 anchorMin,anchorMax,pivot,anchoredPosition,sizeDelta,offsetMin,offsetMax; }
    public readonly record struct Vector2(float x,float y) { public static Vector2 zero=>new(0,0); public static Vector2 one=>new(1,1); }
    public readonly record struct Vector3(float x,float y,float z) { public static Vector3 one=>new(1,1,1); }
    public struct Color(float red,float green,float blue,float alpha)
    { public float r=red,g=green,b=blue,a=alpha; public static Color white=>new(1,1,1,1); }
    public class CanvasRenderer:Component{}
    public class GameObject:Object
    {
        public string name;
        public Transform transform;
        public bool activeSelf=true;
        public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
        readonly List<Component> components=new();
        public GameObject(string label,params Type[] types)
        {
            name=label;
            transform=(Transform)Activator.CreateInstance(types.Contains(typeof(RectTransform))?typeof(RectTransform):typeof(Transform));
            transform.gameObject=this;components.Add(transform);
            foreach(var type in types.Where(t=>!typeof(Transform).IsAssignableFrom(t)))
            { var c=(Component)Activator.CreateInstance(type);c.gameObject=this;components.Add(c); }
        }
        public void SetActive(bool value)=>activeSelf=value;
        public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool inactive) where T:Component
        {
            if(!inactive&&!activeInHierarchy)return [];
            return components.OfType<T>().Concat(transform.Children.SelectMany(t=>t.gameObject.GetComponentsInChildren<T>(inactive))).ToArray();
        }
    }
}
namespace UnityEngine.UI
{
    public class Image:UnityEngine.Component { public UnityEngine.Color color;public bool raycastTarget; }
    public class LayoutElement:UnityEngine.Component { public bool ignoreLayout; }
    public struct Navigation { public enum Mode { None } public Mode mode; }
    public class Selectable:UnityEngine.Component { public enum Transition { None,ColorTint } public Transition transition; }
    public struct ColorBlock { public UnityEngine.Color normalColor,highlightedColor,pressedColor,selectedColor,disabledColor; public float colorMultiplier,fadeDuration; }
    public class Button:Selectable
    {
        public bool interactable;public Navigation navigation;
        public UnityEngine.Component targetGraphic; public ColorBlock colors;
        public ButtonClickedEvent onClick=new();
        public class ButtonClickedEvent
        {
            readonly List<Action> callbacks=new();
            public void AddListener(Action action)=>callbacks.Add(action);
            public void RemoveAllListeners()=>callbacks.Clear();
            public Action[] Snapshot()=>callbacks.ToArray();
            public void Invoke(){foreach(var action in Snapshot())action();}
        }
    }
}
namespace TMPro
{
    public enum FontStyles { Normal }
    public enum TextOverflowModes { Ellipsis }
    public enum TextAlignmentOptions { MidlineLeft }
    public class TMP_Text:UnityEngine.Component
    {
        public UnityEngine.Color RendererTint;
        public void CrossFadeColor(UnityEngine.Color color,float duration,bool ignoreTimeScale,bool useAlpha) => RendererTint=color;
        public string text;
        public object font,fontSharedMaterial,fontStyle;
        public float fontSize,characterSpacing;
        public bool enableWordWrapping,raycastTarget;
        public TextOverflowModes overflowMode;
        public TextAlignmentOptions alignment;
        public UnityEngine.Color color=UnityEngine.Color.white;
        public UnityEngine.RectTransform rectTransform=>(UnityEngine.RectTransform)transform;
    }
    public class TextMeshProUGUI:TMP_Text{}
    public static class TMP_Settings { public static object defaultFontAsset=new(); }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T:new() { public static bool Instantiated=true;public static T Instance=new(); }
}
namespace EFT.UI
{
    public enum EUISoundType { MenuDropdownSelect }
    public class GUISounds { public int Count;public void PlayUISound(EUISoundType sound)=>Count++; }
    public class HoverTooltipArea:UnityEngine.Component { public void SetMessageText(string text,bool rawText){} public void SetUnlockStatus(bool value){} }
    public class ItemUiContext { public static ItemUiContext Instance=new(); }
    public class MenuUI { public static MenuUI Instance=new(); public Offline MatchmakerOfflineRaidScreen=new(); }
    public class Offline { public RaidWindow _raidSettingsWindow=new(); }
    public class RaidWindow { public Dropdown _aiDifficultyDropdown=new(); }
    public class Dropdown
    {
        public UnityEngine.UI.Button _elementTemplate=new UnityEngine.GameObject("native template",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button)).GetComponent<UnityEngine.UI.Button>();
        public TMPro.TMP_Text _currentValueText=new UnityEngine.GameObject("native typography",typeof(UnityEngine.RectTransform),typeof(TMPro.TextMeshProUGUI)).GetComponent<TMPro.TextMeshProUGUI>();
    }
}
namespace ArenaMode
{
    public static class Plugin { public static Logger ModLogger=new(); }
    public class Logger { public void LogWarning(string text){} }
}
namespace ArenaMode.Compat
{
    internal static class PitFireTeamCompat { internal static int SelectedCompanionCount = 4; }
}
