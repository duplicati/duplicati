using System;

namespace Duplicati.Library.Backend.AliyunDrive
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

        /// <summary>
        /// 转为 url 路径
        /// Converts to a URL path.
        /// 例如：由 E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// Example: Converts 'E:\_backups\p00\3e4' to '_backups/p00/3e4'.
        /// </summary>
        /// <param name="path">The original file system path.</param>
        /// <param name="removePrefix">The prefix to remove from the path.</param>
        /// <returns>The converted URL path.</returns>
        public static string ToUrlPath(this string path, string removePrefix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removePrefix))
            {
                if (path.StartsWith(removePrefix))
                {
                    path = path.Substring(removePrefix.Length);
                }
            }

            // 替换所有的反斜杠为斜杠
            // Replace all backslashes with forward slashes.
            // 分割路径，移除空字符串，然后重新连接
            // Split the path, remove empty strings, and then rejoin.
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }
    }
}