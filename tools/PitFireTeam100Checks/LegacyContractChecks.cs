using System.Reflection;
using ArenaMode.Server.Patches;
using Mono.Cecil;

internal static class LegacyContractChecks
{
    internal static void Verify(string file)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(file);
        if (assembly.Name.Version is not { Major: 0, Minor: 10, Build: 0 or 3 }) throw new Exception("Unexpected legacy fixture.");
        Type production = typeof(PitFireTeamDynamicPersistencePatches);
        int contracts = 0;
        foreach (string table in new[] { "RaidPersistenceContracts", "ManagementContracts", "RecoveryContracts", "OperationBoundaryContracts",
                     "ModernRaidContracts", "ModernManagementContracts", "ModernRecoveryContracts", "ModernOperationContracts" })
        foreach (object contract in (Array)production.GetField(table, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)
        {
            if (!(bool)production.GetMethod("AppliesToInstalledBuild", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [contract, assembly.Name.Version])!) continue;
            T Value<T>(string name) => (T)contract.GetType().GetProperty(name)!.GetValue(contract)!;
            string typeName = Value<string>("TypeName"), methodName = Value<string>("MethodName"), expectedResult = Value<string>("ReturnTypeName");
            int session = Value<int>("SessionParameterIndex");
            var type = assembly.MainModule.GetType(typeName);
            var matches = type?.Methods.Where(method => method.Name == methodName && !method.IsStatic
                && method.IsPublic == Value<bool>("IsPublic")
                && method.ReturnType.FullName.Replace("ValueTask`1<", "ValueTask<") == expectedResult
                && method.Parameters.Count > session && method.Parameters[session].ParameterType.FullName == "SPTarkov.Server.Core.Models.Common.MongoId").ToArray();
            if (matches?.Length != 1) throw new Exception($"Legacy {assembly.Name.Version} mismatch: {typeName}.{methodName}");
            contracts++;
        }
        if (contracts != (assembly.Name.Version.Build == 3 ? 64 : 63)) throw new Exception("Legacy contract count drifted.");
        if (assembly.MainModule.GetType("pitTeam.Server.Services.FriendlyTeammateService").Methods.Count(method =>
                method.Name == "BackupFileBeforeRecovery" && method.IsPrivate && method.ReturnType.FullName == "System.Void"
                && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.String") != 1)
            throw new Exception("Legacy recovery backup boundary changed.");
        Console.WriteLine($"PFT_LEGACY_CONTRACTS_VERIFIED ({assembly.Name.Version}, {contracts} production-selected metadata contracts; no old-runtime gameplay claim)");
    }
}
