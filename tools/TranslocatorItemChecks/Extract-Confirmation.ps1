function Write-TranslocatorConfirmation([string]$Client, [string]$Router, [string]$Output) {
    $source=[IO.File]::ReadAllText($Client)
    $route=[IO.File]::ReadAllText($Router)
    $method=[regex]::Matches($source,'(?s)    public static bool ConfirmRaidStarted\(\).*?(?=    public static bool PostRaidEnd)')
    $property=[regex]::Matches($source,'(?s)    internal static bool TranslocatorAvailableForConfirmedRaid =>.*?;')
    $capability=[regex]::Matches($route,'(?m)^\s+translocatorAvailable = ([^\r\n]+),\s*$')
    if($method.Count -ne 1 -or $property.Count -ne 1 -or $capability.Count -ne 1){throw 'Exact production confirmation/capability extraction failed'}
    $prefix=@'
#nullable disable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Services = ArenaMode.Server.Services;
internal static class ConfirmationFixture {
    private static string _pendingArmToken, _activeArmToken, _molotovArmToken, _translocatorArmToken, _boundNativeEndIdentity;
    internal static bool HasPendingArm, HasPendingEnd;
    internal static string Active => _activeArmToken;
    internal static void SetActive(string value) => _activeArmToken = value;
    internal static void Arrange(string pending, string native, bool hasPending = true, bool previousAvailable = false) {
        _pendingArmToken = pending; _boundNativeEndIdentity = native;
        _activeArmToken = previousAvailable ? "old-round" : null;
        _translocatorArmToken = previousAvailable ? "old-round" : null;
        _molotovArmToken = null; HasPendingArm = hasPending; HasPendingEnd = false;
        RequestHandler.Response = null; RequestHandler.Throw = false; RequestHandler.Calls = 0; RequestHandler.LastRequest = null;
    }
'@
    $suffix=@'
}
internal static class RequestHandler {
    internal static string Response; internal static bool Throw; internal static int Calls;
    internal static JObject LastRequest;
    internal static string PostJson(string route, string body) {
        Calls++; LastRequest = JObject.Parse(body);
        if (Throw) throw new InvalidOperationException("native transport failure");
        return Response;
    }
}
internal static class Protocol { internal const string RaidConfirmRoute = "/test/confirm"; }
internal static class Plugin { internal static readonly Logger ModLogger = new(); }
internal sealed class Logger { internal void LogWarning(string message) { } }
'@
    [IO.File]::WriteAllText($Output, $prefix + "`n" + $property[0].Value + "`n" + $method[0].Value + "`n    internal static bool ServerCapability(bool confirmed) => " + $capability[0].Groups[1].Value + ";`n" + $suffix)
}
