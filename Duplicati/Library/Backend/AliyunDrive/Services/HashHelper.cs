using System;
using System.IO;
using System.Security.Cryptography;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 算法
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// 计算文件 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="level"></param>
        /// <param name="alg"></param>
        /// <returns></returns>
        public static string ComputeFileHash(string filePath, int level, string alg)
        {
            // 1 对文件采样计算 hash
            // 2 比较整个文件的 hash
            // 3 比较文件头部 hash
            // 4 比较文件尾部 hash
            if (level > 0 && level <= 4)
            {
                if (level == 1)
                {
                    return ComputeFileSampleHash(filePath, alg);
                }
                else if (level == 2)
                {
                    return ComputeFileHash(filePath, alg);
                }
                else if (level == 3)
                {
                    return ComputeFileStartHash(filePath, alg);
                }
                else if (level == 4)
                {
                    return ComputeFileEndHash(filePath, alg);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 采样间隔
        /// </summary>
        /// <param name="fileSize"></param>
        /// <returns></returns>
        private static long GetSampleInterval(long fileSize)
        {
            // < 10KB 文件不采样
            if (fileSize < 1024 * 10)
            {
                return 0;
            }

            // < 1MB 文件, 每100KB 采样 1KB
            if (fileSize < 1024 * 1024)
            {
                return 1024 * 100;
            }

            // > 10MB 每1MB 采样1KB, 采样数量 > 10
            // > 20MB 每2MB 采样1KB, 采样数量 > 10
            // > 30MB 每3MB 采样1KB, 采样数量 > 10
            // ...
            // > 100MB 每10MB 采样1KB, 采样数量 > 10
            // > 110MB 每11MB 采样1KB, 采样数量 > 10
            // ...
            // > 500MB 每50MB 采样1KB, 采样数量 > 10
            // ...
            // > 1000MB/1GB 每100MB 采样1KB, 采样数量 > 10
            // > 2GB 每200MB 采样1KB, 采样数量 > 10
            // > 10GB 每1000MB/1GB 采样1KB, 采样数量 > 10
            // > 20GB 每2GB 采样1KB, 采样数量 > 10
            // > 100GB 每10GB 采样1KB, 采样数量 > 10
            // > 1000GB/1TB 每100GB 采样1KB, 采样数量 > 10

            // 每10MB增加1MB的采样间隔
            long interval = 1024 * 1024; // 默认间隔为1MB
            for (long size = 10L * 1024 * 1024; size < 1024 * 1024 * 1024 * 1024L; size += 10L * 1024 * 1024)
            {
                if (fileSize <= size)
                    return interval;
                interval += 1024 * 1024;
            }

            // 对于大于1000GB的文件，每100GB采样 1KB
            return 1024 * 1024 * 1024 * 100L;
        }

        /// <summary>
        /// 获取算法（根据算法名称）
        /// </summary>
        /// <param name="alg"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static HashAlgorithm GetAlgorithm(string alg)
        {
            if (alg.Equals("sha256", StringComparison.OrdinalIgnoreCase))
            {
                return SHA256.Create();
            }
            else if (alg.Equals("sha384", StringComparison.OrdinalIgnoreCase))
            {
                return SHA384.Create();
            }
            else if (alg.Equals("sha512", StringComparison.OrdinalIgnoreCase))
            {
                return SHA512.Create();
            }
            else if (alg.Equals("sha1", StringComparison.OrdinalIgnoreCase))
            {
                return SHA1.Create();
            }
            else if (alg.Equals("md5", StringComparison.OrdinalIgnoreCase))
            {
                return MD5.Create();
            }
            else
            {
                throw new ArgumentException("算法不支持");
            }
        }

        /// <summary>
        /// 采样算法文件 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="alg"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string ComputeFileSampleHash(string filePath, string alg)
        {
            var algorithm = GetAlgorithm(alg);
            var hash = ComputeFileSampleHash(filePath, algorithm);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpper();
        }

        /// <summary>
        /// 采样计算文件 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        private static byte[] ComputeFileSampleHash(string filePath, HashAlgorithm algorithm)
        {
            // 每次采样1KB
            const int sampleSize = 1024;

            // 最终计算的 byte
            byte[] finalData = new byte[0];

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                long fileLength = stream.Length;
                long sampleInterval = GetSampleInterval(fileLength);

                // 不需要采样
                if (sampleInterval <= 0)
                {
                    return algorithm.ComputeHash(stream);
                }

                for (long offset = 0; offset < fileLength; offset += sampleInterval)
                {
                    var buffer = new byte[sampleSize];
                    stream.Seek(offset, SeekOrigin.Begin);
                    stream.Read(buffer, 0, sampleSize);
                    finalData = Combine(finalData, buffer);
                }

                // 在文件末尾进行额外的1KB采样
                if (fileLength > sampleSize)
                {
                    var endBuffer = new byte[sampleSize];
                    stream.Seek(-sampleSize, SeekOrigin.End);
                    stream.Read(endBuffer, 0, sampleSize);
                    finalData = Combine(finalData, endBuffer);
                }
            }

            return algorithm.ComputeHash(finalData);
        }

        /// <summary>
        /// 计算文件开始部分 size 的 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="alg"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static string ComputeFileStartHash(string filePath, string alg, int size = 1024)
        {
            using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[size];
                var algorithm = GetAlgorithm(alg);
                int numRead = inputStream.Read(buffer, 0, buffer.Length);
                if (numRead == 0)
                    throw new InvalidOperationException("未能从文件中读取数据");

                algorithm.TransformFinalBlock(buffer, 0, numRead);
                return BitConverter.ToString(algorithm.Hash).Replace("-", string.Empty).ToUpper();
            }
        }

        /// <summary>
        /// 计算文件结束部分 size 的 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="alg"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static string ComputeFileEndHash(string filePath, string alg, int size = 1024)
        {
            using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {

                long fileSize = inputStream.Length;

                // 如果文件大小小于size，则使用整个文件大小
                size = (int)Math.Min(size, fileSize);

                byte[] buffer = new byte[size];
                var algorithm = GetAlgorithm(alg);

                // 移动到文件末尾的size位置
                inputStream.Seek(-size, SeekOrigin.End);
                int numRead = inputStream.Read(buffer, 0, buffer.Length);

                // 确保读取了数据
                if (numRead <= 0)
                    throw new InvalidOperationException("未能从文件末尾读取数据");

                algorithm.TransformFinalBlock(buffer, 0, numRead);
                return BitConverter.ToString(algorithm.Hash).Replace("-", string.Empty).ToUpper();
            }
        }

        /// <summary>
        /// 计算文件完整的 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="alg"></param>
        /// <returns></returns>
        public static string ComputeFileHash(string filePath, string alg)
        {
            var algorithm = GetAlgorithm(alg);
            var hash = ComputeFileHash(filePath, algorithm);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpper();
        }

        /// <summary>
        /// 计算文件完整的 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        private static byte[] ComputeFileHash(string filePath, HashAlgorithm algorithm)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return algorithm.ComputeHash(stream);
            }
        }

        /// <summary>
        /// 合并 byte
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        private static byte[] Combine(byte[] first, byte[] second)
        {
            var ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }
    }
}