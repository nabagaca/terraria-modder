using System.Text.Json.Serialization;

namespace TerrariaModManager.Models;

public class ModManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("entry_dll")]
    public string EntryDll { get; set; } = "";

    [JsonPropertyName("framework_version")]
    public string? FrameworkVersion { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string>? Dependencies { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("nexus_id")]
    public int NexusId { get; set; }
}
