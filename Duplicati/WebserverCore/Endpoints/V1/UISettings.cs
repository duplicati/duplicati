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
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class UISettings : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/uisettings", ([FromServices] Connection connection) => ExecuteGet(connection)).RequireAuthorization();
        group.MapGet("/uisettings/{scheme}", ([FromServices] Connection connection, [FromRoute] string scheme) => ExecuteSchemeGet(connection, scheme)).RequireAuthorization();
        group.MapPatch("/uisettings/{scheme}", ([FromServices] Connection connection, [FromRoute] string scheme, [FromBody] Dictionary<string, string?> settings) => ExecutePatch(connection, scheme, settings)).RequireAuthorization();
    }

    private static IEnumerable<string> ExecuteGet(Connection connection)
        => connection.GetUISettingsSchemes();

    private static IDictionary<string, string> ExecuteSchemeGet(Connection connection, string scheme)
        => connection.GetUISettings(scheme);

    private static void ExecutePatch(Connection connection, string scheme, IDictionary<string, string?> settings)
        => connection.UpdateUISettings(scheme, settings);
}