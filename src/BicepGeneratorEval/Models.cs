using System.Text.Json;
using System.Text.Json.Serialization;

namespace BicepGeneratorEval;

public record EvalPrompt(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("apiVersion")] string ApiVersion,
    [property: JsonPropertyName("prompt")] string Prompt);

public record EvalResult(
    int Id,
    string ResourceType,
    string ApiVersion,
    string Prompt,
    bool ToolCallSucceeded,
    bool SchemaValid,
    bool AzureValidationPassed,
    string? AzureValidationError,
    double IntentScore,
    string? IntentJustification,
    int TotalScore,
    string? ErrorMessage,
    string? Notes,
    string? ResourceBody);

public static class PromptLoader
{
    public static async Task<List<EvalPrompt>> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<EvalPrompt>>(stream)
            ?? throw new InvalidOperationException("Failed to deserialize prompts.");
    }
}
