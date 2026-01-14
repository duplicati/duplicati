// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Duplicati.Proprietary.Office365;

internal static class GraphSelectBuilder
{
    /// <summary>
    /// Cache of built select strings per type.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, string[]> _cache = new();

    /// <summary>
    /// Builds an OData $select list from a CLR type by reading [JsonPropertyName] attributes.
    /// Falls back to camelCase of the property name if [JsonPropertyName] is missing.
    /// Excludes properties marked with [JsonIgnore].
    /// </summary>
    /// <typeparam name="T">The type to build the select for.</typeparam>
    /// <returns>The OData $select string.</returns>
    internal static string BuildSelect<T>(IEnumerable<string>? exclude = null) => BuildSelect(typeof(T), exclude);

    /// <summary>
    /// Builds an OData $select list from a CLR type by reading [JsonPropertyName] attributes.
    /// Falls back to camelCase of the property name if [JsonPropertyName] is missing.
    /// Excludes properties marked with [JsonIgnore].
    /// </summary>
    /// <param name="type">The type to build the select for.</param>
    /// <returns>The OData $select string.</returns>
    internal static string BuildSelect(Type type, IEnumerable<string>? exclude = null)
    {
        var names = _cache.GetOrAdd(type, BuildSelectInternal);
        if (exclude == null || !exclude.Any()) return string.Join(",", names);
        return string.Join(",", names.Except(exclude, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds an OData $select list from a CLR type by reading [JsonPropertyName] attributes.
    /// Falls back to camelCase of the property name if [JsonPropertyName] is missing.
    /// Excludes properties marked with [JsonIgnore].
    /// </summary>
    /// <param name="type">The type to build the select for.</param>
    /// <returns>The OData $select string.</returns>
    private static string[] BuildSelectInternal(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length == 1) return char.ToLowerInvariant(s[0]).ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var names = new List<string>(props.Length);

        foreach (var p in props)
        {
            if (!p.CanRead) continue;

            // Skip [JsonIgnore]
            var ignore = p.GetCustomAttribute<JsonIgnoreAttribute>();
            if (ignore != null && ignore.Condition != JsonIgnoreCondition.Never)
                continue;

            // Skip indexers
            if (p.GetIndexParameters().Length != 0)
                continue;

            var jp = p.GetCustomAttribute<JsonPropertyNameAttribute>();
            var name = jp?.Name;

            if (string.IsNullOrWhiteSpace(name))
                name = ToCamelCase(p.Name);

            // Skip special JSON metadata fields if any
            if (name.StartsWith("@", StringComparison.Ordinal))
                continue;

            names.Add(name);
        }

        // Deterministic output for caching/testability
        names.Sort(StringComparer.Ordinal);

        return names.ToArray();
    }
}