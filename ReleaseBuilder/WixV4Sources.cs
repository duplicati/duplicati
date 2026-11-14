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
/// Prepares the MSI sources for the WiX toolset (version 4 or later) used on Windows.
/// The sources are written for wixl, which reads the WiX 3 format, so a copy of them
/// is converted with <c>wix convert</c>, and the few constructs that wixl accepts but
/// WiX rejects are then rewritten in the copy. The original sources are not changed.
/// </summary>
public static class WixV4Sources
{
    /// <summary>
    /// The value of the <c>BuildEnv</c> preprocessor variable when building with WiX on Windows.
    /// The sources use it to include the elements that only WiX understands; wixl builds get
    /// the same tables from the <c>InstallTables.sql</c> file instead.
    /// </summary>
    public const string BuildEnvWindows = "Windows";

    /// <summary>
    /// The WiX 4 namespace
    /// </summary>
    private static readonly XNamespace Ns = "http://wixtoolset.org/schemas/v4/wxs";

    /// <summary>
    /// The prefix given to a dialog in the sources that has the same name as a WixUI dialog
    /// </summary>
    private const string DialogPrefix = "Duplicati";

    /// <summary>
    /// The events whose value is the name of a dialog
    /// </summary>
    private static readonly HashSet<string> DialogEvents = new(StringComparer.Ordinal) { "NewDialog", "SpawnDialog", "SpawnWaitDialog" };

    /// <summary>
    /// The license text and bitmaps that the copy of WixUI used by wixl refers to (in
    /// <c>WixUIExtension/ui</c>), relative to the source folder, as <c>wix build -bindvariable</c>
    /// values. Without them the WixUI of WiX shows its own sample license and bitmaps.
    /// </summary>
    public static readonly string[] WixUiBindVariables = [
        "WixUILicenseRtf=License.rtf",
        "WixUIBannerBmp=Resources/InstallerSmall.bmp",
        "WixUIDialogBmp=Resources/InstallerLarge.bmp"
    ];

    /// <summary>
    /// Returns the names of the WixUI dialogs, taken from the copy of the WixUI sources used by wixl
    /// </summary>
    /// <param name="wixUiFolder">The folder with the WixUI sources, one file per dialog</param>
    /// <returns>The dialog names</returns>
    public static HashSet<string> GetWixUiDialogNames(string wixUiFolder)
        => Directory.GetFiles(wixUiFolder, "*.wxs")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(x => !x.StartsWith("WixUI_", StringComparison.Ordinal) && x != "Common")
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Copies the MSI sources to a new folder and prepares the copy for WiX
    /// </summary>
    /// <param name="wixCommand">The <c>wix</c> command</param>
    /// <param name="resourcesFolder">The folder with the MSI sources</param>
    /// <param name="fragments">The names of the source files to build</param>
    /// <param name="wixUiDialogNames">The names of the WixUI dialogs</param>
    /// <param name="targetFolder">The folder to write the copy to; it is deleted first</param>
    /// <returns>The paths of the prepared source files, in the order given</returns>
    public static async Task<string[]> PrepareAsync(string wixCommand, string resourcesFolder, IEnumerable<string> fragments, ISet<string> wixUiDialogNames, string targetFolder)
    {
        if (Directory.Exists(targetFolder))
            Directory.Delete(targetFolder, true);
        EnvHelper.CopyDirectory(resourcesFolder, targetFolder, recursive: true);

        var sources = fragments.Select(x => Path.Combine(targetFolder, x)).ToArray();
        var convertFiles = sources.Concat(Directory.GetFiles(targetFolder, "*.wxi")).ToArray();

        // The exit code of "wix convert" is the number of changes it made, so it is not an
        // error code. A second run must find nothing to convert.
        await ProcessHelper.Execute([wixCommand, "convert", .. convertFiles], workingDirectory: targetFolder, codeIsError: _ => false);
        await ProcessHelper.Execute([wixCommand, "convert", .. convertFiles], workingDirectory: targetFolder);

        var documents = sources.ToDictionary(x => x, x => XDocument.Load(x, LoadOptions.PreserveWhitespace));
        foreach (var doc in documents.Values)
        {
            MoveNestedStandardDirectories(doc);
            ReplaceHiddenPropertiesList(doc);
            KeepScriptCustomActions32Bit(doc);
        }
        RenameDialogsOfWixUi(documents.Values, wixUiDialogNames);

        foreach (var (path, doc) in documents)
            doc.Save(path, SaveOptions.DisableFormatting);

        return sources;
    }

    /// <summary>
    /// The converter turns a standard directory placed in another standard directory, such as
    /// <c>StartupFolder</c> in <c>ProgramMenuFolder</c>, into nested <c>StandardDirectory</c>
    /// elements, which WiX rejects. The location of a standard directory is decided by Windows
    /// Installer, so the inner one is moved out next to its parent.
    /// </summary>
    /// <param name="doc">The document to change</param>
    internal static void MoveNestedStandardDirectories(XDocument doc)
    {
        var standardDirectory = Ns + "StandardDirectory";
        foreach (var inner in doc.Descendants(standardDirectory).Where(x => x.Parent?.Name == standardDirectory).ToList())
        {
            var outer = inner.Parent!;
            inner.Remove();
            outer.AddAfterSelf(inner);
        }
    }

    /// <summary>
    /// WiX does not let the <c>MsiHiddenProperties</c> property be written directly. It builds the
    /// list from the <c>Hidden</c> attribute of a property and the <c>HideTarget</c> attribute of a
    /// custom action, so each name in the list is moved to one of those.
    /// </summary>
    /// <param name="doc">The document to change</param>
    internal static void ReplaceHiddenPropertiesList(XDocument doc)
    {
        var list = doc.Descendants(Ns + "Property").FirstOrDefault(x => (string?)x.Attribute("Id") == "MsiHiddenProperties");
        if (list == null)
            return;

        foreach (var name in ((string?)list.Attribute("Value") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var property = doc.Descendants(Ns + "Property").FirstOrDefault(x => (string?)x.Attribute("Id") == name);
            var customAction = doc.Descendants(Ns + "CustomAction").FirstOrDefault(x => (string?)x.Attribute("Id") == name);
            if (property != null)
                property.SetAttributeValue("Hidden", "yes");
            else if (customAction != null)
                customAction.SetAttributeValue("HideTarget", "yes");
            else
                throw new Exception($"MsiHiddenProperties lists \"{name}\", which is neither a property nor a custom action");
        }

        list.Remove();
    }

    /// <summary>
    /// For a 64-bit package WiX runs the script custom actions in the 64-bit script host, but
    /// wixl runs them in the 32-bit one. The scripts are written and tested with wixl builds,
    /// so they keep the 32-bit host unless the sources ask for something else.
    /// </summary>
    /// <param name="doc">The document to change</param>
    internal static void KeepScriptCustomActions32Bit(XDocument doc)
    {
        foreach (var customAction in doc.Descendants(Ns + "CustomAction"))
        {
            var isScript = customAction.Attribute("JScriptCall") != null
                || customAction.Attribute("VBScriptCall") != null
                || customAction.Attribute("Script") != null;
            if (isScript && customAction.Attribute("Bitness") == null)
                customAction.SetAttributeValue("Bitness", "always32");
        }
    }

    /// <summary>
    /// wixl lets the sources replace a WixUI dialog by defining one with the same name, but
    /// WiX reports the two as duplicates. A dialog with the name of a WixUI dialog is given
    /// its own name, together with every place that refers to it.
    /// </summary>
    /// <param name="documents">The documents to change</param>
    /// <param name="wixUiDialogNames">The names of the WixUI dialogs</param>
    internal static void RenameDialogsOfWixUi(IEnumerable<XDocument> documents, ISet<string> wixUiDialogNames)
    {
        var docs = documents.ToList();
        var renamed = docs
            .SelectMany(x => x.Descendants(Ns + "Dialog"))
            .Select(x => (string?)x.Attribute("Id"))
            .OfType<string>()
            .Where(wixUiDialogNames.Contains)
            .ToHashSet(StringComparer.Ordinal);

        if (renamed.Count == 0)
            return;

        void Rename(XAttribute? attribute)
        {
            if (attribute != null && renamed.Contains(attribute.Value))
                attribute.Value = DialogPrefix + attribute.Value;
        }

        foreach (var element in docs.SelectMany(x => x.Descendants()))
        {
            switch (element.Name.LocalName)
            {
                case "Dialog":
                case "DialogRef":
                    Rename(element.Attribute("Id"));
                    break;
                case "Show":
                    Rename(element.Attribute("Dialog"));
                    break;
                case "Publish":
                    Rename(element.Attribute("Dialog"));
                    if (DialogEvents.Contains((string?)element.Attribute("Event") ?? ""))
                        Rename(element.Attribute("Value"));
                    break;
            }
        }
    }
}
