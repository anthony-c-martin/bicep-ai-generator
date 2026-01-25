namespace TemplateProcessor.Snapshots;

using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(SnapshotWithMetadata))]
[JsonSerializable(typeof(Snapshot))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class SnapshotSerializationContext : JsonSerializerContext
{
    public static SnapshotSerializationContext FileSerializer { get; } = new SnapshotSerializationContext(new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NewLine = "\n",
    });
}

internal record Snapshot(
    ImmutableArray<JsonObject> PredictedResources,
    ImmutableArray<string> Diagnostics);

internal record SnapshotWithMetadata(
    Guid Id,
    Uri SourceUri,
    string DisplayName,
    string Description,
    string Summary,
    DateTime DateUpdated,
    Snapshot Snapshot,
    ImmutableArray<string> ResourceTypes);