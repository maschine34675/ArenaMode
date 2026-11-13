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
    ArenaMapCatalog.AirPitId, ArenaMapCatalog.BowlId, ArenaMapCatalog.IcebergId,
    ArenaMapCatalog.AutoServiceId, ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId };
const string RefusalText = "Domination is not available for this map or area, or with a PitFireTeam squad.";
Need(ArenaPopulationPolicy.Domination == Mode && ArenaModeCatalog.DominationId == Mode,
    "client and server use the same independent Domination identity");
Need(ArenaModeCatalog.NormalizeId(Mode) == Mode, "Domination never falls back to Deathmatch");
Need(ArenaModeCatalog.All.Select(m => m.Id).SequenceEqual(new[] {
    "gungame", "deathmatch", "teamdeathmatch", "domination", "tactical", "zombie" }),
    "six playable rows preserve the existing mode order around the new team mode");
var definition = ArenaModeCatalog.All.Single(m => m.Id == Mode);
Need(definition.Label == "DOMINATION" && definition.Description.Length <= 80
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
    Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, Mode, true, out _, out var pftReason)
        && pftReason == RefusalText, "server authority refuses PFT even with a reviewed map: " + map);
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
    if (map == Day || map == Night || map == ArenaMapCatalog.LaboratoryId || map == ArenaMapCatalog.LabyrinthId)
        Need(!ArenaTeamBoxCatalog.Applies(map, Mode),
            "Factory Domination does not inherit ported-box bot spawn shields");
}
foreach (string map in ArenaMapCatalog.DefaultClientAllowlist.Split(',').Concat(new[] {
    "unknown", "", null, "arena_future", "factory4_evening", "woods", "Sandbox", "Interchange", "ReserveBase" }))
{
    if (supported.Contains(map)) continue;
    // Customs has no whole-map form; it is refused whole and admitted through its areas, both pinned below.
    if (map == ArenaMapCatalog.CustomsId) continue;
    Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, map, Mode, false, out _, out var reason)
        && !string.IsNullOrWhiteSpace(reason), "unreviewed objective map rejected by shared authority: " + map);
    Need(!ArenaAreaSelectionState.Create(map, Mode, false, ArenaAreaCatalog.FullMapId, 5).Available,
        "unreviewed objective map omitted by native mode-dependent discovery: " + map);
}
// Customs has no whole-map form in Arena: Domination there is refused whole (a bounded id continues into the
// area table, a whole-map id is never admitted by map id) and admitted only through its three surveyed
// areas, the same shared definitions every other mode uses. The refusal text is true for every refused map,
// area and squad.
Need(!ArenaAreaCatalog.TryResolve(ArenaAreaCatalog.FullMapId, ArenaMapCatalog.CustomsId, Mode, false, out _, out var customsReason)
    && customsReason == RefusalText, "whole Customs stays refused for Domination: " + customsReason);
foreach (string whole in new string[] { null, "", " " })
    Need(!ArenaAreaCatalog.TryResolve(whole, ArenaMapCatalog.CustomsId, Mode, false, out _, out _),
        "an empty area id means whole Customs and stays refused for Domination");
string[] customsAreas = { ArenaAreaCatalog.CustomsFactoryCenterTeamsId, ArenaAreaCatalog.CustomsFactoryEastPolygonId,
    ArenaAreaCatalog.CustomsYardId };
var customsDefinitions = new[] { ArenaAreaCatalog.CustomsFactoryCenterTeams, ArenaAreaCatalog.CustomsFactoryEastPolygon,
    ArenaAreaCatalog.CustomsYard };
for (int ai = 0; ai < customsAreas.Length; ai++)
{
    string customsArea = customsAreas[ai];
    var definition3 = customsDefinitions[ai];
    Need(ArenaAreaCatalog.TryResolve(customsArea, ArenaMapCatalog.CustomsId, Mode, false, out var admitted, out _)
        && ReferenceEquals(admitted, definition3) && admitted.TeamStarts != null && admitted.MaxTeamSize == 5,
        "Customs area admitted for Domination with the shared definition and its faction starts: " + customsArea);
    Need(ArenaAreaCatalog.TryResolve(customsArea, ArenaMapCatalog.CustomsId.ToUpperInvariant(), Mode.ToUpperInvariant(), false, out _, out _)
        && !ArenaAreaCatalog.TryResolve(customsArea.ToUpperInvariant(), ArenaMapCatalog.CustomsId, Mode, false, out _, out _),
        "map and mode identity are case-insensitive, the area identity is exact: " + customsArea);
    Need(!ArenaAreaCatalog.TryResolve(customsArea, ArenaMapCatalog.CustomsId, Mode, true, out _, out var pftAreaReason)
        && pftAreaReason == RefusalText, "a PitFireTeam squad never plays Domination in a Customs area: " + customsArea);
    // The area team size is negotiated exactly like Team Deathmatch (2..5, explicit), and never with a squad.
    foreach (int size in new[] { 2, 3, 4, 5 })
        Need(ArenaAreaCatalog.TryResolveTeamSize(definition3, Mode, false, size, out int resolved, out _) && resolved == size,
            "Domination negotiates the Customs area team size like Team Deathmatch: " + customsArea + " " + size);
    foreach (int invalid in new[] { 0, 1, 6, 12 })
        Need(!ArenaAreaCatalog.TryResolveTeamSize(definition3, Mode, false, invalid, out _, out _),
            "an unsupported Domination area size is refused: " + invalid);
    Need(!ArenaAreaCatalog.TryResolveTeamSize(definition3, Mode, false, null, out _, out _),
        "Domination in a team-start area requires an explicit size, as Team Deathmatch does");
    Need(!ArenaAreaCatalog.TryResolveTeamSize(definition3, Mode, true, 3, out _, out _)
        && ArenaAreaCatalog.TryResolveTeamSize(definition3, Mode, true, 0, out int squadSize, out _) && squadSize == 0,
        "a squad never negotiates a Domination area size");
    foreach (string other in new[] { "deathmatch", "gungame", "tactical", "zombie" })
        Need(!ArenaAreaCatalog.TryResolveTeamSize(definition3, other, false, 3, out _, out _),
            "the other modes still refuse an area team-size override: " + other);
    // The native area model: three area rows and no whole map, the size choice, the exact area.
    var proposal = ArenaAreaSelectionState.Create(ArenaMapCatalog.CustomsId, Mode, false, customsArea, 5, 3);
    Need(proposal.Available && proposal.Options.Select(o => o.Id).SequenceEqual(customsAreas)
        && proposal.SelectedId == customsArea && ReferenceEquals(proposal.SelectedArea, definition3),
        "the native area model offers exactly the three Customs areas under Domination: " + customsArea);
    Need(proposal.HasTeamSizeChoice && proposal.SelectedTeamSize == 3 && proposal.TeamSizeOptions.SequenceEqual(new[] { 2, 3, 4, 5 }),
        "Domination offers the area team-size choice 2..5 like Team Deathmatch: " + customsArea);
    Need(!ArenaAreaSelectionState.Create(ArenaMapCatalog.CustomsId, Mode, true, customsArea, 5, 3).Available,
        "the native area model offers no Customs choice to a PitFireTeam squad under Domination");
    // No participant override inside a bounded team mode, and no team boxes on Customs.
    Need(!ArenaTeamBoxCatalog.Applies(ArenaMapCatalog.CustomsId, Mode)
        && ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.CustomsId, Mode, true, false, null, out var none, out _) && none == null
        && !ArenaParticipantPolicy.TryResolve(ArenaMapCatalog.CustomsId, Mode, true, false, 10, out _, out _),
        "a bounded Domination match takes its team size from the area, never from a participant count");
}
Need(ArenaAreaSelectionState.Create(ArenaMapCatalog.CustomsId, Mode, false, ArenaAreaCatalog.FullMapId, 5).SelectedId == customsAreas[0],
    "the menu proposes the first Customs area when the saved choice is the whole map");
foreach (string retired in new[] { ArenaAreaCatalog.FactorySouthId, ArenaAreaCatalog.CustomsFactoryCenterId,
    ArenaAreaCatalog.CustomsFactoryCenterPolygonId, "customs-factory-east-polygon-v1", "unknown-area" })
    Need(!ArenaAreaCatalog.TryResolve(retired, ArenaMapCatalog.CustomsId, Mode, false, out _, out _),
        "a retired or unknown area never admits Domination on Customs: " + retired);
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
bool MapAvailable(string map, string mode) => ArenaAreaSelectionState.Create(map,
    mode, false, ArenaAreaCatalog.FullMapId, 5).Available;
// The Lab, The Labyrinth and Customs (through its areas) are Domination maps now: their rows stay. A map
// without a reviewed Domination layout is the row that goes.
Need(MapAvailable(ArenaMapCatalog.LaboratoryId, Mode) && MapAvailable(ArenaMapCatalog.LabyrinthId, Mode)
    && MapAvailable(ArenaMapCatalog.CustomsId, Mode),
    "The Lab, The Labyrinth and Customs rows stay visible under Domination");
row.Refresh(MapAvailable(ArenaMapCatalog.CustomsId, Mode), true);
Need(visible && deselections == 0, "switching to Domination keeps a Customs selection: its areas are admitted");
row.Refresh(MapAvailable("unknown-future-map", Mode), true);
Need(!visible && deselections == 1, "switching to Domination clears and hides an old selection of a map without a Domination layout");
row.Refresh(MapAvailable(ArenaMapCatalog.CustomsId, "teamdeathmatch"), false);
Need(visible, "switching back to TDM restores the same native Customs row");
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
Need(!reward.Contains("ArenaModeCatalog.DominationId"),
    "Domination is no longer excluded from the reward capture and submits like the other modes");
string rewardState = Read("ArenaMode.Client/Modes/ArenaRewardModeState.cs");
int dominationState = rewardState.IndexOf("private ArenaRewardModeState ReadDominationRewardState()", StringComparison.Ordinal);
Need(rewardState.Contains("IsDomination ? ReadDominationRewardState()") && dominationState >= 0
    && Ordered(rewardState, "Rule = \"KillsOnly\"", "Target = 0", dominationState)
    && Ordered(rewardState, "Target = 0", "Limit = 0", dominationState)
    && rewardState.Contains("if (elapsed > _dominationRewardElapsed) _dominationRewardElapsed = elapsed;", StringComparison.Ordinal)
    && rewardState.Contains("Elapsed = _dominationRewardElapsed", StringComparison.Ordinal),
    "Domination reward state is KillsOnly with no score target, no time limit and a monotonic reward clock");
string consent = Read("ArenaMode.Client/Session/ArenaRewardConsentView.cs");
Need(!consent.Contains("IsObjectivePrototype") && !consent.Contains("No profile rewards in Domination"),
    "the reward consent card no longer reports Domination as reward-free");
string rewardValidator = Read("ArenaMode.Server/Rewards/RewardResultValidator.cs");
Need(rewardValidator.Contains("or \"zombies\" or \"domination\")")
    && rewardValidator.Contains("\"domination\" => rule == RewardRoundRule.KillsOnly,")
    && rewardValidator.Contains("run.Mode is not (\"teamdeathmatch\" or \"domination\")"),
    "server reward validator admits Domination as a KillsOnly mode with the local forfeit");
string rewardCoordinator = Read("ArenaMode.Server/Rewards/RewardRuntimeCoordinator.cs");
Need(rewardCoordinator.Contains("\"domination\" => rule == RewardRoundRule.KillsOnly,"),
    "server reward begin admits Domination under the KillsOnly rule only");
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
