using System.Text.Json;

namespace LlmHarness.Core.Schema;

/// <summary>
/// Validates the common JSON Schema vocabulary needed by the harness.
/// Supported keywords are type, required, properties, items,
/// additionalProperties, enum, and const.
/// </summary>
public sealed class JsonSchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(string? json, string? schema)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid("$", "Response must contain JSON.");
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            return Invalid("$schema", "A JSON schema is required.");
        }

        try
        {
            using var responseDocument = JsonDocument.Parse(json);
            using var schemaDocument = JsonDocument.Parse(schema);

            if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid("$schema", "Schema root must be a JSON object.");
            }

            var errors = new List<SchemaValidationError>();
            ValidateValue(responseDocument.RootElement, schemaDocument.RootElement, "$", errors);

            return errors.Count == 0
                ? SchemaValidationResult.Valid()
                : SchemaValidationResult.Invalid(errors);
        }
        catch (JsonException)
        {
            return Invalid("$", "Response or schema contains invalid JSON.");
        }
    }

    private static void ValidateValue(
        JsonElement value,
        JsonElement schema,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new SchemaValidationError(path, "Schema nodes must be JSON objects."));
            return;
        }

        if (!ValidateConst(value, schema, path, errors) ||
            !ValidateEnum(value, schema, path, errors))
        {
            return;
        }

        if (!ValidateType(value, schema, path, errors))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(value, schema, path, errors);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            ValidateArray(value, schema, path, errors);
        }
    }

    private static bool ValidateConst(
        JsonElement value,
        JsonElement schema,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (!schema.TryGetProperty("const", out var expected))
        {
            return true;
        }

        if (JsonElement.DeepEquals(value, expected))
        {
            return true;
        }

        errors.Add(new SchemaValidationError(path, "Value does not match the schema const value."));
        return false;
    }

    private static bool ValidateEnum(
        JsonElement value,
        JsonElement schema,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (!schema.TryGetProperty("enum", out var allowedValues))
        {
            return true;
        }

        if (allowedValues.ValueKind != JsonValueKind.Array || allowedValues.GetArrayLength() == 0)
        {
            errors.Add(new SchemaValidationError(
                $"{path}.enum",
                "Schema enum must be a non-empty JSON array."));
            return false;
        }

        foreach (var allowedValue in allowedValues.EnumerateArray())
        {
            if (JsonElement.DeepEquals(value, allowedValue))
            {
                return true;
            }
        }

        errors.Add(new SchemaValidationError(path, "Value is not one of the allowed enum values."));
        return false;
    }

    private static bool ValidateType(
        JsonElement value,
        JsonElement schema,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return true;
        }

        var allowedTypes = GetAllowedTypes(type, path, errors);
        if (allowedTypes.Count == 0)
        {
            return false;
        }

        if (allowedTypes.Any(allowedType => MatchesType(value, allowedType)))
        {
            return true;
        }

        errors.Add(new SchemaValidationError(
            path,
            $"Value must be of type {string.Join(" or ", allowedTypes)}."));
        return false;
    }

    private static IReadOnlyList<string> GetAllowedTypes(
        JsonElement type,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            return [type.GetString()!];
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            var values = type.EnumerateArray().ToArray();
            if (values.Length > 0 && values.All(item => item.ValueKind == JsonValueKind.String))
            {
                return values.Select(item => item.GetString()!).ToArray();
            }
        }

        errors.Add(new SchemaValidationError(
            $"{path}.type",
            "Schema type must be a string or an array of strings."));
        return Array.Empty<string>();
    }

    private static bool MatchesType(JsonElement value, string type) =>
        type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => IsInteger(value),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false
        };

    private static bool IsInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var number))
        {
            return false;
        }

        return decimal.Truncate(number) == number;
    }

    private static void ValidateObject(
        JsonElement value,
        JsonElement schema,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            ValidateRequired(value, required, path, errors);
        }

        if (!schema.TryGetProperty("properties", out var properties))
        {
            return;
        }

        if (properties.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new SchemaValidationError(
                $"{path}.properties",
                "Schema properties must be a JSON object."));
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (value.TryGetProperty(property.Name, out var propertyValue))
            {
                ValidateValue(propertyValue, property.Value, PropertyPath(path, property.Name), errors);
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additionalProperties) &&
            additionalProperties.ValueKind == JsonValueKind.False)
        {
            var knownProperties = properties.EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var property in value.EnumerateObject())
            {
                if (!knownProperties.Contains(property.Name))
                {
                    errors.Add(new SchemaValidationError(
                        PropertyPath(path, property.Name),
                        "Additional properties are not allowed."));
                }
            }
        }
    }

    private static void ValidateRequired(
        JsonElement value,
        JsonElement required,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (required.ValueKind != JsonValueKind.Array ||
            required.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            errors.Add(new SchemaValidationError(
                $"{path}.required",
                "Schema required must be an array of strings."));
            return;
        }

        foreach (var property in required.EnumerateArray())
        {
            var propertyName = property.GetString()!;
            if (!value.TryGetProperty(propertyName, out _))
            {
                errors.Add(new SchemaValidationError(
                    PropertyPath(path, propertyName),
                    "Required property is missing."));
            }
        }
    }

    private static void ValidateArray(
        JsonElement value,
        JsonElement schema,
        string path,
        ICollection<SchemaValidationError> errors)
    {
        if (!schema.TryGetProperty("items", out var items))
        {
            return;
        }

        if (items.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new SchemaValidationError(
                $"{path}.items",
                "Schema items must be a JSON object."));
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateValue(item, items, $"{path}[{index}]", errors);
            index++;
        }
    }

    private static string PropertyPath(string path, string propertyName) =>
        $"{path}.{propertyName}";

    private static SchemaValidationResult Invalid(string path, string message) =>
        SchemaValidationResult.Invalid([new SchemaValidationError(path, message)]);
}
