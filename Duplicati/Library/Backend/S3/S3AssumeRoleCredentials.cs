// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;

namespace Duplicati.Library.Backend;

/// <summary>
/// Credentials of a role assumed through STS with the given access key. The temporary credentials
/// expire, so they are requested again before they do, which keeps a long backup going.
/// </summary>
internal sealed class S3AssumeRoleCredentials : RefreshingAWSCredentials
{
    /// <summary>
    /// The default name of the role session
    /// </summary>
    public const string DEFAULT_SESSION_NAME = "duplicati";

    /// <summary>
    /// The region of the global STS endpoint
    /// </summary>
    private const string DEFAULT_STS_REGION = "us-east-1";

    /// <summary>
    /// The client that assumes the role
    /// </summary>
    private readonly AmazonSecurityTokenServiceClient m_sts;

    /// <summary>
    /// The request that assumes the role
    /// </summary>
    private readonly AssumeRoleRequest m_request;

    /// <summary>
    /// Creates the credentials of a role
    /// </summary>
    /// <param name="awsID">The access key id that assumes the role</param>
    /// <param name="awsKey">The secret access key that assumes the role</param>
    /// <param name="roleArn">The ARN of the role</param>
    /// <param name="sessionName">The name of the role session, or <c>null</c> for the default</param>
    /// <param name="externalId">The external id the role requires, if any</param>
    /// <param name="stsEndpoint">The URL of the STS endpoint, or <c>null</c> for AWS</param>
    /// <param name="region">The region to sign the STS requests for, if any</param>
    public S3AssumeRoleCredentials(string awsID, string awsKey, string roleArn, string? sessionName, string? externalId, string? stsEndpoint, string? region)
    {
        var config = new AmazonSecurityTokenServiceConfig();
        var stsRegion = string.IsNullOrWhiteSpace(region) ? DEFAULT_STS_REGION : region;
        if (!string.IsNullOrWhiteSpace(stsEndpoint))
        {
            config.ServiceURL = stsEndpoint;
            config.AuthenticationRegion = stsRegion;
        }
        else
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(stsRegion);

        m_sts = new AmazonSecurityTokenServiceClient(new BasicAWSCredentials(awsID, awsKey), config);
        m_request = new AssumeRoleRequest
        {
            RoleArn = roleArn,
            RoleSessionName = string.IsNullOrWhiteSpace(sessionName) ? DEFAULT_SESSION_NAME : sessionName,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId
        };

        // Ask for new credentials a while before the old ones expire
        PreemptExpiryTime = TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    protected override CredentialsRefreshState GenerateNewCredentials()
        => GenerateNewCredentialsAsync().GetAwaiter().GetResult();

    /// <inheritdoc/>
    protected override async Task<CredentialsRefreshState> GenerateNewCredentialsAsync()
    {
        var response = await m_sts.AssumeRoleAsync(m_request).ConfigureAwait(false);
        var credentials = response.Credentials;
        // Without an expiration, the credentials are requested again after the shortest duration of a role session
        return new CredentialsRefreshState(
            new ImmutableCredentials(credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken),
            credentials.Expiration?.ToUniversalTime() ?? DateTime.UtcNow.AddMinutes(15));
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            m_sts.Dispose();
        base.Dispose(disposing);
    }
}
