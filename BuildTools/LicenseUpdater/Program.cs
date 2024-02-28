using LicenseUpdater;

var cmdargs = Environment.GetCommandLineArgs();
if (cmdargs.Length != 2)
    throw new Exception($"Usage: dotnet run <path-to-source-folder>");

var startpath = Path.GetFullPath(Environment.GetCommandLineArgs().Skip(1).First());
if (!Directory.Exists(startpath))
    throw new Exception($"Start path not found: {startpath}");

var target_extensions = new[] {
    ".cs"
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

    Console.WriteLine($"{file} skipped, no match");
}



