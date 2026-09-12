using ArenaMode.Session.Preview;
using System.Text.RegularExpressions;

int checks = 0;
void Check(bool value, string name)
{
    ++checks;
    if (!value) throw new InvalidOperationException(name);
}

// Production state, not a duplicate model of it. These orderings correspond to
// native awaited LoadBundles / PlayerBody.Init and screen-hide/failure paths.
var pending = new ArenaPreviewLifetimeState();
Check(pending.TryBeginLoad(), "first load admitted");
Check(!pending.TryBeginLoad(), "duplicate pending load rejected");
Check(!pending.DisposeAndClaimRelease(), "screen close defers release during body Init");
Check(pending.IsDisposed, "late completion cannot publish");
Check(!pending.DisposeAndClaimRelease(), "second close still cannot clear body under Init");
Check(pending.FinishLoadAndClaimRelease(), "settled continuation releases after close");
Check(!pending.FinishLoadAndClaimRelease(), "duplicate completion cannot release twice");
Check(!pending.DisposeAndClaimRelease(), "late close cannot release twice");
Check(!pending.TryBeginLoad(), "disposed instance cannot load again");

var ready = new ArenaPreviewLifetimeState();
Check(ready.TryBeginLoad(), "normal load starts");
Check(!ready.FinishLoadAndClaimRelease(), "finished visible preview retains resources");
Check(!ready.IsDisposed, "finished current preview can publish");
Check(!ready.TryBeginLoad(), "host transition cannot restart the completed load");
Check(ready.DisposeAndClaimRelease(), "ready preview closes immediately");
Check(!ready.DisposeAndClaimRelease(), "ready preview releases once");

var partial = new ArenaPreviewLifetimeState();
Check(partial.DisposeAndClaimRelease(), "constructor failure clears partial resources");
Check(!partial.TryBeginLoad(), "failed construction cannot begin async work");
Check(!partial.FinishLoadAndClaimRelease(), "failure after dispose cannot double-release");

var state = new ArenaPreviewSurfaceState();
var setup = new object();
var otherSetup = new object();
var map = new object();
var accept = new object();
state.Begin(setup);
Check(!state.Matches(otherSetup, true), "other setup cannot borrow Arena model");
Check(!state.Matches(setup, false), "ordinary PMC/Scav never match");
Check(!state.Matches(null, true), "missing settings never match");
Check(state.Attach(setup, map, false) == 0, "unselected attach rejected");
Check(state.Attach(otherSetup, map, true) == 0, "different flow attach rejected");
Check(state.Attach(setup, null, true) == 0, "missing native host rejected");
var mapLease = state.Attach(setup, map, true);
Check(state.IsCurrent(map, mapLease), "map owns accepted attachment");
var acceptLease = state.Attach(setup, accept, true);
Check(!state.IsCurrent(map, mapLease), "late map-hide cannot remove confirmation");
Check(!state.IsCurrent(map, acceptLease), "generation alone cannot borrow another host");
Check(state.IsCurrent(accept, acceptLease), "confirmation owns current attachment");
var secondAcceptLease = state.Attach(setup, accept, true);
Check(!state.IsCurrent(accept, acceptLease), "reused screen rejects its older callback");
Check(state.IsCurrent(accept, secondAcceptLease), "reattached screen owns new callback");
state.Invalidate();
Check(!state.IsCurrent(accept, secondAcceptLease), "hidden host callback invalidated");
Check(state.Matches(setup, true), "screen transition preserves setup ownership");
state.Begin(setup);
Check(!state.IsCurrent(accept, secondAcceptLease), "new flow invalidates same-settings old lease");
state.Clear();
Check(!state.Matches(setup, true), "selection clear removes flow ownership");
Check(state.Attach(setup, map, true) == 0, "clear blocks later attach");

var root = Path.GetFullPath(args[0]);
string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
var preview = Read("ArenaMode.Client/Session/ArenaCharacterPreview.cs");
var flow = Read("ArenaMode.Client/Session/Preview/ArenaPreviewFlow.cs");
var patches = Read("ArenaMode.Client/Session/Preview/ArenaPreviewFlowPatch.cs");
var side = Read("ArenaMode.Client/Session/ArenaSideSelectionPatch.cs");

// Small adapter contracts complement the executable state tests. Every absence
// check below is exercised against a known bad source control as well.
bool NoGlobalModelHook(string source) => !source.Contains("typeof(PlayerModelView)")
    && !source.Contains("typeof(PlayerModelLoader)") && !source.Contains("typeof(PlayerBody)");
Check(NoGlobalModelHook(patches), "hooks target native setup owners rather than every model");
Check(!NoGlobalModelHook(patches + " typeof(PlayerModelView)"), "global hook negative control");
bool NoProfileWrites(string source) => !Regex.IsMatch(source,
    @"\b(?:profile|Profile)\.(?:Inventory|Customization|Info)\b[^;\r\n]*=(?!=)");
Check(NoProfileWrites(preview + flow), "renderer and transfer never assign profile state");
Check(!NoProfileWrites(preview + "profile.Inventory = replacement;"), "profile write negative control");
Check(Regex.Matches(preview, @"await _loader\.CreatePlayerBody\(").Count == 1,
    "one actual body creation call per renderer");
Check(!flow.Contains("new ArenaCharacterPreview") && !flow.Contains("CreatePlayerBody("),
    "downstream transfer cannot create a second Arena body");
Check(flow.Contains("ReferenceEquals(preview, _acceptPreview)") && flow.Contains("!preview._isMainCharacter"),
    "confirmation restricts exact main-character widget");
Check(flow.Contains("!controller.IsReconnecting") && flow.Contains("player.AccountId == _localAccountId"),
    "reconnect excluded and local teammate-list click uses account identity");
Check(flow.Contains("State.IsCurrent(host, generation)"), "callbacks check host lease");
Check(side.IndexOf("KeepFromSide(__instance", StringComparison.Ordinal)
    < side.LastIndexOf("ArenaSideSelectionLayout.Restore(__instance)", StringComparison.Ordinal),
    "side close contains transfer before layout disposal");
Check(preview.Contains("_image.InitCamera(null)") && preview.Contains("_releaseBundles = null")
    && preview.Contains("_loader.Clear") && preview.Contains("Object.Destroy(_filterMaterial)"),
    "owned render resources have explicit cleanup adapters");
Check(preview.Contains("if (_lifetime.IsDisposed) return;")
    && Regex.Matches(preview, @"if \(_lifetime.IsDisposed\) return;").Count >= 3,
    "bundle/body await completions and dispose use production guard");

// Independently calculate representable world-space steps: this establishes a
// numerical source candidate, never claims the visible GPU jitter is measured.
float oldStep = MathF.BitIncrement(10000f) - 10000f;
float nearStep = MathF.BitIncrement(128f) - 128f;
Check(nearStep < oldStep / 32f, "near-origin setup improves float resolution by more than 32x");
Check(preview.Contains("new Vector3(128f, -128f, 128f)"), "renderer uses reviewed near-origin volume");
bool NoAnimatorTuning(string source) => !Regex.IsMatch(source, @"\.(?:speed|updateMode|cullingMode)\s*=");
Check(NoAnimatorTuning(preview), "no speculative animator speed/update mode changes");
Check(!NoAnimatorTuning(preview + "animator.speed = 0.1f;"), "animator tuning negative control");
Console.WriteLine($"{checks} preview assertions passed; float steps: old={oldStep:R} m, current={nearStep:R} m.");
Console.WriteLine("ARENA_CHARACTER_PREVIEW_CHECKS_PASSED");
