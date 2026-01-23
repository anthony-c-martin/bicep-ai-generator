using System.ComponentModel;
using System.Text.Json;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Bicep.Core;
using Bicep.IO.Abstraction;
using Json.More;
using ModelContextProtocol.Server;

namespace BicepGeneratorMcp.Tools;

internal class BicepDeploymentTools(
    BicepCompiler bicepCompiler,
    ArmClient armClient)
{
    [McpServerTool]
    [Description("Performs a what-if analysis for an Azure Resource Manager deployment. Returns the predicted changes that would occur if the deployment were executed, without actually deploying any resources. Use this to preview infrastructure changes before applying them.")]
    public async Task<JsonDocument> WhatIfDeploymentAsync(
        [Description("The Azure subscription ID where the deployment will be targeted (e.g., '00000000-0000-0000-0000-000000000000').")] string subsciptionId,
        [Description("The name of the resource group where resources will be deployed.")] string resourceGroupName,
        [Description("The absolute file path to the Bicep parameters file (.bicepparam) that references the main Bicep template.")] string bicepParamFilePath,
        CancellationToken cancellationToken)
    {
        var result = await bicepCompiler.CompileBicepparamFile(IOUri.FromFilePath(bicepParamFilePath));
        var parametersJson = result.Parameters ?? throw new InvalidOperationException("Failed to compile bicepparam file");
        var templateJson = result.Template?.Template ?? throw new InvalidOperationException("Failed to compile bicep file");

        // Extract just the "parameters" object from the compiled output
        // The compiled output includes $schema and contentVersion which ARM doesn't accept inline
        using var parametersDoc = JsonDocument.Parse(parametersJson);
        var parametersOnly = parametersDoc.RootElement.TryGetProperty("parameters", out var paramsElement)
            ? paramsElement.GetRawText()
            : "{}";

        var resourceGroupId = ResourceGroupResource.CreateResourceIdentifier(subsciptionId, resourceGroupName);
        var deploymentId = ArmDeploymentResource.CreateResourceIdentifier(resourceGroupId, "main");

        ArmDeploymentWhatIfContent requestBody = new(
            new(ArmDeploymentMode.Incremental)
            {
                Template = BinaryData.FromString(templateJson),
                Parameters = BinaryData.FromString(parametersOnly)
            });

        var whatIfOperation = await armClient.GetArmDeploymentResource(deploymentId)
            .WhatIfAsync(WaitUntil.Completed, requestBody, cancellationToken);

        return whatIfOperation.GetRawResponse().Content.ToString().ToJsonDocument();
    }
}
