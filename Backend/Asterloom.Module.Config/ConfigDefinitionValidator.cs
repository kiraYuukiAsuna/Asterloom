using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;

namespace Asterloom.Modules.Config;

public sealed class ConfigDefinitionValidator(ITargetingStore targetingStore)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> SecretLikeSegments = new(
        [
            "apikey",
            "certificate",
            "connectionstring",
            "credential",
            "password",
            "privatekey",
            "secret",
            "token",
        ],
        StringComparer.OrdinalIgnoreCase);

    public async Task<ConfigValidationResult> ValidateAsync(
        ConfigEntry entry,
        ConfigDefinition definition,
        CancellationToken cancellationToken)
    {
        var issues = ValidateShape(entry, definition).ToList();
        JsonDocument? schemaDocument = null;
        if (!string.IsNullOrWhiteSpace(definition.SchemaJson))
        {
            try
            {
                schemaDocument = JsonDocument.Parse(definition.SchemaJson);
                if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(Error(
                        "schema_not_object",
                        "schemaJson",
                        "Configuration schema must be a JSON object."));
                }
                else
                {
                    ValidateSchemaCompatibility(
                        entry.ValueKind,
                        schemaDocument.RootElement,
                        issues);
                    ValidateValueAgainstSchema(
                        definition.DefaultValue,
                        schemaDocument.RootElement,
                        "defaultValue",
                        issues);
                    for (var index = 0; index < definition.TargetingRules.Count; index++)
                    {
                        ValidateValueAgainstSchema(
                            definition.TargetingRules[index].Value,
                            schemaDocument.RootElement,
                            $"targetingRules[{index}].value",
                            issues);
                    }
                }
            }
            catch (JsonException exception)
            {
                issues.Add(Error(
                    "schema_invalid_json",
                    "schemaJson",
                    $"Configuration schema is invalid JSON: {exception.Message}"));
            }
            finally
            {
                schemaDocument?.Dispose();
            }
        }
        else
        {
            issues.Add(new ConfigValidationIssue(
                ConfigValidationSeverity.Warning,
                "schema_missing",
                "schemaJson",
                "No JSON Schema is attached; only the declared value type is enforced."));
        }

        await ValidateTargetingRulesAsync(entry, definition, issues, cancellationToken);
        return new(
            !issues.Any(static issue => issue.Severity == ConfigValidationSeverity.Error),
            issues,
            ComputeHash(definition));
    }

    public static void EnsureDraftSafety(ConfigDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.DefaultValue);
        ArgumentNullException.ThrowIfNull(definition.TargetingRules);
        if (definition.TargetingRules.Count > 50)
        {
            throw new ArgumentException(
                "A configuration definition accepts at most 50 targeting rules.");
        }

        if (definition.SchemaJson.Length > 100_000)
        {
            throw new ArgumentException("Configuration schema must not exceed 100,000 characters.");
        }
    }

    public static string ComputeHash(ConfigDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static IEnumerable<ConfigValidationIssue> ValidateShape(
        ConfigEntry entry,
        ConfigDefinition definition)
    {
        string? safetyError = null;
        try
        {
            EnsureDraftSafety(definition);
        }
        catch (ArgumentException exception)
        {
            safetyError = exception.Message;
        }

        if (safetyError is not null)
        {
            yield return Error("definition_limits", "definition", safetyError);
            yield break;
        }

        if (definition.DefaultValue.Kind != entry.ValueKind)
        {
            yield return Error(
                "default_type_mismatch",
                "defaultValue",
                $"Default value must be {entry.ValueKind}.");
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definition.TargetingRules.Count; index++)
        {
            var rule = definition.TargetingRules[index];
            if (string.IsNullOrWhiteSpace(rule.Id)
                || rule.Id.Length > 100
                || rule.Id.Any(char.IsControl)
                || !ruleIds.Add(rule.Id))
            {
                yield return Error(
                    "targeting_rule_id_invalid",
                    $"targetingRules[{index}].id",
                    "Targeting rule IDs must be unique stable values of 1-100 characters.");
            }

            if (rule.SegmentId == Guid.Empty)
            {
                yield return Error(
                    "targeting_segment_invalid",
                    $"targetingRules[{index}].segmentId",
                    "A valid targeting segment is required.");
            }

            if (rule.Value.Kind != entry.ValueKind)
            {
                yield return Error(
                    "targeting_value_type_mismatch",
                    $"targetingRules[{index}].value",
                    $"Targeted value must be {entry.ValueKind}.");
            }
        }

        if (entry.Visibility == ConfigVisibility.Client && IsSecretLike(entry.Key))
        {
            yield return Error(
                "client_secret_like_key",
                "key",
                "Secret-like configuration keys cannot be published to clients. Use a secret manager or server visibility.");
        }
    }

    private async Task ValidateTargetingRulesAsync(
        ConfigEntry entry,
        ConfigDefinition definition,
        List<ConfigValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < definition.TargetingRules.Count; index++)
        {
            var rule = definition.TargetingRules[index];
            if (rule.SegmentId == Guid.Empty)
            {
                continue;
            }

            var segment = await targetingStore.GetSegmentAsync(
                entry.TenantId,
                entry.ApplicationId,
                entry.EnvironmentId,
                rule.SegmentId,
                cancellationToken);
            if (segment?.Status != TargetingResourceStatus.Active)
            {
                issues.Add(Error(
                    "targeting_segment_unavailable",
                    $"targetingRules[{index}].segmentId",
                    "Targeting segment must exist and be active in the same environment."));
            }
        }
    }

    private static void ValidateSchemaCompatibility(
        ConfigValueKind valueKind,
        JsonElement schema,
        List<ConfigValidationIssue> issues)
    {
        if (schema.TryGetProperty("$ref", out _))
        {
            issues.Add(Error(
                "schema_ref_unsupported",
                "schemaJson.$ref",
                "External or recursive JSON Schema references are not supported."));
        }

        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        if (typeElement.ValueKind != JsonValueKind.String)
        {
            issues.Add(Error(
                "schema_type_invalid",
                "schemaJson.type",
                "Schema type must be a single JSON Schema type string."));
            return;
        }

        var expected = valueKind switch
        {
            ConfigValueKind.Truth => "boolean",
            ConfigValueKind.WholeNumber => "integer",
            ConfigValueKind.DecimalNumber => "number",
            ConfigValueKind.Text => "string",
            ConfigValueKind.Structure => "object",
            _ => string.Empty,
        };
        if (!string.Equals(typeElement.GetString(), expected, StringComparison.Ordinal))
        {
            issues.Add(Error(
                "schema_type_mismatch",
                "schemaJson.type",
                $"Schema type must be '{expected}' for a {valueKind} configuration."));
        }
    }

    private static void ValidateValueAgainstSchema(
        ConfigValue value,
        JsonElement schema,
        string path,
        List<ConfigValidationIssue> issues)
    {
        try
        {
            using var valueDocument = JsonDocument.Parse(value.ToCanonicalJson());
            ValidateElement(valueDocument.RootElement, schema, path, issues, depth: 0);
        }
        catch (JsonException exception)
        {
            issues.Add(Error(
                "value_invalid_json",
                path,
                $"Configuration value cannot be validated: {exception.Message}"));
        }
    }

    private static void ValidateElement(
        JsonElement value,
        JsonElement schema,
        string path,
        List<ConfigValidationIssue> issues,
        int depth)
    {
        if (depth > 32)
        {
            issues.Add(Error(
                "schema_depth_exceeded",
                path,
                "Schema validation exceeds the supported depth of 32."));
            return;
        }

        if (schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && !MatchesType(value, type.GetString()!))
        {
            issues.Add(Error(
                "schema_value_type_mismatch",
                path,
                $"Value does not match schema type '{type.GetString()}'."));
            return;
        }

        if (schema.TryGetProperty("enum", out var enumeration)
            && enumeration.ValueKind == JsonValueKind.Array
            && !enumeration.EnumerateArray().Any(candidate => JsonElement.DeepEquals(
                candidate,
                value)))
        {
            issues.Add(Error(
                "schema_enum_mismatch",
                path,
                "Value is not one of the schema's allowed values."));
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                ValidateString(value.GetString()!, schema, path, issues);
                break;
            case JsonValueKind.Number:
                ValidateNumber(value, schema, path, issues);
                break;
            case JsonValueKind.Object:
                ValidateObject(value, schema, path, issues, depth);
                break;
            case JsonValueKind.Array:
                ValidateArray(value, schema, path, issues, depth);
                break;
        }
    }

    private static void ValidateString(
        string value,
        JsonElement schema,
        string path,
        List<ConfigValidationIssue> issues)
    {
        if (TryGetNonNegativeInteger(schema, "minLength", out var minimum)
            && value.Length < minimum)
        {
            issues.Add(Error(
                "schema_min_length",
                path,
                $"Value must contain at least {minimum} characters."));
        }

        if (TryGetNonNegativeInteger(schema, "maxLength", out var maximum)
            && value.Length > maximum)
        {
            issues.Add(Error(
                "schema_max_length",
                path,
                $"Value must contain at most {maximum} characters."));
        }

        if (schema.TryGetProperty("pattern", out var pattern)
            && pattern.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!Regex.IsMatch(
                        value,
                        pattern.GetString()!,
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100)))
                {
                    issues.Add(Error(
                        "schema_pattern_mismatch",
                        path,
                        "Value does not match the schema pattern."));
                }
            }
            catch (ArgumentException)
            {
                issues.Add(Error(
                    "schema_pattern_invalid",
                    "schemaJson.pattern",
                    "Schema pattern is not a valid regular expression."));
            }
            catch (RegexMatchTimeoutException)
            {
                issues.Add(Error(
                    "schema_pattern_timeout",
                    "schemaJson.pattern",
                    "Schema pattern is too expensive to evaluate safely."));
            }
        }
    }

    private static void ValidateNumber(
        JsonElement value,
        JsonElement schema,
        string path,
        List<ConfigValidationIssue> issues)
    {
        var number = value.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum)
            && minimum.TryGetDouble(out var minimumValue)
            && number < minimumValue)
        {
            issues.Add(Error("schema_minimum", path, $"Value must be at least {minimumValue}."));
        }

        if (schema.TryGetProperty("maximum", out var maximum)
            && maximum.TryGetDouble(out var maximumValue)
            && number > maximumValue)
        {
            issues.Add(Error("schema_maximum", path, $"Value must be at most {maximumValue}."));
        }
    }

    private static void ValidateObject(
        JsonElement value,
        JsonElement schema,
        string path,
        List<ConfigValidationIssue> issues,
        int depth)
    {
        if (schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var requiredName in required.EnumerateArray())
            {
                if (requiredName.ValueKind == JsonValueKind.String
                    && !value.TryGetProperty(requiredName.GetString()!, out _))
                {
                    issues.Add(Error(
                        "schema_required_property",
                        path,
                        $"Required property '{requiredName.GetString()}' is missing."));
                }
            }
        }

        if (!schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (properties.TryGetProperty(property.Name, out var propertySchema)
                && propertySchema.ValueKind == JsonValueKind.Object)
            {
                ValidateElement(
                    property.Value,
                    propertySchema,
                    $"{path}/{EscapePointer(property.Name)}",
                    issues,
                    depth + 1);
            }
            else if (schema.TryGetProperty("additionalProperties", out var additional)
                     && additional.ValueKind == JsonValueKind.False)
            {
                issues.Add(Error(
                    "schema_additional_property",
                    $"{path}/{EscapePointer(property.Name)}",
                    $"Property '{property.Name}' is not allowed by the schema."));
            }
        }
    }

    private static void ValidateArray(
        JsonElement value,
        JsonElement schema,
        string path,
        List<ConfigValidationIssue> issues,
        int depth)
    {
        var length = value.GetArrayLength();
        if (TryGetNonNegativeInteger(schema, "minItems", out var minimum)
            && length < minimum)
        {
            issues.Add(Error("schema_min_items", path, $"Array requires at least {minimum} items."));
        }

        if (TryGetNonNegativeInteger(schema, "maxItems", out var maximum)
            && length > maximum)
        {
            issues.Add(Error("schema_max_items", path, $"Array accepts at most {maximum} items."));
        }

        if (schema.TryGetProperty("items", out var itemSchema)
            && itemSchema.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateElement(item, itemSchema, $"{path}/{index}", issues, depth + 1);
                index++;
            }
        }
    }

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "string" => value.ValueKind == JsonValueKind.String,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static bool TryGetNonNegativeInteger(
        JsonElement schema,
        string propertyName,
        out int value)
    {
        value = 0;
        return schema.TryGetProperty(propertyName, out var element)
            && element.TryGetInt32(out value)
            && value >= 0;
    }

    private static bool IsSecretLike(string key)
    {
        var segments = key.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var normalized = string.Concat(segments);
        return segments.Any(SecretLikeSegments.Contains)
            || SecretLikeSegments.Any(secret => normalized.Contains(
                secret,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static ConfigValidationIssue Error(string code, string path, string message) =>
        new(ConfigValidationSeverity.Error, code, path, message);
}
