using ArenaMode.Hud;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.UI;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
Singleton<GameWorld>.Instance = new(); Singleton<AbstractGame>.Instance = new();
var team = new TacticalMode { _teamArena = true, _phase = TacticalMode.Phase.Staging };
var match = new ArenaMatch { Mode = team }; ArenaMatchService.Current = match;
var banner = new ArenaAssemblyBanner(); banner.Create(new RectTransform());
var root = GameObject.All["ArenaAssemblyBanner"];
Check(!root.activeSelf, "new banner starts hidden");
banner.Tick(match);
Check(root.activeSelf && HudStyle.Texts["AssemblyHeading"].text.Contains("TEAMMATES"), "staging teammates prominently shown");
team._teamEnemiesRequested = true;
for (int i = 0; i <= 5; i++)
{
    team._squad = Enumerable.Range(0, i).Select(_ => new object()).ToList();
    Time.unscaledTime += 10; banner.Tick(match);
    Check(root.activeSelf && HudStyle.Texts["AssemblyHeading"].text == "ASSEMBLING OPPONENTS"
        && HudStyle.Texts["AssemblyDetail"].text.Contains($"{i}/5"), "assembly persists through full arrival until combat readiness");
}
foreach (var phase in new[] { TacticalMode.Phase.Live, TacticalMode.Phase.Aftermath, TacticalMode.Phase.Buy, TacticalMode.Phase.Ended })
{
    team._phase = TacticalMode.Phase.Staging; Time.unscaledTime += 1; banner.Tick(match);
    team._phase = phase; banner.Tick(match);
    Check(!root.activeSelf, "combat/end hides immediately before refresh timer");
}
team._phase = TacticalMode.Phase.Rebinding; team.RoundNumber = 4; banner.Tick(match);
Check(root.activeSelf && HudStyle.Texts["AssemblyDetail"].text.Contains("ROUND 4"), "next round rebuilding has persistent status");
Action[] exclusions = [() => ArenaSession.IsActive = false, () => ArenaSession.Exact = false,
    () => ArenaMatchService.Current = new(), () => Singleton<GameWorld>.Instance = null,
    () => Singleton<AbstractGame>.Instance = null, () => team.IsComplete = true];
foreach (var exclude in exclusions)
{
    ArenaSession.IsActive = ArenaSession.Exact = true; ArenaMatchService.Current = match;
    Singleton<GameWorld>.Instance = new(); Singleton<AbstractGame>.Instance = new(); team.IsComplete = false;
    Time.unscaledTime++; banner.Tick(match); Check(root.activeSelf, "scope positive control visible");
    exclude(); banner.Tick(match); Check(!root.activeSelf, "invalid scope stays hidden");
}
ArenaSession.IsActive = ArenaSession.Exact = true; team.IsComplete = false;
var solo = new TacticalMode { _phase = TacticalMode.Phase.Live, _squadRequested = 3 };
match.Mode = solo; banner.Tick(match); Check(root.activeSelf, "solo asynchronous empty squad shows waiting");
solo._squad.Add(new object()); banner.Tick(match); Check(!root.activeSelf, "solo notice clears on first arrived opponent");
solo._squad.Clear(); solo._playerDown = true; banner.Tick(match); Check(!root.activeSelf, "solo death is not an assembly wait");
match.Mode = new object(); banner.Tick(match); Check(!root.activeSelf, "unrelated modes never show notice");
match.Mode = team; banner.Tick(match); banner.Tick(null); Check(!root.activeSelf, "detached match clears notice");
Check(!((Image)root.Components[typeof(Image)]).raycastTarget
    && HudStyle.Texts.Values.All(t => !t.raycastTarget), "banner does not intercept input");
Check(HudStyle.Texts["AssemblyHeading"].fontSize >= 28, "heading is materially larger than old 16pt subline");
Console.WriteLine($"ARENA_ASSEMBLY_CHECKS_PASSED ({checks} assertions)");
