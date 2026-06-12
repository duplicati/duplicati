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

using Duplicati.Library.Utility;
using Duplicati.Library.Common.IO;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto.V2;
using Microsoft.AspNetCore.Mvc;
using Duplicati.Library.Interface;

namespace Duplicati.WebserverCore.Endpoints.V2;

public class TestFilters : IEndpointV2
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<TestFilters>();

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/filesystem/test-filter", ([FromBody] TestFilterRequestDto request) => Execute(request))
            .RequireAuthorization();
    }

    private static TestFilterResponseDto Execute(TestFilterRequestDto request)
    {
        try
        {
            return TestFilterResponseDto.Create(Evaluate(request).ToArray());
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteErrorMessage(LOGTAG, "TestFilters", ex, "An error occurred while testing filters");
            return ex is UserInformationException uex
                ? TestFilterResponseDto.CreateError(uex.Message, uex.HelpID)
                : TestFilterResponseDto.CreateError(ex.Message, "InternalError");
        }
    }

    private static IEnumerable<TestFilterResponseItem> Evaluate(TestFilterRequestDto request)
    {
        var filter = FilterExpression.Deserialize(request.Filters ?? Array.Empty<string>());
        var enumeratefilter = filter;

        FilterExpression.AnalyzeFilters(filter, out var includes, out var excludes);
        if (includes && !excludes)
            enumeratefilter = FilterExpression.Combine(filter, new FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true));

        var sourceArray = request.Sources ?? Array.Empty<string>();
        var sources = new HashSet<string>(sourceArray, Utility.ClientFilenameStringComparer);

        foreach (var path in request.Paths ?? Array.Empty<string>())
        {
            if (path == null) continue;

            // If the path is exactly one of the sources, it's always included.
            if (sources.Contains(path))
            {
                yield return new TestFilterResponseItem
                {
                    Path = path,
                    Included = true,
                    MatchedFilter = null
                };
                continue;
            }

            // Check if the path is under any of the source folders
            var isUnderSource = sourceArray
               .Any(source => string.Equals(Util.AppendDirSeparator(path), Util.AppendDirSeparator(source), Utility.ClientFilenameStringComparison) ||
                    Utility.IsPathBelowFolder(path, source));

            // Do not return paths that are not part of the source selection
            if (!isUnderSource)
                continue;

            var isIncluded = FilterExpression.Matches(enumeratefilter!, path, out var match);
            yield return new TestFilterResponseItem
            {
                Path = path,
                Included = isIncluded,
                MatchedFilter = match?.ToString()
            };
        }
    }
}
