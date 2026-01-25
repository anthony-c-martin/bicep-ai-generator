using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Azure.Bicep.Types;
using Azure.Bicep.Types.Az;
using Azure.Bicep.Types.Concrete;
using BicepGeneratorMcp.Helpers;
using ModelContextProtocol.Server;
using OpenAI.Chat;

namespace BicepGeneratorMcp.Tools;

internal class BicepGeneratorTools(
    AzTypeLoader azTypeLoader,
    AiClientFactory aiClientFactory)
{
    private readonly Lazy<IReadOnlyDictionary<string, CrossFileTypeReference>> resourceIndexLazy = new(() => azTypeLoader.LoadTypeIndex().Resources.ToDictionary(StringComparer.OrdinalIgnoreCase));

    private readonly GoldenDatasetHelper goldenDatasetHelper = new(aiClientFactory);

    private async Task<string> GetSimilarExamplesPromptAsync(string promptDescription, string? resourceType, CancellationToken cancellationToken)
    {
        var examples = await goldenDatasetHelper.GetRelatedInfraSnapshotsAsync(promptDescription, cancellationToken);
        var examplePromptSection = "";
        foreach (var example in examples)
        {
            // Find example resources matching the requested resource type
            var matchingResources = example.Snapshot.PredictedResources
                .Where(x => resourceType == null || (x.TryGetPropertyValue("type", out var typeNode) && 
                    typeNode?.GetValue<string>().Equals(resourceType, StringComparison.OrdinalIgnoreCase) == true))
                .ToArray();

            if (matchingResources.Length > 0)
            {
                examplePromptSection += $"""
                Resource snapshot from infra file with description: "{example.Description}"
                ```json
                {JsonSerializer.Serialize(matchingResources, new JsonSerializerOptions { WriteIndented = true })}
                ```

                """;
            }
        }

        return examplePromptSection;
    }

    [McpServerTool]
    [Description("Generates a resource body configuration based on the Azure resource type, API version, JSON schema, example bodies, and a human-readable description of requirements.")]
    public async Task<string> GenerateResourceBodyAsync(
        [Description("The Azure resource type (e.g., 'Microsoft.KeyVault/vaults')")] string resourceType,
        [Description("The API version of the resource (e.g., '2024-11-01')")] string apiVersion,
        [Description("Human-readable description of what the resource should do or contain")] string requirements,
        CancellationToken cancellationToken)
    {
        var chatClient = aiClientFactory.GetChatClient();

        ResourceType resourceTypeDef;
        if (resourceIndexLazy.Value.TryGetValue($"{resourceType}@{apiVersion}", out var found))
        {
            resourceTypeDef = azTypeLoader.LoadType(found) as ResourceType ?? throw new UnreachableException();
        }
        else if (resourceIndexLazy.Value.Keys.Where(k => k.StartsWith($"{resourceType}@", StringComparison.OrdinalIgnoreCase)).ToArray() is {} rtMatches && rtMatches.Length != 0)
        {
            var apiVersions = rtMatches.Select(k => k.Split('@')[1]);
            throw new ArgumentException($"The specified API version '{apiVersion}' for resource type '{resourceType}' was not found. Possible api versions: '{string.Join("', '", apiVersions)}'", nameof(apiVersion));
        }
        else if (resourceType.Split('/')[0] is {} providerNamespace &&
            resourceIndexLazy.Value.Keys.Where(k => k.StartsWith($"{providerNamespace}/", StringComparison.OrdinalIgnoreCase)).ToArray() is {} providerMatches && providerMatches.Length != 0)
        {
            throw new ArgumentException($"The specified resource type '{resourceType}@{apiVersion}' was not found. Possible resource types for provider '{providerNamespace}': '{string.Join("', '", providerMatches)}'", nameof(resourceType)); 
        }
        else
        {
            throw new ArgumentException($"The specified resource type '{resourceType}@{apiVersion}' was not found.", nameof(resourceType));
        }

        var schema = ResourceSchemaGenerator.ToJsonSchema(resourceTypeDef.Body.Type);
        var serializedSchema = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });

        var examplePromptSection = await GetSimilarExamplesPromptAsync(requirements, resourceType, cancellationToken);

        var systemPrompt = $@"You are an expert Azure infrastructure architect specializing in creating resource configurations.

Your task is to generate a valid JSON resource body for an Azure resource based on:
- The resource type and API version
- The JSON schema that defines the structure
- Example resource bodies for reference
- A human-readable description of the requirements

Generate a complete, valid JSON resource body that:
1. Conforms to the provided JSON schema
2. Follows patterns from the example bodies
3. Meets the requirements in the description
4. Uses realistic and appropriate values
5. Includes all required properties
6. Follows Azure best practices

Return ONLY the JSON resource body, with no additional explanation or markdown formatting.";

        var userPrompt = $@"Generate a resource body for the following Azure resource:

Resource Type: {resourceType}
API Version: {apiVersion}

JSON Schema:
```json
{serializedSchema}
```

Requirements:
{requirements}

Similar examples:
{examplePromptSection}

Generate the complete predicted resource body as JSON.";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.3f, // Lower temperature for more consistent outputs
            MaxOutputTokenCount = 4000
        };

        var completion = await chatClient.CompleteChatAsync(messages, options);
        var response = completion.Value.Content[0].Text;

        // Clean up the response - remove markdown code fences if present
        response = response.Trim();
        if (response.StartsWith("```json"))
        {
            response = response[7..];
        }
        else if (response.StartsWith("```"))
        {
            response = response[3..];
        }
        
        if (response.EndsWith("```"))
        {
            response = response[..^3];
        }

        return response.Trim();
    }

    [McpServerTool]
    [Description("Generates a detailed Azure infrastructure architecture design document based on user requirements. Returns a comprehensive plan including overview, required resources, relationships, and configuration requirements.")]
    public async Task<string> GenerateInfrastructurePlanAsync(
        [Description("Description of the Azure infrastructure requirements and business needs")] string requirements,
        CancellationToken cancellationToken)
    {
        var chatClient = aiClientFactory.GetChatClient();

        var examplePromptSection = await GetSimilarExamplesPromptAsync(requirements, null, cancellationToken);

        var systemPrompt = @"You are an expert Azure architect specializing in infrastructure design.";

        var userPrompt = $@"Based on the following user requirements for Azure infrastructure, generate a detailed architecture design document in markdown format.

User Requirements:
{requirements}

Create a comprehensive architecture design that includes:
1. Overview of the solution
2. List of Azure resources needed (with specific resource types and API versions where applicable)
3. Resource relationships and dependencies
4. Configuration requirements
5. Any assumptions made

If the requirements are too vague or missing critical information, respond with 'INSUFFICIENT_CONTEXT' followed by specific questions you need answered.

Resource snapshots from similar examples:
{examplePromptSection}

Architecture Design:";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.4f, // Balanced temperature for creative but consistent architecture design
            MaxOutputTokenCount = 8000
        };

        var completion = await chatClient.CompleteChatAsync(messages, options);
        return completion.Value.Content[0].Text;
    }
}
