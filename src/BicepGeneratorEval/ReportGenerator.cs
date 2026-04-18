using System.Text;

namespace BicepGeneratorEval;

public static class ReportGenerator
{
    public static async Task WriteReportAsync(string outputPath, List<EvalResult> results, bool wasCanceled = false, int totalPrompts = 0)
    {
        var sb = new StringBuilder();
        var ordered = results.OrderBy(r => r.Id).ToList();

        sb.AppendLine("# Bicep Generator MCP Evaluation Report");
        sb.AppendLine();
        sb.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Total prompts:** {results.Count}");
        if (wasCanceled)
        {
            sb.AppendLine();
            sb.AppendLine($"> **Note:** Evaluation was canceled early. Only {results.Count}/{totalPrompts} prompts were evaluated.");
        }
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| # | Prompt | Score |");
        sb.AppendLine("|---|--------|-------|");

        foreach (var r in ordered)
        {
            var anchor = $"prompt-{r.Id}";
            var promptSummary = r.Prompt.Length > 80 ? r.Prompt[..77] + "..." : r.Prompt;
            sb.AppendLine($"| [{r.Id}](#{anchor}) | {promptSummary} | {r.TotalScore}/100 |");
        }

        sb.AppendLine();

        // Detailed section per result
        foreach (var r in ordered)
        {
            sb.AppendLine($"---");
            sb.AppendLine();
            sb.AppendLine($"### Prompt {r.Id}");
            sb.AppendLine($"<a id=\"prompt-{r.Id}\"></a>");
            sb.AppendLine();
            sb.AppendLine($"**Resource Type:** `{r.ResourceType}` (API version `{r.ApiVersion}`)");
            sb.AppendLine();
            sb.AppendLine("**User Prompt:**");
            sb.AppendLine();
            sb.AppendLine($"> {r.Prompt}");
            sb.AppendLine();

            // Score breakdown
            sb.AppendLine($"**Score: {r.TotalScore}/100**");
            sb.AppendLine();
            sb.AppendLine("| Component | Result | Multiplier |");
            sb.AppendLine("|-----------|--------|------------|" );
            sb.AppendLine($"| Tool call | {(r.ToolCallSucceeded ? "✅ Succeeded" : "❌ Failed")} | {(r.ToolCallSucceeded ? "1x" : "0x")} |");
            sb.AppendLine($"| Schema validation | {(r.SchemaValid ? "✅ Valid" : "❌ Invalid")} | {(r.SchemaValid ? "1x" : "0.5x")} |");
            sb.AppendLine($"| Azure validation | {(r.AzureValidationPassed ? "✅ Passed" : "❌ Failed")} | {(r.AzureValidationPassed ? "1x" : "0.8x")} |");
            sb.AppendLine($"| Intent score | {(r.ToolCallSucceeded ? $"{r.IntentScore:F2}" : "N/A")} | {(r.ToolCallSucceeded ? $"{r.IntentScore:F2}x" : "0x")} |");

            if (r.AzureValidationError is not null)
            {
                sb.AppendLine();
                sb.AppendLine("**Azure Validation Error:**");
                sb.AppendLine();
                sb.AppendLine($"```json\n{r.AzureValidationError}\n```");
            }
            else if (r.ErrorMessage is not null)
            {
                sb.AppendLine();
                sb.AppendLine($"**Error:** {r.ErrorMessage}");
            }

            // Intent justification
            if (r.IntentJustification is not null)
            {
                sb.AppendLine();
                sb.AppendLine("**Intent Justification:**");
                sb.AppendLine();
                sb.AppendLine(r.IntentJustification);
            }

            // Generated resource body
            if (r.ResourceBody is not null)
            {
                sb.AppendLine();
                sb.AppendLine("**Generated Resource Body:**");
                sb.AppendLine();
                sb.AppendLine("```json");
                sb.AppendLine(r.ResourceBody);
                sb.AppendLine("```");
            }

            // Tool notes
            if (r.Notes is not null)
            {
                sb.AppendLine();
                sb.AppendLine("**Notes:**");
                sb.AppendLine();
                sb.AppendLine(r.Notes);
            }

            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }
}
