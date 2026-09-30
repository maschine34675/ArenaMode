using ArenaMode.Combat;
using ArenaMode.Config;
using ArenaMode.Hud;
using EFT.CameraControl;
using TMPro;
using UnityEngine;

static class Program
{
 private static int _checks;
 private static DamageNumbersElement New(bool combine=true, float scale=1f)
 {
  Settings.CombineDamageNumbers.Value=combine;
  HudStyle.Created.Clear();Time.unscaledTime=0;CameraManager.Instance=new();
  var result=new DamageNumbersElement();result.Create(new RectTransform(),new Canvas{scaleFactor=scale});
  Check(HudStyle.Created.Count==12,"pool has exactly twelve reusable labels");
  return result;
 }
 private static TextMeshProUGUI[] Visible()=>HudStyle.Created.Where(t=>t.rectTransform.gameObject.activeSelf).ToArray();
 private static void Hit(DamageNumbersElement view,int victim,float time,float flesh=10f,float armor=0f,bool kill=false,bool headshot=false,float x=.5f)
 {
  Time.unscaledTime=time;
  view.Spawn(new ArenaDamage{VictimBodyId=victim,Time=time,BodyDamage=flesh,ArmorDamage=armor,Kill=kill,Headshot=headshot,WorldPos=new Vector3(x,.5f,1f)});
 }
 private static void Check(bool yes,string label)
 { _checks++;if(!yes)throw new InvalidOperationException("CHECK: "+label); }
 private static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 private static Rect TextRect(TextMeshProUGUI text)
 {var size=text.GetPreferredValues();var pos=text.rectTransform.anchoredPosition;return new(pos.x-size.x*.5f,pos.y-size.y*.5f,size.x,size.y);}

 private static void StaggeredRise(float scale, bool disableBeforeLaterHit)
 {
  var view=New(scale:scale);
  for(var i=0;i<=7;i++)Hit(view,7,i*.2f);
  view.Tick();
  var older=Visible().Single();var olderY=older.rectTransform.anchoredPosition.y;
  if(disableBeforeLaterHit)Settings.CombineDamageNumbers.Value=false;
  Hit(view,8,1.4f);view.Tick();
  var later=Visible().Single(t=>!ReferenceEquals(t,older));
  var laterY=later.rectTransform.anchoredPosition.y;
  foreach(var now in new[]{1.4f,1.6f,1.8f,2f,2.2f,2.4f,2.6f})
  {
   Time.unscaledTime=now;view.Tick();
   Check(Visible().Length==2,"staggered rise keeps both labels alive");
   Check(!TextRect(older).Overlaps(TextRect(later)),"staggered rise cannot enter a persistent burst");
   Check(Near(older.rectTransform.anchoredPosition.y,olderY),"staggered rise never shifts older lane");
   var expectedRise=Math.Clamp((now-1.4f)/1.25f,0f,1f)*30f/scale;
   Check(Near(later.rectTransform.anchoredPosition.y-laterY,expectedRise),"staggered rise keeps its assigned lane and canvas scale");
  }
 }

 public static void Main()
 {
  var view=New(false);
  Hit(view,7,0,10.4f,.4f);Hit(view,7,.1f,10.4f,.4f);
  Check(Visible().Length==2&&Visible().All(t=>t.text=="10"),"disabled preserves independent rounded hits");
  view.Tick();Check(Visible().All(t=>t.color.r==1f),"normal color preserved");

  view=New();
  Hit(view,7,0,10.4f,.4f);Hit(view,7,.1f,10.4f,.4f);
  Check(Visible().Length==1,"same body rapid hits combine");
  Check(Visible()[0].text=="21 <color=#00BFFF>(1)</color>","float flesh and armor sums round independently after accumulation");
  Hit(view,8,.15f,3f,7f);
  Check(Visible().Length==2,"different physical targets never combine");
  Check(Visible()[1].text=="3 <color=#00BFFF>(7)</color>","other target keeps its own body and armor");
  Hit(view,7,.2f,1f,3f);
  Check(Visible().Length==2&&Visible()[0].text=="22 <color=#00BFFF>(4)</color>","interleaved target retains only its own sums");

  view=New();
  Hit(view,7,0);Hit(view,7,.25f);Hit(view,7,.5f);
  Check(Visible().Length==1&&Visible()[0].text=="30","sliding quarter-second window includes boundary");
  Hit(view,7,.751f);
  Check(Visible().Length==2,"gap beyond quarter-second starts a new burst");
  Hit(view,7,.70f);
  Check(Visible().Length==3,"out-of-order event does not join a newer burst");

  view=New();
  Hit(view,0,0);Hit(view,0,.1f);
  Check(Visible().Length==2,"unknown body ids never combine");
  Hit(view,-15,.2f);Hit(view,-15,.3f);
  Check(Visible().Length==3&&Visible()[2].text=="20","negative Unity instance ids remain valid");

  view=New();
  Hit(view,7,0);Hit(view,7,.1f,kill:true);Hit(view,7,.2f);
  Check(Visible().Length==2&&Visible()[0].text=="20"&&Visible()[1].text=="10","killing hit seals the old physical life group");
  Hit(view,8,.21f);Check(Visible().Length==3,"replacement physical body starts a new group");

  view=New();
  Hit(view,7,0,headshot:true);Hit(view,7,.1f);
  Check(Visible()[0].color==ArenaUiTheme.Danger,"combined headshot emphasis persists");
  Hit(view,7,.2f,kill:true);
  Check(Visible()[0].color==ArenaUiTheme.Danger,"combined kill emphasizes the group");

  view=New();
  Hit(view,7,0);view.Tick();
  var first=Visible()[0];var firstPosition=first.rectTransform.anchoredPosition;
  Hit(view,7,.2f,x:.9f);view.Tick();
  Check(Near(first.rectTransform.anchoredPosition.x,firstPosition.x),"merged hits keep the initial impact anchor");
  Check(Near(first.rectTransform.anchoredPosition.y-firstPosition.y,4.8f),"merged hit keeps continuous rise");
  Check(Near(first.color.a,1f),"merged hit refreshes fade");
  Time.unscaledTime=1.3f;view.Tick();Check(Visible().Length==1,"merged hit extends visibility beyond original lifetime");
  Time.unscaledTime=1.451f;view.Tick();Check(Visible().Length==0,"merged lifetime expires after the latest hit");

  view=New();
  Hit(view,7,0);Time.unscaledTime=2f;
  view.Spawn(new ArenaDamage{VictimBodyId=7,Time=.1f,BodyDamage=10f});
  Check(Visible().Length==2,"expired row cannot merge before the next tick");
  view.Tick();Check(Visible().Length==0,"stale rows expire together");
  Hit(view,7,2.1f);view.Tick();Check(Visible().Length==1&&Visible()[0].text=="10","expired pooled row never resurrects old damage");

  view=New();
  Hit(view,7,0);Settings.CombineDamageNumbers.Value=false;view.Tick();Settings.CombineDamageNumbers.Value=true;
  Hit(view,7,.1f);
  Check(Visible().Length==2,"off-on toggle cannot revive a previously visible group");
  Settings.CombineDamageNumbers.Value=false;Hit(view,7,.15f);Settings.CombineDamageNumbers.Value=true;Hit(view,7,.2f);
  Check(Visible().Length==4,"hits while disabled cannot become a group on re-enable");

  view=New();
  for(var id=1;id<=12;id++)Hit(view,id,id*.001f);
  Hit(view,13,.02f);Hit(view,1,.03f);
  Check(Visible().Length==12&&HudStyle.Created.Count==12,"eviction keeps allocation and visibility bounded");
  Check(Visible().All(t=>t.text=="10"),"evicted target cannot reuse a stale group reference");
  Hit(view,13,.04f);
  Check(Visible().Count(t=>t.text=="20")==1,"surviving target still merges after eviction");

  view=New();
  Hit(view,7,0,1f);Hit(view,8,0,1f,x:.54f);view.Tick();
  var neighbors=Visible();var positions=neighbors.Select(t=>t.rectTransform.anchoredPosition).ToArray();
  Hit(view,7,0,888888f,888888f);view.Tick();
  Check(neighbors[0].text=="888889 <color=#00BFFF>(888888)</color>","widening updates both displayed channels");
  Check(neighbors.Select((t,i)=>Near(t.rectTransform.anchoredPosition.x,positions[i].x)&&Near(t.rectTransform.anchoredPosition.y,positions[i].y)).All(s=>s),"widening never moves already placed labels");
  Check(!TextRect(neighbors[0]).Overlaps(TextRect(neighbors[1])),"reserved burst width prevents growing labels from overlapping");
  Hit(view,9,0,5f,x:.52f);view.Tick();
  Check(!TextRect(Visible()[2]).Overlaps(TextRect(neighbors[0])),"new labels see reserved space of existing bursts");

  view=New();
  for(var id=1;id<=12;id++)Hit(view,id,0);
  view.Tick();var full=Visible();
  Check(full.Length==12,"all twelve colocated targets remain visible");
  Check(full.SelectMany((a,i)=>full.Skip(i+1).Select(b=>!TextRect(a).Overlaps(TextRect(b)))).All(s=>s),"full burst pool has enough vertical lanes");

  StaggeredRise(1f,false);StaggeredRise(2f,false);StaggeredRise(.5f,false);
  StaggeredRise(1f,true);StaggeredRise(2f,true);StaggeredRise(.5f,true);

  view=New(scale:2f);Hit(view,7,0);view.Tick();
  Check(Near(Visible()[0].rectTransform.anchoredPosition.x,250f),"projection retains canvas scale");
  CameraManager.Instance=null;view.Tick();Check(Visible().Length==0,"camera loss expires active groups");
  CameraManager.Instance=new();Hit(view,7,.1f);Check(Visible().Length==1&&Visible()[0].text=="10","camera recovery cannot revive stale sums");
  Console.WriteLine($"BURST DAMAGE CONTRACTS PASSED ({_checks} assertions)");
 }
}
