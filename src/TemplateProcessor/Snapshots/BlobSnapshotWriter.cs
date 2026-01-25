using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace TemplateProcessor.Snapshots;

internal class BlobSnapshotWriter(BlobContainerClient containerClient) : ISnapshotWriter
{
    public async Task WriteSnapshot(SnapshotWithMetadata snapshot, CancellationToken cancellationToken)
    {
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotSerializationContext.FileSerializer.SnapshotWithMetadata);
        var blobClient = containerClient.GetBlobClient(snapshot.Id.ToString());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(snapshotJson));
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
        }, cancellationToken);
    }
}