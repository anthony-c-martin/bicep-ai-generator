namespace BicepGeneratorMcp.Helpers;

public static class TextHelper
{
    public static string ExtractBetweenTags(string text, string tagName)
        => TryExtractBetweenTags(text, tagName) ?? throw new InvalidOperationException($"Tag <{tagName}> not found in the provided text.");

    public static string? TryExtractBetweenTags(string text, string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";

        var startIndex = text.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
        {
            return null;
        }

        startIndex += openTag.Length;
        var endIndex = text.IndexOf(closeTag, startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex == -1)
        {
            return null;
        }

        return text[startIndex..endIndex];
    }
}