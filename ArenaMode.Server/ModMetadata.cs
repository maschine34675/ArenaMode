using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;
using Range = SemanticVersioning.Range;

namespace ArenaMode.Server;

// IModBlazorMetadata is a marker: implementing it is what makes the server
// add this assembly to its Blazor router and serve our wwwroot.
public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.maschine.ArenaMode";
    public string Name { get; init; } = "ArenaMode";
    public string Author { get; init; } = "maschine";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.1.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/maschine34675/ArenaMode";
    public string License { get; init; } = "MIT";

    public string? WWWRootUrl { get; init; }
    public string? HomePage { get; init; } = "/arenamode";
    public string? HomePageDescription { get; init; } = "Configure ArenaMode — server and client, one page.";
}
