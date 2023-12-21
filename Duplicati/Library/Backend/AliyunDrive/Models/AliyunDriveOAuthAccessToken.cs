using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 授权 code 获取 access_token
    /// https://www.yuque.com/aliyundrive/zpfszx/efabcs
    /// </summary>
    public class AliyunDriveOAuthAccessToken
    {
        /// <summary>
        /// Bearer
        /// </summary>
        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        /// <summary>
        /// 用来获取用户信息的 access_token。 刷新后，旧 access_token 不会立即失效。
        /// </summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// 单次有效，用来刷新 access_token，90 天有效期。刷新后，返回新的 refresh_token，请保存以便下一次刷新使用。
        /// </summary>
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// access_token的过期时间，单位秒。
        /// </summary>
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }
}