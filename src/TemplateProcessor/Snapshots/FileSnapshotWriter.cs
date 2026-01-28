using System.Text;
using System.Text.Json;

namespace TemplateProcessor.Snapshots;

internal class FileSnapshotWriter(string outputDirectory) : ISnapshotWriter
{
    public async Task WriteSnapshot(SnapshotWithMetadata snapshot, CancellationToken cancellationToken)
    {
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotSerializationContext.FileSerializer.SnapshotWithMetadata);
        var filePath = Path.Combine(outputDirectory, $"{snapshot.Id}.json");

        await File.WriteAllTextAsync(filePath, snapshotJson, Encoding.UTF8, cancellationToken);
    }
}