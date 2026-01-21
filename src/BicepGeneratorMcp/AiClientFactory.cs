using Azure.AI.OpenAI;
using Azure.Core;
using OpenAI.Chat;

namespace BicepGeneratorMcp;

public class AiClientFactory(Configuration configuration, TokenCredential credential)
{
    public ChatClient GetChatClient()
    {
        var openAiEndpoint = configuration.AzureOpenAIEndpoint;

        AzureOpenAIClient openAIClient = new(new(openAiEndpoint), credential);

        return openAIClient.GetChatClient(configuration.DeploymentName);
    }
}
