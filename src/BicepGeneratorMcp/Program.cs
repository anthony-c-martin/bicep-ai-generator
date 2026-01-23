using Azure.Bicep.Types.Az;
using Azure.Core;
using Azure.Identity;
using Bicep.Core;
using BicepGeneratorMcp;
using BicepGeneratorMcp.Tools;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("The AZURE_OPENAI_ENDPOINT environment variable is not set.");

var builder = Host.CreateApplicationBuilder(args);

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddSingleton<TokenCredential, DefaultAzureCredential>()
    .AddSingleton(new Configuration(
        AzureOpenAIEndpoint: endpoint,
        DeploymentName: "gpt-4.1"))
    .AddSingleton<AiClientFactory>()
    .AddSingleton<AzTypeLoader>()
    .AddSingleton<BicepCompiler>(BicepCompiler.Create());

builder.Services
    .AddAzureClients(clientBuilder =>
    {
        clientBuilder.AddArmClient("00000000-0000-0000-0000-000000000000");
        clientBuilder.UseCredential(provider => provider.GetRequiredService<TokenCredential>());
    });

builder.Services
    .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<BicepGeneratorTools>()
        .WithTools<BicepDeploymentTools>()
        .AddCallToolFilter((next) => async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken);
            }
            catch (Exception ex)
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Error: {ex.Message}" }],
                    IsError = true
                };
            }
        });

await builder.Build().RunAsync();
