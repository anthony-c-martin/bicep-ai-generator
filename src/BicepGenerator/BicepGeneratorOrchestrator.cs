using OpenAI.Chat;

namespace BicepGenerator;

/// <summary>
/// Orchestrates the Bicep generation workflow
/// </summary>
public class BicepGeneratorOrchestrator(
    ChatClient chatClient,
    IBicepTools bicepTools)
{
    private readonly List<ChatMessage> _conversationHistory = new();

    /// <summary>
    /// Runs the complete Bicep generation workflow
    /// </summary>
    public async Task RunAsync()
    {
        Console.WriteLine("=== Bicep Generator ===");
        Console.WriteLine("Describe the Azure infrastructure you would like to create.");
        Console.WriteLine("(Press Enter twice on an empty line to submit)\n");

        // Requirements gathering phase
        var architectureDesign = await GatherRequirementsAsync();
        
        if (architectureDesign == null)
        {
            Console.WriteLine("\nGeneration cancelled.");
            return;
        }

        // Bicep generation phase
        await GenerateBicepFilesAsync(architectureDesign);

        Console.WriteLine("\n=== Generation Complete ===");
        Console.WriteLine("Your Bicep files have been written to the 'output' folder.");
    }

    /// <summary>
    /// Phase 1: Requirements gathering
    /// </summary>
    private async Task<string?> GatherRequirementsAsync()
    {
        Console.Write("Your requirements: ");
        var initialDescription = ReadMultilineInput();

        if (string.IsNullOrWhiteSpace(initialDescription))
        {
            Console.WriteLine("No input provided. Exiting.");
            return null;
        }

        // Generate initial architecture design
        var designPrompt = $@"Based on the following user requirements for Azure infrastructure, generate a detailed architecture design document in markdown format.

User Requirements:
{initialDescription}

Create a comprehensive architecture design that includes:
1. Overview of the solution
2. List of Azure resources needed (with specific resource types)
3. Resource relationships and dependencies
4. Configuration requirements
5. Any assumptions made

If the requirements are too vague or missing critical information, respond with 'INSUFFICIENT_CONTEXT' followed by specific questions you need answered.

Architecture Design:";

        _conversationHistory.Clear();
        _conversationHistory.Add(new SystemChatMessage("You are an expert Azure architect specializing in infrastructure design."));
        _conversationHistory.Add(new UserChatMessage(designPrompt));

        Console.WriteLine();
        var designResponse = await GetChatCompletionAsync("Analyzing your requirements");

        // Check if we need more information
        while (designResponse.StartsWith("INSUFFICIENT_CONTEXT"))
        {
            var questions = designResponse.Replace("INSUFFICIENT_CONTEXT", "").Trim();
            Console.WriteLine($"\n{questions}");
            Console.Write("\nYour response: ");
            var additionalInfo = ReadMultilineInput();

            if (string.IsNullOrWhiteSpace(additionalInfo))
            {
                Console.WriteLine("No additional information provided. Exiting.");
                return null;
            }

            _conversationHistory.Add(new UserChatMessage($"Additional information: {additionalInfo}"));
            Console.WriteLine();
            designResponse = await GetChatCompletionAsync("Analyzing additional information");
        }

        // Show summary and confirm
        Console.WriteLine("\n=== Architecture Design Summary ===");
        var summaryPrompt = "Provide a brief 3-5 sentence summary of the architecture design for the user to review.";
        _conversationHistory.Add(new UserChatMessage(summaryPrompt));
        var summary = await GetChatCompletionAsync("Generating summary");
        Console.WriteLine(summary);

        Console.Write("\nWould you like to proceed with generating Bicep files? (yes/no): ");
        var confirmation = Console.ReadLine()?.Trim().ToLower();

        if (confirmation != "yes" && confirmation != "y")
        {
            return null;
        }

        return designResponse;
    }

    /// <summary>
    /// Phase 2: Bicep generation
    /// </summary>
    private async Task GenerateBicepFilesAsync(string architectureDesign)
    {
        Console.WriteLine("\n=== Generating Bicep Files ===");

        // Create output directory
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "out");
        Directory.CreateDirectory(outputDir);

        // Create blank initial files
        var mainBicepPath = Path.Combine(outputDir, "main.bicep");
        var mainParamPath = Path.Combine(outputDir, "main.bicepparam");

        await File.WriteAllTextAsync(mainBicepPath, "");
        await File.WriteAllTextAsync(mainParamPath, "");

        Console.WriteLine("Creating initial file structure...");

        // Generate high-level structure
        var structurePrompt = $@"Based on this architecture design:

{architectureDesign}

Generate the high-level structure for a Bicep deployment. Create:
1. main.bicep - with the expected 'module' or 'resource' statements (use placeholder values for now)
2. main.bicepparam - with the required parameters

For main.bicep, include:
- Required parameters at the top
- Module or resource declarations with // TODO comments where details need to be filled in
- Outputs at the bottom

For main.bicepparam, include:
- The using statement pointing to main.bicep
- All required parameter values (use sensible placeholder values)

Provide the content for both files separately. Format your response as:
FILE: main.bicep
```bicep
<content>
```

FILE: main.bicepparam
```bicepparam
<content>
```";

        _conversationHistory.Clear();
        _conversationHistory.Add(new SystemChatMessage("You are an expert in Azure Bicep templates and infrastructure as code."));
        _conversationHistory.Add(new UserChatMessage(structurePrompt));

        Console.WriteLine();
        var structureResponse = await GetChatCompletionAsync("Generating Bicep structure");
        Console.WriteLine("Bicep structure generated successfully.");

        // Parse and write files
        var files = ParseFileContents(structureResponse);
        
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(outputDir, fileName);
            await File.WriteAllTextAsync(filePath, content);
            Console.WriteLine($"Generated: {fileName}");
        }

        // TODO: Iterate through modules and generate detailed implementations
        // TODO: Fix diagnostic errors
        // For now, we have the basic structure in place
        
        Console.WriteLine("\nNote: The generated files contain a basic structure. Further refinement would include:");
        Console.WriteLine("- Generating detailed module implementations");
        Console.WriteLine("- Resolving TODOs with specific resource configurations");
        Console.WriteLine("- Running diagnostics and fixing any errors");
    }

    /// <summary>
    /// Reads multi-line input terminated by a double newline
    /// </summary>
    private string ReadMultilineInput()
    {
        var lines = new List<string>();
        var emptyLineCount = 0;

        while (true)
        {
            var line = Console.ReadLine();
            
            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                emptyLineCount++;
                if (emptyLineCount >= 2)
                {
                    break;
                }
                lines.Add(line);
            }
            else
            {
                emptyLineCount = 0;
                lines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    /// <summary>
    /// Gets a chat completion from the AI
    /// </summary>
    private async Task<string> GetChatCompletionAsync(string loadingMessage = "Processing")
    {
        using var spinner = new LoadingSpinner(loadingMessage);
        var completion = await chatClient.CompleteChatAsync(_conversationHistory);
        var response = completion.Value.Content[0].Text;
        
        _conversationHistory.Add(new AssistantChatMessage(response));
        
        return response;
    }

    /// <summary>
    /// Parses file contents from AI response
    /// </summary>
    private static Dictionary<string, string> ParseFileContents(string response)
    {
        var files = new Dictionary<string, string>();
        var lines = response.Split('\n');
        string? currentFile = null;
        var currentContent = new List<string>();
        var inCodeBlock = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("FILE:"))
            {
                // Save previous file if exists
                if (currentFile != null && currentContent.Count > 0)
                {
                    files[currentFile] = string.Join('\n', currentContent).Trim();
                }

                currentFile = line.Replace("FILE:", "").Trim();
                currentContent.Clear();
                inCodeBlock = false;
            }
            else if (line.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                // Skip the code fence line
            }
            else if (currentFile != null && inCodeBlock)
            {
                currentContent.Add(line);
            }
        }

        // Save last file
        if (currentFile != null && currentContent.Count > 0)
        {
            files[currentFile] = string.Join('\n', currentContent).Trim();
        }

        return files;
    }
}
