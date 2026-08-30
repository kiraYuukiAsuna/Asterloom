using System.Text.Json;
using Asterloom.Modules.Analytics.Model;
using Asterloom.Modules.Errors;

namespace Asterloom.Modules.Analytics;

public static class AnalyticsSchemaValidator
{
    private const int MaximumSchemaLength = 100_000;
    private const int MaximumPropertiesLength = 64 * 1024;
    private const int MaximumContextLength = 32 * 1024;
    private static readonly HashSet<string> SupportedTypes =
        ["string", "number", "integer", "boolean", "object", "array", "null"];

    public static string ValidateAndNormalizeSchema(string value)
    {
        var schema = value?.Trim() ?? string.Empty;
        if (schema.Length is 0 or > MaximumSchemaLength)
        {
            throw Invalid(
                "schemaJson",
                $"Schema JSON must contain between 1 and {MaximumSchemaLength} characters.");
        }

        try
        {
            using var document = JsonDocument.Parse(schema);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("schemaJson", "Event schema must be a JSON object.");
            }

            if (root.TryGetProperty("type", out var type)
                && (type.ValueKind != JsonValueKind.String
                    || !string.Equals(type.GetString(), "object", StringComparison.Ordinal)))
            {
                throw Invalid("schemaJson", "The root schema type must be 'object'.");
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("properties", out var properties))
            {
                if (properties.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("schemaJson", "The schema properties member must be an object.");
                }

                var count = 0;
                foreach (var property in properties.EnumerateObject())
                {
                    count++;
                    if (count > 100)
                    {
                        throw Invalid("schemaJson", "An event schema accepts at most 100 properties.");
                    }

                    if (!IsPropertyNameValid(property.Name))
                    {
                        throw Invalid(
                            "schemaJson",
                            $"Property '{property.Name}' is not a valid analytics property name.");
                    }

                    propertyNames.Add(property.Name);
                    if (property.Value.ValueKind != JsonValueKind.Object
                        || !property.Value.TryGetProperty("type", out var propertyType)
                        || propertyType.ValueKind != JsonValueKind.String
                        || !SupportedTypes.Contains(propertyType.GetString() ?? string.Empty))
                    {
                        throw Invalid(
                            "schemaJson",
                            $"Property '{property.Name}' must declare one supported scalar or JSON type.");
                    }

                    if (property.Value.TryGetProperty("x-asterloom-sensitive", out var sensitive)
                        && sensitive.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        throw Invalid(
                            "schemaJson",
                            $"Property '{property.Name}' has an invalid x-asterloom-sensitive marker.");
                    }
                }
            }

            if (root.TryGetProperty("required", out var required))
            {
                if (required.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid("schemaJson", "The schema required member must be an array.");
                }

                foreach (var item in required.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String
                        || !propertyNames.Contains(item.GetString() ?? string.Empty))
                    {
                        throw Invalid(
                            "schemaJson",
                            "Every required property must be declared in schema properties.");
                    }
                }
            }

            if (root.TryGetProperty("additionalProperties", out var additionalProperties)
                && additionalProperties.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw Invalid(
                    "schemaJson",
                    "additionalProperties must be a boolean in the supported schema subset.");
            }

            return JsonSerializer.Serialize(root);
        }
        catch (JsonException exception)
        {
            throw Invalid("schemaJson", $"Schema JSON is invalid: {exception.Message}");
        }
    }

    public static string ValidateAndRedactProperties(EventSchema schema, string value)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        if (json.Length > MaximumPropertiesLength)
        {
            throw Invalid(
                "propertiesJson",
                $"Event properties must not exceed {MaximumPropertiesLength} characters.");
        }

        try
        {
            using var schemaDocument = JsonDocument.Parse(schema.SchemaJson);
            using var valueDocument = JsonDocument.Parse(json);
            if (valueDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("propertiesJson", "Event properties must be a JSON object.");
            }

            var rootSchema = schemaDocument.RootElement;
            var definitions = rootSchema.TryGetProperty("properties", out var properties)
                ? properties
                : default;
            var allowAdditional = !rootSchema.TryGetProperty("additionalProperties", out var additional)
                || additional.ValueKind != JsonValueKind.False;

            if (rootSchema.TryGetProperty("required", out var required))
            {
                foreach (var requiredProperty in required.EnumerateArray())
                {
                    var name = requiredProperty.GetString()!;
                    if (!valueDocument.RootElement.TryGetProperty(name, out _))
                    {
                        throw Invalid("propertiesJson", $"Required property '{name}' is missing.");
                    }
                }
            }

            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                foreach (var property in valueDocument.RootElement.EnumerateObject())
                {
                    if (definitions.ValueKind != JsonValueKind.Object
                        || !definitions.TryGetProperty(property.Name, out var definition))
                    {
                        if (!allowAdditional)
                        {
                            throw Invalid(
                                "propertiesJson",
                                $"Property '{property.Name}' is not declared by the event schema.");
                        }

                        property.WriteTo(writer);
                        continue;
                    }

                    var expectedType = definition.GetProperty("type").GetString()!;
                    if (!MatchesType(property.Value, expectedType))
                    {
                        throw Invalid(
                            "propertiesJson",
                            $"Property '{property.Name}' must be of type '{expectedType}'.");
                    }

                    writer.WritePropertyName(property.Name);
                    if (definition.TryGetProperty("x-asterloom-sensitive", out var sensitive)
                        && sensitive.ValueKind == JsonValueKind.True)
                    {
                        writer.WriteStringValue("[REDACTED]");
                    }
                    else
                    {
                        property.Value.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(output.ToArray());
        }
        catch (JsonException exception)
        {
            throw Invalid("propertiesJson", $"Event properties JSON is invalid: {exception.Message}");
        }
    }

    public static string ValidateAndNormalizeContext(string value)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        if (json.Length > MaximumContextLength)
        {
            throw Invalid(
                "contextJson",
                $"Event context must not exceed {MaximumContextLength} characters.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("contextJson", "Event context must be a JSON object.");
            }

            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw Invalid("contextJson", $"Event context JSON is invalid: {exception.Message}");
        }
    }

    private static bool IsPropertyNameValid(string name) =>
        name.Length is >= 1 and <= 100
        && char.IsLetter(name[0])
        && name.All(static character => char.IsLetterOrDigit(character) || character is '_' or '.' or '-');

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "analytics_validation_failed",
        "The analytics payload is invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });
}
