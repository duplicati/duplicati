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
using Duplicati.Library.Main;
using Duplicati.Library.RestAPI;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public record WebModules : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/webmodules", ExecuteGet).RequireAuthorization();
        group.MapPost("/webmodule/{modulekey}", ([FromRoute] string modulekey, [FromBody] Dictionary<string, string> options, CancellationToken cancellationToken) => ExecutePost(modulekey, options, cancellationToken)).RequireAuthorization();
    }

    private static IEnumerable<IWebModule> ExecuteGet()
        => Library.DynamicLoader.WebLoader.Modules;

    private static async Task<Dto.WebModuleOutputDto> ExecutePost(string modulekey, Dictionary<string, string> inputOptions, CancellationToken cancellationToken)
    {
        var m = Library.DynamicLoader.WebLoader.Modules.FirstOrDefault(x => x.Key.Equals(modulekey, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("No such module found");

        var options = Runner.GetCommonOptions();
        foreach (var k in inputOptions.Keys)
            options[k] = inputOptions[k];

        await SecretProviderHelper.ApplySecretProviderAsync([], [], options, Library.Utility.TempFolder.SystemTempPath, FIXMEGlobal.SecretProvider, cancellationToken);

        return new Dto.WebModuleOutputDto(
            Status: "OK",
            Result: m.Execute(options)
        );
    }

}
