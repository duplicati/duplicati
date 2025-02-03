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

using System.Text.RegularExpressions;

namespace LicenseUpdater;

public static class Fragments
{
    public static string NEW_LICENSE = @"Copyright (C) YYYY, The Duplicati Team
https://duplicati.com, hello@duplicati.com

Permission is hereby granted, free of charge, to any person obtaining a 
copy of this software and associated documentation files (the ""Software""), 
to deal in the Software without restriction, including without limitation 
the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in 
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
DEALINGS IN THE SOFTWARE.".Replace("YYYY", DateTime.UtcNow.Year.ToString());

    public static RegexOptions RE_OPTS = RegexOptions.Singleline | RegexOptions.IgnoreCase;

    public static Regex CS_REGION_MATCH = new Regex(@"(?<license>#region Disclaimer / License(?<licensebody>.+(?:Free Software Foundation)[^#]+)#endregion)", RE_OPTS);

    public static Regex CS_LGPL_MATCH = new Regex(@"(?<license>//\s+Copyright \(C\) \d{4}, The Duplicati Team.+(?:Free Software Foundation).+Boston,\s+MA\s+0211\d-130\d\s+USA)", RE_OPTS);

    public static Regex CS_MIT_MATCH = new Regex(@"(?<license>//\s+Copyright \(C\) \d{4}, The Duplicati Team.+(?:copyright notice and this permission notice).+IN THE SOFTWARE\.)", RE_OPTS);

    public static Regex CSPROJ_COPYRIGHT_MATCH = new Regex(@"<Copyright>(.*?)<\/Copyright>");

    public static string GetLicenseTextWithPrefixedLines(string linePrefix = "// ")
        => string.Join("\n", Fragments.NEW_LICENSE.Split("\n").Select(x => linePrefix + x).Append(string.Empty));

    private static readonly string LicenseTextWithPrefixedLines = GetLicenseTextWithPrefixedLines();

    public static readonly string CopyrightText = $"Copyright © {DateTime.UtcNow.Year.ToString()} Team Duplicati, MIT license";

    public static bool MatchAndReplace(ref string data)
    {
        foreach (var m in new[] { CS_REGION_MATCH, CS_MIT_MATCH })
        {
            var match = m.Match(data);
            if (match.Success && match.Groups["license"].Success)
            {
                var g = match.Groups["license"];
                var newLicense = LicenseTextWithPrefixedLines;

                if (data[(g.Index + g.Length)..].StartsWith('\n') || data[(g.Index + g.Length)..].StartsWith("\r\n"))
                    newLicense = newLicense.TrimEnd();

                data = string.Join(
                    string.Empty,

                    data.Substring(0, g.Index),
                    newLicense,
                    data.Substring(g.Index + g.Length)
                );

                return true;
            }
        }

        return false;
    }
}


