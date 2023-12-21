using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 获取授权二维码
    /// https://www.yuque.com/aliyundrive/zpfszx/ttfoy0xt2pza8lof
    /// </summary>
    public class AliyunDriveOAuthAuthorizeQrCodeResponse
    {
        /// <summary>
        /// 二维码地址
        /// </summary>
        [JsonProperty("qrCodeUrl")]
        public string QrCodeUrl { get; set; }

        /// <summary>
        /// 登录随机数，用来获取用户登录状态
        /// </summary>
        [JsonProperty("sid")]
        public string Sid { get; set; }
    }
}