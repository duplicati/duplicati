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
using Duplicati.Library.AutoUpdater;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Changelog : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/changelog", ([FromQuery(Name = "from-update")] bool? fromUpdate, [FromServices] Connection connection) => Execute(connection, fromUpdate ?? false)).RequireAuthorization();
    }

    private static Dto.ChangelogDto Execute(Connection connection, bool fromUpdate)
    {
        if (fromUpdate)
        {
            var updateInfo = connection.ApplicationSettings.UpdatedVersion;
            if (updateInfo == null)
                throw new NotFoundException("No update found");


            return new Dto.ChangelogDto()
            {
                Version = updateInfo.Version,
                Changelog = updateInfo.ChangeInfo
            };
        }
        else
        {
            var path = Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".", "changelog.txt");
            return new Dto.ChangelogDto()
            {
                Version = UpdaterManager.SelfVersion.Version,
                Changelog = System.IO.File.ReadAllText(path)
            };
        }
    }
}
