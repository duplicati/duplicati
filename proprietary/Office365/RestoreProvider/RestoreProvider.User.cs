// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal EmailApiImpl EmailApi => new EmailApiImpl(_apiHelper);

    internal class EmailApiImpl(APIHelper provider)
    {
        /// <summary>
        /// Limit for ensuring we stay under the 4MB limit for simple email restore.
        /// </summary>
        private const long MAX_SIZE_FOR_SIMPLE_EMAIL_RESTORE = (long)((4 * 1024 * 1024) * (1 - 0.33)) - 1024; // 4MB - 33% base64 overhead - 1KB margin
        public async Task<string> RestoreEmailToFolderAsync(
            string userId,
            string targetFolderId,
            Stream contentStream,
            Stream metadataStream,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = System.Uri.EscapeDataString(userId);

            long length = 0;
            try { length = contentStream.Length; } catch { }

            if (length > MAX_SIZE_FOR_SIMPLE_EMAIL_RESTORE)
                return await RestoreLargeEmailAsync(userId, targetFolderId, contentStream, metadataStream, ct);

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

            using var createResp = await provider.SendWithRetryShortAsync(
                createRequestFactory,
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

            using var moveResp = await provider.SendWithRetryShortAsync(
                moveRequestFactory,
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

        private async Task<string> RestoreLargeEmailAsync(
            string userId,
            string targetFolderId,
            Stream contentStream,
            Stream metadataStream,
            CancellationToken ct)
        {
            // 1. Parse MIME
            if (contentStream.CanSeek) contentStream.Position = 0;
            var message = await MimeMessage.LoadAsync(contentStream, ct);

            // 2. Create Draft Message (without attachments)
            var draft = new GraphMessage
            {
                Subject = message.Subject,
                Body = new GraphBody
                {
                    ContentType = message.HtmlBody != null ? "html" : "text",
                    Content = message.HtmlBody ?? message.TextBody ?? ""
                },
                From = ConvertToGraphRecipient(message.From),
                Sender = ConvertToGraphRecipient(message.Sender),
                ToRecipients = ConvertToGraphRecipients(message.To),
                CcRecipients = ConvertToGraphRecipients(message.Cc),
                BccRecipients = ConvertToGraphRecipients(message.Bcc),
                HasAttachments = message.Attachments.Any()
            };

            var createdDraft = await CreateMessageAsync(userId, draft, ct);

            // 3. Upload Attachments
            foreach (var attachment in message.Attachments)
            {
                await UploadAttachmentAsync(userId, createdDraft.Id, attachment, ct);
            }

            // 4. Move to target folder
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var userEncoded = Uri.EscapeDataString(userId);
            var moveUrl = $"{baseUrl}/v1.0/users/{userEncoded}/messages/{Uri.EscapeDataString(createdDraft.Id)}/move";

            async Task<HttpRequestMessage> moveRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, moveUrl);
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);

                var moveBody = JsonSerializer.SerializeToUtf8Bytes(new GraphMoveRequest { DestinationId = targetFolderId });
                req.Content = new ByteArrayContent(moveBody);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return req;
            }

            using var moveResp = await provider.SendWithRetryShortAsync(
                moveRequestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(moveResp, ct).ConfigureAwait(false);

            var movedId = await ReadIdAsync(moveResp, ct).ConfigureAwait(false);

            // 5. Patch metadata
            if (metadataStream != null)
            {
                await PatchRestoredEmailMetadataAsync(
                    userId,
                    movedId,
                    metadataStream,
                    ct).ConfigureAwait(false);
            }

            return movedId;
        }

        private async Task<GraphCreatedMessage> CreateMessageAsync(string userId, GraphMessage message, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var url = $"{baseUrl}/v1.0/users/{user}/messages";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(message);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<GraphCreatedMessage>(s, cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to create draft message");
        }

        private async Task UploadAttachmentAsync(string userId, string messageId, MimeEntity attachment, CancellationToken ct)
        {
            if (attachment is MessagePart msgPart)
            {
                // Handle attached message as .eml file
                using var ms = new MemoryStream();
                await msgPart.Message.WriteToAsync(ms, ct);
                ms.Position = 0;

                // Create a fake MimePart for upload logic
                var fakePart = new MimePart("message", "rfc822")
                {
                    Content = new MimeContent(ms),
                    FileName = (msgPart.Message.Subject ?? "attached") + ".eml"
                };

                await UploadAttachmentAsync(userId, messageId, fakePart, ct);
                return;
            }

            if (attachment is not MimePart part) return;

            // Check size
            long size = 0;
            using (var measure = new MemoryStream())
            {
                await part.Content.DecodeToAsync(measure, ct);
                size = measure.Length;
            }

            if (size < 3 * 1024 * 1024)
            {
                // Small attachment: POST /users/{id}/messages/{id}/attachments
                await UploadSmallAttachmentAsync(userId, messageId, part, size, ct);
            }
            else
            {
                // Large attachment: createUploadSession
                await UploadLargeAttachmentAsync(userId, messageId, part, size, ct);
            }
        }

        private async Task UploadSmallAttachmentAsync(string userId, string messageId, MimePart part, long size, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var msg = Uri.EscapeDataString(messageId);
            var url = $"{baseUrl}/v1.0/users/{user}/messages/{msg}/attachments";

            using var ms = new MemoryStream();
            await part.Content.DecodeToAsync(ms, ct);
            var bytes = ms.ToArray();

            var attach = new GraphAttachment
            {
                ODataType = "#microsoft.graph.fileAttachment",
                Name = part.FileName ?? "attachment",
                ContentType = part.ContentType.MimeType,
                Size = (int)size,
                IsInline = !string.IsNullOrEmpty(part.ContentId),
                ContentId = part.ContentId,
                ContentBytes = Convert.ToBase64String(bytes)
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(attach);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }

        private async Task UploadLargeAttachmentAsync(string userId, string messageId, MimePart part, long size, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userId);
            var msg = Uri.EscapeDataString(messageId);
            var url = $"{baseUrl}/v1.0/users/{user}/messages/{msg}/attachments/createUploadSession";

            var attachmentItem = new GraphAttachmentItem
            {
                AttachmentType = "file",
                Name = part.FileName ?? "attachment",
                Size = size
            };

            var body = new { AttachmentItem = attachmentItem };

            async Task<HttpRequestMessage> sessionRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
                return req;
            }

            using var sessionResp = await provider.SendWithRetryShortAsync(sessionRequestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(sessionResp, ct).ConfigureAwait(false);

            using var sessionStream = await sessionResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var session = await JsonSerializer.DeserializeAsync<GraphUploadSession>(sessionStream, cancellationToken: ct).ConfigureAwait(false);

            if (session?.UploadUrl == null) throw new InvalidOperationException("Failed to create upload session");

            // Upload chunks
            using var contentStream = new MemoryStream();
            await part.Content.DecodeToAsync(contentStream, ct);
            contentStream.Position = 0;

            await provider.UploadFileToSessionAsync(session.UploadUrl, contentStream, ct);
        }

        private GraphRecipient? ConvertToGraphRecipient(InternetAddressList? addresses)
        {
            var addr = addresses?.Mailboxes.FirstOrDefault();
            if (addr == null) return null;
            return new GraphRecipient
            {
                EmailAddress = new GraphEmailAddress { Name = addr.Name, Address = addr.Address }
            };
        }

        private GraphRecipient? ConvertToGraphRecipient(InternetAddress? address)
        {
            if (address is MailboxAddress mailbox)
            {
                return new GraphRecipient
                {
                    EmailAddress = new GraphEmailAddress { Name = mailbox.Name, Address = mailbox.Address }
                };
            }
            return null;
        }

        private List<GraphRecipient>? ConvertToGraphRecipients(InternetAddressList? addresses)
        {
            if (addresses == null || addresses.Count == 0) return null;
            return addresses.Mailboxes.Select(addr => new GraphRecipient
            {
                EmailAddress = new GraphEmailAddress { Name = addr.Name, Address = addr.Address }
            }).ToList();
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

                using var resp = await provider.SendWithRetryShortAsync(
                    requestFactory,
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

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
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

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
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

            using var stream = await provider.GetGraphItemAsStreamAsync(
                url, "application/json", ct).ConfigureAwait(false);

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            return doc.RootElement.TryGetProperty("value", out var value) &&
                   value.ValueKind == JsonValueKind.Array &&
                   value.GetArrayLength() > 0;
        }

        public async Task UpdateMailboxSettingsAsync(string userIdOrUpn, GraphMailboxSettings settings, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/mailboxSettings";

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            await provider.PatchGraphItemAsync(url, content, ct);
        }

        public async Task CreateMessageRuleAsync(string userIdOrUpn, GraphMessageRule rule, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/mailFolders/inbox/messageRules";

            // Remove ID and read-only properties before creating
            rule.Id = "";
            rule.IsReadOnly = null;
            rule.HasError = null;

            var json = JsonSerializer.Serialize(rule, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            await provider.PostGraphItemAsync<GraphMessageRule>(url, content, ct);
        }
    }
}