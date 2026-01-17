using System.Collections.Immutable;

namespace BicepGenerator;

/// <summary>
/// Interface for interacting with Bicep tools
/// </summary>
public interface IBicepTools
{
    /// <summary>
    /// Calls bicep CLI for formatting
    /// </summary>
    Task<string> FormatBicepFile(string filePath);

    /// <summary>
    /// Gets a markdown file containing a set of best practices
    /// </summary>
    string GetBicepBestPractices();

    /// <summary>
    /// Gets diagnostics for a bicep file
    /// </summary>
    Task<ImmutableArray<BicepTools.DiagnosticDefinition>> GetBicepFileDiagnostics(string filePath);

    /// <summary>
    /// Gets a list of available resource types
    /// </summary>
    ImmutableArray<(string Name, string ApiVersion)> ListAzResourceTypes();

    /// <summary>
    /// Fetches type information for a particular resource type
    /// </summary>
    string GetAzResourceTypeSchema(string resourceType, string apiVersion);
}
