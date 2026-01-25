using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Deployments.Templates.Engines;
using Bicep.RpcClient;
using TemplateProcessor.Processors.Quickstarts;
using TemplateProcessor.Snapshots;

namespace TemplateProcessor.Processors;

internal static class AvmProcessor
{
    public static Guid GenerateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    const string EmptyParametersFile = """
    {
      "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
      "contentVersion": "1.0.0.0",
      "parameters": {}
    }
    """;

    public static async Task ProcessAsync(string repoRootPath, ISnapshotWriter snapshotWriter, CancellationToken cancellationToken)
    {
        var clientFactory = new BicepClientFactory(new HttpClient());
        using var bicep = await clientFactory.DownloadAndInitialize(new(), cancellationToken);

        foreach (var readmePath in Directory.GetFiles(Path.Combine(repoRootPath, "avm/res"), "README.md", SearchOption.AllDirectories))
        {
            var parentDir = Path.GetDirectoryName(readmePath)!;

            var bicepPath = Path.Combine(parentDir, "main.bicep");
            if (!File.Exists(bicepPath))
            {
                continue;
            }

            var result = await bicep.Compile(new(bicepPath), cancellationToken);
            if (result.Contents is null)
            {
                continue;
            }

            var metadata = await bicep.GetMetadata(new(bicepPath), cancellationToken);
            var name = metadata.Metadata.First(x => x.Name.Equals("name", StringComparison.OrdinalIgnoreCase));
            var description = metadata.Metadata.First(x => x.Name.Equals("description", StringComparison.OrdinalIgnoreCase));

            var templateContents = result.Contents;

            var template = TemplateEngine.ParseTemplate(templateContents);
            if (!template.Schema.Value.Contains("/deploymentTemplate.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var snapshot = await SnapshotBuilder.GetSnapshot(new(
                    TemplateContents: templateContents,
                    ParametersContents: EmptyParametersFile,
                    TenantId: null,
                    SubscriptionId: null,
                    ResourceGroup: null,
                    Location: null,
                    DeploymentName: null), cancellationToken);

                var snapshotPath = Path.ChangeExtension(bicepPath, ".snapshot.json");
                await File.WriteAllTextAsync(
                    snapshotPath,
                    JsonSerializer.Serialize(snapshot, SnapshotSerializationContext.FileSerializer.Snapshot),
                    cancellationToken);

                var resourceTypes = snapshot.PredictedResources
                    .Select(r => r["type"]?.GetValue<string>())
                    .Where(t => t is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();

                var metadataUri = $"https://github.com/Azure/bicep-registry-modules/blob/main/{Path.GetRelativePath(repoRootPath, readmePath).Replace('\\', '/')}";
                await snapshotWriter.WriteSnapshot(new(
                        Id: GenerateDeterministicGuid(metadataUri),
                        SourceUri: new Uri(metadataUri),
                        DisplayName: name.Value,
                        Description: description.Value,
                        Summary: description.Value,
                        DateUpdated: null,
                        Snapshot: snapshot,
                        ResourceTypes: resourceTypes!),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to process {readmePath}: {ex}");
            }
        }
    }
}