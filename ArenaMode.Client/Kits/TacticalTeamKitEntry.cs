using ArenaMode.Compat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Threading.Tasks;

namespace ArenaMode.Kits;

/// <summary>
/// One optional PMC kit, before native LocalGame.Create/Run constructs the PFT
/// leader. The existing team snapshot then captures that issued entry loadout;
/// neither live leader replacement nor companion inventory changes are needed.
/// </summary>
internal static class TacticalTeamKitEntry
{
    internal sealed class Request
    {
        internal int Epoch;
        internal Profile Profile;
        internal string ProfileId;
        internal string AccountId;
        internal string ArmToken;
        internal GameWorld World;
        internal Inventory Inventory;
        internal InventoryEquipment Original;
        internal InventoryEquipment Issued;
        internal bool Started;
    }

    private static Request _request;
    internal static bool Selected => _request != null;

    internal static void Capture(string mode, bool protectedTeam, bool ownGear, int epoch)
    {
        _request = protectedTeam && !ownGear
            && string.Equals(mode?.Trim(), Shared.ArenaPopulationPolicy.Tactical, StringComparison.OrdinalIgnoreCase)
            ? new Request { Epoch = epoch } : null;
    }

    internal static void Reset() => _request = null;

    internal static Request CaptureRequest(Profile profile)
    {
        var request = _request;
        if (request == null) return null;
        if (request.Profile != null) throw new InvalidOperationException("Tactical kit entry was requested twice.");
        request.Profile = profile ?? throw new InvalidOperationException("Tactical kit entry has no PMC profile.");
        request.ProfileId = profile.Id;
        request.AccountId = profile.AccountId;
        request.ArmToken = ArenaServerClient.PendingArmToken;
        request.Inventory = profile.Inventory;
        request.Original = profile.Inventory?.Equipment;
        request.World = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        RequireCurrent(request, profile);
        return request;
    }

    internal static async Task IssueAsync(Request request, Func<Profile> currentProfile)
    {
        if (request == null) return;
        RequireCurrent(request, currentProfile());
        if (request.Started) throw new InvalidOperationException("Tactical kit entry was already started.");
        request.Started = true;
        var kit = KitCatalog.GetBaseKit()
            ?? throw new InvalidOperationException("Tactical team has no buildable Arena kit.");
        var equipment = TeamArenaEntryKit.Prepare(request.Profile, kit);
        await KitBundlePreloader.PreloadEntryEquipment(equipment, () => IsCurrent(request, currentProfile()));
        RequireCurrent(request, currentProfile());
        TeamArenaEntryKit.Commit(request.Profile, request.Original, equipment);
        if (!ReferenceEquals(request.Profile.Inventory.Equipment, equipment))
            throw new InvalidOperationException("Tactical kit entry did not retain its prepared equipment.");
        request.Issued = equipment;
        request.Original = null; // Never retain the replaced personal combat gear for the whole match.
        Plugin.ModLogger.LogInfo($"Tactical team: issued Arena kit '{kit.Name ?? kit.Id}' to the PMC; companion gear is unchanged.");
    }

    private static bool IsCurrent(Request request, Profile profile)
        => ReferenceEquals(request, _request)
           && ArenaSession.ArmedForNextRaid && !ArenaSession.IsActive
           && ArenaSession.ArmEpoch == request.Epoch
           && string.Equals(ArenaSession.ArmedMode, Shared.ArenaPopulationPolicy.Tactical, StringComparison.OrdinalIgnoreCase)
           && ArenaServerClient.HasPendingArm && !string.IsNullOrWhiteSpace(request.ArmToken)
           && request.ArmToken == ArenaServerClient.PendingArmToken && PitFireTeamCompat.TeamArenaActive
           && request.World != null && Singleton<GameWorld>.Instantiated
           && ReferenceEquals(Singleton<GameWorld>.Instance, request.World)
           && request.World.MainPlayer == null
           && string.Equals(request.World.LocationId, ArenaSession.ArmedMap, StringComparison.OrdinalIgnoreCase)
           && ReferenceEquals(profile, request.Profile) && profile != null
           && !string.IsNullOrWhiteSpace(request.ProfileId) && profile.Id == request.ProfileId
           && !string.IsNullOrWhiteSpace(request.AccountId) && profile.AccountId == request.AccountId
           && profile.Info != null && profile.Info.Side != EPlayerSide.Savage
           && ReferenceEquals(profile.Inventory, request.Inventory) && request.Original != null
           && ReferenceEquals(profile.Inventory.Equipment, request.Original);

    private static void RequireCurrent(Request request, Profile profile)
    {
        if (!IsCurrent(request, profile))
            throw new InvalidOperationException("Tactical kit entry lost its exact protected arm, map, profile or pre-body ownership.");
    }

    internal static void VerifyIssued(Profile profile, GameWorld world)
    {
        var request = _request;
        if (ArenaSession.TacticalTeamOwnGearSelection) return;
        if (request == null) throw new InvalidOperationException("Tactical team has no issued kit request.");
        if (!ArenaSession.IsActive || ArenaSession.ActiveMode != Shared.ArenaPopulationPolicy.Tactical
            || !PitFireTeamCompat.TeamArenaActive || request.Epoch != ArenaSession.ArmEpoch
            || !ReferenceEquals(request.World, world) || !ReferenceEquals(request.Profile, profile)
            || request.Issued == null || !ReferenceEquals(profile?.Inventory, request.Inventory)
            || !ReferenceEquals(profile.Inventory.Equipment, request.Issued))
            throw new InvalidOperationException("Tactical team could not verify its issued PMC entry kit.");
    }
}
