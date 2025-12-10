// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Backend.Filen;

/// <summary>
/// Web module to help users obtain an API key for Filen.io
/// </summary>
public class GetApiKeyModule : IWebModule
{
    /// <inheritdoc/>
    public string Key => "filen-get-api-key";

    /// <inheritdoc/>
    public string DisplayName => "Get Filen.io API Key";

    /// <inheritdoc/>
    public string Description => "Module to help users obtain an API key for Filen.io using their email password, and MFA code.";

    /// <summary>
    /// Constructor for metadata loading
    /// </summary>
    public GetApiKeyModule()
    {
    }

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands =>
    [
        .. AuthOptionsHelper.GetOptions(),
        new CommandLineArgument("two-factor", CommandLineArgument.ArgumentType.String, Strings.FilenBackend.TwoFactorShort, Strings.FilenBackend.TwoFactorLong)
    ];

    /// <inheritdoc/>
    public IDictionary<string, string> Execute(IDictionary<string, string?> options)
    {
        options.TryGetValue("filen-operation", out var operation);
        if (operation != "GetApiKey")
            throw new UserInformationException("Invalid operation", "InvalidOperation");

        options.TryGetValue("url", out var url);
        if (string.IsNullOrEmpty(url))
            throw new UserInformationException("URL is required", "UrlOptionMissing");

        var uri = new Utility.Uri(url);

        var newOpts = new Dictionary<string, string?>(options);
        foreach (var key in uri.QueryParameters.AllKeys)
            if (key != null)
                newOpts[key] = uri.QueryParameters[key];

        var backend = new FilenBackend(url, newOpts);
        var apiKey = backend.GetApiKey(CancellationToken.None).Await();
        return new Dictionary<string, string> { { "api-key", apiKey ?? string.Empty } };
    }

    /// <inheritdoc/>
    public IDictionary<string, IDictionary<string, string>> GetLookups()
        => new Dictionary<string, IDictionary<string, string>>();
}
