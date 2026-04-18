using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using Azure.Core;
using OpenAI.Chat;

namespace BicepGeneratorEval;

public record IntentScoreResult(double Score, string Justification);

public class IntentScorer
{
    private readonly ChatClient _chatClient;

    public IntentScorer(TokenCredential credential, string endpoint, string deploymentName)
    {
        var openAiClient = new AzureOpenAIClient(new Uri(endpoint), credential);
        _chatClient = openAiClient.GetChatClient(deploymentName);
    }

    public async Task<IntentScoreResult> ScoreAsync(string prompt, string resourceType, JsonObject resourceBody, CancellationToken cancellationToken)
    {
        var systemMessage = """
            You are an expert evaluator of Azure resource configurations.
            Given a user's intent description and the generated resource body JSON, score how well the output matches the intent.

            Return your response in exactly this format (two lines):
            SCORE: <number between 0 and 1>
            JUSTIFICATION: <one-sentence explanation of the score>

            Scoring guide:
            - 1.0 = perfectly matches all aspects of the intent
            - 0.0 = completely fails to match the intent
            Consider: Are the specific properties, values, SKUs, settings, and configurations described in the intent present in the body?
            """;

        var userMessage = $"""
            User intent: {prompt}
            Resource type: {resourceType}
            Generated resource body:
            {resourceBody.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemMessage),
            new UserChatMessage(userMessage)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.1f,
            MaxOutputTokenCount = 200,
        };

        var completion = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
        var response = completion.Value.Content[0].Text.Trim();

        return ParseResponse(response);
    }

    private static IntentScoreResult ParseResponse(string response)
    {
        double score = 0;
        string justification = "";

        foreach (var line in response.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("SCORE:", StringComparison.OrdinalIgnoreCase))
            {
                var scoreText = line["SCORE:".Length..].Trim();
                if (double.TryParse(scoreText, out var parsed) && parsed >= 0 && parsed <= 1)
                    score = parsed;
            }
            else if (line.StartsWith("JUSTIFICATION:", StringComparison.OrdinalIgnoreCase))
            {
                justification = line["JUSTIFICATION:".Length..].Trim();
            }
        }

        return new IntentScoreResult(score, justification);
    }
}
