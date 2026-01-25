using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Deployments.Templates.Engines;
using Bicep.RpcClient;
using TemplateProcessor.Snapshots;

namespace TemplateProcessor.Quickstarts;

internal static class QuickstartsProcessor
{
    public static Guid GenerateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static async Task ProcessQuickstartAsync(string quickstartFolderPath, ISnapshotWriter snapshotWriter, CancellationToken cancellationToken)
    {
        var clientFactory = new BicepClientFactory(new HttpClient());
        using var bicep = await clientFactory.DownloadAndInitialize(new(), cancellationToken);

        foreach (var metadataPath in Directory.GetFiles(quickstartFolderPath, "metadata.json", SearchOption.AllDirectories))
        {
            var metadataContents = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize(metadataContents, MetadataSerializationContext.FileSerializer.Metadata);

            var parentDir = Path.GetDirectoryName(metadataPath)!;

            var bicepPath = Path.Combine(parentDir, "main.bicep");
            var templatePath = Path.Combine(parentDir, "azuredeploy.json");
            var parametersPath = Path.Combine(parentDir, "azuredeploy.parameters.json");

            string templateContents;
            if (File.Exists(bicepPath))
            {
                var result = await bicep.Compile(new(bicepPath), cancellationToken);
                if (result.Contents is null)
                {
                    continue;
                }

                templateContents = result.Contents;
            }
            else
            {
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                templateContents = await File.ReadAllTextAsync(templatePath, cancellationToken);
            }

            if (!File.Exists(parametersPath))
            {
                continue;
            }
            var parametersContents = await File.ReadAllTextAsync(parametersPath, cancellationToken);

            var template = TemplateEngine.ParseTemplate(templateContents);
            if (!template.Schema.Value.Contains("/deploymentTemplate.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var snapshot = await SnapshotBuilder.GetSnapshot(new(
                    TemplateContents: templateContents,
                    ParametersContents: parametersContents,
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

                var metadataUri = $"https://github.com/Azure/azure-quickstart-templates/blob/master/{Path.GetRelativePath(quickstartFolderPath, metadataPath).Replace('\\', '/')}";
                await snapshotWriter.WriteSnapshot(new(
                        Id: GenerateDeterministicGuid(metadataUri),
                        SourceUri: new Uri(metadataUri),
                        DisplayName: metadata!.ItemDisplayName,
                        Description: metadata.Description,
                        Summary: metadata.Summary,
                        DateUpdated: DateTime.Parse(metadata.DateUpdated),
                        Snapshot: snapshot,
                        ResourceTypes: resourceTypes!),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to process {metadataPath}: {ex}");
            }
        }
    }
}