using Azure.Identity;
using Azure.Storage.Blobs;
using Bicep.RpcClient;
using TemplateProcessor.Quickstarts;
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

var quickStartsPath = "/Users/ant/Code/azure-quickstart-templates";
await QuickstartsProcessor.ProcessQuickstartAsync(quickStartsPath, new BlobSnapshotWriter(containerClient), cancellationToken);