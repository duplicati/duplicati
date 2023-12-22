using RestSharp;
using System;
using System.Net;

namespace Duplicati.Library.Backend.AliyunDrive
{
    /// <summary>
    /// 服务商 API
    /// Service Provider API
    /// </summary>
    public static class ProviderApiHelper
    {
        /// <summary>
        /// 阿里云盘 API
        /// Aliyun Drive API
        /// </summary>
        public const string ALIYUNDRIVE_API_HOST = "https://openapi.alipan.com";

        /// <summary>
        /// DUPLICATI 服务商 API
        /// DUPLICATI Service Provider API
        /// </summary>
        public const string DUPLICATI_SERVER_API_HOST = "https://api.duplicati.net";

        /// <summary>
        /// 获取登录授权二维码
        /// Get Login Authorization QR Code
        /// </summary>
        /// <returns>The Aliyun Drive OAuth Authorize QR Code Response.</returns>
        public static AliyunDriveOAuthAuthorizeQrCodeResponse GetAuthQrcode()
        {
            var client = new RestClient(DUPLICATI_SERVER_API_HOST)
            {
                Timeout = -1
            };
            var request = new RestRequest("/api/open/aliyundrive/qrcode", Method.GET);
            var response = client.Execute<AliyunDriveOAuthAuthorizeQrCodeResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("Get Login Authorization QR Code Fail");
        }

        /// <summary>
        /// 刷新请求令牌
        /// Refresh Request Token
        /// </summary>
        /// <returns>The Aliyun Drive OAuth Access Token.</returns>
        public static AliyunDriveOAuthAccessToken RefreshToken(string refreshToken)
        {
            var client = new RestClient(DUPLICATI_SERVER_API_HOST)
            {
                Timeout = -1
            };
            var request = new RestRequest($"/api/open/aliyundrive/refresh-token", Method.POST);
            request.AddHeader("Content-Type", "application/json");
            var body = new RefreshTokenRequest
            {
                RefreshToken = refreshToken
            };
            request.AddJsonBody(body);
            var response = client.Execute<AliyunDriveOAuthAccessToken>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("Refresh Token Fail");
        }

        /// <summary>
        /// 文件删除
        /// File Deletion
        /// https://www.yuque.com/aliyundrive/zpfszx/get3mkr677pf10ws
        /// </summary>
        /// <param name="driveId">The drive ID.</param>
        /// <param name="fileId">The file ID.</param>
        /// <param name="accessToken">The access token.</param>
        public static AliyunDriveOpenFileDeleteResponse FileDelete(string driveId, string fileId, string accessToken)
        {
            var client = new RestClient(ALIYUNDRIVE_API_HOST)
            {
                Timeout = -1
            };
            var request = new RestRequest($"/adrive/v1.0/openFile/delete", Method.POST);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            object body = new
            {
                drive_id = driveId,
                file_id = fileId
            };
            request.AddJsonBody(body);
            var response = client.Execute<AliyunDriveOpenFileDeleteResponse>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("File Deletion Fail");
        }

        /// <summary>
        /// 文件更新
        /// File Update
        /// https://www.yuque.com/aliyundrive/zpfszx/dp9gn443hh8oksgd
        /// </summary>
        /// <param name="driveId">The drive ID.</param>
        /// <param name="fileId">The file ID.</param>
        /// <param name="name">The new name of the file.</param>
        /// <param name="accessToken">The access token.</param>
        /// <returns>The updated Aliyun Drive File Item.</returns>
        public static AliyunDriveFileItem FileUpdate(string driveId, string fileId, string name, string accessToken)
        {
            var client = new RestClient(ALIYUNDRIVE_API_HOST)
            {
                Timeout = -1
            };
            var request = new RestRequest($"/adrive/v1.0/openFile/update", Method.POST);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {accessToken}");
            object body = new
            {
                drive_id = driveId,
                file_id = fileId,
                name = name,
                check_name_mode = "ignore"
            };
            request.AddJsonBody(body);
            var response = client.Execute<AliyunDriveFileItem>(request);
            if (response.StatusCode == HttpStatusCode.OK && response.Data != null)
            {
                return response.Data;
            }

            throw response?.ErrorException ?? new Exception("File Update Fail");
        }
    }
}