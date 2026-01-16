// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal EmailApiImpl EmailApi => new EmailApiImpl(_apiHelper);

    internal class EmailApiImpl(APIHelper provider)
    {
        public async Task<string> RestoreEmailToFolderAsync(
            string userId,
            string targetFolderId,
            Stream contentStream,
            Stream metadataStream,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = System.Uri.EscapeDataString(userId);

            // 1) Create draft message via MIME (always goes to Drafts by default)
            // POST /users/{id}/messages (MIME = base64 in text/plain body)
            var createUrl = $"{baseUrl}/v1.0/users/{user}/messages";

            using var base64TempFile = new Library.Utility.TempFile();

            // Produce base64(MIME) as a stream without holding it in memory.
            // We write once to disk so retries can reopen a fresh stream.
            if (contentStream.CanSeek) contentStream.Position = 0;

            await using (var outFs = new FileStream(base64TempFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            await using (var crypto = new System.Security.Cryptography.CryptoStream(
                outFs,
                new System.Security.Cryptography.ToBase64Transform(),
                System.Security.Cryptography.CryptoStreamMode.Write))
            {
                await contentStream.CopyToAsync(crypto, ct).ConfigureAwait(false);
                crypto.FlushFinalBlock();
                await outFs.FlushAsync(ct).ConfigureAwait(false);
            }

            async Task<HttpRequestMessage> createRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, createUrl);
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);

                req.Content = new StreamContent(new FileStream(base64TempFile, FileMode.Open, FileAccess.Read, FileShare.Read));
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                return req;
            }

            using var createResp = await provider.SendWithRetryAsync(
                createRequestFactory,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(createResp, ct).ConfigureAwait(false);

            var createdId = await ReadIdAsync(createResp, ct).ConfigureAwait(false);

            // 2) Move to target folder (move returns a NEW message id)
            // POST /users/{id}/messages/{id}/move
            var moveUrl = $"{baseUrl}/v1.0/users/{user}/messages/{Uri.EscapeDataString(createdId)}/move";

            async Task<HttpRequestMessage> moveRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, moveUrl);
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);

                var moveBody = JsonSerializer.SerializeToUtf8Bytes(new GraphMoveRequest { DestinationId = targetFolderId });
                req.Content = new ByteArrayContent(moveBody);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return req;
            }

            using var moveResp = await provider.SendWithRetryAsync(
                moveRequestFactory,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(moveResp, ct).ConfigureAwait(false);

            var movedId = await ReadIdAsync(moveResp, ct).ConfigureAwait(false);

            if (metadataStream != null)
            {
                // 3) Patch metadata on moved message
                await PatchRestoredEmailMetadataAsync(
                    userId,
                    movedId,
                    metadataStream,
                    ct).ConfigureAwait(false);
            }

            return movedId;
        }

        private static async Task<string> ReadIdAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphCreatedMessage>(s, cancellationToken: ct)
                .ConfigureAwait(false);

            if (created?.Id is null || created.Id.Length == 0)
                throw new InvalidOperationException("Graph did not return an id in the response body.");

            return created.Id;
        }

        public async Task PatchRestoredEmailMetadataAsync(
            string userIdOrUpn,
            string restoredMessageId,
            Stream metadataStream, // seekable
            CancellationToken cancellationToken)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var msg = Uri.EscapeDataString(restoredMessageId);

            var url = $"{baseUrl}/v1.0/users/{user}/messages/{msg}";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Deserialize from the seekable stream (no buffering)
            if (metadataStream.CanSeek) metadataStream.Position = 0;
            var metadata = await JsonSerializer.DeserializeAsync<GraphEmailMessageMetadata>(
                metadataStream, jsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new GraphEmailMessageMetadata();

            var patch = new GraphEmailMessagePatch
            {
                IsRead = metadata.IsRead,
                Importance = metadata.Importance,
                Categories = metadata.Categories,
                Flag = metadata.Flag
            };

            // Write PATCH payload to a temp file so retries can reopen a fresh stream
            var tempPath = Path.Combine(Path.GetTempPath(), $"msgpatch_{Guid.NewGuid():N}.json");
            try
            {
                await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    await JsonSerializer.SerializeAsync(fs, patch, jsonOptions, cancellationToken).ConfigureAwait(false);
                    await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
                {
                    var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                    req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);

                    // New stream per attempt (safe for retries; avoids in-memory buffering)
                    var bodyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    req.Content = new StreamContent(bodyStream);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    return req;
                }

                using var resp = await provider.SendWithRetryAsync(
                    requestFactory,
                    HttpCompletionOption.ResponseHeadersRead,
                    null,
                    cancellationToken).ConfigureAwait(false);

                await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }
        }

        public async Task<GraphMailFolder?> GetChildFolderAsync(
            string userIdOrUpn,
            string parentFolderId,
            string displayName,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var parent = Uri.EscapeDataString(parentFolderId);
            var filterName = displayName.Replace("'", "''");

            var url = $"{baseUrl}/v1.0/users/{user}/mailFolders/{parent}/childFolders?$filter=displayName eq '{filterName}'&$top=1";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryAsync(
                requestFactory,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphPage<GraphMailFolder>>(stream, cancellationToken: ct).ConfigureAwait(false);

            return result?.Value?.FirstOrDefault();
        }

        public async Task<GraphMailFolder> CreateMailFolderAsync(
            string userIdOrUpn,
            string parentFolderId,
            string displayName,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var parent = Uri.EscapeDataString(parentFolderId);

            // POST /users/{id}/mailFolders/{id}/childFolders
            var url = $"{baseUrl}/v1.0/users/{user}/mailFolders/{parent}/childFolders";

            var body = new GraphCreateMailFolderRequest
            {
                DisplayName = displayName
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
                => new HttpRequestMessage(HttpMethod.Post, new Uri(url))
                {
                    Headers =
                    {
                        Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false)
                    },
                    Content = JsonContent.Create(body)
                };

            using var resp = await provider.SendWithRetryAsync(
                requestFactory,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var respStream =
                await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var created = await JsonSerializer.DeserializeAsync<GraphMailFolder>(
                respStream,
                cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created folder id.");

            return created;
        }

        public async Task<bool> EmailExistsInFolderByInternetMessageIdAsync(
            string userIdOrUpn,
            string folderIdOrWellKnownName,
            string internetMessageId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            // Escape OData string literals (single quotes doubled)
            var imid = internetMessageId.Replace("'", "''");
            var folder = folderIdOrWellKnownName.Replace("'", "''");

            // Constrain by both internetMessageId AND parentFolderId
            var url =
                $"{baseUrl}/v1.0/users/{user}/messages" +
                $"?$filter=internetMessageId eq '{imid}' and parentFolderId eq '{folder}'" +
                $"&$select=id" +
                $"&$top=1";

            using var stream = await provider.GetGraphAsStreamAsync(
                url, "application/json", ct).ConfigureAwait(false);

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            return doc.RootElement.TryGetProperty("value", out var value) &&
                   value.ValueKind == JsonValueKind.Array &&
                   value.GetArrayLength() > 0;
        }

        public async Task RestoreCalendarEventToCalendarAsync(
            string userId,
            string targetCalendarId,
            Stream eventJsonStream,
            CancellationToken cancellationToken)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var calendar = Uri.EscapeDataString(targetCalendarId);

            // Create event in a specific calendar:
            // POST /users/{id}/calendars/{id}/events  (application/json event body)
            var url = $"{baseUrl}/v1.0/users/{user}/calendars/{calendar}/events";

            // Buffer the body so retries can resend it
            byte[] payload;
            using (var ms = new MemoryStream())
            {
                await eventJsonStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                payload = ms.ToArray();
            }

            async Task<HttpRequestMessage> requestFactory(CancellationToken ct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, ct).ConfigureAwait(false);

                // Important: create new content per attempt
                req.Content = new ByteArrayContent(payload);
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                return req;
            }

            using var resp = await provider.SendWithRetryAsync(
                requestFactory,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
        }

        public async Task RestoreDriveItemToFolderAsync(
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

            using var resp = await provider.SendWithRetryAsync(
                requestFactory,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
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

            using var resp = await provider.SendWithRetryAsync(
                requestFactory, HttpCompletionOption.ResponseHeadersRead, null, cancellationToken).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, cancellationToken).ConfigureAwait(false);
        }
    }
}