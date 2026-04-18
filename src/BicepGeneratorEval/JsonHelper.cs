using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BicepGeneratorEval;

internal static class JsonHelper
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [return: NotNullIfNotNull(nameof(value))]
    public static string? SerializeIndented<T>(T? value)
        => value is not null ? JsonSerializer.Serialize(value, IndentedOptions) : null;
}
