namespace Duplicati.Library.Snapshots.MacOS;

/// <summary>
/// Enum for how to handle MacOS Photos libraries
/// </summary>
public enum MacOSPhotosHandling
{
    /// <summary>
    /// Do not handle MacOS Photos libraries specially, just back them up as regular folders
    /// </summary>
    LibraryOnly,
    /// <summary>
    /// Only back up photos from MacOS Photos libraries
    /// </summary>
    PhotosOnly,
    /// <summary>
    /// Back up both photos and the library structure
    /// </summary>
    PhotosAndLibrary,
}
