using System.Text;

namespace TestDataGenerator.Commands;

/// <summary>
/// Shared utility methods for the test data generator
/// </summary>
public static class Shared
{
    /// <summary>
    /// Distributes the files in the list to the folders in the list
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="folders">The list of folders to distribute the files to</param>
    /// <param name="list">The list of files to distribute</param>
    /// <returns>The list of files distributed to the folders</returns>
    public static List<string> DistributeFiles(Random rnd, List<string> folders, List<string> list)
    {
        var expanded = new List<string>();
        foreach (var file in list)
            expanded.Add(Path.Combine(folders[rnd.Next(folders.Count)], file));

        return expanded;
    }

    /// <summary>
    /// Converts a size in bytes to a human-readable string
    /// </summary>
    /// <param name="size">The size in bytes</param>
    /// <returns>The human-readable string</returns>
    public static string SizeToHumanReadable(long size)
    {
        if (size < 1024)
            return $"{size} B";
        if (size < 1024 * 1000)
            return $"{size / 1024m:F2} KiB";
        if (size < 1024 * 1024 * 1000)
            return $"{size / 1024 / 1024m:F2} MiB";
        if (size < 1024L * 1024 * 1024 * 1000)
            return $"{size / 1024 / 1024 / 1024m:F2} GiB";
        return $"{size / 1024 / 1024 / 1024 / 1024m:F2} TiB";
    }

    /// <summary>
    /// The list of file extensions to use for random file names
    /// </summary>
    private static readonly IReadOnlyList<string> FileExtensions = new List<string>([".bin", ".dat", ".binary", ".data", ".test", ""]);

    /// <summary>
    /// The characters to use for random path segments
    /// </summary>
    private const string PathSegmentChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Generates a random path segment
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="maxLength">The maximum length of the path segment</param>
    /// <returns>The random path segment</returns>
    public static string GetPathSegment(Random rnd, int maxLength)
    {
        var length = rnd.Next(1, maxLength + 1);
        var sb = new StringBuilder(length);
        for (int j = 0; j < length; j++)
            sb.Append(PathSegmentChars[rnd.Next(PathSegmentChars.Length)]);
        return sb.ToString();
    }

    /// <summary>
    /// Generates a list of random path segments
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="count">The number of path segments to generate</param>
    /// <param name="maxLength">The maximum length of each path segment</param>
    /// <returns>The list of path segments</returns>
    public static List<string> GeneratePathSegments(Random rnd, int count, int maxLength)
    {
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
            names.Add(GetPathSegment(rnd, maxLength));
        return names;
    }

    /// <summary>
    /// Generates a random file name
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="maxLength">The maximum length of the file name</param>
    public static string GenerateFileName(Random rnd, int maxLength)
    {
        return GetPathSegment(rnd, maxLength) + FileExtensions[rnd.Next(FileExtensions.Count)];
    }

    /// <summary>
    /// Generates a list of random file names
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="count">The number of file names to generate</param>
    /// <param name="maxLength">The maximum length of each file name</param>
    /// <returns>The list of file names</returns>
    public static List<string> GenerateFileNames(Random rnd, int count, int maxLength)
    {
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
            names.Add(GenerateFileName(rnd, maxLength));
        return names;
    }

    /// <summary>
    /// Writes random data to a stream
    /// </summary>
    /// <param name="rnd">The random number generator to use</param>
    /// <param name="fs">The stream to write to</param>
    /// <param name="size">The size of the data to write</param>
    /// <param name="sparseFactor">The factor to use for sparse data</param>
    /// <remarks>
    /// The sparse factor is a percentage value that determines how many bytes are skipped in the stream.
    /// For example, a sparse factor of 10 means that 10% of the data will be not be overwritten.
    /// </remarks>
    public static void WriteRandomData(Random rnd, Stream fs, long size, int sparseFactor)
    {
        var buffer = new byte[4096];
        while (size > 0)
        {
            var chunkSize = rnd.Next(1, (int)Math.Min(size, buffer.Length));
            var sp = buffer.AsSpan(chunkSize);
            if (rnd.Next(100) < sparseFactor)
            {
                // Skip the bytes
                fs.Position += sp.Length;
            }
            else
            {
                rnd.NextBytes(sp);
                fs.Write(sp);
            }

            size -= sp.Length;
        }
    }

}