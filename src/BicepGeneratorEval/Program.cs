using System.CommandLine;
using Azure.Identity;
using BicepGeneratorEval;

var subscriptionIdOption = new Option<string>("--subscription-id")
{
    Description = "The Azure subscription ID for resource validation.",
};

var resourceGroupOption = new Option<string>("--resource-group")
{
    Description = "The resource group name for resource validation.",
};

var mcpServerPathOption = new Option<string>("--mcp-server-path")
{
    Description = "Path to the BicepGeneratorMcp server executable.",
};

var promptsPathOption = new Option<string>("--prompts-path")
{
    Description = "Path to the prompts JSON configuration file.",
};

var outputPathOption = new Option<string>("--output")
{
    Description = "Path for the output markdown report.",
};

var rootCommand = new RootCommand("Bicep Generator MCP Evaluation Framework")
{
    subscriptionIdOption,
    resourceGroupOption,
    mcpServerPathOption,
    promptsPathOption,
    outputPathOption,
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var subscriptionId = parseResult.GetRequiredValue(subscriptionIdOption);
    var resourceGroupName = parseResult.GetRequiredValue(resourceGroupOption);
    var openAiEndpoint = "https://mcp-ai-test.openai.azure.com/";
    var deploymentName = "gpt-4.1";
    var mcpServerPath = parseResult.GetRequiredValue(mcpServerPathOption);
    var promptsPath = parseResult.GetRequiredValue(promptsPathOption);
    var outputPath = parseResult.GetRequiredValue(outputPathOption);

    Console.WriteLine("=== Bicep Generator MCP Evaluation ===");
    Console.WriteLine($"MCP Server: {mcpServerPath}");
    Console.WriteLine($"Prompts: {promptsPath}");
    Console.WriteLine($"Output: {outputPath}");
    Console.WriteLine();

    var credential = new DefaultAzureCredential();

    var prompts = await PromptLoader.LoadAsync(promptsPath);
    Console.WriteLine($"Loaded {prompts.Count} prompts.");

    var runner = new EvalRunner(credential, subscriptionId, resourceGroupName, openAiEndpoint, deploymentName);

    var (results, wasCanceled) = await runner.RunAsync(prompts, mcpServerPath, cancellationToken);

    await ReportGenerator.WriteReportAsync(outputPath, results, wasCanceled, prompts.Count);

    Console.WriteLine();
    Console.WriteLine($"Evaluation {(wasCanceled ? "canceled" : "complete")}. Report written to: {outputPath}");
    if (results.Count > 0)
    {
        Console.WriteLine($"Average score: {results.Average(r => r.TotalScore):F1}/100");
    }
});

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
