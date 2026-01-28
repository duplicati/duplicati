// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider
{
    internal ChannelApiImpl ChannelApi => new ChannelApiImpl(_apiHelper);

    internal class ChannelApiImpl(APIHelper provider)
    {
        internal async Task<GraphChannel> CreateChannelInMigrationModeAsync(
            string teamId,
            string displayName,
            string? description,
            DateTimeOffset createdDateTimeUtc,
            CancellationToken ct)
        {
            // Requires:
            // - Application permission: Teamwork.Migrate.All
            // - Channel.Create (or equivalent per tenant configuration)
            // Notes:
            // - Migration-mode channels are standard channels.
            // - createdDateTime must be in the past (not in the future).

            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(teamId);
            var url = $"{baseUrl}/v1.0/teams/{team}/channels";

            // Need to emit the instance annotation property name exactly:
            // "@microsoft.graph.channelCreationMode": "migration"
            var payload = new Dictionary<string, object?>
            {
                ["displayName"] = displayName,
                ["description"] = description,
                ["createdDateTime"] = createdDateTimeUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                ["@microsoft.graph.channelCreationMode"] = "migration"
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(payload);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphChannel>(stream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created channel id.");

            return created;
        }

        internal async Task StartChannelMigrationAsync(string teamId, string channelId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(teamId);
            var channel = Uri.EscapeDataString(channelId);

            // Beta-only
            var url = $"{baseUrl}/beta/teams/{team}/channels/{channel}/startMigration";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                return;

            // Idempotency: treat "already in migration mode" as success.
            if (resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                string bodyText = "";
                try { bodyText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }

                if (bodyText.IndexOf("already in migration mode", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }

        internal async Task CompleteChannelMigrationAsync(string teamId, string channelId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(teamId);
            var channel = Uri.EscapeDataString(channelId);

            // v1.0 supported action
            var url = $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/completeMigration";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);
        }

        internal async Task<GraphChannel?> GetChannelByNameAsync(
            string groupId,
            string channelName,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(groupId);

            // Channels do NOT support $filter reliably on displayName,
            // so enumerate and match client-side.
            var url = $"{baseUrl}/v1.0/teams/{team}/channels";

            await foreach (var channel in provider.GetAllGraphItemsAsync<GraphChannel>(url, ct))
            {
                if (string.Equals(channel.DisplayName, channelName, StringComparison.OrdinalIgnoreCase))
                    return channel;
            }

            return null;
        }

        internal async Task<GraphChatMessage> ImportChannelMessageAsync(
            string teamId,
            string channelId,
            string senderUserId,
            string senderDisplayName,
            DateTimeOffset createdDateTimeUtc,
            string htmlContent,
            string? replyToId,
            CancellationToken ct)
        {
            // Requires:
            // - Application permission: Teamwork.Migrate.All
            // - The channel must be in migration mode (new channel created with @microsoft.graph.channelCreationMode="migration"
            //   OR existing channel put into migration mode via /beta/.../startMigration)

            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var team = Uri.EscapeDataString(teamId);
            var channel = Uri.EscapeDataString(channelId);

            // Import uses the same endpoint as normal posting, but allows createdDateTime/from when Teamwork.Migrate.All is present.
            var url = $"{baseUrl}/v1.0/teams/{team}/channels/{channel}/messages";

            var payload = new Dictionary<string, object?>
            {
                ["createdDateTime"] = createdDateTimeUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                ["from"] = new
                {
                    user = new
                    {
                        id = senderUserId,
                        displayName = senderDisplayName,
                        userIdentityType = "aadUser"
                    }
                },
                ["body"] = new
                {
                    contentType = "html",
                    content = htmlContent
                }
            };

            if (!string.IsNullOrWhiteSpace(replyToId))
                payload["replyToId"] = replyToId;

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(payload);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphChatMessage>(stream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the imported message id.");

            return created;
        }
    }
}
