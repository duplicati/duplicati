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

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Health
{
    public static Command Create() =>
        new Command("health", "Checks the server health endpoint")
        .WithHandler(CommandHandler.Create<Settings, OutputInterceptor>(async (settings, output) =>
        {
            // The health endpoint only needs to know if the server is reachable.
            // When the user has not requested any certificate overrides, the
            // default OS validation is used by leaving the callback unset.
            // Mirror Connection.cs: --host-cert "*" is treated as accept-all, the same
            // as --insecure, so the documented wildcard is honored consistently.
            var trustedCertificateHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(settings.AcceptedHostCertificate))
                trustedCertificateHashes.UnionWith(settings.AcceptedHostCertificate.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            using var handler = new HttpClientHandler();
            var acceptAll = settings.Insecure || trustedCertificateHashes.Contains("*");
            if (acceptAll || trustedCertificateHashes.Count > 0 || settings.IgnoreRevocationFailure)
                HttpClientHelper.ConfigureHandlerCertificateValidator(handler, acceptAll, acceptAll ? null : [.. trustedCertificateHashes], settings.IgnoreRevocationFailure);

            using var client = new HttpClient(handler);
            client.BaseAddress = settings.HostUrl;
            client.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                var response = await client.GetAsync("health");
                response.EnsureSuccessStatusCode();
                output.SetResult(true);
                output.AppendCustomObject("healthy", true);
                output.AppendConsoleMessage("Server is healthy");
                return 0;
            }
            catch (HttpRequestException)
            {
                output.AppendConsoleMessage("Server is unhealthy");
                output.AppendCustomObject("healthy", false);
                output.SetResult(false);
                return 1;
            }
        })
    );
}
