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

using LicenseUpdater;

var cmdargs = Environment.GetCommandLineArgs();
if (cmdargs.Length != 2)
    throw new Exception($"Usage: dotnet run <path-to-source-folder>");

var startpath = Path.GetFullPath(Environment.GetCommandLineArgs().Skip(1).First());
if (!Directory.Exists(startpath))
    throw new Exception($"Start path not found: {startpath}");

var target_extensions = new[] {
    ".cs",
    ".csproj"
    // ".html",
    // ".js",
    // ".css"
}.ToHashSet(StringComparer.OrdinalIgnoreCase);

File.WriteAllText(Path.Combine(startpath, "LICENSE.txt"), Fragments.GetLicenseTextWithPrefixedLines(string.Empty));

var candidates = Directory.EnumerateFiles(startpath, "*", SearchOption.AllDirectories)
    .Where(x => target_extensions.Contains(Path.GetExtension(x) ?? string.Empty));

var blacklistSubFolders = new[] { "bin", "obj", "packages" };

foreach (var file in candidates)
{
    if (string.Equals("AssemblyInfo.cs", Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
        continue;
    if (blacklistSubFolders.Any(x => file.Contains($"{Path.DirectorySeparatorChar}{x}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        continue;

    if (new FileInfo(file).Length > 10 * 1024 * 1024)
    {
        Console.WriteLine($"{file} skipped due to size");
        continue;
    }

    var data = File.ReadAllText(file);
    if (Fragments.MatchAndReplace(ref data))
    {
        File.WriteAllText(file, data);
        // Console.WriteLine($"{file} updated!");
        continue;
    }

    if (string.Equals(Path.GetExtension(file), ".cs", StringComparison.OrdinalIgnoreCase) && !data.StartsWith("// Copyright", StringComparison.OrdinalIgnoreCase))
    {
        data = Fragments.GetLicenseTextWithPrefixedLines() + data;
        File.WriteAllText(file, data);
        // Console.WriteLine($"{file} updated!");
        continue;
    }

    if (string.Equals(Path.GetExtension(file), ".csproj", StringComparison.OrdinalIgnoreCase))
    {
        data = Fragments.CSPROJ_COPYRIGHT_MATCH.Replace(data, $"<Copyright>{Fragments.CopyrightText}</Copyright>");
        File.WriteAllText(file, data);
        // Console.WriteLine($"{file} updated!");
        continue;
    }


    Console.WriteLine($"{file} skipped, no match");
}



