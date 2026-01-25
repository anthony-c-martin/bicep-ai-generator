using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using OpenAI.Chat;

namespace BicepGeneratorMcp;

public class AzureClientFactory(Configuration configuration, TokenCredential credential)
{
    public ChatClient GetChatClient()
    {
        var openAiEndpoint = configuration.AzureOpenAIEndpoint;

        AzureOpenAIClient openAIClient = new(new(openAiEndpoint), credential);

        return openAIClient.GetChatClient(configuration.DeploymentName);
    }

    public SearchClient GetSearchClient()
    {
        return new(new(configuration.AzureSearchEndpoint), configuration.AzureSearchIndexName, credential);
    }

    public BlobContainerClient GetSnapshotContainerClient()
    {
        BlobServiceClient blobServiceClient = new(new(configuration.StorageAccountEndpoint), credential);

        return blobServiceClient.GetBlobContainerClient(configuration.SnapshotContainerName);
    }
}
