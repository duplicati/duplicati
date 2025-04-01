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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ReleaseBuilder;

/// <summary>
/// Implemenation of the &quot;heat&quot; command line tool from Wix
/// </summary>
public static class WixHeatBuilder
{

    /// <summary>
    /// Converts a string to a valid Wix identifier.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The WiX identifier.</returns>
    public static string ConvertToIdentifier(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Input string cannot be null or empty.");

        // Remove invalid characters (only allow A-Z, a-z, 0-9, _, .)
        var cleanedInput = Regex.Replace(input, @"[^A-Za-z0-9_.]", "_");

        if (cleanedInput.Length > 72)
        {
            var hash = BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(cleanedInput)))
                .Replace("-", string.Empty)[..12];
            cleanedInput = cleanedInput[..(72 - hash.Length)] + hash;
        }

        // Ensure the identifier starts with a letter or underscore
        if (!char.IsLetter(cleanedInput[0]) && cleanedInput[0] != '_')
            cleanedInput = "_" + cleanedInput;

        return cleanedInput;
    }

    /// <summary>
    /// Creates a Wix filelist from a directory
    /// </summary>
    /// <param name="sourceFolder">The source folder to create the filelist from</param>
    /// <param name="directoryRefName">The name of the directory reference</param>
    /// <param name="componentGroupId">The name of the component group</param>
    /// <param name="fileIdGenerator">A function to generate file IDs.</param>
    /// <returns>The wix file xml contents</returns>
    public static string CreateWixFilelist(string sourceFolder, string version, string folderPrefix = "$(var.HarvestPath)", string directoryRefName = "INSTALLLOCATION", string componentGroupId = "DUPLICATIBIN", string wixNs = "http://schemas.microsoft.com/wix/2006/wi", Func<string, string>? fileIdGenerator = null)
    {
        var itemIds = new Dictionary<string, string>();
        fileIdGenerator ??= (x) => ConvertToIdentifier(Path.GetRelativePath(sourceFolder, x));
        Func<string, string> pathTransformer = (x) => $"{folderPrefix}{Path.GetRelativePath(sourceFolder, x)}";

        var doc = new XmlDocument();
        doc.LoadXml($"<Wix xmlns=\"{wixNs}\"></Wix>");
        var root = doc.DocumentElement!;
        var fragment = doc.CreateElement("Fragment");
        root.AppendChild(fragment);
        var directoryRef = doc.CreateElement("DirectoryRef");
        directoryRef.SetAttribute("Id", directoryRefName);
        fragment.AppendChild(directoryRef);

        foreach (var f in Directory.EnumerateFileSystemEntries(sourceFolder))
            if (File.Exists(f))
                AddFile(doc, directoryRef, f, version, itemIds, fileIdGenerator, pathTransformer);
            else if (Directory.Exists(f))
                AddDirectory(doc, directoryRef, f, version, itemIds, fileIdGenerator, pathTransformer);

        var fragment2 = doc.CreateElement("Fragment");
        root.AppendChild(fragment2);
        var componentGroup = doc.CreateElement("ComponentGroup");
        componentGroup.SetAttribute("Id", componentGroupId);
        fragment2.AppendChild(componentGroup);

        foreach (var file in itemIds.Keys)
        {
            var componentRef = doc.CreateElement("ComponentRef");
            componentRef.SetAttribute("Id", itemIds[file]);
            componentGroup.AppendChild(componentRef);
        }

        return doc.OuterXml;
    }


    /// <summary>
    /// Adds a file to the XML document.
    /// </summary>
    /// <param name="doc">The XML document.</param>
    /// <param name="directoryRef">The XML element representing the directory reference.</param>
    /// <param name="file">The file to be added.</param>
    /// <param name="version">The version of the file.</param>
    /// <param name="itemIds">The dictionary containing the item IDs.</param>
    /// <param name="fileIdGenerator">The function to generate file IDs.</param>
    private static void AddFile(XmlDocument doc, XmlElement directoryRef, string file, string version, Dictionary<string, string> itemIds, Func<string, string> fileIdGenerator, Func<string, string> pathTransformer)
    {
        var id = fileIdGenerator.Invoke(file);
        itemIds.Add(file, id);

        var component = doc.CreateElement("Component");
        component.SetAttribute("Id", id);
        component.SetAttribute("Guid", "*");
        directoryRef.AppendChild(component);

        var fileElement = doc.CreateElement("File");
        fileElement.SetAttribute("Id", id);
        fileElement.SetAttribute("KeyPath", "yes");
        fileElement.SetAttribute("DefaultVersion", version);
        fileElement.SetAttribute("Source", pathTransformer(file));
        component.AppendChild(fileElement);
    }

    /// <summary>
    /// Recursively adds a directory and its contents to an XML document.
    /// </summary>
    /// <param name="doc">The XML document to add the directory to.</param>
    /// <param name="directoryRef">The parent directory reference element.</param>
    /// <param name="dir">The directory path to add.</param>
    /// <param name="version">The version of the directory.</param>
    /// <param name="itemIds">A dictionary to store the mapping between directory paths and their generated IDs.</param>
    /// <param name="fileIdGenerator">A function to generate file IDs.</param>
    private static void AddDirectory(XmlDocument doc, XmlElement directoryRef, string dir, string version, Dictionary<string, string> itemIds, Func<string, string> fileIdGenerator, Func<string, string> pathTransformer)
    {
        var id = fileIdGenerator.Invoke(dir);

        var dirName = Path.GetFileName(dir);
        var directory = doc.CreateElement("Directory");
        directory.SetAttribute("Id", id);
        directory.SetAttribute("Name", dirName);
        directoryRef.AppendChild(directory);

        foreach (var file in Directory.GetFiles(dir))
            AddFile(doc, directory, file, version, itemIds, fileIdGenerator, pathTransformer);

        foreach (var subDir in Directory.GetDirectories(dir))
            AddDirectory(doc, directory, subDir, version, itemIds, fileIdGenerator, pathTransformer);
    }
}