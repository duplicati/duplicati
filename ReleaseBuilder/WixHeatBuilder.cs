using System.Xml;

namespace ReleaseBuilder;

/// <summary>
/// Implemenation of the &quot;heat&quot; command line tool from Wix
/// </summary>
public static class WixHeatBuilder
{
    /// <summary>
    /// Creates a Wix filelist from a directory
    /// </summary>
    /// <param name="sourceFolder">The source folder to create the filelist from</param>
    /// <param name="directoryRefName">The name of the directory reference</param>
    /// <param name="componentGroupId">The name of the component group</param>
    /// <param name="fileIdGenerator">A function to generate file IDs.</param>
    /// <returns>The wix file xml contents</returns>
    public static string CreateWixFilelist(string sourceFolder, string folderPrefix = "$(var.HarvestPath)", string directoryRefName = "INSTALLLOCATION", string componentGroupId = "DUPLICATIBIN", Func<string, string>? fileIdGenerator = null)
    {
        var itemIds = new Dictionary<string, string>();
        fileIdGenerator ??= (x) => Path.GetRelativePath(sourceFolder, x).Replace("\\", "_").Replace("/", "_").Replace(":", "_").Replace(" ", "_");
        Func<string, string> pathTransformer = (x) => $"{folderPrefix}{Path.GetRelativePath(sourceFolder, x)}";

        var doc = new XmlDocument();
        doc.LoadXml("<Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\"></Wix>");
        var root = doc.DocumentElement!;
        var fragment = doc.CreateElement("Fragment");
        root.AppendChild(fragment);
        var directoryRef = doc.CreateElement("DirectoryRef");
        directoryRef.SetAttribute("Id", directoryRefName);
        fragment.AppendChild(directoryRef);

        foreach (var f in Directory.EnumerateFileSystemEntries(sourceFolder))
            if (File.Exists(f))
                AddFile(doc, directoryRef, f, itemIds, fileIdGenerator, pathTransformer);
            else if (Directory.Exists(f))
                AddDirectory(doc, directoryRef, f, itemIds, fileIdGenerator, pathTransformer);

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
    /// <param name="itemIds">The dictionary containing the item IDs.</param>
    /// <param name="fileIdGenerator">The function to generate file IDs.</param>
    private static void AddFile(XmlDocument doc, XmlElement directoryRef, string file, Dictionary<string, string> itemIds, Func<string, string> fileIdGenerator, Func<string, string> pathTransformer)
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
        fileElement.SetAttribute("Source", pathTransformer(file));
        component.AppendChild(fileElement);
    }

    /// <summary>
    /// Recursively adds a directory and its contents to an XML document.
    /// </summary>
    /// <param name="doc">The XML document to add the directory to.</param>
    /// <param name="directoryRef">The parent directory reference element.</param>
    /// <param name="dir">The directory path to add.</param>
    /// <param name="itemIds">A dictionary to store the mapping between directory paths and their generated IDs.</param>
    /// <param name="fileIdGenerator">A function to generate file IDs.</param>
    private static void AddDirectory(XmlDocument doc, XmlElement directoryRef, string dir, Dictionary<string, string> itemIds, Func<string, string> fileIdGenerator, Func<string, string> pathTransformer)
    {
        var id = fileIdGenerator.Invoke(dir);

        var dirName = Path.GetFileName(dir);
        var directory = doc.CreateElement("Directory");
        directory.SetAttribute("Id", id);
        directory.SetAttribute("Name", dirName);
        directoryRef.AppendChild(directory);

        foreach (var file in Directory.GetFiles(dir))
            AddFile(doc, directory, file, itemIds, fileIdGenerator, pathTransformer);

        foreach (var subDir in Directory.GetDirectories(dir))
            AddDirectory(doc, directory, subDir, itemIds, fileIdGenerator, pathTransformer);
    }
}