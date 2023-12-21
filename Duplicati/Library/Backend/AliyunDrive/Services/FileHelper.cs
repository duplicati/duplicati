using System;
using System.IO;

namespace Duplicati.Library.Backend.AliyunDrive
{
    public class FileHelper
    {
        /// <summary>
        /// 创建一个随机文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="fileSize"></param>
        public static void CreateRandomFile(string filePath, long fileSize)
        {
            using (var fileStream = new FileStream(filePath, FileMode.CreateNew))
            {
                var random = new Random();
                byte[] buffer = new byte[8192];
                long bytesRemaining = fileSize;

                while (bytesRemaining > 0)
                {
                    int bytesToWrite = (int)Math.Min(bytesRemaining, buffer.Length);
                    random.NextBytes(buffer);
                    fileStream.Write(buffer, 0, bytesToWrite);
                    bytesRemaining -= bytesToWrite;
                }
            }
        }

        /// <summary>
        /// 修改文件的一个字节，并重置文件的最后写入时间（用于测试）
        /// </summary>
        /// <param name="filePath"></param>
        public static void ModifyFileAndResetLastWriteTime(string filePath, int index)
        {
            // 保存原始修改时间
            DateTime originalWriteTime = File.GetLastWriteTime(filePath);

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            {
                // 移动到文件的某个中间位置
                // 例如，移动到第1025个字节
                fs.Seek(index, SeekOrigin.Begin);

                // 写入一个新的字节
                byte[] newByte = { 0x01 }; // 例如，写入字节 0x01
                fs.Write(newByte, 0, 1);
            }

            // 重置修改时间
            File.SetLastWriteTime(filePath, originalWriteTime);
        }
    }
}