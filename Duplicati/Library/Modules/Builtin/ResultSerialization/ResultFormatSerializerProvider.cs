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
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Modules.Builtin.ResultSerialization
{
    /// <summary>
    /// Factory class to provide result serialization
    /// </summary>
    public static class ResultFormatSerializerProvider
    {
        /// <summary>
        /// Gets a serializer for the specified format
        /// </summary>
        /// <param name="format">The format to get a serializer for</param>
        /// <returns>A serializer for the format</returns>
        public static IResultFormatSerializer GetSerializer(ResultExportFormat format)
        {
            return GetSerializer(format, null);
        }

        /// <summary>
        /// Gets a serializer for the specified format with optional template configuration
        /// </summary>
        /// <param name="format">The format to get a serializer for</param>
        /// <param name="templatePathOrName">For Template format: path to custom template file, embedded template name, or null for default</param>
        /// <returns>A serializer for the format</returns>
        public static IResultFormatSerializer GetSerializer(ResultExportFormat format, string templatePathOrName)
        {
            switch (format)
            {
                case ResultExportFormat.Duplicati:
                    return new DuplicatiFormatSerializer();
                case ResultExportFormat.Json:
                    return new JsonFormatSerializer();
                case ResultExportFormat.Template:
                    return GetTemplateSerializer(templatePathOrName);
                default:
                    throw new Interface.UserInformationException(LC.L("The format is not supported: {0}", format), "SerializationFormatNotSupported");
            }
        }

        /// <summary>
        /// Gets a template serializer with the specified template configuration
        /// </summary>
        private static TemplateFormatSerializer GetTemplateSerializer(string templatePathOrName)
        {
            if (string.IsNullOrEmpty(templatePathOrName))
                return new TemplateFormatSerializer();

            // If it looks like a file path (contains path separators or file extension), try to load it as a file
            if (templatePathOrName.Contains(System.IO.Path.DirectorySeparatorChar) ||
                templatePathOrName.Contains(System.IO.Path.AltDirectorySeparatorChar) ||
                templatePathOrName.EndsWith(".hbs", StringComparison.OrdinalIgnoreCase))
            {
                if (System.IO.File.Exists(templatePathOrName))
                    return TemplateFormatSerializer.FromFile(templatePathOrName);
            }

            // Otherwise, treat it as an embedded template name
            return new TemplateFormatSerializer(templatePathOrName, true);
        }
    }
}
