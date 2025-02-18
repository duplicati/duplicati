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
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class Licenses : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/licenses", () => Execute()).RequireAuthorization();
    }

    private static IEnumerable<Dto.LicenseDto> Execute()
    {
        var exefolder = Path.GetDirectoryName(Duplicati.Library.Utility.Utility.getEntryAssembly().Location) ?? ".";
        var path = Path.Combine(exefolder, "licenses");
        if (OperatingSystem.IsMacOS() && !Directory.Exists(path))
        {
            // Go up one, as the licenses cannot be in the binary folder in MacOS Packages
            exefolder = Path.GetDirectoryName(exefolder) ?? ".";
            var test = Path.Combine(exefolder, "Licenses");
            if (Directory.Exists(test))
                path = test;
        }
        return Duplicati.License.LicenseReader.ReadLicenses(path)
            .Select(x => new Dto.LicenseDto
            {
                Title = x.Title,
                Url = x.Url,
                License = x.License,
                Jsondata = x.Jsondata
            });

    }
}
