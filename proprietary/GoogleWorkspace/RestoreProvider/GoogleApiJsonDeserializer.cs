// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Duplicati.Proprietary.GoogleWorkspace;

/// <summary>
/// Helper class for JSON serialization that respects Newtonsoft.Json ignore attributes
/// when using System.Text.Json.
/// </summary>
internal static class GoogleApiJsonDeserializer
{
    private static readonly JsonSerializerOptions _options;

    static GoogleApiJsonDeserializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null, // Use exact C# member names (PascalCase)
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { RespectNewtonsoftJsonIgnore }
            }
        };
    }

    /// <summary>
    /// Gets the JSON serializer options that respect Newtonsoft.Json ignore attributes.
    /// </summary>
    public static JsonSerializerOptions Options => _options;

    /// <summary>
    /// Modifier that ignores properties marked with Newtonsoft.Json.JsonIgnoreAttribute.
    /// </summary>
    private static void RespectNewtonsoftJsonIgnore(JsonTypeInfo typeInfo)
    {
        // Collect properties to remove
        var propertiesToRemove = new List<JsonPropertyInfo>();

        foreach (var property in typeInfo.Properties)
        {
            // Check if the property has Newtonsoft.Json.JsonIgnoreAttribute
            var propertyInfo = property.AttributeProvider as System.Reflection.PropertyInfo;
            if (propertyInfo != null)
            {
                var hasJsonIgnore = propertyInfo.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), inherit: true).Any();
                if (hasJsonIgnore)
                {
                    propertiesToRemove.Add(property);
                }
            }
        }

        // Remove the ignored properties from the type info
        foreach (var property in propertiesToRemove)
        {
            typeInfo.Properties.Remove(property);
        }
    }
}
