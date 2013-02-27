using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Implementation of a class that ensures all filenames are emitted to the compression stream using / as the directory seperator,
    /// but reports the local path format to the caller. This ensures that the archives are cross-platform and avoids cluttering the code
    /// with if()'s. The compression modules can be made totally unaware of the path issues.
    /// It also adds a few convenience methods, so each compression module gets these methods without having to implement them.
    /// </summary>
    public class CompressionWrapper : IDisposable
    {
        /// <summary>
        /// The compression instance being wrapped
        /// </summary>
        private ICompression m_compressor;

        /// <summary>
        /// Flag describing if we should use UTC times
        /// </summary>
        private bool m_useUtcTimes = true;

        /// <summary>
        /// Constructs a new compression wrapper
        /// </summary>
        /// <param name="compressor">The compression instance to wrap</param>
        public CompressionWrapper(ICompression compressor)
        {
            if (compressor == null)
                throw new ArgumentNullException("compressor");

            m_compressor = compressor;
        }

        /// <summary>
        /// Converts all paths to use / as the directory separator
        /// </summary>
        /// <param name="paths">The paths to convert</param>
        /// <returns>The converted paths</returns>
        public static string[] ToArchivePaths(string[] paths)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                for (int i = 0; i < paths.Length; i++)
                    paths[i] = ToArchivePath(paths[i]);

            return paths;
        }

        /// <summary>
        /// Converts all paths from using / as the directory separator
        /// </summary>
        /// <param name="paths">The paths to convert</param>
        /// <returns>The converted paths</returns>
        public static string[] ToSystemPaths(string[] paths)
        {
            if (System.IO.Path.DirectorySeparatorChar != '/')
                for (int i = 0; i < paths.Length; i++)
                    paths[i] = ToSystemPath(paths[i]);

            return paths;
        }

        /// <summary>
        /// Converts a path from using / as the directory separator
        /// </summary>
        /// <param name="path">The path to convert</param>
        /// <returns>The converted path</returns>
        public static string ToSystemPath(string path)
        {
            return System.IO.Path.DirectorySeparatorChar == '/' ? path : path.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a path to use / as the directory separator
        /// </summary>
        /// <param name="path">The path to convert</param>
        /// <returns>The converted path</returns>
        public static string ToArchivePath(string path)
        {
            return System.IO.Path.DirectorySeparatorChar == '/' ? path : path.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        }


        /// <summary>
        /// Returns all files in the archive, matching the prefix, if any.
        /// </summary>
        /// <param name="prefix">An optional prefix for limiting the files returned</param>
        /// <returns>All files in the archive, matching the prefix, if any</returns>
        public string[] ListFiles(string prefix)
        {
            string[] files = m_compressor.ListFiles(ToArchivePath(prefix));
            for (int i = 0; i < files.Length; i++)
                files[i] = ToSystemPath(files[i]);
            return files;
        }

        /// <summary>
        /// Reads a path from a file as a string
        /// </summary>
        /// <param name="file">The file to read from</param>
        /// <returns>All the text found in the file</returns>
        public string ReadPathString(string file)
        {
            return ToSystemPath(ReadAllText(file));
        }

        /// <summary>
        /// Returns all paths from a file
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>All paths in the given file</returns>
        public string[] ReadPathLines(string file)
        {
            return ToSystemPaths(ReadAllLines(file));
        }

        /// <summary>
        /// Reads all text from a file as a string
        /// </summary>
        /// <param name="file">The file to read from</param>
        /// <returns>All the text found in the file</returns>
        protected string ReadAllText(string file)
        {
            using (StreamReader s = new StreamReader(OpenRead(file), Encoding.UTF8, true))
                return s.ReadToEnd();
        }

        /// <summary>
        /// Returns all lines in the given file
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>All lines in the given file</returns>
        public string[] ReadAllLines(string file)
        {
            return ReadAllText(file).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        /// <summary>
        /// Returns a stream with data from the given file
        /// </summary>
        /// <param name="file">The file to read the data from</param>
        /// <returns>A stream with data from the given file</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return m_compressor.OpenRead(ToArchivePath(file));
        }

        /// <summary>
        /// Writes the given path to a file
        /// </summary>
        /// <param name="file">The file to write to</param>
        /// <param name="path">The path to write</param>
        public void WritePathString(string file, string path)
        {
            WriteAllText(file, ToArchivePath(path));
        }

        /// <summary>
        /// Writes the given paths to a file
        /// </summary>
        /// <param name="file">The file to write to</param>
        /// <param name="paths">The paths to write</param>
        public void WritePathLines(string file, string[] paths)
        {
            WriteAllLines(file, ToArchivePaths(paths));
        }

        /// <summary>
        /// Writes the given string to a file
        /// </summary>
        /// <param name="file">The file to write to</param>
        /// <param name="data">The data to write</param>
        private void WriteAllText(string file, string data)
        {
            using (StreamWriter sw = new StreamWriter(CreateFile(file, CompressionHint.Default, DateTime.Now), Encoding.UTF8))
                sw.Write(data);
        }

        /// <summary>
        /// Writes the given lines to a file
        /// </summary>
        /// <param name="file">The file to write to</param>
        /// <param name="data">The data to write</param>
        public void WriteAllLines(string file, string[] data)
        {
            bool first = true;
            using (StreamWriter sw = new StreamWriter(CreateFile(file, CompressionHint.Default, DateTime.Now), Encoding.UTF8))
                foreach (string s in data)
                {
                    if (first)
                        first = false;
                    else
                        sw.Write("\n");

                    sw.Write(s);
                }
        }

        /// <summary>
        /// Creates a file in the archive
        /// </summary>
        /// <param name="file">The file to create</param>
        /// <param name="lastWrite">The time the file was last written</param>
        /// <returns>A stream with the data to write into the file</returns>
        public System.IO.Stream CreateFile(string file, CompressionHint hint, DateTime lastWrite)
        {
            return m_compressor.CreateFile(ToArchivePath(file), hint, m_useUtcTimes ? lastWrite.ToUniversalTime() : lastWrite);
        }

        /// <summary>
        /// Returns a value indicating if the specified file exists
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>True if the file exists, false otherwise</returns>
        public bool FileExists(string file)
        {
            return m_compressor.FileExists(ToArchivePath(file));
        }

        /// <summary>
        /// The total size of the archive
        /// </summary>
        public long Size
        {
            get { return m_compressor.Size; }
        }

        /// <summary>
        /// Returns the last modification time for the file
        /// </summary>
        /// <param name="file">The name of the file to examine</param>
        /// <returns>The timestamp on the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            return m_compressor.GetLastWriteTime(ToArchivePath(file));
        }

        /// <summary>
        /// The size in bytes of the buffer that will be written when flushed
        /// </summary>
        public long FlushBufferSize
        {
            get { return m_compressor.FlushBufferSize; }
        }

        /// <summary>
        /// Disposes all resources held by this instance
        /// </summary>
        public void Dispose()
        {
            if (m_compressor != null)
                m_compressor.Dispose();
            m_compressor = null;
        }

        /// <summary>
        /// Instanciates a specific compression module, given the file extension and options
        /// </summary>
        /// <param name="fileExtension">The file extension to create the instance for</param>
        /// <param name="filename">The filename of the file used to compress/decompress contents</param>
        /// <param name="options">The options to pass to the instance constructor</param>
        /// <returns>The instanciated compression module or null if the file extension is not supported</returns>
        public static CompressionWrapper GetModule(string fileextension, string filename, Dictionary<string, string> options)
        {
            return new CompressionWrapper(DynamicLoader.CompressionLoader.GetModule(fileextension, filename, options));
        }
    }
}
