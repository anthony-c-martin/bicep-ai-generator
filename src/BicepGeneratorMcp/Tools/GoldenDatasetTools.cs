using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Search.Documents.Models;
using ModelContextProtocol.Server;
using TemplateProcessor.Snapshots;

namespace BicepGeneratorMcp.Tools;

internal class GoldenDatasetTools(AiClientFactory aiClientFactory)
{
    public class SnapshotData
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        [JsonPropertyName("summary")]
        public required string Summary { get; set; }

        [JsonPropertyName("displayName")]
        public required string DisplayName { get; set; }

        [JsonPropertyName("sourceUri")]
        public required string SourceUri { get; set; }
    }

    public record GetRelatedInfraExamplesResult(
        [Description("The display name of the example")]
        string DisplayName,
        [Description("The description of the example")]
        string Description,
        [Description("The source URI of the example")]
        Uri SourceUri,
        [Description("The expanded snapshot of resources in JSON format")]
        ImmutableArray<JsonObject> PredictedResources);

    [McpServerTool]
    [Description("Searches for similar example Bicep infrastructure templates based on a natural language description. Returns relevant examples that can be used as reference for patterns, naming conventions, and resource configurations.")]
    public async Task<ImmutableArray<GetRelatedInfraExamplesResult>> GetRelatedInfraExamplesAsync(
        [Description("The prompt describing the desired infrastructure")] string prompt,
        CancellationToken cancellationToken)
    {
        var client = aiClientFactory.GetSearchClient();

        var response = await client.SearchAsync<SnapshotData>(new()
        {
            VectorSearch = new()
            {
                Queries = { new VectorizableTextQuery(prompt) {
                KNearestNeighborsCount = 3,
                Fields = { "text_vector" } } },
            }
        }, cancellationToken);

        var containerClient = aiClientFactory.GetSnapshotContainerClient();

        ImmutableArray<GetRelatedInfraExamplesResult>.Builder results = ImmutableArray.CreateBuilder<GetRelatedInfraExamplesResult>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var blobClient = containerClient.GetBlobClient(result.Document.Id);
            var content = await blobClient.DownloadContentAsync(cancellationToken);

            var snapshotWithMetadata = JsonSerializer.Deserialize(content.Value.Content, SnapshotSerializationContext.FileSerializer.SnapshotWithMetadata)
                ?? throw new InvalidOperationException("Failed to deserialize snapshot.");

            results.Add(new GetRelatedInfraExamplesResult(
                snapshotWithMetadata.DisplayName,
                snapshotWithMetadata.Description,
                snapshotWithMetadata.SourceUri,
                snapshotWithMetadata.Snapshot.PredictedResources));
        }

        return results.ToImmutable();
    }
}
