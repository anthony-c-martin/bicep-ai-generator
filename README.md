# Bicep Generator

A CLI application that generates Azure Bicep infrastructure-as-code files from natural language descriptions using Azure OpenAI.

## Features

- Interactive requirements gathering with AI-powered architecture design
- Automatic generation of Bicep files (`.bicep` and `.bicepparam`)
- Extensible Bicep tools interface for future enhancements

## Prerequisites

- .NET 10.0 SDK
- Azure OpenAI resource with a deployed model (e.g., GPT-4)
- Bicep CLI (optional, for future tool implementations)

## Setup

### Environment Variables

Set the following environment variables before running the application:

```bash
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT="your-deployment-name"
```

Replace the values with your actual Azure OpenAI credentials:
- `AZURE_OPENAI_ENDPOINT`: Your Azure OpenAI resource endpoint
- `AZURE_OPENAI_API_KEY`: Your Azure OpenAI API key
- `AZURE_OPENAI_DEPLOYMENT`: Your deployment name (e.g., "gpt-4")

## Running the Application

```bash
dotnet run
```

## Usage

1. **Describe your infrastructure**: When prompted, type your Azure infrastructure requirements. For example:
   ```
   Create a CosmosDB account with a SQL database and container
   ```

2. **Submit your input**: Press Enter twice (double newline) to submit your description.

3. **Answer clarifying questions**: If the AI needs more information, it will ask follow-up questions. Provide the requested details.

4. **Review and confirm**: The application will show you a summary of the planned architecture. Confirm with 'yes' to proceed.

5. **Get your files**: The generated Bicep files will be written to the `output/` folder in the current directory.

## Project Structure

- `Program.cs` - Main entry point and Azure OpenAI client setup
- `BicepGeneratorOrchestrator.cs` - Orchestrates the workflow (requirements gathering and file generation)
- `IBicepTools.cs` - Interface for Bicep CLI and tooling operations
- `BicepTools.cs` - Stub implementation (to be completed)
- `output/` - Generated Bicep files (created at runtime)

## Workflow

### Phase 1: Requirements Gathering
1. Collects initial description from user
2. Generates an architecture design document using AI
3. Asks for clarification if needed
4. Shows summary and requests confirmation

### Phase 2: Bicep Generation
1. Creates `main.bicep` and `main.bicepparam` files
2. Generates high-level structure with resource/module declarations
3. Writes files to the `output/` folder

## Extending the Application

The `IBicepTools` interface provides methods for:
- `FormatBicepFileAsync` - Format Bicep files using the CLI
- `GetAzResourceTypeSchemaAsync` - Fetch resource type schemas
- `GetBicepBestPracticesAsync` - Retrieve best practices documentation
- `GetBicepFileDiagnosticsAsync` - Get compilation diagnostics
- `GetSnapshotAsync` - Predict deployment operations
- `ListAzResourceTypesForProviderAsync` - List available resource types

Implement these methods in `BicepTools.cs` to enable advanced features like diagnostics checking and iterative refinement.

## Example Output

After running the application, you'll find files like:

- `output/main.bicep` - Main Bicep template with resource declarations
- `output/main.bicepparam` - Parameter file with configuration values

## Notes

- The current implementation generates a basic structure. Future enhancements could include:
  - Module generation for complex architectures
  - Diagnostic error checking and auto-fixing
  - Iterative refinement based on Bicep compilation errors
  - Integration with Azure Resource Graph for validation
