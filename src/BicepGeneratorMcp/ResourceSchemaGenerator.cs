using System.Diagnostics;
using System.Text.Json.Nodes;
using Azure.Bicep.Types.Concrete;
using Json.More;

namespace BicepGeneratorMcp;

public static class ResourceSchemaGenerator
{
    private class SchemaContext
    {
        public Dictionary<TypeBase, string> TypeDefinitionNames { get; } = [];
        public JsonObject Definitions { get; } = [];
        public HashSet<TypeBase> ProcessingTypes { get; } = [];

        public string GetOrCreateDefinitionName(TypeBase type)
        {
            if (TypeDefinitionNames.TryGetValue(type, out var existingName))
            {
                return existingName;
            }

            var baseName = type switch
            {
                ObjectType obj => obj.Name,
                DiscriminatedObjectType disc => disc.Name,
                _ => throw new UnreachableException(),
            };
            string uniqueName = baseName;
            int suffix = 1;

            // Ensure uniqueness
            while (TypeDefinitionNames.Values.Contains(uniqueName))
            {
                uniqueName = $"{baseName}_{suffix++}";
            }

            TypeDefinitionNames[type] = uniqueName;
            return uniqueName;
        }
    }

    public static JsonObject ToJsonSchema(TypeBase type)
    {
        var context = new SchemaContext();
        var schema = ToJsonSchemaWithContext(type, context);

        // If we generated any definitions, wrap the schema
        if (context.Definitions.Count > 0)
        {
            var wrappedSchema = new JsonObject();
            
            // Copy all properties from the root schema
            foreach (var prop in schema)
            {
                wrappedSchema[prop.Key] = prop.Value?.DeepClone();
            }
            
            // Add definitions section
            wrappedSchema["definitions"] = context.Definitions;
            
            return wrappedSchema;
        }

        return schema;
    }

    private static JsonObject ToJsonSchemaWithContext(TypeBase type, SchemaContext context)
    {
        // Check if this is a named type that should be in definitions
        if (ShouldUseDefinition(type))
        {
            var defName = context.GetOrCreateDefinitionName(type);
            
            // If we're already processing this type, return a ref immediately (cycle detected)
            if (context.ProcessingTypes.Contains(type))
            {
                return new JsonObject
                {
                    ["$ref"] = $"#/definitions/{defName}"
                };
            }

            // If we haven't generated the definition yet, generate it
            if (!context.Definitions.ContainsKey(defName))
            {
                context.ProcessingTypes.Add(type);
                var definition = ToJsonSchemaInternal(type, context);
                context.Definitions[defName] = definition;
                context.ProcessingTypes.Remove(type);
            }

            // Return a reference
            return new JsonObject
            {
                ["$ref"] = $"#/definitions/{defName}"
            };
        }

        return ToJsonSchemaInternal(type, context);
    }

    private static bool ShouldUseDefinition(TypeBase type) => type switch
    {
        ObjectType obj when !string.IsNullOrEmpty(obj.Name) => true,
        DiscriminatedObjectType => true,
        _ => false
    };

    private static JsonObject ToJsonSchemaInternal(TypeBase type, SchemaContext context)
    {
        switch (type)
        {
            case StringType _:
                return new JsonObject
                {
                    ["type"] = "string"
                };
            case StringLiteralType stringLiteral:
                return new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { stringLiteral.Value }
                };
            case UnionType unionType:
            {
                if (unionType.Elements.All(e => e.Type is StringLiteralType))
                {
                    var enumValues = unionType.Elements.Select(e => e.Type).OfType<StringLiteralType>();
                    return new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = enumValues.Select(e => JsonValue.Create(e.Value)).ToJsonArray(),
                    };
                }

                var elements = unionType.Elements.Select(x => ToJsonSchemaWithContext(x.Type, context));
                return new JsonObject
                {
                    ["oneOf"] = elements.ToJsonArray(),
                };
            }
            case IntegerType _:
                return new JsonObject
                {
                    ["type"] = "number"
                };
            case BooleanType _:
                return new JsonObject
                {
                    ["type"] = "boolean"
                };
            case ArrayType arrayType:
                return new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = ToJsonSchemaWithContext(arrayType.ItemType.Type, context)
                };
            case ObjectType objectType:
            {
                var writableProps = objectType.Properties.Where(x => !x.Value.Flags.HasFlag(ObjectTypePropertyFlags.ReadOnly));
                var requiredProps = writableProps.Where(x => x.Value.Flags.HasFlag(ObjectTypePropertyFlags.Required));

                var properties = writableProps.Select(x => new KeyValuePair<string, JsonNode?>(x.Key, ToJsonSchemaWithContext(x.Value.Type.Type, context)));
                
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(properties),
                };

                var requiredArray = requiredProps.Select(x => JsonValue.Create(x.Key)).ToArray();
                if (requiredArray.Length > 0)
                {
                    schema["required"] = new JsonArray(requiredArray);
                }

                return schema;
            }
            case DiscriminatedObjectType discriminatedObjectType:
            {
                var writableProps = discriminatedObjectType.BaseProperties.Where(x => !x.Value.Flags.HasFlag(ObjectTypePropertyFlags.ReadOnly));
                var requiredProps = writableProps.Where(x => x.Value.Flags.HasFlag(ObjectTypePropertyFlags.Required));

                var members = discriminatedObjectType.Elements.Select(x => ToJsonSchemaWithContext(x.Value.Type, context));

                var properties = writableProps.Select(x => new KeyValuePair<string, JsonNode?>(x.Key, ToJsonSchemaWithContext(x.Value.Type.Type, context)));
                
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(properties),
                    ["oneOf"] = members.ToJsonArray(),
                };

                var requiredArray = requiredProps.Select(x => JsonValue.Create(x.Key)).ToArray();
                if (requiredArray.Length > 0)
                {
                    schema["required"] = new JsonArray(requiredArray);
                }

                return schema;
            }
            case AnyType _:
                return new JsonObject();
            case NullType _:
                return new JsonObject
                {
                    ["type"] = "null"
                };
            default:
                throw new NotImplementedException($"{type.GetType()}");
        }
    }
}