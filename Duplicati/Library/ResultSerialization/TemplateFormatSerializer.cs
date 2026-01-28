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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HandlebarsDotNet;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    /// <summary>
    /// Implements serialization of results using Handlebars templates.
    /// This allows users to fully customize the report output format.
    /// </summary>
    public class TemplateFormatSerializer : IResultFormatSerializer
    {
        /// <summary>
        /// The Handlebars instance configured for report rendering
        /// </summary>
        private readonly IHandlebars _handlebars;

        /// <summary>
        /// The compiled template function
        /// </summary>
        private readonly HandlebarsTemplate<object, object> _template;

        /// <summary>
        /// The template source string (for debugging/logging)
        /// </summary>
        private readonly string _templateSource;

        /// <summary>
        /// Default template name to use when no custom template is provided
        /// </summary>
        public const string DefaultTemplateName = "default";

        /// <summary>
        /// Creates a new instance with the default embedded template
        /// </summary>
        public TemplateFormatSerializer()
            : this(LoadEmbeddedTemplate(DefaultTemplateName), false)
        {
        }

        /// <summary>
        /// Creates a new instance with the specified template source or name
        /// </summary>
        /// <param name="templateSourceOrName">The Handlebars template source or template name</param>
        /// <param name="isTemplateName">If true, templateSourceOrName is treated as a template name; if false, as template content</param>
        public TemplateFormatSerializer(string templateSourceOrName, bool isTemplateName)
        {
            string templateSource;
            if (isTemplateName)
                templateSource = LoadEmbeddedTemplate(templateSourceOrName);
            else
                templateSource = templateSourceOrName;

            _templateSource = templateSource ?? throw new ArgumentNullException(nameof(templateSourceOrName));
            _handlebars = Handlebars.Create();
            RegisterHelpers();
            _template = _handlebars.Compile(templateSource);
        }

        /// <summary>
        /// Creates a serializer from a template file path
        /// </summary>
        /// <param name="filePath">Path to the template file</param>
        /// <returns>A new TemplateFormatSerializer instance</returns>
        public static TemplateFormatSerializer FromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Template file not found: {filePath}", filePath);

            var templateContent = File.ReadAllText(filePath);
            return new TemplateFormatSerializer(templateContent, false);
        }

        /// <summary>
        /// Registers custom Handlebars helpers for report formatting
        /// </summary>
        private void RegisterHelpers()
        {
            // Helper to format file sizes in human-readable form
            _handlebars.RegisterHelper("formatSize", (output, context, arguments) =>
            {
                if (arguments.Length == 0 || arguments[0] == null)
                {
                    output.Write("0 B");
                    return;
                }

                if (!long.TryParse(arguments[0].ToString(), out var bytes))
                {
                    output.Write(arguments[0].ToString());
                    return;
                }

                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                int order = 0;
                double size = bytes;
                while (size >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    size /= 1024;
                }
                output.Write($"{size:0.##} {sizes[order]}");
            });

            // Helper to format durations
            _handlebars.RegisterHelper("formatDuration", (output, context, arguments) =>
            {
                if (arguments.Length == 0 || arguments[0] == null)
                {
                    output.Write("0s");
                    return;
                }

                if (arguments[0] is TimeSpan ts)
                {
                    output.Write(FormatTimeSpan(ts));
                    return;
                }

                if (double.TryParse(arguments[0].ToString(), out var seconds))
                {
                    output.Write(FormatTimeSpan(TimeSpan.FromSeconds(seconds)));
                    return;
                }

                output.Write(arguments[0].ToString());
            });

            // Helper to format dates
            _handlebars.RegisterHelper("formatDate", (output, context, arguments) =>
            {
                if (arguments.Length == 0 || arguments[0] == null)
                {
                    output.Write("");
                    return;
                }

                var format = arguments.Length > 1 ? arguments[1].ToString() : "yyyy-MM-dd HH:mm:ss";

                if (arguments[0] is DateTime dt)
                {
                    output.Write(dt.ToString(format));
                    return;
                }

                if (DateTime.TryParse(arguments[0].ToString(), out var parsed))
                {
                    output.Write(parsed.ToString(format));
                    return;
                }

                output.Write(arguments[0].ToString());
            });

            // Helper for conditional equality
            _handlebars.RegisterHelper("eq", (output, options, context, arguments) =>
            {
                if (arguments.Length < 2)
                {
                    options.Inverse(output, context);
                    return;
                }

                var left = arguments[0]?.ToString() ?? "";
                var right = arguments[1]?.ToString() ?? "";

                if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                    options.Template(output, context);
                else
                    options.Inverse(output, context);
            });

            // Helper for status-based CSS classes or indicators
            _handlebars.RegisterHelper("statusClass", (output, context, arguments) =>
            {
                if (arguments.Length == 0 || arguments[0] == null)
                {
                    output.Write("unknown");
                    return;
                }

                var status = arguments[0].ToString().ToLowerInvariant();
                var result = status switch
                {
                    "success" => "success",
                    "warning" => "warning",
                    "error" => "error",
                    "fatal" => "fatal",
                    _ => "unknown"
                };
                output.Write(result);
            });

            // Helper to iterate over object properties
            _handlebars.RegisterHelper("eachProperty", (output, options, context, arguments) =>
            {
                if (arguments.Length == 0 || arguments[0] == null)
                    return;

                var obj = arguments[0];
                if (obj is IDictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        options.Template(output, new { Key = kvp.Key, Value = kvp.Value });
                    }
                }
                else if (obj is IDictionary<string, string> stringDict)
                {
                    foreach (var kvp in stringDict)
                    {
                        options.Template(output, new { Key = kvp.Key, Value = kvp.Value });
                    }
                }
                else
                {
                    var type = obj.GetType();
                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        try
                        {
                            var value = prop.GetValue(obj);
                            options.Template(output, new { Key = prop.Name, Value = value });
                        }
                        catch
                        {
                            // Skip properties that throw exceptions
                        }
                    }
                }
            });
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.TotalSeconds:0.#}s";
        }

        /// <summary>
        /// Loads an embedded template by name
        /// </summary>
        private static string LoadEmbeddedTemplate(string templateName)
        {
            var assembly = typeof(TemplateFormatSerializer).Assembly;
            var resourceName = $"Duplicati.Library.Modules.Builtin.Templates.{templateName}.hbs";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Fall back to the default template if the requested one does not exist
                var defaultResource = $"Duplicati.Library.Modules.Builtin.Templates.{DefaultTemplateName}.hbs";
                using var defaultStream = assembly.GetManifestResourceStream(defaultResource);
                if (defaultStream == null)
                    return GetFallbackTemplate();

                using var defaultReader = new StreamReader(defaultStream);
                return defaultReader.ReadToEnd();
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Returns a simple fallback template if no embedded templates are found
        /// </summary>
        private static string GetFallbackTemplate()
        {
            return @"Duplicati {{OperationName}} Report
======================================

Status: {{ParsedResult}}
Backup Name: {{BackupName}}
Machine: {{MachineName}}

{{#if Data}}
{{#eachProperty Data}}
{{Key}}: {{Value}}
{{/eachProperty}}
{{/if}}

{{#if LogLines}}
Log Messages:
{{#each LogLines}}
  {{this}}
{{/each}}
{{/if}}

{{#if Exception}}
Exception: {{Exception}}
{{/if}}
";
        }

        /// <summary>
        /// Returns a list of available embedded template names
        /// </summary>
        public static IEnumerable<string> GetAvailableTemplates()
        {
            var assembly = typeof(TemplateFormatSerializer).Assembly;
            var prefix = "Duplicati.Library.Modules.Builtin.Templates.";
            var suffix = ".hbs";

            return assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(prefix) && n.EndsWith(suffix))
                .Select(n => n.Substring(prefix.Length, n.Length - prefix.Length - suffix.Length))
                .OrderBy(n => n);
        }

        /// <summary>
        /// Serialize the specified result using the Handlebars template
        /// </summary>
        public string Serialize(object result, Exception exception, IEnumerable<string> loglines, Dictionary<string, string> additional)
        {
            var context = new Dictionary<string, object>
            {
                ["Data"] = result,
                ["Exception"] = exception?.ToString(),
                ["LogLines"] = loglines?.ToList() ?? new List<string>()
            };

            // Add additional parameters directly to the context
            if (additional != null)
            {
                foreach (var kvp in additional)
                {
                    context[kvp.Key] = kvp.Value;
                }
            }

            return _template(context);
        }

        /// <summary>
        /// Returns the format that the serializer represents
        /// </summary>
        public ResultExportFormat Format => ResultExportFormat.Template;
    }
}
