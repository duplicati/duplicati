using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class AliyunDriveBackend
    {
        public static string DisplayName { get { return LC.L(@"Aliyun Drive"); } }

        public static string Description { get { return LC.L(@"阿里云盘为您提供文件的网络备份、同步和分享服务，免费不限速。"); } }

        public static string AliyunDriveAccountDescriptionShort { get { return LC.L(@"授权码"); } }

        public static string AliyunDriveAccountDescriptionLong { get { return LC.L(@"阿里云盘授权码，请通过上方链接获取授权。"); } }
    }
}
