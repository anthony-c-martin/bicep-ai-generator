using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BicepGeneratorEval;

public class EvalRunner
{
    private readonly SchemaValidator _schemaValidator = new();
    private readonly AzureResourceValidator _azureValidator;
    private readonly IntentScorer _intentScorer;

    public EvalRunner(TokenCredential credential, string subscriptionId, string resourceGroupName, string openAiEndpoint, string deploymentName)
    {
        _azureValidator = new AzureResourceValidator(credential, subscriptionId, resourceGroupName);
        _intentScorer = new IntentScorer(credential, openAiEndpoint, deploymentName);
    }

    public async Task<(List<EvalResult> Results, bool WasCanceled)> RunAsync(List<EvalPrompt> prompts, string mcpServerPath, CancellationToken cancellationToken)
    {
        // Start the MCP server as a subprocess and connect via stdio
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = mcpServerPath,
        });

        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"Connected to MCP server. Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

        var results = new List<EvalResult>();

        foreach (var prompt in prompts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Cancellation requested. Completed {results.Count}/{prompts.Count} prompts.");
                return (results, WasCanceled: true);
            }

            Console.WriteLine($"[{prompt.Id}/{prompts.Count}] Evaluating: {prompt.ResourceType} - {prompt.Prompt[..Math.Min(60, prompt.Prompt.Length)]}...");

            try
            {
                var result = await EvaluateSingleAsync(mcpClient, prompt, cancellationToken);
                results.Add(result);

                Console.WriteLine($"  -> Score: {result.TotalScore}/100 (Tool:{result.ToolCallSucceeded}, Schema:{result.SchemaValid}, Azure:{result.AzureValidationPassed}, Intent:{result.IntentScore:F2})");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Cancellation requested. Completed {results.Count}/{prompts.Count} prompts.");
                return (results, WasCanceled: true);
            }
        }

        return (results, WasCanceled: false);
    }

    private async Task<EvalResult> EvaluateSingleAsync(McpClient mcpClient, EvalPrompt prompt, CancellationToken cancellationToken)
    {
        bool toolCallSucceeded = false;
        bool schemaValid = false;
        bool azureValidationPassed = false;
        string? azureValidationError = null;
        double intentScore = 0;
        string? errorMessage = null;
        string? notes = null;
        JsonObject? resourceBody = null;

        // Step 1: Invoke the MCP tool
        try
        {
            var toolResult = await mcpClient.CallToolAsync(
                "generate_resource_body",
                new Dictionary<string, object?>
                {
                    ["resourceType"] = prompt.ResourceType,
                    ["apiVersion"] = prompt.ApiVersion,
                    ["requirements"] = prompt.Prompt,
                },
                cancellationToken: cancellationToken);

            if (toolResult.IsError == true)
            {
                errorMessage = (toolResult.Content.FirstOrDefault() as TextContentBlock)?.Text ?? "Tool returned error";
            }
            else
            {
                var responseText = (toolResult.Content.FirstOrDefault() as TextContentBlock)?.Text;
                if (responseText is not null)
                {
                    var parsed = JsonSerializer.Deserialize<JsonObject>(responseText);
                    if (parsed is not null)
                    {
                        resourceBody = parsed.TryGetPropertyValue("resource", out var res) ? res?.AsObject() : parsed;
                        notes = parsed.TryGetPropertyValue("notes", out var n) ? n?.GetValue<string>() : null;
                        toolCallSucceeded = true;
                    }
                    else
                    {
                        errorMessage = "Failed to parse tool response as JSON.";
                    }
                }
                else
                {
                    errorMessage = "Tool returned no content.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errorMessage = $"Tool call exception: {ex.Message}";
        }

        // Step 2: Schema validation
        if (toolCallSucceeded && resourceBody is not null)
        {
            schemaValid = _schemaValidator.Validate(prompt.ResourceType, prompt.ApiVersion, resourceBody, out var schemaError);
            if (!schemaValid)
                errorMessage ??= schemaError;
        }

        // Step 3: Azure validation
        if (toolCallSucceeded && resourceBody is not null)
        {
            try
            {
                var (azValid, azError) = await _azureValidator.ValidateAsync(
                    prompt.ResourceType, prompt.ApiVersion, resourceBody, cancellationToken);
                azureValidationPassed = azValid;
                if (!azValid)
                {
                    azureValidationError = azError;
                    errorMessage ??= azError;
                }
            }
            catch (Exception ex)
            {
                azureValidationError = $"Azure validation error: {ex.Message}";
                errorMessage ??= azureValidationError;
            }
        }

        // Step 4: Intent scoring
        string? intentJustification = null;
        if (toolCallSucceeded && resourceBody is not null)
        {
            try
            {
                var intentResult = await _intentScorer.ScoreAsync(prompt.Prompt, prompt.ResourceType, resourceBody, cancellationToken);
                intentScore = intentResult.Score;
                intentJustification = intentResult.Justification;
            }
            catch (Exception ex)
            {
                errorMessage ??= $"Intent scoring error: {ex.Message}";
            }
        }

        // Calculate total score (out of 100) using multipliers
        var toolMultiplier = toolCallSucceeded ? 1.0 : 0.0;
        var schemaMultiplier = schemaValid ? 1.0 : 0.5;
        var azureMultiplier = azureValidationPassed ? 1.0 : 0.8;
        var intentMultiplier = intentScore;
        var totalScore = (int)(100 * toolMultiplier * schemaMultiplier * azureMultiplier * intentMultiplier);

        return new EvalResult(
            prompt.Id,
            prompt.ResourceType,
            prompt.ApiVersion,
            prompt.Prompt,
            toolCallSucceeded,
            schemaValid,
            azureValidationPassed,
            azureValidationError,
            intentScore,
            intentJustification,
            totalScore,
            errorMessage,
            notes,
            resourceBody?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
