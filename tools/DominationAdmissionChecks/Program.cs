using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;

int checks = 0;
void Need(bool ok, string detail)
{
    checks++;
    if (!ok) throw new InvalidOperationException(detail);
}
const string Mode = "domination";
string Day = ArenaMapCatalog.FactoryDayId, Night = ArenaMapCatalog.FactoryNightId;
string[] supported = { Day, Night, ArenaMapCatalog.Bay5Id, ArenaMapCatalog.EquatorId,
    ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId };
Need(ArenaPopulationPolicy.Domination == Mode && ArenaModeCatalog.DominationId == Mode,
    "client and server use the same independent Domination identity");
Need(ArenaModeCatalog.NormalizeId(Mode) == Mode, "Domination never falls back to Deathmatch");
Need(ArenaModeCatalog.All.Select(m => m.Id).SequenceEqual(new[] {
    "gungame", "deathmatch", "teamdeathmatch", "domination", "tactical", "zombie" }),
    "six playable rows preserve the existing mode order around the new team mode");
var definition = ArenaModeCatalog.All.Single(m => m.Id == Mode);
Need(definition.Label == "DOMINATION (WIP)" && definition.Description.Length <= 80
    && definition.Description.EndsWith(".", StringComparison.Ordinal)
    && definition.Description.Contains("Choose the match limits", StringComparison.Ordinal)
    && !definition.Description.Contains("Factory", StringComparison.OrdinalIgnoreCase),
    "native Domination description points at the limit controls without a Factory-only claim");
Need(definition.Rules.Length > 0 && definition.Rules.Length <= 18
    && definition.Rules.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3
    && !definition.Rules.Any(char.IsDigit),
    "native objective fact fits 18 characters/3 words without a fixed point count: " + definition.Rules);
Need(definition.Respawn == "Instant", "Domination retains instant respawn in its common facts");
Need(ArenaModeCatalog.CanSelect(Mode, false) && !ArenaModeCatalog.CanSelect(Mode, true),
    "independent teams do not acquire PFT lifecycle semantics");
var selected = new ArenaModeSelectionState("deathmatch", false, _ => true);
int dominationIndex = Array.FindIndex(ArenaModeCatalog.All.ToArray(), m => m.Id == Mode);
Need(selected.TrySelectIndex(dominationIndex) && selected.SelectedMode == Mode,
    "native selection state retains the new exact mode ID");
selected.Synchronize(Mode, true);
Need(selected.SelectedMode == "tactical", "inviting a PFT squad repairs the independent mode");

foreach (string map in supported)
{
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, Mode, false, out var area, out _)
        && area == null, "reviewed map admits a full-map objective match: " + map);
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map.ToUpperInvariant(), Mode.ToUpperInvariant(), false,
        out _, out _), "case-insensitive map and mode identity remains accepted: " + map);
    var proposed = ArenaAreaSelectionState.Create(map, Mode, false, ArenaAreaCatalog.FullMapId, 5);
    Need(proposed.Available && proposed.Options.Count == 1 && proposed.SelectedId == ArenaAreaCatalog.FullMapId,
        "the native area model offers exactly the reviewed whole map: " + map);
    Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, Mode, true, out _, out _),
        "server authority refuses PFT even with a reviewed map: " + map);
    foreach (string bounded in new[] { ArenaAreaCatalog.FactorySouthId,
        ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaAreaCatalog.CustomsFactoryEastPolygonId,
        ArenaAreaCatalog.CustomsYardId, "unknown-area" })
        Need(!ArenaAreaCatalog.TryResolve(bounded, map, Mode, false, out _, out _),
            "the prototype never silently repairs an area at arming: " + bounded);
    Need(ArenaPopulationPolicy.SkipsProfilePreload(true, map, Mode, map)
        && !ArenaPopulationPolicy.SkipsProfilePreload(false, map, Mode, map)
        && !ArenaPopulationPolicy.SkipsProfilePreload(true, map, Mode, "different-map"),
        "the independent team preload exception retains armed exact-map boundaries");
    Need(ArenaParticipantPolicy.TryResolve(map, Mode, false, false, 10, out int? count, out _)
        && count == 10 && ArenaParticipantPolicy.IsTeam(Mode)
        && ArenaParticipantPolicy.MinimumTotal(Mode) == 4 && ArenaParticipantPolicy.Step(Mode) == 2,
        "Domination admits explicit equal teams including the player");
    foreach (int invalid in new[] { 0, 2, 3, 5, 25 })
        Need(!ArenaParticipantPolicy.TryResolve(map, Mode, false, false, invalid, out _, out _),
            "unequal or out-of-range team count is refused: " + invalid);
    if (map == Day || map == Night)
        Need(!ArenaTeamBoxCatalog.Applies(map, Mode),
            "Factory Domination does not inherit ported-box bot spawn shields");
}
foreach (string map in ArenaMapCatalog.DefaultClientAllowlist.Split(',').Concat(new[] {
    "unknown", "", null, "arena_future", "factory4_evening", "woods", "Sandbox", "Interchange", "ReserveBase" }))
{
    if (supported.Contains(map)) continue;
    Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, Mode, false, out _, out var reason)
        && !string.IsNullOrWhiteSpace(reason), "unreviewed objective map rejected by shared authority: " + map);
    Need(!ArenaAreaSelectionState.Create(map, Mode, false, ArenaAreaCatalog.FullMapId, 5).Available,
        "unreviewed objective map omitted by native mode-dependent discovery: " + map);
}
Need(ArenaPopulationPolicy.OwnsEntirePopulation(Mode)
    && ArenaPopulationPolicy.IsIndependentTeam(Mode)
    && !ArenaPopulationPolicy.IsIndependentTeam("deathmatch")
    && !ArenaPopulationPolicy.IsIndependentTeam("tactical")
    && !ArenaPopulationPolicy.IsIndependentTeam("zombie")
    && !ArenaPopulationPolicy.IsIndependentTeam("unknown"),
    "new team identity closes ambient spawns without broadening other contracts");
foreach (string existing in new[] { "gungame", "deathmatch", "teamdeathmatch", "tactical", "zombie" })
{
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.LaboratoryId,
        existing, false, out _, out _), "original Labs modes keep admission: " + existing);
    Need(ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, Day, existing,
        false, out _, out _), "original Factory modes keep admission: " + existing);
}

// A native Factory tile represents both time variants. Dynamic filtering must
// retain it when only the night variant is configured, and restore other maps.
Need(ArenaMapCatalog.CanSelectNativeLocation(Day, map => map == Night),
    "Factory night-only still has a clickable native day tile");
bool visible = true;
int deselections = 0;
var row = new ArenaConditionalMapView(true, v => visible = v, () => deselections++);
bool LabsAvailable(string mode) => ArenaAreaSelectionState.Create(ArenaMapCatalog.LaboratoryId,
    mode, false, ArenaAreaCatalog.FullMapId, 5).Available;
row.Refresh(LabsAvailable(Mode), true);
Need(!visible && deselections == 1, "switching to Domination clears and hides an old Labs selection");
row.Refresh(LabsAvailable("teamdeathmatch"), false);
Need(visible, "switching back to TDM restores the same native Labs row");
row.Refresh(false, false);
row.Close();
Need(visible, "leaving Arena restores the original native row visibility");

// Production virtual-side lease must support Domination without writing the
// profile, and reject stale token/native faction/world/body identity changes.
var game = new object();
var world = new object();
ArenaTeamSideSelection.Capture(Mode, "BEAR", true, "local", 17, "arm-17");
Need(ArenaTeamSideSelection.NativeUsec == true && ArenaTeamSideSelection.SelectedUsec == false
    && ArenaTeamSideSelection.TryResolve(17, "arm-17", game, world, "local", true, out bool usec) && !usec,
    "Domination virtual BEAR can retain an unchanged native USEC profile");
Need(!ArenaTeamSideSelection.TryResolve(18, "arm-17", game, world, "local", true, out _)
    && !ArenaTeamSideSelection.TryResolve(17, "old-arm", game, world, "local", true, out _)
    && !ArenaTeamSideSelection.TryResolve(17, "arm-17", new object(), world, "local", true, out _)
    && !ArenaTeamSideSelection.TryResolve(17, "arm-17", game, world, "local", false, out _),
    "existing epoch/token/world/native faction isolation remains exact");
ArenaTeamSideSelection.Capture("deathmatch", "USEC", true, "local", 18, "arm-18");
Need(!ArenaTeamSideSelection.SelectedUsec.HasValue
    && !ArenaTeamSideSelection.TryResolve(17, "arm-17", game, world, "local", true, out _),
    "switching away clears the virtual faction lease");

// Source contracts complement the executable shared checks. They explicitly
// do not claim native UI, Harmony hooks or server reward runtime acceptance.
string root = Environment.GetEnvironmentVariable("ARENA_REPO_ROOT")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string Read(string path) => File.ReadAllText(path == Environment.GetEnvironmentVariable("ARENA_ADMISSION_OVERRIDE_PATH")
    ? Environment.GetEnvironmentVariable("ARENA_ADMISSION_OVERRIDE_SOURCE") : Path.Combine(root, path));
bool Ordered(string source, string first, string second, int start = 0)
{
    if (start < 0) return false;
    int a = source.IndexOf(first, start, StringComparison.Ordinal);
    int b = source.IndexOf(second, start, StringComparison.Ordinal);
    return a >= 0 && b > a;
}
string reward = Read("ArenaMode.Client/Session/ArenaRewardCapture.cs");
Need(Ordered(reward, "match.Mode.Id == ArenaModeCatalog.DominationId", "new RewardBeginRequest"),
    "Domination reward opt-out precedes creating or posting a kill-based reward begin");
string menu = Read("ArenaMode.Client/Session/ArenaMenuSelection.cs");
int validate = menu.IndexOf("public static bool TryValidateAndFreezeRequest", StringComparison.Ordinal);
Need(menu.Contains("Bots.DominationObjectiveBridge.Available) return null;")
    && Ordered(menu, "reason = ModeAvailabilityRejection", "_freezeRoster?.Invoke", validate),
    "missing objective companion is rejected before roster freeze and token arming");
Need(Ordered(menu, "var eligibility = CurrentEligibility;", "_freezeRoster?.Invoke", validate),
    "final validation refreshes eligibility before freezing the roster");
string nativeStart = Read("ArenaMode.Client/Session/ArenaLocalRaidStartedPatch.cs");
Need(Ordered(nativeStart, "ArenaMenuSelection.TryValidateAndFreezeRequest", "ArenaServerClient.PostRaidStart"),
    "final availability validation still precedes the first server-arm request");
string clientArm = Read("ArenaMode.Client/Session/ArenaServerClient.cs");
int post = clientArm.IndexOf("internal static bool PostRaidStart", StringComparison.Ordinal);
Need(Ordered(clientArm, "ArenaAreaCatalog.TryResolve(areaId, map, mode, pitFireTeamTeam", "HasPendingArm = true;", post),
    "shared map guard precedes pending-arm publication and HTTP");
string server = Read("ArenaMode.Server/Services/ArenaSessionService.cs");
int arm = server.IndexOf("public string Arm(", StringComparison.Ordinal);
int prepare = server.IndexOf("public bool BeginArmPreparation(", StringComparison.Ordinal);
Need(Ordered(server, "ArenaAreaCatalog.TryResolve(areaId!, map, mode!, protectPitFireTeam", "_sessions[key] = new SessionEntry", arm)
    && Ordered(server, "ArenaAreaCatalog.TryResolve(areaId!, map, mode!, protectPitFireTeam", "PublishArmPreparationLocked(", prepare),
    "server direct and preparation arms retain the shared map guard before session publication");
string view = Read("ArenaMode.Client/Session/ArenaModeSelectionView.cs");
Need(!view.Contains("CreatePreviewRow(template, \"DOMINATION\"")
    && view.Contains("CreatePreviewRow(template, \"CAPTURE THE FLAG")
    && view.Contains("if (_selection == null || !CanSelectRow(index)) return;"),
    "Domination row is playable with an explicit dependency lock; CTF remains a preview");
Console.WriteLine($"Domination admission: {checks} checks passed.");
