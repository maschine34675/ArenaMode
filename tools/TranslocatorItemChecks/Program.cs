using System.Reflection;
using ArenaMode.Server.Services;
using ArenaMode.Shared;
using ArenaMode.Tests;
using Xunit;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("FAIL: " + name);
    }

    private static int Main(string[] args)
    {
        try
        {
            var facts = typeof(TranslocatorRegistrationTests).GetMethods()
                .Where(method => method.GetCustomAttribute<FactAttribute>() != null).OrderBy(method => method.Name).ToArray();
            foreach (var fact in facts)
            {
                try { fact.Invoke(new TranslocatorRegistrationTests(), null); }
                catch (TargetInvocationException error) { throw new InvalidOperationException("FAIL: " + fact.Name, error.InnerException); }
            }
            NativeContract(args[0], args[1]);
            ConfirmationContract();
            Console.WriteLine($"TRANSLOCATOR_ITEM_PRODUCTION_PASSED unitCases={facts.Length} harnessAssertions={_checks}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void ConfirmationContract()
    {
        var items = TranslocatorRegistrationTests.Fresh();
        Check(TranslocatorRegistrationTests.Register(items), "server capability registration control");
        Check(ConfirmationFixture.ServerCapability(true), "confirmed server capability available");
        Check(!ConfirmationFixture.ServerCapability(false), "unconfirmed server cannot grant capability");
        items[TranslocatorRegistrationTests.DeviceId].Properties!.CanRequireOnRagfair = true;
        Check(!ConfirmationFixture.ServerCapability(true), "changed server object cannot grant capability");

        foreach (var value in new[] { "true", "false", "\"true\"", "1", "null", "missing" })
        {
            ConfirmationFixture.Arrange("round-a", "native-a");
            RequestHandler.Response = value == "missing" ? "{\"confirmed\":true}" : "{\"confirmed\":true,\"translocatorAvailable\":" + value + "}";
            Check(ConfirmationFixture.ConfirmRaidStarted(), "valid confirmation still completes " + value);
            Check(ConfirmationFixture.TranslocatorAvailableForConfirmedRaid == (value == "true"), "capability requires explicit boolean true " + value);
            Check(RequestHandler.LastRequest?["armToken"]?.Value<string>() == "round-a"
                && RequestHandler.LastRequest?["nativeEndIdentity"]?.Value<string>() == "native-a", "confirmation sends exact transaction identity");
            Check(!ConfirmationFixture.HasPendingArm && ConfirmationFixture.Active == "round-a", "confirmation activates exact pending arm");
            ConfirmationFixture.SetActive("foreign-round");
            Check(!ConfirmationFixture.TranslocatorAvailableForConfirmedRaid, "capability cannot follow another active arm");
            ConfirmationFixture.SetActive(null);
            Check(!ConfirmationFixture.TranslocatorAvailableForConfirmedRaid, "capability requires nonempty active arm");
        }
        foreach (var refusal in new[] { "server", "exception", "missing-token", "missing-native" })
        {
            ConfirmationFixture.Arrange(refusal == "missing-token" ? null : "round-b", refusal == "missing-native" ? null : "native-b", previousAvailable: true);
            RequestHandler.Response = "{\"confirmed\":false,\"translocatorAvailable\":true}";
            RequestHandler.Throw = refusal == "exception";
            Check(!ConfirmationFixture.ConfirmRaidStarted(), "confirmation refusal " + refusal);
            Check(!ConfirmationFixture.TranslocatorAvailableForConfirmedRaid, "failed confirmation revokes stale capability " + refusal);
        }
        ConfirmationFixture.Arrange("round-c", "native-c", hasPending: false);
        Check(!ConfirmationFixture.ConfirmRaidStarted() && RequestHandler.Calls == 0, "no pending arm does not contact server");
    }
}
