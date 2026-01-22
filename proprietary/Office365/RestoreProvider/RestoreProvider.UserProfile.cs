// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;
using NetUri = System.Uri;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal UserProfileApiImpl UserProfileApi => new UserProfileApiImpl(_apiHelper);

    internal class UserProfileApiImpl(APIHelper provider)
    {
        public async Task UpdateUserPhotoAsync(string userIdOrUpn, Stream photoStream, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = NetUri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/photo/$value";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Put, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);

                if (photoStream.CanSeek) photoStream.Position = 0;

                req.Content = new StreamContent(photoStream);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }

        public async Task UpdateUserPropertiesAsync(string userIdOrUpn, Stream propertiesStream, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = NetUri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}";

            using var doc = await JsonDocument.ParseAsync(propertiesStream, cancellationToken: ct);
            var root = doc.RootElement;

            var updateDict = new Dictionary<string, object?>();

            if (root.TryGetProperty("displayName", out var displayName)) updateDict["displayName"] = displayName.GetString();
            if (root.TryGetProperty("jobTitle", out var jobTitle)) updateDict["jobTitle"] = jobTitle.GetString();
            if (root.TryGetProperty("department", out var department)) updateDict["department"] = department.GetString();
            if (root.TryGetProperty("officeLocation", out var officeLocation)) updateDict["officeLocation"] = officeLocation.GetString();
            if (root.TryGetProperty("mobilePhone", out var mobilePhone)) updateDict["mobilePhone"] = mobilePhone.GetString();
            if (root.TryGetProperty("usageLocation", out var usageLocation)) updateDict["usageLocation"] = usageLocation.GetString();
            if (root.TryGetProperty("preferredLanguage", out var preferredLanguage)) updateDict["preferredLanguage"] = preferredLanguage.GetString();

            if (root.TryGetProperty("businessPhones", out var businessPhones) && businessPhones.ValueKind == JsonValueKind.Array)
            {
                var phones = new List<string?>();
                foreach (var phone in businessPhones.EnumerateArray())
                    phones.Add(phone.GetString());
                updateDict["businessPhones"] = phones;
            }

            if (root.TryGetProperty("accountEnabled", out var accountEnabled)) updateDict["accountEnabled"] = accountEnabled.GetBoolean();

            if (updateDict.Count == 0) return;

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(updateDict);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }
    }

    private async Task RestoreUserProfile(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var userProfiles = GetMetadataByType(SourceItemType.UserProfile);
        if (userProfiles.Count == 0)
            return;

        string? targetUserId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            targetUserId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            Log.WriteWarningMessage(LOGTAG, "RestoreUserProfileMissingTargetUser", null, "Cannot restore user profile without a target user.");
            return;
        }

        foreach (var profile in userProfiles)
        {
            if (cancel.IsCancellationRequested) break;

            try
            {
                var originalPath = profile.Key;

                // Restore Properties
                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "user.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);

                if (contentEntry != null)
                {
                    using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                    {
                        await UserProfileApi.UpdateUserPropertiesAsync(targetUserId, contentStream, cancel);
                    }
                    _temporaryFiles.TryRemove(contentPath, out var f);
                    f?.Dispose();
                }

                // Restore Photo
                var photoContentPath = SystemIO.IO_OS.PathCombine(originalPath, "photo.jpg");
                var photoEntry = _temporaryFiles.GetValueOrDefault(photoContentPath);

                if (photoEntry != null)
                {
                    using (var photoStream = SystemIO.IO_OS.FileOpenRead(photoEntry))
                    {
                        await UserProfileApi.UpdateUserPhotoAsync(targetUserId, photoStream, cancel);
                    }
                    _temporaryFiles.TryRemove(photoContentPath, out var f);
                    f?.Dispose();
                }

                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreUserProfileFailed", ex, $"Failed to restore user profile {profile.Key}");
            }
        }
    }
}
