using Azure.Identity;
using Azure.Storage.Blobs;
using Bicep.RpcClient;
using TemplateProcessor.Processors;
using TemplateProcessor.Snapshots;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    cts.Cancel();
};

var cancellationToken = cts.Token;

var clientFactory = new BicepClientFactory(new HttpClient());
using var bicep = await clientFactory.DownloadAndInitialize(new(), cancellationToken);

var credential = new DefaultAzureCredential();
var containerClient = new BlobContainerClient(new Uri("https://mcpaitest.blob.core.windows.net/snapshots"), credential);
var snapshotWriter = new BlobSnapshotWriter(containerClient);

var quickStartsPath = "/Users/ant/Code/azure-quickstart-templates";
await QuickstartsProcessor.ProcessAsync(quickStartsPath, snapshotWriter, cancellationToken);

var avmPath = "/Users/ant/Code/bicep-registry-modules";
await AvmProcessor.ProcessAsync(avmPath, snapshotWriter, cancellationToken);