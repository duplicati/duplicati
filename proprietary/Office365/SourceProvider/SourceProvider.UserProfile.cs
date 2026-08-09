// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365;

partial class SourceProvider
{
    internal UserProfileImpl UserProfileApi => new UserProfileImpl(_apiHelper);

    internal class UserProfileImpl(APIHelper provider)
    {
        /// <summary>
        /// Gets the mailbox <c>userPurpose</c> for a user, which differentiates a regular
        /// user mailbox from a shared, room or equipment mailbox.
        /// Returns <c>null</c> if the user has no accessible mailbox (unlicensed, disabled,
        /// soft-deleted, or hosted on-premise) or if the value cannot be determined.
        /// </summary>
        /// <param name="userIdOrUpn">The user id or user principal name.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The mailbox purpose (e.g. <c>user</c>, <c>shared</c>, <c>room</c>, <c>equipment</c>), or null.</returns>
        public async Task<string?> GetMailboxUserPurposeAsync(string userIdOrUpn, CancellationToken ct)
        {
            // MailboxSettings.Read (application) is required to read another user's mailbox purpose.
            var url = $"{provider.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(userIdOrUpn)}/mailboxSettings/userPurpose";
            try
            {
                var purpose = await provider.GetGraphItemAsync<GraphMailboxUserPurpose>(url, ct).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(purpose.Value) ? null : purpose.Value;
            }
            catch (Exception ex) when (APIHelper.IsMailboxNotEnabled(ex))
            {
                // No accessible mailbox; treat as unknown purpose.
                return null;
            }
        }

        // /users/{id}
        public Task<Stream> GetUserObjectStreamAsync(string userIdOrUpn, CancellationToken ct)
        {
            var url = $"{provider.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(userIdOrUpn)}" +
                      "?$select=id,displayName,userPrincipalName,mail,accountEnabled,jobTitle,department,officeLocation,mobilePhone,businessPhones,usageLocation,preferredLanguage,onPremisesSyncEnabled,onPremisesImmutableId";
            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        // /users/{id}/photo/$value
        public Task<Stream> GetUserPhotoStreamAsync(string userIdOrUpn, CancellationToken ct)
        {
            var url = $"{provider.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(userIdOrUpn)}/photo/$value";
            return provider.GetGraphItemAsStreamAsync(url, "application/octet-stream", ct);
        }

        // /users/{id}/licenseDetails
        public Task<Stream> GetUserLicenseDetailsStreamAsync(string userIdOrUpn, CancellationToken ct)
        {
            var url = $"{provider.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(userIdOrUpn)}/licenseDetails";
            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        // /users/{id}/manager
        public Task<Stream> GetUserManagerStreamAsync(string userIdOrUpn, CancellationToken ct)
        {
            var url = $"{provider.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(userIdOrUpn)}/manager";
            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }

        // /users/{id}/joinedTeams
        public Task<Stream> GetUserTeamMembershipStreamAsync(string userIdOrUpn, CancellationToken ct)
        {
            // Lists the Teams the user has joined.
            // Note: This returns a collection of Team objects (membership context), not the full Team configuration.
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);

            var select = "id,displayName,description";
            var url =
                $"{baseUrl}/v1.0/users/{user}/joinedTeams" +
                $"?$select={Uri.EscapeDataString(select)}";

            return provider.GetGraphItemAsStreamAsync(url, "application/json", ct);
        }
    }
}
