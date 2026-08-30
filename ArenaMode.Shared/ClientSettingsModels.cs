using System.Collections.Generic;

namespace ArenaMode.Shared;

/// <summary>
/// The client's own BepInEx settings, described well enough for the server's
/// config page to render and edit them.
///
/// Deliberately attribute-free: this file is compiled into both assemblies, and
/// they do not share a JSON library — the client has Newtonsoft, the server has
/// System.Text.Json. Both write PascalCase by default and both read it back
/// case-insensitively, so plain property names are the one shape that survives
/// the trip in either direction without a serializer attribute either side
/// would have to reference.
/// </summary>
public class ClientSettingsSchema
{
    /// <summary>Bumped by the server every time the page saves; the client applies on change.</summary>
    public int Revision { get; set; }

    /// <summary>Set when the client uploaded this; false means the page is showing nothing real yet.</summary>
    public bool Known { get; set; }

    /// <summary>Local time of the client's last upload, for the page to show staleness.</summary>
    public string? UpdatedAt { get; set; }

    public List<ClientSettingEntry> Entries { get; set; } = new();
}

public class ClientSettingEntry
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>"bool", "int", "float", "string" or "enum" — what the editor should render.</summary>
    public string Type { get; set; } = "string";

    /// <summary>Always a string: one transport for every type, parsed back on the client.</summary>
    public string Value { get; set; } = string.Empty;

    public string Default { get; set; } = string.Empty;

    /// <summary>Range bounds, when the entry declared an AcceptableValueRange.</summary>
    public string? Min { get; set; }
    public string? Max { get; set; }

    /// <summary>Allowed values, when the entry declared an AcceptableValueList.</summary>
    public List<string>? Options { get; set; }

    public string Id => $"{Section}/{Key}";
}
