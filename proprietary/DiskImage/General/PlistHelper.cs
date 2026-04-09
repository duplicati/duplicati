// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Duplicati.Proprietary.DiskImage.General;

/// <summary>
/// Helper methods for parsing macOS plist XML format output from diskutil commands.
/// </summary>
public static class PlistHelper
{
    /// <summary>
    /// Gets a string value from a plist dict element.
    /// </summary>
    /// <param name="dictElement">The plist dict element.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The string value, or null if not found.</returns>
    public static string? GetStringValue(XElement dictElement, string key)
    {
        var keys = dictElement.Elements("key").ToList();
        var values = dictElement.Elements().Where(e => e.Name != "key").ToList();

        for (int i = 0; i < keys.Count && i < values.Count; i++)
        {
            if (keys[i].Value == key)
                return values[i].Value;
        }
        return null;
    }

    /// <summary>
    /// Gets an array element from a plist dict element.
    /// </summary>
    /// <param name="dictElement">The plist dict element.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The array element, or null if not found.</returns>
    public static XElement? GetArrayElement(XElement dictElement, string key)
    {
        var keys = dictElement.Elements("key").ToList();
        var values = dictElement.Elements().Where(e => e.Name != "key").ToList();

        for (int i = 0; i < keys.Count && i < values.Count; i++)
        {
            if (keys[i].Value == key && values[i].Name == "array")
                return values[i];
        }
        return null;
    }

    /// <summary>
    /// Gets a long value from a plist dict element.
    /// </summary>
    /// <param name="dictElement">The plist dict element.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The long value, or 0 if not found or not parseable.</returns>
    public static long GetLongValue(XElement dictElement, string key)
    {
        var value = GetStringValue(dictElement, key);
        return long.TryParse(value, out var result) ? result : 0;
    }

    /// <summary>
    /// Gets a boolean value from a plist dict element.
    /// </summary>
    /// <param name="dictElement">The plist dict element.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>True if the value is a "true" element, false otherwise.</returns>
    public static bool GetBoolValue(XElement dictElement, string key)
    {
        var keys = dictElement.Elements("key").ToList();
        var values = dictElement.Elements().Where(e => e.Name != "key").ToList();

        for (int i = 0; i < keys.Count && i < values.Count; i++)
        {
            if (keys[i].Value == key)
                return values[i].Name == "true";
        }
        return false;
    }

    /// <summary>
    /// Parses a plist XML string and returns the root dict element.
    /// </summary>
    /// <param name="plistXml">The plist XML string.</param>
    /// <returns>The root dict element, or null if parsing fails.</returns>
    public static XElement? ParsePlistDict(string plistXml)
    {
        try
        {
            var plist = XDocument.Parse(plistXml);
            return plist.Element("plist")?.Element("dict");
        }
        catch
        {
            return null;
        }
    }
}
