using ArenaMode.Server.Patches;
using ArenaMode.Shared;
using SPTarkov.Server.Core.Models.Common;
using System.Reflection;
using System.Reflection.Emit;

namespace ArenaMode.Tests;

[Collection(ArenaContextSerialCollection.Name)]
public class PitFireTeamVersionTests
{
    [Theory]
    [InlineData("0.10.0", true, false)]
    [InlineData("0.10.0.0", true, false)]
    [InlineData("0.10.0.1", false, false)]
    [InlineData("0.10.1", false, false)]
    [InlineData("0.10.2", false, false)]
    [InlineData("0.10.3", true, false)]
    [InlineData("0.10.3.0", true, false)]
    [InlineData("0.10.3.1", true, true)]
    [InlineData("0.10.4", true, true)]
    [InlineData("0.11.0", true, true)]
    [InlineData("1.0.0", true, true)]
    [InlineData("15.3.2.4", true, true)]
    public void NumericAdmissionPreservesBaselinesAndWarnsOnlyForFutureBuilds(string text, bool admitted, bool warn)
    {
        var version = Version.Parse(text);
        Assert.Equal(admitted, PitFireTeamVersions.IsSupportedPluginVersion(text));
        Assert.Equal(admitted, PitFireTeamVersions.IsSupportedAssemblyVersion(version));
        Assert.Equal(admitted, PitFireTeamVersions.IsSupportedIdentity(text, version));
        Assert.Equal(warn, PitFireTeamVersions.IsNewerThanVerified(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0.10")]
    [InlineData("0.10.3.0.0")]
    [InlineData("0.10.3-beta")]
    [InlineData("beta-0.10.4")]
    [InlineData("0.10.-3")]
    [InlineData("0.10.+3")]
    [InlineData("0.10.999999999999")]
    public void MalformedAndReleaseTagStringsAreNotRuntimeVersions(string? text)
        => Assert.False(PitFireTeamVersions.IsSupportedPluginVersion(text));

    [Theory]
    [InlineData("0.10.0", "0.10.0.0", true)]
    [InlineData("0.10.3.0", "0.10.3", true)]
    [InlineData(" 0.10.4 ", "0.10.4.0", true)]
    [InlineData("0.10.4.7", "0.10.4.7", true)]
    [InlineData("0.10.4", "0.10.3", false)]
    [InlineData("0.10.0", "0.10.3", false)]
    [InlineData("0.10.4.7", "0.10.4.8", false)]
    [InlineData("0.10.4", "0.10.4.1", false)]
    [InlineData("0.10.4", "0.10", false)]
    public void PairValidationNormalizesOnlyMissingZeroRevision(string plugin, string assembly, bool expected)
        => Assert.Equal(expected, PitFireTeamVersions.IsSupportedIdentity(plugin, Version.Parse(assembly)));

    [Fact]
    public void MissingVersionsAreRejected()
    {
        Assert.False(PitFireTeamVersions.IsSupportedAssemblyVersion(null));
        Assert.False(PitFireTeamVersions.IsSupportedIdentity(null, new Version(0, 10, 3)));
        Assert.False(PitFireTeamVersions.IsSupportedIdentity("0.10.3", null));
        Assert.False(PitFireTeamVersions.IsNewerThanVerified(null));
    }

    [Theory]
    [InlineData("0.10.0.0", "missing", true, 0)]
    [InlineData("0.10.3.0", "valid", true, 1)]
    [InlineData("0.10.3.0", "missing", false, 0)]
    [InlineData("0.10.3", "valid", true, 1)]
    [InlineData("0.10.3", "missing", false, 0)]
    [InlineData("0.10.4.0", "valid", true, 1)]
    [InlineData("0.10.4.0", "missing", false, 0)]
    [InlineData("0.10.4.0", "return", false, 0)]
    [InlineData("0.10.4.0", "session", false, 0)]
    [InlineData("0.10.4.0", "static", false, 0)]
    [InlineData("0.10.3.1", "missing", false, 0)]
    [InlineData("1.0.0.0", "missing", false, 0)]
    public void FutureBuildCannotDropOrBreakProficiencyProtection(string versionText, string shape, bool accepted, int count)
    {
        // Execute the production table resolver used before any Harmony patches.
        // Only the real proficiency row is isolated; neither its introduction
        // threshold nor its required type/signature is copied into this fixture.
        var guard = typeof(PitFireTeamDynamicPersistencePatches);
        var table = (Array)guard.GetField("ManagementContracts", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        object contract = table.Cast<object>().Single(row =>
            (string)row.GetType().GetProperty("MethodName")!.GetValue(row)! == "SetTeammateProficiency");
        var contracts = Array.CreateInstance(contract.GetType(), 1);
        contracts.SetValue(contract, 0);
        var version = Version.Parse(versionText);
        Assert.True(PitFireTeamVersions.IsSupportedAssemblyVersion(version));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PftContractFixture_" + Guid.NewGuid().ToString("N")) { Version = version }, AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType(
            "pitTeam.Server.Services.FriendlyTeammateService", TypeAttributes.Public);
        if (shape != "missing")
        {
            Type returnType = shape == "return" ? typeof(string) : typeof(void);
            var attributes = MethodAttributes.Public | (shape == "static" ? MethodAttributes.Static : 0);
            var method = type.DefineMethod("SetTeammateProficiency", attributes, returnType,
                [shape == "session" ? typeof(string) : typeof(MongoId)]);
            var il = method.GetILGenerator();
            if (returnType != typeof(void)) il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
        type.CreateType();
        var resolved = new List<MethodInfo>();
        var compatible = guard.GetProperty(nameof(PitFireTeamDynamicPersistencePatches.IsCompatible))!;
        var problem = guard.GetProperty(nameof(PitFireTeamDynamicPersistencePatches.IncompatibilityReason))!;
        object? beforeCompatible = compatible.GetValue(null), beforeProblem = problem.GetValue(null);
        try
        {
            compatible.SetValue(null, true);
            var resolver = guard.GetMethod("TryResolveApplicableContracts", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool result = (bool)resolver.Invoke(null, [assembly, version, contracts, resolved])!;
            Assert.Equal(accepted, result);
            Assert.Equal(count, resolved.Count);
            if (!accepted)
            {
                Assert.False(PitFireTeamDynamicPersistencePatches.IsCompatible);
                Assert.False(PitFireTeamDynamicPersistencePatches.ProtectionReady);
                Assert.Contains("SetTeammateProficiency", PitFireTeamDynamicPersistencePatches.IncompatibilityReason);
            }
        }
        finally
        {
            compatible.SetValue(null, beforeCompatible);
            problem.SetValue(null, beforeProblem);
        }
    }
}
