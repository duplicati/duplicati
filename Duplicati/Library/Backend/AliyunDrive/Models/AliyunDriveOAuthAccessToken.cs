using Newtonsoft.Json;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 授权 code 获取 access_token
    /// Obtain access_token using authorization code
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
        /// Access token used to retrieve user information. The old access token will not immediately become invalid after refresh.
        /// </summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// 单次有效，用来刷新 access_token，90 天有效期。刷新后，返回新的 refresh_token，请保存以便下一次刷新使用。
        /// Single-use token to refresh the access token, valid for 90 days. After refreshing, a new refresh token is returned. Please save it for the next refresh.
        /// </summary>
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// access_token的过期时间，单位秒。
        /// Expiration time of the access token, in seconds.
        /// </summary>
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

}