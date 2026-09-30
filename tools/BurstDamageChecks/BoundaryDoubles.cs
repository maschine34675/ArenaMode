// Engine, font measurement, style and configuration boundaries only. The actual
// production pooling, sums, burst selection, expiration, layout and fading run.
using System.Globalization;
using System.Text.RegularExpressions;
namespace UnityEngine
{
 public struct Vector2(float x, float y)
 {
  public float x=x,y=y;
  public static Vector2 zero=>new(0,0);
  public static Vector2 operator /(Vector2 value,float divisor)=>new(value.x/divisor,value.y/divisor);
 }
 public struct Vector3(float x, float y, float z) {public float x=x,y=y,z=z;}
 public struct Rect(float x,float y,float width,float height)
 {
  public float x=x,y=y,width=width,height=height;
  public Vector2 center=>new(x+width*.5f,y+height*.5f);
  public bool Overlaps(Rect other)=>x<other.x+other.width&&x+width>other.x&&y<other.y+other.height&&y+height>other.y;
 }
 public record struct Color(float r,float g,float b,float a);
 public class GameObject {public bool activeSelf=true;public void SetActive(bool value)=>activeSelf=value;}
 public class RectTransform {public GameObject gameObject=new();public Vector2 anchorMin,anchorMax,pivot,anchoredPosition;}
 public class Canvas {public float scaleFactor=1f;}
 public static class Screen {public static int width=1000,height=1000;}
 public static class Time {public static float unscaledTime;}
 public class Camera {public Vector3 WorldToViewportPoint(Vector3 world)=>world;}
 public static class Mathf
 {
  public static float Max(float a,float b)=>MathF.Max(a,b);
  public static int RoundToInt(float value)=>(int)MathF.Round(value,MidpointRounding.ToEven);
  public static float Clamp(float value,float min,float max)=>Math.Clamp(value,min,max);
  public static float Clamp01(float value)=>Clamp(value,0f,1f);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
  public static float Ceil(float value)=>MathF.Ceiling(value);
 }
}
namespace TMPro
{
 public enum TextAlignmentOptions {Center}
 public class TextMeshProUGUI
 {
  public UnityEngine.RectTransform rectTransform=new();
  public UnityEngine.Color color;
  public string text="";
  public void SetText(string format,float first)=>text=string.Format(CultureInfo.InvariantCulture,format,first);
  public void SetText(string format,float first,float second)=>text=string.Format(CultureInfo.InvariantCulture,format,first,second);
  public UnityEngine.Vector2 GetPreferredValues()=>GetPreferredValues(text);
  public UnityEngine.Vector2 GetPreferredValues(string content)
  {
   var literal=Regex.Replace(content,"<[^>]*>","");
   // Deliberately proportional digits; this is not native Bender pixel evidence.
   return new(literal.Sum(c=>c=='1'?4f:c=='8'?12f:9f),20f);
  }
 }
}
namespace EFT.CameraControl
{
 public class CameraManager {public static CameraManager Instance=new();public UnityEngine.Camera Camera=new();}
}
namespace ArenaMode.Config
{
 public class ValueEntry<T>(T value) {public T Value=value;}
 public static class Settings {public static ValueEntry<bool> CombineDamageNumbers=new(false);}
}
namespace ArenaMode.Hud
{
 internal static class ArenaUiTheme
 {
  internal static readonly UnityEngine.Color Text=new(1,1,1,1),Danger=new(1,.2f,.2f,1);
 }
 internal static class HudStyle
 {
  internal static readonly List<TMPro.TextMeshProUGUI> Created=new();
  internal static TMPro.TextMeshProUGUI CreateText(UnityEngine.RectTransform parent,string name,float size,TMPro.TextAlignmentOptions alignment)
  {var text=new TMPro.TextMeshProUGUI();Created.Add(text);return text;}
 }
}
