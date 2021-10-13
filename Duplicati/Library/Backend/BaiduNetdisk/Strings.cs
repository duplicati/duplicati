using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class NetdiskBackend
    {
        public static string DisplayName { get { return LC.L(@"Baidu Netdisk"); } }

        public static string Description { get { return LC.L(@"百度网盘为您提供文件的网络备份、同步和分享服务。空间大、速度快、安全稳固，支持教育网加速，支持手机端。现在注册即有机会享受2TB的免费存储空间。至2021年，百度网盘8年来，市场份额超过85%，坐拥7亿注册用户。"); } }

        public static string BaiduNetdiskAccountDescriptionShort { get { return LC.L(@"授权码"); } }

        public static string BaiduNetdiskAccountDescriptionLong { get { return LC.L(@"百度云授权码，请通过上方链接获取授权。"); } }

        public static string BaiduNetdiskBlockSizeDescriptionShort { get { return LC.L(@"单个分片大小"); } }

        public static string BaiduNetdiskBlockSizeDescriptionLong { get { return LC.L(@"普通用户单个分片大小固定为4MB，单文件总大小上限为4G；普通会员用户单个分片大小上限为16MB，单文件总大小上限为10G；超级会员用户单个分片大小上限为32MB，单文件总大小上限为20G。"); } }
    }
}
