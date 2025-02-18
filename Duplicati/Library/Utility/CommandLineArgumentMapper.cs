// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility;

/// <summary>
/// Helper to get the command line argument type attribute
/// </summary>
public interface ICommandLineArgumentMapper
{
    /// <summary>
    /// Get the command line argument type attribute
    /// </summary>
    /// <param name="mi">The member info to get the attribute from</param>
    /// <returns>The command line argument type attribute</returns>
    CommandLineArgumentDescriptionAttribute? GetCommandLineArgumentDescription(MemberInfo mi);
}

/// <summary>
/// Attribute to define command line argument types
/// </summary>
public class CommandLineArgumentDescriptionAttribute
{
    /// <summary>
    /// The type of the argument, using the property type as default
    /// </summary>
    public CommandLineArgument.ArgumentType? Type { get; init; } = null;
    /// <summary>
    /// The name of the argument, using the property name as default
    /// </summary>
    public string? Name { get; init; } = null;
    /// <summary>
    /// A short description of the argument, using the property name as default
    /// </summary>
    public string? ShortDescription { get; init; } = null;
    /// <summary>
    /// A long description of the argument, using the property name as default
    /// </summary>
    public string? LongDescription { get; init; } = null;
    /// <summary>
    /// The default value for the argument, using the property value as default
    /// </summary>
    public string? DefaultValue { get; init; } = null;
    /// <summary>
    /// The list of valid values for the argument
    /// </summary>
    public string[]? ValueList { get; init; } = null;
    /// <summary>
    /// The converter to use for the argument
    /// </summary>
    public Func<string, object>? Converter { get; init; } = null;
}

/// <summary>
/// Implementation of commandline argument mapping
/// </summary>
public static class CommandLineArgumentMapper
{
    /// <summary>
    /// Helper method to detect slow-access properties
    /// </summary>
    /// <param name="p">The property to read</param>
    /// <param name="parent">The parent object to read from</param>
    /// <param name="excludeDefaultValue">A list of properties to exclude</param>
    /// <returns>The value of the property</returns>
    private static object? GetValueGuarded(PropertyInfo p, object parent, HashSet<string>? excludeDefaultValue)
    {
        object? value = null;
        if (excludeDefaultValue != null && excludeDefaultValue.Contains(p.Name))
            return value;
#if DEBUG
        var start = DateTime.Now;
        value = p.GetValue(parent);
        var duration = DateTime.Now - start;
        if (duration.TotalMilliseconds > 100)
            Console.WriteLine($"Warning: {parent.GetType().FullName}.{p.Name} took {duration.TotalMilliseconds}ms to get value");
#else
        value = p.GetValue(parent);
#endif
        return value;
    }

    /// <summary>
    /// Extracts all primitive types from a class and maps them to command line arguments
    /// </summary>
    /// <param name="type">The type to extract arguments from</param>
    /// <param name="prefix">The prefix to use for the arguments</param>
    /// <param name="exclude">A list of properties to exclude</param>
    /// <returns>A list of command line arguments</returns>
    public static IEnumerable<ICommandLineArgument> MapArguments(object obj, string prefix = "", HashSet<string>? exclude = null, HashSet<string>? excludeDefaultValue = null)
    {
        exclude ??= new HashSet<string>();
        foreach (var p in obj.GetType().GetProperties().Where(p => p.CanWrite && p.CanRead))
        {
            if (exclude.Contains(p.Name))
                continue;

            var customAttr = (obj as ICommandLineArgumentMapper)?.GetCommandLineArgumentDescription(p);

            var name = (prefix + (customAttr?.Name ?? p.Name)).ToLowerInvariant();
            var shortDescription = customAttr?.ShortDescription ?? p.Name;
            var longDescription = customAttr?.LongDescription ?? p.Name;
            var defaultValue = customAttr?.DefaultValue ?? GetValueGuarded(p, obj, excludeDefaultValue)?.ToString();
            var argumentType = customAttr?.Type;

            // Fully custom argument
            if (customAttr != null && customAttr.Converter != null && argumentType != null)
            {
                yield return new CommandLineArgument(name, argumentType.Value, shortDescription, longDescription, defaultValue, null, customAttr.ValueList);
                continue;
            }

            var propType = p.PropertyType;
            if (Nullable.GetUnderlyingType(propType) != null)
                propType = Nullable.GetUnderlyingType(propType);

            if (propType == null)
                continue;

            if (propType == typeof(string))
                yield return new CommandLineArgument(name, argumentType ?? CommandLineArgument.ArgumentType.String, shortDescription, longDescription, defaultValue);
            else if (propType == typeof(int))
                yield return new CommandLineArgument(name, argumentType ?? CommandLineArgument.ArgumentType.Integer, shortDescription, longDescription, defaultValue);
            else if (propType == typeof(bool))
                yield return new CommandLineArgument(name, argumentType ?? CommandLineArgument.ArgumentType.Boolean, shortDescription, longDescription, defaultValue);
            else if (propType.IsEnum)
                yield return new CommandLineArgument(name, argumentType ?? CommandLineArgument.ArgumentType.String, shortDescription, longDescription, defaultValue, null, customAttr?.ValueList ?? Enum.GetNames(propType));
            else if (propType == typeof(Uri))
                yield return new CommandLineArgument(name, argumentType ?? CommandLineArgument.ArgumentType.String, shortDescription, longDescription, defaultValue);
            else if (propType == typeof(TimeSpan))
                yield return new CommandLineArgument(name, argumentType ?? CommandLineArgument.ArgumentType.Timespan, shortDescription, longDescription, defaultValue);
        }
    }

    /// <summary>
    /// Applies the arguments to the object
    /// </summary>
    /// <typeparam name="T">The type of object to apply the arguments to</typeparam>
    /// <param name="obj">The object to apply the arguments to</param>
    /// <param name="args">The arguments to apply</param>
    /// <param name="prefix">The prefix to use for the arguments</param>
    /// <returns>The object with the arguments applied</returns>
    public static T ApplyArguments<T>(T obj, IReadOnlyDictionary<string, string?> args, string prefix = "")
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        foreach (var p in obj.GetType().GetProperties().Where(p => p.CanWrite && p.CanRead))
        {
            var customAttr = (obj as ICommandLineArgumentMapper)?.GetCommandLineArgumentDescription(p);
            var name = (prefix + (customAttr?.Name ?? p.Name)).ToLowerInvariant();

            var propType = p.PropertyType;
            if (Nullable.GetUnderlyingType(propType) != null)
                propType = Nullable.GetUnderlyingType(propType);

            if (propType == null)
                continue;

            // Ignore case-sensitive properties of input dictionary
            var value = args.Keys.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) is string key ? args[key] : null;
            if (value != null)
            {
                if (customAttr?.Converter != null)
                    p.SetValue(obj, customAttr.Converter(value));
                else if (propType == typeof(string))
                    p.SetValue(obj, value);
                else if (propType == typeof(int))
                    p.SetValue(obj, int.Parse(value));
                else if (propType == typeof(bool))
                    p.SetValue(obj, bool.Parse(value));
                else if (propType.IsEnum)
                    p.SetValue(obj, Enum.Parse(propType, value, true));
                else if (propType == typeof(Uri))
                    p.SetValue(obj, new Uri(value));
                else if (propType == typeof(TimeSpan))
                    p.SetValue(obj, Timeparser.ParseTimeSpan(value));
            }
        }

        return obj;
    }

    /// <summary>
    /// Applies the arguments to the object
    /// </summary>
    /// <typeparam name="T">The type of object to apply the arguments to</typeparam>
    /// <param name="obj">The object to apply the arguments to</param>
    /// <param name="nameValueCollection">The arguments to apply</param>
    /// <param name="prefix">The prefix to use for the arguments</param>
    /// <returns>The object with the arguments applied</returns>
    public static T ApplyArguments<T>(T obj, NameValueCollection nameValueCollection, string prefix = "")
        => ApplyArguments(obj, nameValueCollection.AllKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToDictionary(k => k!, k => nameValueCollection[k]), prefix);

}
