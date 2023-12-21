namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 刷新令牌请求参数
    /// </summary>
    public class RefreshTokenRequest
    {
        /// <summary>
        /// 身份类型 authorization_code 或 refresh_token
        /// </summary>
        public string GrantType { get; set; }

        /// <summary>
        /// 授权码
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string RefreshToken { get; set; }
    }
}