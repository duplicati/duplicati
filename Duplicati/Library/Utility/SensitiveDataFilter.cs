// Copyright (C) 2026, The Duplicati Team
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
using System.Text.RegularExpressions;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Helper class to filter sensitive data from strings
    /// </summary>
    public static class SensitiveDataFilter
    {
        /// <summary>
        /// The replacement text used for redacted paths
        /// </summary>
        public const string REDACTED = "-redacted-";

        /// <summary>
        /// Regex that detects file system paths in text
        /// </summary>
        private static readonly Regex PathDetectionRegex = new Regex(
            @"(?<![\w/\\])" +
            @"(?:" +
                @"(?:[a-zA-Z]:\\(?:[^\\\s\n\r]*\\?)*)" +       // Windows drive paths
                @"|(?:\\\\[^\\\s\n\r]+(?:\\[^\\\s\n\r]*)*)" +     // UNC paths
                @"|(?:/[a-zA-Z0-9_.][^/\s\n\r]*(?:/[a-zA-Z0-9_.][^/\s\n\r]*)*)" + // Unix absolute paths
                @"|(?:file:///[^\s\n\r]*)" +                       // File URIs
            @")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        /// <summary>
        /// The path separator characters used to detect path-like values
        /// </summary>
        private static readonly char[] PathSeparators = new[] { '/', '\\' };

        /// <summary>
        /// Characters that are commonly trailing delimiters and should not be part of a path
        /// </summary>
        private static readonly char[] PathTrimChars = new[] { '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>', ',', ';', ':' };

        /// <summary>
        /// Replaces detected file system paths with "-redacted-"
        /// </summary>
        /// <param name="input">The input string to filter</param>
        /// <returns>The filtered string with paths redacted</returns>
        public static string RedactPaths(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return PathDetectionRegex.Replace(input, match =>
            {
                var value = match.Value;
                var end = TrimmedPathLength(value);

                if (end < value.Length)
                    return REDACTED + value.Substring(end);

                return REDACTED;
            });
        }

        /// <summary>
        /// Returns the length of a detected path without the trailing punctuation that is likely not part of it
        /// </summary>
        /// <param name="value">The detected path</param>
        /// <returns>The length of the path without the trailing punctuation</returns>
        private static int TrimmedPathLength(string value)
        {
            var end = value.Length;
            while (end > 0 && Array.IndexOf(PathTrimChars, value[end - 1]) >= 0)
                end--;
            return end;
        }

        /// <summary>
        /// Regex that matches a tag, comment or other markup in an HTML text
        /// </summary>
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^<>]*>", RegexOptions.Compiled);

        /// <summary>
        /// Regex that matches the start of an element tag, up to and including the element name.
        /// The name must end the tag or be followed by a space or slash, so "&lt;C:\x&gt;" is not taken for an element.
        /// </summary>
        private static readonly Regex HtmlElementNameRegex = new Regex(@"^</?[A-Za-z][A-Za-z0-9:-]*(?=[\s/>])", RegexOptions.Compiled);

        /// <summary>
        /// Regex that matches a character reference at the start of the searched range of an HTML text.
        /// Browsers also accept numeric references without the closing semicolon.
        /// </summary>
        private static readonly Regex HtmlCharacterReferenceRegex = new Regex(@"\G&(?:#[0-9]+;?|#[xX][0-9a-fA-F]+;?|[A-Za-z][A-Za-z0-9]*;)", RegexOptions.Compiled);

        /// <summary>
        /// Named character references for path characters that are not decoded by <see cref="System.Net.WebUtility.HtmlDecode(string)"/>
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> HtmlPathCharacterReferences = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["&sol;"] = "/",
            ["&bsol;"] = "\\",
            ["&colon;"] = ":",
            ["&period;"] = ".",
            ["&lowbar;"] = "_",
            ["&UnderBar;"] = "_",
            ["&Tab;"] = "\t",
            ["&NewLine;"] = "\n"
        };

        /// <summary>
        /// A part of an HTML text: a tag, or a character or character reference that is shown as text
        /// </summary>
        /// <param name="Start">The position of the part in the HTML text</param>
        /// <param name="Length">The length of the part in the HTML text</param>
        /// <param name="Text">The text shown for the part, or <c>null</c> for a tag</param>
        private readonly record struct HtmlPart(int Start, int Length, string Text);

        /// <summary>
        /// Replaces the file system paths shown by an HTML text with "-redacted-", and keeps the markup.
        /// <see cref="RedactPaths"/> would take the slash of a closing tag for a path, and would not see a path
        /// that is split by a tag or written with character references, so the paths are detected in the text
        /// that is shown. The element names are kept, and paths in attributes and comments are redacted as well.
        /// </summary>
        /// <param name="html">The HTML text to filter</param>
        /// <returns>The HTML text with the paths it shows or carries redacted</returns>
        public static string RedactPathsInHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            var parts = new System.Collections.Generic.List<HtmlPart>();
            var position = 0;
            foreach (Match tag in HtmlTagRegex.Matches(html))
            {
                AddHtmlTextParts(parts, html, position, tag.Index);
                parts.Add(new HtmlPart(tag.Index, tag.Length, null));
                position = tag.Index + tag.Length;
            }
            AddHtmlTextParts(parts, html, position, html.Length);

            // A tag can join two words of the shown text (<b>) or separate them (<p>), so the paths are
            // detected both ways and every part that is in a path either way is redacted
            var redact = new bool[parts.Count];
            MarkHtmlPathParts(parts, redact, null);
            MarkHtmlPathParts(parts, redact, " ");

            var result = new System.Text.StringBuilder(html.Length);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Text == null)
                    result.Append(RedactHtmlTag(html.Substring(part.Start, part.Length)));
                else if (!redact[i])
                    result.Append(html, part.Start, part.Length);
                else if (i == 0 || !redact[i - 1])
                    result.Append(REDACTED);
            }

            return result.ToString();
        }

        /// <summary>
        /// Adds the characters and character references of a text between tags as parts
        /// </summary>
        /// <param name="parts">The list to add the parts to</param>
        /// <param name="html">The HTML text</param>
        /// <param name="start">The start of the text between tags</param>
        /// <param name="end">The end of the text between tags</param>
        private static void AddHtmlTextParts(System.Collections.Generic.List<HtmlPart> parts, string html, int start, int end)
        {
            var position = start;
            while (position < end)
            {
                var reference = html[position] == '&'
                    ? HtmlCharacterReferenceRegex.Match(html, position, end - position)
                    : Match.Empty;

                if (reference.Success)
                {
                    parts.Add(new HtmlPart(position, reference.Length, DecodeHtmlCharacterReference(reference.Value)));
                    position += reference.Length;
                }
                else
                {
                    parts.Add(new HtmlPart(position, 1, html.Substring(position, 1)));
                    position++;
                }
            }
        }

        /// <summary>
        /// Returns the text shown for a character reference
        /// </summary>
        /// <param name="reference">The character reference</param>
        /// <returns>The text shown for the reference, or the reference itself if it is not known</returns>
        private static string DecodeHtmlCharacterReference(string reference)
        {
            if (reference.StartsWith("&#", StringComparison.Ordinal))
            {
                var digits = reference.Substring(2).TrimEnd(';');
                var hex = digits.StartsWith("x", StringComparison.OrdinalIgnoreCase);
                if (int.TryParse(hex ? digits.Substring(1) : digits, hex ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var codepoint)
                    && codepoint > 0 && codepoint <= 0x10FFFF && (codepoint < 0xD800 || codepoint > 0xDFFF))
                    return char.ConvertFromUtf32(codepoint);

                return reference;
            }

            if (HtmlPathCharacterReferences.TryGetValue(reference, out var text))
                return text;

            return System.Net.WebUtility.HtmlDecode(reference);
        }

        /// <summary>
        /// Marks the parts that are in a path of the shown text
        /// </summary>
        /// <param name="parts">The parts of the HTML text</param>
        /// <param name="redact">The marks, one for each part</param>
        /// <param name="tagText">The text a tag is shown as, or <c>null</c> if a tag is not shown</param>
        private static void MarkHtmlPathParts(System.Collections.Generic.List<HtmlPart> parts, bool[] redact, string tagText)
        {
            var text = new System.Text.StringBuilder();
            var owners = new System.Collections.Generic.List<int>();
            for (var i = 0; i < parts.Count; i++)
            {
                var shown = parts[i].Text ?? tagText;
                if (shown == null)
                    continue;

                text.Append(shown);
                for (var j = 0; j < shown.Length; j++)
                    owners.Add(parts[i].Text == null ? -1 : i);
            }

            foreach (Match match in PathDetectionRegex.Matches(text.ToString()))
            {
                var end = match.Index + TrimmedPathLength(match.Value);
                for (var k = match.Index; k < end; k++)
                    if (owners[k] >= 0)
                        redact[owners[k]] = true;
            }
        }

        /// <summary>
        /// Redacts the paths in the attributes or the content of a tag, comment or other markup, and keeps the element name
        /// </summary>
        /// <param name="tag">The tag, from '&lt;' to '&gt;'</param>
        /// <returns>The tag with the paths in it redacted</returns>
        private static string RedactHtmlTag(string tag)
        {
            var name = HtmlElementNameRegex.Match(tag);
            var keep = name.Success ? name.Length : 1;
            return tag.Substring(0, keep) + RedactPathsInHtml(tag.Substring(keep, tag.Length - keep - 1)) + ">";
        }

        /// <summary>
        /// Returns <c>true</c> if the value looks like a local path or a remote entry name,
        /// i.e. it contains a path separator. This is a deliberately broad test that is
        /// meant for values known to be a single argument (such as a log message
        /// argument), where the text-based detection in <see cref="RedactPaths"/> is
        /// too narrow: it cannot see paths that contain spaces or names that only end
        /// with a separator.
        /// </summary>
        /// <param name="value">The value to test</param>
        /// <returns><c>true</c> if the value looks like a path; <c>false</c> otherwise</returns>
        public static bool LooksLikePath(string value)
            => !string.IsNullOrEmpty(value) && value.IndexOfAny(PathSeparators) >= 0;

        /// <summary>
        /// Returns the format arguments with every string argument that looks like a
        /// path (see <see cref="LooksLikePath"/>) replaced by <see cref="REDACTED"/>.
        /// Log messages pass paths as separate format arguments, so redacting at the
        /// argument level catches whole paths, including those with spaces, before the
        /// message is formatted. The input array is not modified; it is returned as-is
        /// when nothing needs redaction.
        /// </summary>
        /// <param name="arguments">The format arguments; may be <c>null</c></param>
        /// <returns>The arguments with path-like strings redacted</returns>
        public static object[] RedactPathArguments(object[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
                return arguments;

            object[] result = null;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is string s && LooksLikePath(s))
                {
                    result ??= (object[])arguments.Clone();
                    result[i] = REDACTED;
                }
            }

            return result ?? arguments;
        }
    }
}
