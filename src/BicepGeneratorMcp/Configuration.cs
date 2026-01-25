namespace BicepGeneratorMcp;

public record Configuration(
    string AzureOpenAIEndpoint,
    string DeploymentName,
    string AzureSearchEndpoint,
    string AzureSearchIndexName,
    string StorageAccountEndpoint,
    string SnapshotContainerName);