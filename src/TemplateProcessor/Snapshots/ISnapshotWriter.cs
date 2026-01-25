namespace TemplateProcessor.Snapshots;

internal interface ISnapshotWriter
{
    Task WriteSnapshot(SnapshotWithMetadata entry, CancellationToken cancellationToken);
}
