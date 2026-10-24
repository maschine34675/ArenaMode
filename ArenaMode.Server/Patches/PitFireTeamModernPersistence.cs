using System.Globalization;
using System.Reflection;
using ArenaMode.Server.Services;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;

namespace ArenaMode.Server.Patches;

public static partial class PitFireTeamDynamicPersistencePatches
{
    // This is a layout transition, not a claim of native gameplay acceptance.
    private static Version DatabaseVersion => new(1, 0, 0, 0);

    private static MethodContract Modern(string type, string name, string result = "System.Void",
        bool isPublic = true, int sessionIndex = 0) =>
        new("pitTeam.Server." + type, name, result, sessionIndex, isPublic, DatabaseVersion);

    private static readonly MethodContract[] ModernManagementContracts =
    [
        Modern("Services.FriendlyTeammateService", "CreateTeammate", "pitTeam.Server.Models.FriendlyTeammateBuyKitResponse"),
        Modern("Services.FriendlyTeammateService", "PrepareTeammateCreation", "pitTeam.Server.Models.FriendlyTeammateCreationPreview"),
        Modern("Services.FriendlyTeammateService", "CancelTeammateCreation"),
        Modern("Services.FriendlyTeammateService", "DeleteTeammateWithPayment", "pitTeam.Server.Models.FriendlyTeammateDeleteResponse"),
        Modern("Services.FriendlyTeammateService", "ApplyLoadoutManagementModeChange"),
        Modern("Services.FriendlyTeammateService", "InsureTeammateEquipment", "pitTeam.Server.Models.FriendlyTeammateDefaultEquipmentResponse"),
        Modern("Services.FriendlyTeammateService", "WipeProfile"),
        Modern("Services.FriendlySquadOnboardingService", "Complete", "pitTeam.Server.Models.FriendlySquadOnboardingResponse"),
    ];

    private static readonly MethodContract[] ModernRaidContracts =
    [
        Modern("Services.FriendlyScavCargoService", "BeginRaid"),
        Modern("Services.FriendlyScavCargoService", "Queue"),
        Modern("Services.FriendlyScavCargoService", "FinishRaid"),
        // Native co-op mail calls this outside the HTTP callback graph.
        Modern("Services.FriendlyScavCargoService", "PrepareCoopCargo", "pitTeam.Server.Services.FriendlyScavCargoService+CoopCargoDelivery", false),
        Modern("Services.FriendlyEncounterPenaltyService", "Record", "pitTeam.Shared.FriendlyEncounterPenaltyState"),
        Modern("Services.PmcKarmaService", "Record", "System.Threading.Tasks.ValueTask<pitTeam.Shared.PmcKarmaResult>"),
        Modern("Services.FriendlySquadOnboardingService", "ScreenRefreshed", "pitTeam.Server.Models.FriendlySquadOnboardingResponse"),
        Modern("Services.FriendlySquadOnboardingService", "DeliverWelcomeInvitation", "pitTeam.Server.Models.FriendlySquadOnboardingResponse"),
    ];

    private static readonly MethodContract[] ModernRecoveryContracts =
    [
        Modern("Services.FriendlyTeammateService", "RecoverTeammateCreation"),
        Modern("Services.FriendlyTeammateService", "RecoverTeammateDeletion"),
        Modern("Services.FriendlyTeammateService", "RestoreLegacyPresetSelection", isPublic: false),
        Modern("Services.FriendlyEncounterPenaltyService", "Get", "pitTeam.Shared.FriendlyEncounterPenaltyState"),
        // These can also be nested in deliberate postraid management. Only that
        // exact-session scope may authorize them during bounded finalization.
        Modern("Services.GiveGearInsuranceService", "Cancel"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "CancelGiftInsurance"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "ObserveMemberRemovalCourier"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "BeginRaid"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "CaptureGeneratedFollower"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "ObserveOutcomes"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "ObserveCourier"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "CompleteReports"),
        Modern("Services.FollowerInsuranceRaidDiagnostics", "EndRaid"),
        Modern("Services.FollowerInsuranceSettlementService", "TryTransferRetainedPlayerPolicies"),
        Modern("Services.FollowerInsuranceSettlementService", "TrySettle"),
        Modern("Services.FollowerInsuranceCourierTransferService", "CancelUnclaimed"),
        Modern("Services.FollowerInsuranceCourierTransferService", "ObserveDelivered"),
        Modern("Services.FollowerInsuranceCourierTransferService", "TryAuthorize"),
        Modern("Services.FollowerInsuranceCourierTransferService", "TryApplyClaimed"),
    ];

    private static readonly MethodContract[] ModernOperationContracts =
    [
        Modern("Callbacks.FriendlyTeammateCallbacks", "PrepareCreation", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 2),
        Modern("Callbacks.FriendlyTeammateCallbacks", "CancelCreation", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 2),
        Modern("Callbacks.FriendlyTeammateCallbacks", "InsureEquipment", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 2),
        Modern("Callbacks.FriendlyTeammateCallbacks", "SetProficiency", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 2),
        Modern("Callbacks.FriendlyPostRaidCallbacks", "StartLocalRaid", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 2),
        Modern("Callbacks.FriendlyPostRaidCallbacks", "GiveGearInsurance", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 1),
        Modern("Callbacks.FriendlyPostRaidCallbacks", "ScavCargo", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 1),
        Modern("Callbacks.FriendlyPostRaidCallbacks", "InsuranceReportsComplete", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 2),
        Modern("Callbacks.FriendlyPostRaidCallbacks", "InsuranceMailClaim", "System.Threading.Tasks.ValueTask<System.String>", sessionIndex: 1),
    ];

    private sealed record ModernDatabaseContract(Type Database, FieldInfo ProfileId, PropertyInfo DatabasePath,
        MethodInfo Initialize, MethodInfo Read, MethodInfo Open, MethodInfo[] Mutations, MethodInfo Backup,
        Type ConnectionString, ConstructorInfo ConnectionConstructor, ConstructorInfo LiteConstructor,
        PropertyInfo Filename, PropertyInfo ReadOnly, PropertyInfo Collation, ConstructorInfo CollationConstructor,
        PropertyInfo UserVersion, MethodInfo CollectionExists);

    private static ModernDatabaseContract? _modernDatabase;

    private static bool TryResolveModernDatabase(Assembly assembly, out ModernDatabaseContract? contract)
    {
        contract = null;
        try
        {
            Type database = assembly.GetType("pitTeam.Server.Persistence.TeammateDatabase", true)!;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            MethodInfo Require(Type type, string name, Type result, params Type[] parameters)
            {
                MethodInfo? method = type.GetMethod(name, all, null, parameters, null);
                return method != null && method.ReturnType == result ? method : throw new MissingMethodException(type.FullName, name);
            }
            FieldInfo id = database.GetField("profileId", all) ?? throw new MissingFieldException(database.FullName, "profileId");
            PropertyInfo path = database.GetProperty("DatabasePath", all)!;
            if (id.FieldType != typeof(string) || path?.PropertyType != typeof(string)) throw new InvalidDataException("Database identity changed.");
            Type documents = typeof(IReadOnlyDictionary<string, string>);
            MethodInfo initialize = Require(database, "Initialize", typeof(int), typeof(Func<IReadOnlyDictionary<string, string>>));
            MethodInfo read = Require(database, "Read", typeof(string), typeof(string));
            MethodInfo open = database.GetMethod("Open", all, null, [typeof(bool)], null) ?? throw new MissingMethodException(database.FullName, "Open");
            Type lite = open.ReturnType;
            if (lite.FullName != "LiteDB.LiteDatabase") throw new InvalidDataException("Database engine changed.");
            Type connection = lite.Assembly.GetType("LiteDB.ConnectionString", true)!;
            Type collation = lite.Assembly.GetType("LiteDB.Collation", true)!;
            Type mapper = lite.Assembly.GetType("LiteDB.BsonMapper", true)!;
            PropertyInfo Property(Type type, string name, Type value) =>
                type.GetProperty(name) is { CanWrite: true } property && property.PropertyType == value
                    ? property : throw new MissingMemberException(type.FullName, name);
            Type storage = assembly.GetType("pitTeam.Server.Services.FriendlyTeammateStorage", true)!;
            contract = new(database, id, path, initialize, read, open,
                [Require(database, "WriteBatch", typeof(void), documents, typeof(bool)),
                 Require(database, "DeleteTeammate", typeof(bool), typeof(int), documents),
                 Require(database, "Wipe", typeof(void))],
                Require(storage, "TryBackupImportedDirectory", typeof(void), database),
                connection, connection.GetConstructor(Type.EmptyTypes)!,
                lite.GetConstructor([connection, mapper])!,
                Property(connection, "Filename", typeof(string)), Property(connection, "ReadOnly", typeof(bool)),
                Property(connection, "Collation", collation), collation.GetConstructor([typeof(int), typeof(CompareOptions)])!,
                lite.GetProperty("UserVersion")!, Require(lite, "CollectionExists", typeof(bool), typeof(string)));
            // LiteDB's optional mapper type is reflected, never introduced into
            // Arena metadata or loaded when the optional mod is absent.
            if (contract.ConnectionConstructor == null || contract.LiteConstructor == null || contract.CollationConstructor == null
                || contract.UserVersion?.PropertyType != typeof(int)) throw new InvalidDataException("Read-only database contract changed.");
            return true;
        }
        catch (Exception ex)
        {
            RejectContract("pitTeam.Server.Persistence.TeammateDatabase", "read-only persistence boundaries: " + ex.Message);
            return false;
        }
    }

    private static void PatchModernDatabase(Harmony harmony, ModernDatabaseContract contract)
    {
        _modernDatabase = contract;
        MethodInfo Method(string name) => typeof(PitFireTeamDynamicPersistencePatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (MethodInfo target in contract.Mutations.Append(contract.Initialize).Append(contract.Backup).Append(contract.Open))
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PitFireTeamOperationBoundaryPatch), nameof(PitFireTeamOperationBoundaryPatch.Prefix)),
                finalizer: new HarmonyMethod(typeof(PitFireTeamOperationBoundaryPatch), nameof(PitFireTeamOperationBoundaryPatch.Finalizer)));
        foreach (MethodInfo target in contract.Mutations)
            harmony.Patch(target, prefix: new HarmonyMethod(Method(nameof(DatabaseMutationPrefix))));
        harmony.Patch(contract.Initialize, prefix: new HarmonyMethod(Method(nameof(DatabaseInitializePrefix))));
        harmony.Patch(contract.Backup, prefix: new HarmonyMethod(Method(nameof(DatabaseBackupPrefix))));
        harmony.Patch(contract.Open, prefix: new HarmonyMethod(Method(nameof(DatabaseOpenPrefix)).MakeGenericMethod(contract.Open.ReturnType)));
    }

    private static bool DatabaseIsProtected(object database)
    {
        var contract = _modernDatabase ?? throw new InvalidOperationException("PFT database contract is not available.");
        string? id = contract.ProfileId.GetValue(database) as string;
        if (id?.Length != 24 || !id.All(char.IsAsciiHexDigit)) throw new InvalidDataException("PFT database has an invalid profile identity.");
        return ArenaContext.Sessions?.IsExternalProfileWriteProtected(new MongoId(id)) == true
               && !PitFireTeamManagementScope.Authorizes(id);
    }

    private static bool DatabaseMutationPrefix(object __instance) => !DatabaseIsProtected(__instance);
    private static bool DatabaseBackupPrefix(object __0) => !DatabaseIsProtected(__0);

    private static bool DatabaseInitializePrefix(object __instance, ref int __result)
    {
        if (!DatabaseIsProtected(__instance)) return true;
        var contract = _modernDatabase!;
        // Do not let GetDatabase cache an uninitialized owner after a skipped
        // import. Validate the already-completed schema/receipt without writing.
        using (var database = (IDisposable)OpenProtectedDatabase(__instance))
        {
            if ((int)contract.UserVersion.GetValue(database)! != 1
                || !(bool)contract.CollectionExists.Invoke(database, ["documents"])!)
                throw new InvalidDataException("PitFireTeam database must be initialized before entering Arena.");
        }
        if (contract.Read.Invoke(__instance, ["migration/legacy-json-v1"]) is not string)
            throw new InvalidDataException("PitFireTeam database migration must finish before entering Arena.");
        __result = 0;
        return false;
    }

    private static bool DatabaseOpenPrefix<T>(object __instance, ref T __result)
    {
        if (!DatabaseIsProtected(__instance)) return true;
        __result = (T)OpenProtectedDatabase(__instance);
        return false;
    }

    private static object OpenProtectedDatabase(object owner)
    {
        var contract = _modernDatabase!;
        string path = (string)contract.DatabasePath.GetValue(owner)!;
        if (!File.Exists(path)) throw new FileNotFoundException("PitFireTeam database must exist before entering Arena; protected reads cannot create it.", path);
        object connection = contract.ConnectionConstructor.Invoke(null);
        contract.Filename.SetValue(connection, path);
        contract.ReadOnly.SetValue(connection, true);
        contract.Collation.SetValue(connection, contract.CollationConstructor.Invoke([CultureInfo.InvariantCulture.LCID, CompareOptions.Ordinal]));
        return contract.LiteConstructor.Invoke([connection, null]);
    }

    private static void PatchAsyncTransaction(Harmony harmony, MethodInfo target)
    {
        Type patch = typeof(PitFireTeamAsyncTransactionPatch<>).MakeGenericType(target.ReturnType.GetGenericArguments()[0]);
        harmony.Patch(target, prefix: new HarmonyMethod(patch, "Prefix"), postfix: new HarmonyMethod(patch, "Postfix"), finalizer: new HarmonyMethod(patch, "Finalizer"));
    }
}

// PFT 1.0 karma writes and reads back the profile after an await. A synchronous
// Harmony finalizer cannot release its protection lease at the first yield.
internal static class PitFireTeamAsyncTransactionPatch<T>
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix(out IDisposable __state) => __state = PitFireTeamAsyncSideEffectGate.Enter();
    public static void Postfix(ref ValueTask<T> __result, IDisposable? __state)
    {
        if (__state != null) __result = AwaitAndRelease(__result, __state);
    }
    public static Exception? Finalizer(Exception? __exception, IDisposable? __state)
    {
        if (__exception != null) __state?.Dispose();
        return __exception;
    }
    private static async ValueTask<T> AwaitAndRelease(ValueTask<T> original, IDisposable lease)
    {
        try { return await original.ConfigureAwait(false); }
        finally { lease.Dispose(); }
    }
}
