using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;

namespace BicepGeneratorEval;

public class AzureResourceValidator
{
    private readonly ArmClient _armClient;
    private readonly string _subscriptionId;
    private readonly string _resourceGroupName;

    public AzureResourceValidator(TokenCredential credential, string subscriptionId, string resourceGroupName)
    {
        _armClient = new ArmClient(credential);
        _subscriptionId = subscriptionId;
        _resourceGroupName = resourceGroupName;
    }

    public async Task<(bool Success, string? Error)> ValidateAsync(
        string resourceType, string apiVersion, JsonObject resourceBody, CancellationToken cancellationToken)
    {
        try
        {
            // Build a minimal ARM template that deploys this single resource
            var template = new JsonObject
            {
                ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
                ["contentVersion"] = "1.0.0.0",
                ["resources"] = new JsonArray
                {
                    BuildResourceDefinition(resourceType, apiVersion, resourceBody)
                }
            };

            var templateJson = JsonSerializer.Serialize(template);

            var resourceGroupId = ResourceGroupResource.CreateResourceIdentifier(_subscriptionId, _resourceGroupName);
            var deploymentName = $"eval-validate-{Guid.NewGuid():N}";
            var deploymentId = ArmDeploymentResource.CreateResourceIdentifier(resourceGroupId, deploymentName);

            var content = new ArmDeploymentContent(
                new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
                {
                    Template = BinaryData.FromString(templateJson),
                });

            var validationResult = await _armClient.GetArmDeploymentResource(deploymentId)
                .ValidateAsync(Azure.WaitUntil.Completed, content, cancellationToken);

            var response = validationResult.Value;
            if (response.Error is not null)
            {
                return (false, $"Validation error: {response.Error.Code} - {response.Error.Message}");
            }

            return (true, null);
        }
        catch (Azure.RequestFailedException ex)
        {
            return (false, FormatRequestFailedError(ex));
        }
        catch (Exception ex)
        {
            return (false, $"Validation error: {ex.Message}");
        }
    }

    private static JsonObject BuildResourceDefinition(string resourceType, string apiVersion, JsonObject resourceBody)
    {
        var resource = new JsonObject
        {
            ["type"] = resourceType,
            ["apiVersion"] = apiVersion,
            ["name"] = resourceBody["name"]?.GetValue<string>() ?? "eval-test-resource",
            ["location"] = resourceBody["location"]?.GetValue<string>() ?? "eastus"
        };

        if (resourceBody.TryGetPropertyValue("properties", out var props))
            resource["properties"] = props?.DeepClone();

        if (resourceBody.TryGetPropertyValue("sku", out var sku))
            resource["sku"] = sku?.DeepClone();

        if (resourceBody.TryGetPropertyValue("kind", out var kind))
            resource["kind"] = kind?.DeepClone();

        if (resourceBody.TryGetPropertyValue("identity", out var identity))
            resource["identity"] = identity?.DeepClone();

        if (resourceBody.TryGetPropertyValue("tags", out var tags))
            resource["tags"] = tags?.DeepClone();

        return resource;
    }

    private static string FormatRequestFailedError(Azure.RequestFailedException ex)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };

        if (ex.GetRawResponse()?.Content is { } content)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(content);

                // Extract inner details from PreflightValidationCheckFailed
                if (json.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("details", out var details) &&
                    details.GetArrayLength() > 0)
                {
                    var first = details[0];
                    if (first.TryGetProperty("code", out var code) &&
                        code.GetString() == "PreflightValidationCheckFailed" &&
                        first.TryGetProperty("details", out var innerDetails))
                    {
                        return JsonSerializer.Serialize(innerDetails, options);
                    }
                }

                return JsonSerializer.Serialize(json, options);
            }
            catch { }
        }

        return ex.Message;
    }
}
