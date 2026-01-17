using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Bicep.Types.Az;
using Azure.Bicep.Types.Index;
using Bicep.Core;
using Bicep.Core.Extensions;
using Bicep.Core.PrettyPrintV2;
using Bicep.Core.TypeSystem.Providers.Az;
using Bicep.IO.Abstraction;

namespace BicepGenerator;

/// <summary>
/// Implementation of IBicepTools that calls the Bicep CLI and interacts with Azure Resource Manager
/// </summary>
public class BicepTools(
    BicepCompiler compiler,
    AzTypeLoader azTypeLoader) : IBicepTools
{
    public record DiagnosticDefinition(
        Uri FileUri,
        string Code,
        string Level,
        string Message,
        Uri? DocumentationUri,
        int Position,
        int Length);

    private Lazy<TypeIndex> AzTypeIndexLazy { get; } = new(() => azTypeLoader.LoadTypeIndex());

    private static Lazy<BinaryData> BestPracticesMarkdownLazy { get; } = new(() =>
        BinaryData.FromStream(
            typeof(BicepTools).Assembly.GetManifestResourceStream("Files/bestpractices.md") ??
            throw new InvalidOperationException("Could not find embedded resource 'Files/bestpractices.md'")));

    public async Task<string> FormatBicepFile(string filePath)
    {
        var fileUri = IOUri.FromFilePath(filePath);
        if (!fileUri.HasBicepExtension() && !fileUri.HasBicepParamExtension())
        {
            throw new ArgumentException("The specified file must have a .bicep or .bicepparam extension.", nameof(filePath));
        }

        var compilation = await compiler.CreateCompilation(fileUri);
        var sourceFile = compilation.GetEntrypointSemanticModel().SourceFile;

        var options = sourceFile.Configuration.Formatting.Data;
        var context = PrettyPrinterV2Context.Create(options, sourceFile.LexingErrorLookup, sourceFile.ParsingErrorLookup);

        return PrettyPrinterV2.Print(sourceFile.ProgramSyntax, context);
    }

    public string GetBicepBestPractices()
        => BestPracticesMarkdownLazy.Value.ToString();

    public async Task<ImmutableArray<DiagnosticDefinition>> GetBicepFileDiagnostics(string filePath)
    {
        var fileUri = IOUri.FromFilePath(filePath);
        if (!fileUri.HasBicepExtension() && !fileUri.HasBicepParamExtension())
        {
            throw new ArgumentException("The specified file must have a .bicep or .bicepparam extension.", nameof(filePath));
        }

        var compilation = await compiler.CreateCompilation(fileUri);

        return [.. compilation
            .GetAllDiagnosticsByBicepFile()
            .SelectMany(kvp => kvp.Value.Select(x => new DiagnosticDefinition(
                kvp.Key.FileHandle.Uri.ToUri(),
                x.Code,
                x.Level.ToString(),
                x.Message,
                x.Uri,
                x.Span.Position,
                x.Span.Length)))];
    }

    public ImmutableArray<(string Name, string ApiVersion)> ListAzResourceTypes()
        => [.. AzTypeIndexLazy.Value.Resources
            .Select(x => x.Key.Split('@'))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0], parts[1]))];

    public string GetAzResourceTypeSchema(string resourceType, string apiVersion)
    {
        var type = azTypeLoader.LoadTypeIndex().Resources.TryGetValue($"{resourceType}@{apiVersion}", out var found)
            ? azTypeLoader.LoadType(found)
            : throw new ArgumentException($"The specified resource type '{resourceType}' was not found.", nameof(resourceType));

        var schema = ResourceSchemaGenerator.ToJsonSchemaRecursive(type);
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
