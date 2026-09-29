using ArenaMode.Combat;
using ArenaMode.Hud;
using ArenaMode.Modes;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
int checks=0;
void Need(bool value,string reason){if(!value)throw new Exception("CHECK: "+reason);checks++;}
var icons=Enum.GetValues<ArenaUiIcon>();
var assets=new ArenaUiAssets();
foreach(var icon in icons)
{
 var spec=ArenaUiAssets.Describe(icon);var texture=assets.Texture(icon);
 Need(texture!=null&&texture.width==spec.Width&&texture.height==spec.Height,"real embedded dimensions "+icon);
 Need(texture.MarkedNonReadable,"native texture finalised "+icon);
 Need(ReferenceEquals(texture,assets.Texture(icon))&&ReferenceEquals(assets.Sprite(icon),assets.Sprite(icon)),"cached identity "+icon);
 using var stream=typeof(ArenaUiAssets).Assembly.GetManifestResourceStream("ArenaMode.Ui."+spec.File);
 var bytes=new byte[stream.Length];stream.ReadExactly(bytes);
 Need(ArenaUiAssets.MatchesHeader(bytes,spec),"valid header "+icon);
 bytes[16]^=1;Need(!ArenaUiAssets.MatchesHeader(bytes,spec),"dimension drift rejected "+icon);
}
Need(assets.Texture(ArenaUiIcon.TeamPanel).Pixels.All(p=>p.r==p.g&&p.g==p.b&&p.a==90),"panel neutralization preserves alpha");
var held=assets.Sprite(ArenaUiIcon.Headshot);var heldTexture=held.texture;
assets.Dispose();assets.Dispose();Object.EndFrame();
Need(held.Destroyed&&heldTexture.Destroyed&&assets.Sprite(ArenaUiIcon.Headshot)==null,"disposal releases sprites and textures and prevents reload");
int attempts=0;using(var missing=new ArenaUiAssets(_=>{attempts++;return null;}))
{Need(missing.Texture(ArenaUiIcon.Kill)==null&&missing.Texture(ArenaUiIcon.Kill)==null&&attempts==1,"failure cached once");}
using(var broken=new ArenaUiAssets())
{Texture2D.FailNextDecode=true;int live=Object.Live<Texture2D>();Need(broken.Texture(ArenaUiIcon.Kill)==null,"decode fails softly");Object.EndFrame();Need(Object.Live<Texture2D>()==live,"failed decode disposes allocation");}
foreach(var damage in new[]{"HeavyBleeding","LightBleeding","GrenadeFragment","Melee","Flame","Explosion","Landmine","Undefined",null,"Bullet, GrenadeFragment"})
{
 var expected=damage is "HeavyBleeding" or "LightBleeding"?ArenaUiIcon.Bleeding:damage=="GrenadeFragment"?ArenaUiIcon.Grenade:damage=="Melee"?ArenaUiIcon.Melee:damage=="Flame"?ArenaUiIcon.Fire:ArenaUiIcon.Kill;
 Need(KillfeedIcon.Select(new(){DamageKind=damage,WeaponClass="sniperRifle",WeaponName="Pistol"})==expected,"final cause takes precedence "+damage);
}
foreach(var pair in new[]{("sniperRifle",ArenaUiIcon.Sniper),("assaultRifle",ArenaUiIcon.AssaultRifle),("assaultCarbine",ArenaUiIcon.Carbine),("marksmanRifle",ArenaUiIcon.Marksman),("machinegun",ArenaUiIcon.Machinegun),("shotgun",ArenaUiIcon.Shotgun),("pistol",ArenaUiIcon.Pistol),("smg",ArenaUiIcon.Smg),("unknown",ArenaUiIcon.Kill)})
 Need(KillfeedIcon.Select(new(){DamageKind="Bullet",WeaponClass=pair.Item1,WeaponName="wrong localized name"})==pair.Item2,"native class mapping "+pair.Item1);

var root=new GameObject("hud",typeof(RectTransform));var feed=new KillfeedView();feed.Create((RectTransform)root.transform);
Need(root.GetComponentsInChildren<CanvasGroup>().Length==6,"exactly six pooled groups");
Need(root.GetComponentsInChildren<Image>().All(i=>!i.raycastTarget),"all decoration passive");
var capture=new ArenaKill{KillerName="Long_attacker_name_123456789",VictimName="Long_victim_name_987654321",KillerRoleLabel="USEC",VictimRoleLabel="BEAR",WeaponName="HK 416A5",DamageKind="Bullet",WeaponClass="assaultRifle",Headshot=true,DistanceKnown=true,Distance=42};
for(int i=0;i<6;i++)feed.Push(capture);
int loadCount=Texture2D.LoadCalls;
capture.Headshot=false;capture.WeaponClass="unknown";feed.Push(capture);
var pool=root.GetComponentsInChildren<CanvasGroup>();
var reused=pool.Single(g=>g.gameObject.name=="Line0");
Need(!reused.gameObject.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Headshot").enabled,"pooled headshot is cleared");
Need(reused.gameObject.GetComponentsInChildren<Image>().Single(i=>i.gameObject.name=="Kill method").sprite.texture.name.EndsWith("kill.png"),"pooled method replaced by neutral fallback");
var text=reused.gameObject.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Single();
Need(text.text.Contains(capture.KillerName)&&text.text.Contains(capture.VictimName)&&text.text.Contains("42 m"),"feed retains complete text");
Need(text.MeasuredWidth==436f,"icon rail preserves original text width");
Need(Texture2D.LoadCalls==loadCount,"push reuses textures");
Preview.WriteFeed(root,Path.Combine(Path.GetDirectoryName(args.Length>0?args[0]:"artifacts/ui-assets-scoreboard.svg")!,"killfeed.svg"));
ArenaMode.Config.Settings.KillfeedOffsetX.Value=31;feed.Tick();Need(((RectTransform)reused.transform.parent).anchoredPosition.x==55,"offset moves complete feed");
Time.unscaledTime=5.25f;feed.Tick();Need(pool.All(g=>Math.Abs(g.alpha-.5f)<.001f),"pooled icon and text share fade");
Time.unscaledTime=6.1f;feed.Tick();Need(pool.All(g=>!g.gameObject.activeSelf),"all expired rows hidden");
var borrowed=root.GetComponentsInChildren<Image>().First(i=>i.sprite!=null).sprite;
Object.Destroy(root);Object.EndFrame();Need(borrowed.Destroyed&&borrowed.texture.Destroyed,"canvas destruction releases owned assets");

using var table=new ArenaScoreboardPresentation(new GUIStyle(),new GUIStyle());
var rows=new[]{new ArenaScoreboardEntry {DisplayName="Player with a very long readable display name",RoleLabel="PMC USEC",Side=ArenaScoreboardSide.Friendly,IsLocal=true,IsActive=true,Kills=23,Deaths=4,Assists=7,Headshots=12,Score=20450},
 new ArenaScoreboardEntry {DisplayName="Opponent",RoleLabel="PMC BEAR",Side=ArenaScoreboardSide.Opponent,Kills=3,Deaths=6,Assists=2,Headshots=1,Score=950}};
Color friendly=new(.2f,.65f,1f,1f),enemy=new(1f,.2f,.2f,1f),sentinel=new(.7f,.4f,.3f,.8f);GUI.color=sentinel;
foreach(float width in new[]{360f,560f,814f})
{
 Need(ArenaScoreboardPresentation.Columns(width).All(w=>w>0)&&Math.Abs(ArenaScoreboardPresentation.Columns(width).Sum()-width)<.01,"columns fit "+width);
 GUI.Reset(EventType.Layout);table.Draw("YOUR TEAM",rows,friendly,friendly,enemy,width);var reserved=GUILayoutUtility.Reserved.ToArray();
 Need(GUI.Labels.Count==0&&GUI.Images.Count==0,"layout has no draws");
 GUI.Reset(EventType.Repaint);table.Draw("YOUR TEAM",rows,friendly,friendly,enemy,width);
 Need(reserved.SequenceEqual(GUILayoutUtility.Reserved),"layout/repaint reserve same heights");
 Need(GUI.Labels.Select(l=>l.Text).Contains(rows[0].DisplayName+"  (YOU)")&&GUI.Labels.Any(l=>l.Text=="OUT"),"names and states retained");
 string[] expected={"23","4","7","12",20450.ToString("n0")};
 var values=GUI.Labels.Where(l=>expected.Contains(l.Text)).ToArray();
 Need(values.Select(l=>l.Text).SequenceEqual(expected),"K D A HS score order retained");
 Need(values.All(l=>l.Rect.x>=0&&l.Rect.x+l.Rect.width<=width),"counts inside table");
 Need(GUI.color==sentinel,"global tint restored");
}
GUI.Reset(EventType.Repaint);table.Draw("YOUR TEAM",rows.Take(1).ToArray(),friendly,friendly,enemy,814f);
table.Draw("OPPOSITION",rows.Skip(1).ToArray(),enemy,friendly,enemy,814f);
Preview.Write(args.Length>0?args[0]:"artifacts/ui-assets-scoreboard.svg");
using(var fallback=new ArenaScoreboardPresentation(new GUIStyle(),new GUIStyle(),new ArenaUiAssets(_=>null)))
{GUI.Reset(EventType.Repaint);fallback.Draw("TEAM",rows,friendly,friendly,enemy,814);Need(new[]{"K","D","A","HS","SCORE"}.All(s=>GUI.Labels.Any(l=>l.Text==s)),"missing asset textual headers survive");}
GUI.ThrowDraw=true;try{table.Draw("TEAM",rows,friendly,friendly,enemy,814);}catch(Exception){}finally{GUI.ThrowDraw=false;}
Need(GUI.color==sentinel,"global tint restored after render exception");
Console.WriteLine($"ARENA UI ASSET CONTRACTS PASSED ({checks} checks; native pixels still require EFT)");
