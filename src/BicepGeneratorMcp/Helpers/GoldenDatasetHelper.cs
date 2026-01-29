using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Search.Documents.Models;
using Microsoft.WindowsAzure.ResourceStack.Common.Swagger.Validators;
using ModelContextProtocol.Server;
using TemplateProcessor.Snapshots;

namespace BicepGeneratorMcp.Helpers;

internal class GoldenDatasetHelper(AzureClientFactory azureClientFactory)
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

    [McpServerTool]
    [Description("Searches for similar example Bicep infrastructure templates based on a natural language description. Returns relevant examples that can be used as reference for patterns, naming conventions, and resource configurations.")]
    public async Task<ImmutableArray<(SnapshotWithMetadata result, double? score)>> GetRelatedInfraSnapshotsAsync(
        [Description("The prompt describing the desired infrastructure")] string prompt,
        CancellationToken cancellationToken)
    {
        var client = azureClientFactory.GetSearchClient();

        var response = await client.SearchAsync<SnapshotData>(new()
        {
            VectorSearch = new()
            {
                Queries = { new VectorizableTextQuery(prompt) {
                KNearestNeighborsCount = 3,
                Fields = { "text_vector" } } },
            }
        }, cancellationToken);

        var containerClient = azureClientFactory.GetSnapshotContainerClient();

        List<(SnapshotWithMetadata result, double? score)> results = [];
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var blobClient = containerClient.GetBlobClient(result.Document.Id);
            var content = await blobClient.DownloadContentAsync(cancellationToken);

            var snapshotWithMetadata = JsonSerializer.Deserialize(content.Value.Content, SnapshotSerializationContext.FileSerializer.SnapshotWithMetadata)
                ?? throw new InvalidOperationException("Failed to deserialize snapshot.");

            results.Add((snapshotWithMetadata, result.Score));
        }

        return [.. results];
    }
}
