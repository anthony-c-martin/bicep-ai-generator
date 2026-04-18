using System.Text.Json.Nodes;
using Azure.Bicep.Types;
using Azure.Bicep.Types.Az;
using Azure.Bicep.Types.Concrete;

namespace BicepGeneratorEval;

public class SchemaValidator
{
    private readonly Lazy<IReadOnlyDictionary<string, CrossFileTypeReference>> _resourceIndex;
    private readonly AzTypeLoader _azTypeLoader;

    public SchemaValidator()
    {
        _azTypeLoader = new AzTypeLoader();
        _resourceIndex = new(() => _azTypeLoader.LoadTypeIndex().Resources.ToDictionary(StringComparer.OrdinalIgnoreCase));
    }

    public bool Validate(string resourceType, string apiVersion, JsonObject resourceBody, out string? error)
    {
        var key = $"{resourceType}@{apiVersion}";
        if (!_resourceIndex.Value.TryGetValue(key, out var found))
        {
            error = $"Resource type '{key}' not found in type index.";
            return false;
        }

        var resourceTypeDef = _azTypeLoader.LoadType(found) as ResourceType;
        if (resourceTypeDef is null)
        {
            error = "Failed to load resource type definition.";
            return false;
        }

        var errors = new List<string>();
        ValidateType(resourceBody, resourceTypeDef.Body.Type, "$", errors, []);

        if (errors.Count == 0)
        {
            error = null;
            return true;
        }

        error = string.Join("; ", errors.Take(10));
        return false;
    }

    private static void ValidateType(JsonNode? node, TypeBase type, string path, List<string> errors, HashSet<TypeBase> visited)
    {
        // Guard against cycles in the type graph
        if (!visited.Add(type))
            return;

        try
        {
            switch (type)
            {
                case ObjectType objectType:
                    ValidateObject(node, objectType, path, errors, visited);
                    break;
                case DiscriminatedObjectType discriminatedObjectType:
                    ValidateDiscriminatedObject(node, discriminatedObjectType, path, errors, visited);
                    break;
                case ArrayType arrayType:
                    ValidateArray(node, arrayType, path, errors, visited);
                    break;
                case StringType or StringLiteralType:
                    if (node is not JsonValue v || !v.TryGetValue<string>(out _))
                        errors.Add($"{path}: expected string");
                    if (type is StringLiteralType slt && node is JsonValue sv && sv.TryGetValue<string>(out var s) && s != slt.Value)
                        errors.Add($"{path}: expected '{slt.Value}', got '{s}'");
                    break;
                case IntegerType:
                    if (node is not JsonValue iv || (!iv.TryGetValue<int>(out _) && !iv.TryGetValue<long>(out _) && !iv.TryGetValue<double>(out _)))
                        errors.Add($"{path}: expected number");
                    break;
                case BooleanType:
                    if (node is not JsonValue bv || !bv.TryGetValue<bool>(out _))
                        errors.Add($"{path}: expected boolean");
                    break;
                case UnionType unionType:
                    ValidateUnion(node, unionType, path, errors, visited);
                    break;
                case AnyType:
                    break; // anything is valid
                case NullType:
                    if (node is not null)
                        errors.Add($"{path}: expected null");
                    break;
            }
        }
        finally
        {
            visited.Remove(type);
        }
    }

    private static void ValidateObject(JsonNode? node, ObjectType objectType, string path, List<string> errors, HashSet<TypeBase> visited)
    {
        if (node is not JsonObject obj)
        {
            errors.Add($"{path}: expected object");
            return;
        }

        var writableProps = objectType.Properties
            .Where(x => !x.Value.Flags.HasFlag(ObjectTypePropertyFlags.ReadOnly))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        // Check required properties
        foreach (var (name, prop) in writableProps)
        {
            if (prop.Flags.HasFlag(ObjectTypePropertyFlags.Required) && !obj.ContainsKey(name))
                errors.Add($"{path}: missing required property '{name}'");
        }

        // Validate each property present in the body
        foreach (var (name, value) in obj)
        {
            if (writableProps.TryGetValue(name, out var prop))
            {
                ValidateType(value, prop.Type.Type, $"{path}.{name}", errors, visited);
            }
            // Skip unknown properties - generated bodies may include read-only props like name/location
        }
    }

    private static void ValidateDiscriminatedObject(JsonNode? node, DiscriminatedObjectType discriminatedObjectType, string path, List<string> errors, HashSet<TypeBase> visited)
    {
        if (node is not JsonObject obj)
        {
            errors.Add($"{path}: expected object");
            return;
        }

        // Check base properties
        var writableBaseProps = discriminatedObjectType.BaseProperties
            .Where(x => !x.Value.Flags.HasFlag(ObjectTypePropertyFlags.ReadOnly))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, prop) in writableBaseProps)
        {
            if (prop.Flags.HasFlag(ObjectTypePropertyFlags.Required) && !obj.ContainsKey(name))
                errors.Add($"{path}: missing required property '{name}'");
        }

        // Try to match a discriminator member
        var discriminatorKey = discriminatedObjectType.Discriminator;
        if (obj.TryGetPropertyValue(discriminatorKey, out var discValue) &&
            discValue is JsonValue jv && jv.TryGetValue<string>(out var discString) &&
            discriminatedObjectType.Elements.TryGetValue(discString, out var matchedRef))
        {
            ValidateType(node, matchedRef.Type, path, errors, visited);
        }
    }

    private static void ValidateArray(JsonNode? node, ArrayType arrayType, string path, List<string> errors, HashSet<TypeBase> visited)
    {
        if (node is not JsonArray arr)
        {
            errors.Add($"{path}: expected array");
            return;
        }

        for (var i = 0; i < arr.Count; i++)
        {
            ValidateType(arr[i], arrayType.ItemType.Type, $"{path}[{i}]", errors, visited);
        }
    }

    private static void ValidateUnion(JsonNode? node, UnionType unionType, string path, List<string> errors, HashSet<TypeBase> visited)
    {
        // If all elements are string literals, validate as enum
        if (unionType.Elements.All(e => e.Type is StringLiteralType))
        {
            if (node is not JsonValue sv || !sv.TryGetValue<string>(out var str))
            {
                errors.Add($"{path}: expected string");
                return;
            }

            var allowed = unionType.Elements.Select(e => ((StringLiteralType)e.Type).Value).ToList();
            if (!allowed.Contains(str, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{path}: value '{str}' not in allowed values [{string.Join(", ", allowed.Take(5))}{(allowed.Count > 5 ? ", ..." : "")}]");
            return;
        }

        // Try each union member - pass if any succeeds
        foreach (var element in unionType.Elements)
        {
            var memberErrors = new List<string>();
            ValidateType(node, element.Type, path, memberErrors, visited);
            if (memberErrors.Count == 0)
                return;
        }

        errors.Add($"{path}: value does not match any union member");
    }
}
