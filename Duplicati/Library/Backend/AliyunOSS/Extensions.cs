namespace Duplicati.Library.Backend.AliyunOSS
{
    public static class Extensions
    {
        /// <summary>
        /// 移除路径首尾 ' ', '/', '\'
        /// Removes leading and trailing ' ', '/', and '\' from a path.
        /// </summary>
        /// <param name="path">The path to trim.</param>
        /// <returns>The trimmed path.</returns>
        public static string TrimPath(this string path)
        {
            return path?.Trim().Trim('/').Trim('\\').Trim('/').Trim();
        }
    }
}