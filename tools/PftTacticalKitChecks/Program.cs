using ArenaMode.Kits;
using ArenaMode.Session;
using ArenaMode.Compat;
using Comfort.Common;
using EFT;

int checks = 0;
void Need(bool condition, string why) { checks++; if (!condition) throw new Exception(why); }
Profile Setup()
{
    TacticalTeamKitEntry.Reset();
    ArenaSession.ArmedForNextRaid = true; ArenaSession.IsActive = false;
    ArenaSession.ArmEpoch = 10; ArenaSession.ArmedMode = "tactical"; ArenaSession.ActiveMode = null;
    ArenaSession.ArmedMap = "factory4_day"; ArenaSession.TacticalTeamOwnGearSelection = false;
    ArenaServerClient.HasPendingArm = true; ArenaServerClient.PendingArmToken = "token"; PitFireTeamCompat.TeamArenaActive = true;
    Singleton<GameWorld>.Instance = new(); Singleton<ItemFactory>.Instance = new(); Singleton<ObjectsFactory>.Instance = new();
    ObjectsFactory.DefaultCancellationToken = CancellationToken.None;
    TeamArenaEntryKit.Prepares = TeamArenaEntryKit.Commits = KitCatalog.Draws = 0;
    TeamArenaEntryKit.FailPrepare = TeamArenaEntryKit.FailCommit = false;
    KitCatalog.Kit = new();
    TacticalTeamKitEntry.Capture("tactical", true, false, 10);
    return new();
}
async Task Reject(Func<Task> action, string why)
{
    bool refused = false;
    try { await action(); } catch (InvalidOperationException) { refused = true; }
    Need(refused, why);
    Need(KitOpticPreloadGuard.Active == 0, why + " guard cleanup");
}
foreach (var mode in new[] { "tactical", "zombie", "gungame", "deathmatch", "teamdeathmatch", null })
foreach (bool team in new[] { false, true })
foreach (bool own in new[] { false, true })
{
    Setup(); TacticalTeamKitEntry.Capture(mode, team, own, 10);
    Need(TacticalTeamKitEntry.Selected == (mode == "tactical" && team && !own), "selection matrix");
    if (!TacticalTeamKitEntry.Selected)
    { Need(TacticalTeamKitEntry.CaptureRequest(null, null) == null, "non-kit mode does not inspect profile"); await TacticalTeamKitEntry.IssueAsync(null, () => throw new Exception()); }
}
var profile = Setup(); var original = profile.Inventory.Equipment;
Need(Singleton<GameWorld>.Instance.LocationId == null, "native pre-Run world has no location id yet");
var request = TacticalTeamKitEntry.CaptureRequest(profile, "factory4_day");
var pools = Singleton<ObjectsFactory>.Instance;
pools.Pending = new();
Task pending = TacticalTeamKitEntry.IssueAsync(request, () => profile);
Need(!pending.IsCompleted && TeamArenaEntryKit.Commits == 0 && ReferenceEquals(original, profile.Inventory.Equipment), "mandatory assets precede mutation");
Need(KitCatalog.Draws == 1 && pools.Loads == 1, "one kit and one required pool request");
pools.Pending.SetResult(true); await pending;
Need(TeamArenaEntryKit.Commits == 1 && !ReferenceEquals(original, profile.Inventory.Equipment), "issued once");
Need(request.Original == null, "old personal combat items not retained");
await Reject(() => TacticalTeamKitEntry.IssueAsync(request, () => profile), "duplicate continuation");
Need(TeamArenaEntryKit.Commits == 1, "duplicate has no second commit");
ArenaSession.ArmedForNextRaid = false; ArenaSession.IsActive = true; ArenaSession.ActiveMode = "tactical";
Singleton<GameWorld>.Instance.LocationId = "factory4_day"; // Assigned by native BaseLocalGame.Run.
TacticalTeamKitEntry.VerifyIssued(profile, Singleton<GameWorld>.Instance); Need(true, "actual issued root accepted at match start");
Singleton<GameWorld>.Instance.LocationId = "other";
await Reject(() => { TacticalTeamKitEntry.VerifyIssued(profile, Singleton<GameWorld>.Instance); return Task.CompletedTask; }, "active world must now match the requested map");
Singleton<GameWorld>.Instance.LocationId = "factory4_day";
await Reject(() => { TacticalTeamKitEntry.VerifyIssued(new Profile(), Singleton<GameWorld>.Instance); return Task.CompletedTask; }, "foreign profile cannot become entry snapshot");
profile.Inventory.Equipment = new();
await Reject(() => { TacticalTeamKitEntry.VerifyIssued(profile, Singleton<GameWorld>.Instance); return Task.CompletedTask; }, "changed root cannot become entry snapshot");

foreach (var change in new Action<Profile>[] {
    p => ArenaSession.ArmEpoch++, p => ArenaSession.ArmedForNextRaid = false,
    p => ArenaSession.IsActive = true, p => ArenaSession.ArmedMode = "zombie",
    p => ArenaSession.ArmedMap = "other",
    p => ArenaServerClient.HasPendingArm = false, p => PitFireTeamCompat.TeamArenaActive = false,
    p => ArenaServerClient.PendingArmToken = "new-arm",
    p => Singleton<GameWorld>.Instance = new(), p => Singleton<GameWorld>.Instance.MainPlayer = new(),
    p => Singleton<GameWorld>.Instance.LocationId = "other", p => p.Id = "foreign", p => p.AccountId = "foreign",
    p => p.Info.Side = EPlayerSide.Savage, p => p.Inventory = new(), p => p.Inventory.Equipment = new(),
    p => TacticalTeamKitEntry.Reset(), p => ObjectsFactory.DefaultCancellationToken = new CancellationToken(true),
    p => Singleton<ObjectsFactory>.Instance = new(), p => Singleton<ObjectsFactory>.Instance.Ready = false,
    p => Singleton<ItemFactory>.Instance = new()
})
{
    profile = Setup(); original = profile.Inventory.Equipment;
    request = TacticalTeamKitEntry.CaptureRequest(profile, "factory4_day"); pools = Singleton<ObjectsFactory>.Instance; pools.Pending = new();
    pending = TacticalTeamKitEntry.IssueAsync(request, () => profile);
    change(profile); pools.Pending.SetResult(true);
    await Reject(() => pending, "ownership drift during awaited loading");
    Need(TeamArenaEntryKit.Commits == 0, "drift never commits");
}
foreach (var map in new[] { null, "", "other" })
{
    profile = Setup();
    await Reject(() => { TacticalTeamKitEntry.CaptureRequest(profile, map); return Task.CompletedTask; }, "missing or wrong native request map");
    Need(TeamArenaEntryKit.Prepares == 0 && TeamArenaEntryKit.Commits == 0, "bad request map cannot issue");
}
foreach (string failure in new[] { "kit", "prepare", "pool", "empty", "unknown", "malformed", "commit" })
{
    profile = Setup(); original = profile.Inventory.Equipment;
    if (failure == "kit") KitCatalog.Kit = null;
    if (failure == "prepare") TeamArenaEntryKit.FailPrepare = true;
    if (failure == "pool") Singleton<ObjectsFactory>.Instance.Failure = new InvalidOperationException("pool fault");
    if (failure == "empty") foreach (var template in Singleton<ItemFactory>.Instance.ItemTemplates.Values) template.Resources = new[] { ResourceKey.EmptyResourceKey };
    if (failure == "malformed") Singleton<ItemFactory>.Instance.ItemTemplates[new("rifle")].Malformed = true;
    if (failure == "unknown") Singleton<ItemFactory>.Instance.ItemTemplates.Clear();
    if (failure == "commit") TeamArenaEntryKit.FailCommit = true;
    request = TacticalTeamKitEntry.CaptureRequest(profile, "factory4_day");
    await Reject(() => TacticalTeamKitEntry.IssueAsync(request, () => profile), failure);
    Need(ReferenceEquals(original, profile.Inventory.Equipment), failure + " no successful profile publication");
    Need(request.Issued == null, failure + " not falsely marked issued");
}
profile = Setup(); TacticalTeamKitEntry.Reset(); ArenaSession.IsActive = true;
await Reject(() => { TacticalTeamKitEntry.VerifyIssued(profile, Singleton<GameWorld>.Instance); return Task.CompletedTask; }, "missing required issuance fails closed");
ArenaSession.TacticalTeamOwnGearSelection = true;
TacticalTeamKitEntry.VerifyIssued(profile, null); Need(true, "own-gear route does not require issued-kit proof");
Console.WriteLine($"PFT_TACTICAL_KIT_CHECKS_OK {checks} assertions");
