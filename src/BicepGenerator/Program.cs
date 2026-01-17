using Azure.AI.OpenAI;
using BicepGenerator;
using Bicep.Core;
using Azure.Bicep.Types.Az;

// Check for required environment variables
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
var deployment = "gpt-4.1";

if (string.IsNullOrEmpty(endpoint))
{
    Console.WriteLine("Error: AZURE_OPENAI_ENDPOINT environment variable not set.");
    return;
}

if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Error: AZURE_OPENAI_API_KEY environment variable not set.");
    return;
}

try
{
    // Create Azure OpenAI client
    var azureClient = new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(apiKey));
    var chatClient = azureClient.GetChatClient(deployment);

    // Create Bicep tools instance
    var bicepTools = new BicepTools(BicepCompiler.Create(), new AzTypeLoader());

    // Create and run orchestrator
    var orchestrator = new BicepGeneratorOrchestrator(chatClient, bicepTools);
    await orchestrator.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"\nError: {ex.Message}");
    return;
}