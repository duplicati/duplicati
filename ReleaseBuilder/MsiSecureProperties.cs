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
using System.Xml.Linq;

namespace ReleaseBuilder;

/// <summary>
/// wixl ignores the <c>Secure="yes"</c> attribute of a property, so the
/// <c>SecureCustomProperties</c> list of a wixl build only has the upgrade properties.
/// Windows Installer passes a public property that is not in the list from the user
/// interface to the elevated installation only when the user is an administrator,
/// so the list is written after the wixl build, the way WiX writes it.
/// </summary>
public static class MsiSecureProperties
{
    /// <summary>
    /// Returns the properties that the sources mark as secure, and the properties set by
    /// <c>UpgradeVersion</c> elements, which wixl already lists. Elements inside preprocessor
    /// conditions are included as well, as the sources are not preprocessed here.
    /// </summary>
    /// <param name="sourceFiles">The source files of the package</param>
    /// <returns>The property names, sorted</returns>
    public static string[] Get(IEnumerable<string> sourceFiles)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in sourceFiles)
        {
            var doc = XDocument.Load(file);
            foreach (var element in doc.Descendants())
            {
                var name = element.Name.LocalName == "Property" && (string?)element.Attribute("Secure") == "yes"
                    ? (string?)element.Attribute("Id")
                    : element.Name.LocalName == "UpgradeVersion"
                        ? (string?)element.Attribute("Property")
                        : null;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
        }

        return names.ToArray();
    }

    /// <summary>
    /// Returns the <c>msibuild -q</c> queries that set the <c>SecureCustomProperties</c> property
    /// </summary>
    /// <param name="properties">The property names</param>
    /// <returns>The queries, to run in order</returns>
    public static string[] GetQueries(IEnumerable<string> properties)
    {
        var value = string.Join(";", properties);
        if (value.Contains('\''))
            throw new ArgumentException("A property name cannot contain a quote", nameof(properties));

        // A DELETE that matches no row is not an error, so this also works when wixl wrote no list
        return [
            "DELETE FROM `Property` WHERE `Property` = 'SecureCustomProperties'",
            $"INSERT INTO `Property` (`Property`, `Value`) VALUES ('SecureCustomProperties', '{value}')"
        ];
    }
}
