namespace TemplateProcessor.Processors.Quickstarts;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(Metadata))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class MetadataSerializationContext : JsonSerializerContext
{
    public static MetadataSerializationContext FileSerializer { get; } = new MetadataSerializationContext(new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NewLine = "\n",
    });
}

internal record Metadata(
    string Type,
    string ItemDisplayName,
    string Description,
    string Summary,
    string GithubUsername,
    string DateUpdated,
    string[] Environments);
