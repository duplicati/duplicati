using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Duplicati.Library.Backend.AliyunDrive
{
    public class AliyunDriveHelper
    {
        /// <summary>
        /// 计算开始的 1KB 的 sha1
        /// Calculate SHA1 for the first 1KB
        /// </summary>
        /// <param name="inputStream">Input stream.</param>
        /// <returns>SHA1 hash as string.</returns>
        public static string GenerateStartSHA1(Stream inputStream)
        {
            inputStream.Seek(0, SeekOrigin.Begin);

            byte[] buffer = new byte[1024];
            var sha1 = SHA1.Create();
            int numRead = inputStream.Read(buffer, 0, buffer.Length);
            if (numRead > 0)
            {
                sha1.TransformBlock(buffer, 0, numRead, null, 0);
            }
            sha1.TransformFinalBlock(buffer, 0, 0);
            return BitConverter.ToString(sha1.Hash).Replace("-", string.Empty).ToUpper();
        }

        /// <summary>
        /// 计算文件的完整的 sha1
        /// Calculate the complete SHA1 of the file
        /// </summary>
        /// <param name="inputStream">Input stream.</param>
        /// <returns>SHA1 hash as string.</returns>
        public static string GenerateSHA1(Stream inputStream)
        {
            inputStream.Seek(0, SeekOrigin.Begin);

            var sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(inputStream);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpper();
        }

        /// <summary>
        /// 计算文件的 MD5
        /// Compute file's MD5
        /// </summary>
        /// <param name="input">Input string.</param>
        /// <returns>MD5 hash as string.</returns>
        public static string ComputeMD5(string input)
        {
            var md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 秒传 proof code
        /// Instant transmission proof code
        /// </summary>
        /// <param name="fileStream">File stream.</param>
        /// <param name="size">File size.</param>
        /// <param name="token">Token string.</param>
        /// <returns>Proof code.</returns>
        public static string GenerateProofCode(Stream fileStream, long size, string token)
        {
            var proofRange = GetProofRange(token, size);
            if (proofRange.Start == proofRange.End)
            {
                return string.Empty;
            }

            fileStream.Seek(proofRange.Start, SeekOrigin.Begin);
            byte[] buffer = new byte[proofRange.End - proofRange.Start];
            fileStream.Read(buffer, 0, buffer.Length);
            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// 读取本地文件的range值的byte，计算proof_code
        /// Read local file's range of bytes to calculate proof_code
        /// </summary>
        /// <param name="input">Input string.</param>
        /// <param name="size">File size.</param>
        /// <returns>Proof range (Start, End).</returns>
        private static (long Start, long End) GetProofRange(string input, long size)
        {
            if (size == 0)
            {
                return (0, 0);
            }

            string md5Str = ComputeMD5(input).Substring(0, 16);
            ulong tmpInt = Convert.ToUInt64(md5Str, 16);
            long index = (long)(tmpInt % (ulong)size);
            long end = index + 8;

            if (end > size)
            {
                end = size;
            }

            return (index, end);
        }

        /// <summary>
        /// 文件/文件夹编码/验证
        /// File/Folder Encoding/Validation
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>Encoded file name.</returns>
        /// <exception cref="ArgumentException">Thrown when file name is too long.</exception>
        public static string EncodeFileName(string fileName)
        {
            // 将文件名转换为 UTF-8 编码的字节
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(fileName);

            // 检查编码后的字节长度
            if (utf8Bytes.Length > 1024)
            {
                throw new ArgumentException("文件名称过长");
            }

            // 检查文件名是否以斜杠 '/' 结尾
            if (fileName.EndsWith("/"))
            {
                fileName = fileName.TrimEnd('/');
            }

            // 返回原始文件名，因为它满足条件
            return fileName;
        }
    }
}