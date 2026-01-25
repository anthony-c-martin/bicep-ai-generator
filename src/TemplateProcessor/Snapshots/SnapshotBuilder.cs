using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Azure.Deployments.Templates.Engines.NestedDeploymentExpansion;
using Azure.Deployments.Templates.Expressions.PartialEvaluation;
using Azure.Deployments.Core.Configuration;
using Azure.Deployments.Core.Definitions;
using Azure.Deployments.Core.Definitions.Schema;
using Azure.Deployments.Core.Entities;
using Azure.Deployments.Expression.Intermediate;
using Azure.Deployments.Expression.Intermediate.Extensions;
using Azure.Deployments.Templates.Engines;
using Azure.Deployments.Templates.ParsedEntities;
using Microsoft.WindowsAzure.ResourceStack.Common.Json;
using Newtonsoft.Json.Linq;
using Azure.Deployments.Templates.Extensions;
using ExpressionBuiltInFunctions = Azure.Deployments.Expression.Expressions.ExpressionBuiltInFunctions;

namespace TemplateProcessor.Snapshots;
internal class SnapshotBuilder
{
    public record DeploymentData(
        string TemplateContents,
        string? ParametersContents,
        string? TenantId,
        string? SubscriptionId,
        string? ResourceGroup,
        string? Location,
        string? DeploymentName);

    public static async Task<Snapshot> GetSnapshot(DeploymentData deployment, CancellationToken cancellationToken)
    {
        Dictionary<string, DeploymentParameterDefinition> parameters = deployment.ParametersContents?.FromJson<DeploymentParametersDefinition>().Parameters ?? new();
        var template = TemplateEngine.ParseTemplate(deployment.TemplateContents);

        var deploymentMetadata = GetDeploymentMetadata(deployment, template);

        ExpressionEvaluationContext evaluationContext = new([
            ExpressionBuiltInFunctions.Functions,
            ResourceIdScope.Functions,
            new DeploymentMetadataScope(template.ParsedLanguageVersion, deploymentMetadata),
        ], null);

        var expansionResult = await TemplateEngine.ExpandNestedDeployments(
            "2025-04-01",
            TemplateDeploymentScope.ResourceGroup,
            template,
            parameters: GetParameters(template, deploymentMetadata, parameters),
            rootDeploymentMetadata: deploymentMetadata,
            referenceFunctionPreflightEnabled: true,
            contentLinkResolver: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new(
            [
                .. expansionResult.preflightResources.Select(x => JsonNode.Parse(DeploymentPreflightResourceWithParsedExpressions.From(x).ToJson())!),
                .. expansionResult.extensibleResources.Select(x => JsonNode.Parse(JsonExtensions.ToJson(new DeploymentWhatIfExtensibleResource
                    {
                        Type = x.Type,
                        ApiVersion = x.ApiVersion,
                        Identifiers = x.Identifiers?.ToObject<JObject>(),
                        Properties = x.Properties?.ToObject<JObject>(),
                    }))!),
            ],
            [
                .. expansionResult.diagnostics.Select(d => $"{d.Target} {d.Level} {d.Code}: {d.Message}")
            ]);
    }

    private static ITemplateLanguageExpression CreateConcatExpression(params ITemplateLanguageExpression[] expressions)
        => new FunctionExpression("concat", expressions.ToImmutableArray(), position: null);

    private static ITemplateLanguageExpression CreateUnresolvedExpression(string type, string value)
        => new FunctionExpression(
            "unresolved",
            [type.AsExpression(), value.AsExpression()],
            [],
            null,
            irreducible: true);

    private static IReadOnlyDictionary<string, ITemplateLanguageExpression> GetParameters(Template template, IReadOnlyDictionary<string, ITemplateLanguageExpression> deploymentMetadata, Dictionary<string, DeploymentParameterDefinition> parameters)
    {
        var parsedParameters = TemplateEngine.ConvertParametersDict(
            parameters,
            externalInputs: null,
            TemplateDeploymentScope.ResourceGroup,
            template.ContentVersion.Value,
            template.ParsedLanguageVersion,
            template.TryGetTemplateMetadata(),
            deploymentMetadata);

        var result = new Dictionary<string, ITemplateLanguageExpression>();

        foreach (var param in template.Parameters ?? [])
        {
            if (parsedParameters.TryGetValue(param.Key, out var value))
            {
                result[param.Key] = value;
            }
            else if (param.Value.DefaultValue == null)
            {
                result[param.Key] = CreateUnresolvedExpression("parameter", param.Key);
            }
        }

        return result;
        
    }

    private static IReadOnlyDictionary<string, ITemplateLanguageExpression> GetDeploymentMetadata(
        DeploymentData deployment,
        Template template)
    {
        var tenantId = deployment.TenantId?.AsExpression() ?? MetadataPlaceholder("tenant", "tenantId");
        var subscriptionId = deployment.SubscriptionId?.AsExpression() ?? MetadataPlaceholder("subscription", "id");
        var resourceGroup = deployment.ResourceGroup?.AsExpression() ?? MetadataPlaceholder("resourceGroup", "name");
        var location = deployment.Location?.AsExpression() ?? MetadataPlaceholder("resourceGroup", "location");
        var deploymentName = deployment.DeploymentName?.AsExpression() ?? MetadataPlaceholder("deployment", "name");

        var tenantMetadata = new ObjectExpression([
            new("countryCode".AsExpression(), MetadataPlaceholder("tenant", "countryCode")),
            new("displayName".AsExpression(), MetadataPlaceholder("tenant", "displayName")),
            new("id".AsExpression(), CreateConcatExpression("/tenants/".AsExpression(), tenantId)),
            new("tenantId".AsExpression(), tenantId),
        ],  position: null);

        return new DeploymentMetadataSynthesizer(null, tenantMetadata, null)
            .SynthesizeResourceGroupScopedMetadata(
                resourceGroupName: resourceGroup,
                resourceGroupLocation: location,
                subscriptionId: subscriptionId,
                languageVersion: template.ParsedLanguageVersion,
                contentVersion: template.ContentVersion.Value,
                templateLink: null,
                deploymentName: deploymentName,
                templateMetadata: null,
                position: null);
    }

    private static ITemplateLanguageExpression MetadataPlaceholder(string name, params string[] properties)
        => new FunctionExpression(name, [], [.. properties.Select(p => p.AsExpression())], null, irreducible: true);
}
