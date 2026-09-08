using ArenaMode.Session;
using ArenaMode.Shared;
using UnityEngine;
using UnityEngine.UI;

int checks=0;
void Need(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
ArenaAreaSelectionState State(string id,int size=2,string map=ArenaMapCatalog.CustomsId)
    =>ArenaAreaSelectionState.Create(map,"teamdeathmatch",false,id,7,size);
const string teams=ArenaAreaCatalog.CustomsFactoryCenterTeamsId;
const string contour=ArenaAreaCatalog.CustomsFactoryCenterPolygonId;
var screen=new GameObject("native screen");
var view=new ArenaAreaSelectionView();
int calls=0,selected=2;
bool accept=true;
view.Show(screen.transform,null,_=>true,(area,size)=>
{
    Need(area==teams,"size callback includes exact owning area");calls++;
    if(accept){selected=size;view.Refresh(State(teams,size),false);}
    return accept;
});
view.Refresh(State(teams),false);
Button[] Buttons()=>view.Root.GetComponentsInChildren<Button>(true);
Button Size(int size)=>Buttons().Single(b=>b.name=="Arena Team Size "+size);
Need(Buttons().Length==7,"three area rows and four team buttons are materialized");
var rootRect=(RectTransform)view.Root.transform;
Need(-rootRect.anchoredPosition.y>=668&&-rootRect.anchoredPosition.y+rootRect.sizeDelta.y<=892,
    "largest supported panel remains below mode panel and above native bottom controls");
foreach(var button in Buttons())
{
    var r=(RectTransform)button.transform;
    Need(r.anchoredPosition.x>=0&&r.anchoredPosition.x+r.sizeDelta.x<=rootRect.sizeDelta.x
        &&-r.anchoredPosition.y>=0&&-r.anchoredPosition.y+r.sizeDelta.y<=rootRect.sizeDelta.y,
        "all owned buttons remain inside panel");
}
var oldButtons=Buttons();
view.Refresh(State(teams,2),false);
Need(oldButtons.SequenceEqual(Buttons()),"periodic identical refresh preserves native button identity");
Size(5).onClick.Invoke();
Need(calls==1&&selected==5,"real button routes selected count through owner callback");
Need(Size(5).transform.Children.Single(t=>t.gameObject.name=="Selected").gameObject.activeSelf,
    "acknowledged selected size owns the only marker");
accept=false;Size(3).onClick.Invoke();
Need(calls==2&&selected==5&&!Size(3).transform.Children.Single(t=>t.gameObject.name=="Selected").gameObject.activeSelf,
    "refused size change does not create a selected marker");
var retired=Size(4).onClick.Snapshot();
view.Refresh(State(contour),false);
Need(Buttons().Length==3,"legacy area removes size row entirely");
foreach(var callback in retired)callback();
Need(calls==2,"retired row callback cannot affect replacement legacy state");
view.Refresh(State(teams,99),false);
Need(Buttons().Where(b=>b.name.StartsWith("Arena Team Size")).All(b=>!b.transform.Children.Single(t=>t.gameObject.name=="Selected").gameObject.activeSelf),
    "invalid saved count is displayed unselected, never clamped to a fabricated choice");
retired=Size(2).onClick.Snapshot();
view.Close();
foreach(var callback in retired)callback();
Need(calls==2&&view.Root==null,"closed panel rejects retained row delegates");
int replacementCalls=0;
view.Show(screen.transform,null,_=>true,(area,size)=>{replacementCalls++;return true;});
view.Refresh(State(teams),false);
foreach(var callback in retired)callback();
Need(calls==2&&replacementCalls==0,"same-view Close/Show replacement cannot inherit old row ownership");
view.Refresh(State(ArenaAreaCatalog.FullMapId,map:ArenaMapCatalog.FactoryDayId),false);
Need(Buttons().Length==2,"whole map Factory retains only its existing two area choices");
view.Close();
Console.WriteLine($"ARENA_TEAM_AREA_VIEW_CHECKS_PASSED ({checks} assertions)");
