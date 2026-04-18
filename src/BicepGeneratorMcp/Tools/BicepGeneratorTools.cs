using System.ComponentModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Bicep.Types;
using Azure.Bicep.Types.Az;
using Azure.Bicep.Types.Concrete;
using BicepGeneratorMcp.Helpers;
using ModelContextProtocol.Server;
using OpenAI.Chat;

namespace BicepGeneratorMcp.Tools;

internal class BicepGeneratorTools(
    AzTypeLoader azTypeLoader,
    AzureClientFactory azureClientFactory)
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Lazy<IReadOnlyDictionary<string, CrossFileTypeReference>> resourceIndexLazy = new(() => azTypeLoader.LoadTypeIndex().Resources.ToDictionary(StringComparer.OrdinalIgnoreCase));

    private readonly GoldenDatasetHelper goldenDatasetHelper = new(azureClientFactory);

    public record GenerateResourceBodyResult(
        JsonObject Resource,
        string? Notes);

    private async Task<string> GetSimilarExamplesPromptAsync(string promptDescription, string? resourceType, CancellationToken cancellationToken)
    {
        var results = await goldenDatasetHelper.GetRelatedInfraSnapshotsAsync(promptDescription, cancellationToken);
        var exampleNumber = 1;
        var examplePromptSection = "";
        foreach (var (example, score) in results.OrderByDescending(x => x.score))
        {
            // Find example resources matching the requested resource type
            var matchingResources = example.Snapshot.PredictedResources
                .Where(x => resourceType == null || (x.TryGetPropertyValue("type", out var typeNode) && 
                    typeNode?.GetValue<string>().Equals(resourceType, StringComparison.OrdinalIgnoreCase) == true))
                .ToArray();

            if (matchingResources.Length > 0)
            {
                examplePromptSection += $"""
                Example {exampleNumber++}:
                Relevance score: {score:F4}
                Summary: "{example.Summary}"
                Description: "{example.Description}"
                ```json
                {JsonSerializer.Serialize(matchingResources, IndentedJsonOptions)}
                ```

                """;
            }
        }

        return examplePromptSection;
    }

    [McpServerTool]
    [Description("Generates a resource body configuration based on the Azure resource type, API version, JSON schema, example bodies, and a human-readable description of requirements.")]
    public async Task<GenerateResourceBodyResult> GenerateResourceBodyAsync(
        [Description("The Azure resource type (e.g., 'Microsoft.KeyVault/vaults')")] string resourceType,
        [Description("The API version of the resource (e.g., '2024-11-01')")] string apiVersion,
        [Description("Human-readable description of what the resource should do or contain")] string requirements,
        CancellationToken cancellationToken)
    {
        var chatClient = azureClientFactory.GetChatClient();

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
        var serializedSchema = JsonSerializer.Serialize(schema, IndentedJsonOptions);

        var examplePromptSection = await GetSimilarExamplesPromptAsync(requirements, resourceType, cancellationToken);

        var systemPrompt = $"""
You are an expert Azure infrastructure architect specializing in creating resource configurations.

Your task is to generate a valid JSON resource body for an Azure resource based on:
- The resource type and API version
- The JSON schema that defines the structure
- Example resource bodies for reference
- A human-readable description of the requirements

When generating the resource body, ensure that:
- The critical rules below are adhered to
- The intent from the requirements is accurately expressed in the configuration
- Security best-practices for the specified resource type are followed where applicable

CRITICAL RULES - FOLLOW EXACTLY:
- ONLY use properties that exist in the provided JSON schema. Do NOT invent or hallucinate properties.
- If a requested feature is not available in the schema, DO NOT include it. Instead, add an explanation of the limitation to the notes.
- The JSON schema is the authoritative source of truth for what properties are valid.
- Example bodies are for reference patterns only - verify any property from examples exists in the schema before using it.

Return ONLY the JSON resource body, with no additional explanation or markdown formatting.

If you cannot meet the original user requirements in a way that fits with the above rules, do not return the body, and explain the problem in notes.

To format the output:
- If the resource body can be generated, return it as JSON between tags <RESOURCE_BODY> and </RESOURCE_BODY>. Do not use ``` at all.
- If there are important notes or explanations, return them as a bulleted list between tags <NOTES> and </NOTES>.
- Do not output comments in the JSON - use the notes section instead if notes are needed.
""";

        var userPrompt = $"""
Generate a resource body for the following Azure resource:

Resource Type: {resourceType}
API Version: {apiVersion}

JSON Schema:
```json
{serializedSchema}
```

Requirements:
{requirements}

Similar example resources, with a relevance score (number between 0 and 1) indicating how closely they match the original requirements (higher indicates a closer match). Use the relevance score to guide which examples to prioritize, and assess the summary + descriptions to determine what may be similar or different:
{examplePromptSection}
""";

        await Console.Error.WriteLineAsync($"{nameof(GenerateResourceBodyAsync)}: Prompt: {userPrompt}");

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.3f, // Lower temperature for more consistent outputs
            MaxOutputTokenCount = 4000,
        };

        var completion = await chatClient.CompleteChatAsync(messages, options);
        var response = completion.Value.Content[0].Text;

        var json = TextHelper.ExtractBetweenTags(response, "RESOURCE_BODY");
        var notes = TextHelper.TryExtractBetweenTags(response, "NOTES");

        var resource = JsonSerializer.Deserialize<JsonObject>(json)
            ?? throw new InvalidOperationException("Generated resource body is not valid JSON.");

        return new(resource, notes);
    }

    [McpServerTool]
    [Description("Generates a detailed Azure infrastructure architecture design document based on user requirements. Returns a comprehensive plan including overview, required resources, relationships, and configuration requirements.")]
    public async Task<string> GenerateInfrastructurePlanAsync(
        [Description("Description of the Azure infrastructure requirements and business needs")] string requirements,
        CancellationToken cancellationToken)
    {
        var chatClient = azureClientFactory.GetChatClient();

        var examplePromptSection = await GetSimilarExamplesPromptAsync(requirements, null, cancellationToken);

        var systemPrompt = @"You are an expert Azure architect specializing in infrastructure design.

IMPORTANT: When listing Azure resources, only include capabilities that are actually configurable via Azure Resource Manager (ARM) templates and Bicep.
Some Azure features are configured via data plane APIs or the Azure Portal and cannot be deployed via ARM/Bicep.
If a feature requires special deployment methods (like deployment scripts or post-deployment configuration), note this explicitly.";

        var userPrompt = $@"Based on the following user requirements for Azure infrastructure, generate a detailed architecture design document in markdown format.

User Requirements:
{requirements}

Create a comprehensive architecture design that includes:
1. Overview of the solution
2. List of Azure resources needed (with specific resource types and API versions where applicable)
3. Resource relationships and dependencies
4. Configuration requirements
5. Any assumptions made
6. NOTE: If any features require data plane configuration (not available via ARM/Bicep), explicitly call this out and explain what additional steps are needed post-deployment

If the requirements are too vague or missing critical information, respond with 'INSUFFICIENT_CONTEXT' followed by specific questions you need answered.

Resource snapshots from similar examples:
{examplePromptSection}

Architecture Design:";

        await Console.Error.WriteLineAsync($"{nameof(GenerateInfrastructurePlanAsync)}: Prompt: {userPrompt}");

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
