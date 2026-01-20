// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal DriveApiImpl DriveApi => new DriveApiImpl(_apiHelper);

    internal class DriveApiImpl(APIHelper provider)
    {
        public async Task<GraphDriveItem> CreateDriveFolderAsync(
            string driveId,
            string parentItemId,
            string name,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var parent = Uri.EscapeDataString(parentItemId);

            // POST /drives/{drive-id}/items/{parent-id}/children
            var url = $"{baseUrl}/v1.0/drives/{drive}/items/{parent}/children";

            var body = new
            {
                name = name,
                folder = new { },
                @microsoft_graph_conflictBehavior = "rename"
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphDriveItem>(respStream, cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Graph did not return the created folder item.");
        }

        public async Task<GraphDriveItem> RestoreDriveItemToFolderAsync(
            string driveId,
            string targetFolderItemId,
            string fileName,
            Stream contentStream,
            string? contentType,
            CancellationToken cancellationToken)
        {
            // Use upload session for files larger than 4MB
            if (contentStream.Length > 4 * 1024 * 1024)
            {
                return await RestoreLargeDriveItemToFolderAsync(driveId, targetFolderItemId, fileName, contentStream, contentType, cancellationToken).ConfigureAwait(false);
            }

            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var folder = Uri.EscapeDataString(targetFolderItemId);
            var name = Uri.EscapeDataString(fileName);

            // PUT /drives/{drive-id}/items/{parent-id}:/{filename}:/content
            var url =
                $"{baseUrl}/v1.0/drives/{drive}/items/{folder}:/{name}:/content";

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Put, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);

                contentStream.Position = 0;
                req.Content = new StreamContent(contentStream);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");

                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);

            await using var respStream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphDriveItem>(respStream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Graph did not return the restored item.");
        }

        private async Task<GraphDriveItem> RestoreLargeDriveItemToFolderAsync(
            string driveId,
            string targetFolderItemId,
            string fileName,
            Stream contentStream,
            string? contentType,
            CancellationToken cancellationToken)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var folder = Uri.EscapeDataString(targetFolderItemId);
            var name = Uri.EscapeDataString(fileName);

            // 1. Create upload session
            // POST /drives/{drive-id}/items/{parent-id}:/{filename}:/createUploadSession
            var createSessionUrl = $"{baseUrl}/v1.0/drives/{drive}/items/{folder}:/{name}:/createUploadSession";

            var sessionBody = new
            {
                item = new
                {
                    @microsoft_graph_conflictBehavior = "rename",
                    name = name
                }
            };

            async Task<HttpRequestMessage> createSessionRequestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(createSessionUrl));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
                req.Content = JsonContent.Create(sessionBody);
                return req;
            }

            using var sessionResp = await provider.SendWithRetryShortAsync(
                createSessionRequestFactory,
                cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(sessionResp, cancellationToken).ConfigureAwait(false);

            var uploadSession = await sessionResp.Content.ReadFromJsonAsync<GraphUploadSession>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to create upload session.");

            // 2. Upload fragments
            var uploadUrl = uploadSession.uploadUrl;
            var totalSize = contentStream.Length;
            // 320 KiB is the minimum fragment size, we use a multiple of it (e.g. 5MB approx)
            // 320 * 1024 = 327680 bytes. 16 * 327680 = 5242880 bytes (5MB)
            var fragmentSize = 320 * 1024 * 16;
            var buffer = new byte[fragmentSize];
            long position = 0;

            contentStream.Position = 0;

            GraphDriveItem? resultItem = null;

            while (position < totalSize)
            {
                var bytesRead = await contentStream.ReadAsync(buffer, 0, fragmentSize, cancellationToken).ConfigureAwait(false);
                var isLast = position + bytesRead >= totalSize;

                async Task<HttpRequestMessage> uploadFragmentRequestFactory(CancellationToken ct)
                {
                    var req = new HttpRequestMessage(HttpMethod.Put, new Uri(uploadUrl));
                    // No auth header needed for upload session URL
                    req.Content = new ByteArrayContent(buffer, 0, bytesRead);
                    req.Content.Headers.ContentRange = new ContentRangeHeaderValue(position, position + bytesRead - 1, totalSize);
                    req.Content.Headers.ContentLength = bytesRead;

                    return req;
                }

                using var uploadResp = await provider.SendWithRetryShortAsync(
                    uploadFragmentRequestFactory,
                    cancellationToken).ConfigureAwait(false);

                if (!uploadResp.IsSuccessStatusCode)
                {
                    // If we get an error, we should probably check if the session is still valid or if we can retry.
                    // For now, we rely on SendWithRetryAsync for transient errors, but if it fails here, it's likely fatal or needs session recreation.
                    // We'll throw and let the caller handle it.
                    var error = await uploadResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException($"Failed to upload fragment: {uploadResp.StatusCode} {error}");
                }

                if (isLast)
                {
                    await using var respStream = await uploadResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    resultItem = await JsonSerializer.DeserializeAsync<GraphDriveItem>(respStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                position += bytesRead;
            }

            return resultItem ?? throw new InvalidOperationException("Upload completed but no item returned.");
        }

        public async Task UpdateDriveItemFileSystemInfoAsync(
            string driveId,
            string itemId,
            DateTimeOffset? created,
            DateTimeOffset? modified,
            CancellationToken cancellationToken)
        {
            if (created == null && modified == null) return;

            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var item = Uri.EscapeDataString(itemId);

            var url = $"{baseUrl}/v1.0/drives/{drive}/items/{item}";

            var body = new
            {
                fileSystemInfo = new
                {
                    createdDateTime = created,
                    lastModifiedDateTime = modified
                }
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory, cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
        }

        private class GraphUploadSession
        {
            public string uploadUrl { get; set; } = "";
            public DateTimeOffset? expirationDateTime { get; set; }
            public string[]? nextExpectedRanges { get; set; }
        }

        public async Task RestoreDriveItemMetadataAsync(
            string driveId,
            string itemId,
            Stream metadataJsonStream,
            CancellationToken cancellationToken)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var item = Uri.EscapeDataString(itemId);

            var url = $"{baseUrl}/v1.0/drives/{drive}/items/{item}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);

                metadataJsonStream.Position = 0;
                req.Content = new StreamContent(metadataJsonStream);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory, cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
        }

        public async Task<GraphDriveItem?> GetDriveItemAsync(
            string driveId,
            string parentId,
            string name,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var parent = Uri.EscapeDataString(parentId);
            var itemName = Uri.EscapeDataString(name);

            var url = $"{baseUrl}/v1.0/drives/{drive}/items/{parent}:/{itemName}";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphDriveItem>(stream, cancellationToken: ct).ConfigureAwait(false);
        }

        public async Task RestoreDriveItemPermissionsAsync(
            string driveId,
            string itemId,
            string permissionsJson,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(permissionsJson)) return;

            List<GraphPermission>? permissions;
            try
            {
                permissions = JsonSerializer.Deserialize<List<GraphPermission>>(permissionsJson);
            }
            catch
            {
                return;
            }

            if (permissions == null || permissions.Count == 0) return;

            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var drive = Uri.EscapeDataString(driveId);
            var item = Uri.EscapeDataString(itemId);

            // POST /drives/{drive-id}/items/{item-id}/invite
            var url = $"{baseUrl}/v1.0/drives/{drive}/items/{item}/invite";

            foreach (var perm in permissions)
            {
                // We can only restore permissions that have roles and recipients (grantedTo or grantedToIdentities)
                // We skip links for now as they are complex to restore (might need to create new links)
                // We also skip if no roles are defined.

                if (perm.Roles == null || perm.Roles.Count == 0) continue;

                var recipients = new List<object>();

                if (perm.GrantedTo?.User?.Email != null)
                {
                    recipients.Add(new { email = perm.GrantedTo.User.Email });
                }

                if (perm.GrantedToIdentities != null)
                {
                    foreach (var identity in perm.GrantedToIdentities)
                    {
                        if (identity.User?.Email != null)
                        {
                            recipients.Add(new { email = identity.User.Email });
                        }
                    }
                }

                // Also check Invitation email
                if (perm.Invitation?.Email != null)
                {
                    recipients.Add(new { email = perm.Invitation.Email });
                }

                if (recipients.Count == 0) continue;

                var body = new
                {
                    requireSignIn = true, // Default to true for security
                    sendInvitation = false, // Don't spam users during restore
                    roles = perm.Roles,
                    recipients = recipients,
                    message = "Restored by Duplicati"
                };

                async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                    req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);
                    req.Content = JsonContent.Create(body);
                    return req;
                }

                try
                {
                    using var resp = await provider.SendWithRetryShortAsync(
                        requestFactory,
                        cancellationToken).ConfigureAwait(false);

                    // We don't throw on permission restore failure, just log/ignore
                    // await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore permission restore errors
                }
            }
        }
    }
}
