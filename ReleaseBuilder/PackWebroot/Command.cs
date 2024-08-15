using System.CommandLine;
using System.Text.RegularExpressions;

namespace ReleaseBuilder.PackWebroot;

public static class Command
{
    public static System.CommandLine.Command Create()
    {
        var webrootOption = new Option<DirectoryInfo>(
            name: "webroot",
            description: "Path to keyfile to use for signing release manifests",
            getDefaultValue: () => new DirectoryInfo(Path.Combine("..", "Duplicati", "Server", "webroot", "ngax"))
        );

        var command = new System.CommandLine.Command("pack-webroot", "Packs a few files from the webroot to reduce load times")
        {
            webrootOption
        };

        command.SetHandler(async (webroot) =>
        {
            if (!webroot.Exists)
            {
                Console.WriteLine($"Webroot does not exist at {webroot.FullName}");
                Program.ReturnCode = 1;
                return;
            }

            await PackWebroot(webroot.FullName);
        }, webrootOption);

        return command;
    }

    public static async Task<IEnumerable<string>> PackWebroot(string webroot)
    {
        var indexhtml = Path.Combine(webroot, "index.html");
        if (!File.Exists(indexhtml))
            throw new Exception($"File index.html does not exist at {indexhtml}");

        var index = await File.ReadAllTextAsync(indexhtml);
        var scripts = Regex.Matches(index, @"<script( type=""text/javascript"")? src=""(?<name>[^""]*)""></script>")
            .Select(m => new { Tag = m.Value, Path = m.Groups["name"].Value })
            .Where(s => !s.Path.EndsWith(".min.js"))
            .Where(s => !s.Path.Contains("/libs/"))
            .Where(s => !s.Path.Contains("compiled"))
            .Select(s => new
            {
                s.Tag,
                Path = s.Path.IndexOf("?") > 0
                    ? s.Path.Substring(0, s.Path.IndexOf("?"))
                    : s.Path
            })
            .ToArray();

        var combinedScriptPath = Path.Combine(Path.GetDirectoryName(scripts.First().Path) ?? throw new Exception("Missing folder?"), "combined-scripts.js");
        File.WriteAllText(Path.Combine(webroot, combinedScriptPath), string.Join("\n", scripts.Select(s => File.ReadAllText(Path.Combine(webroot, s.Path)))));
        index = index.Replace(scripts.First().Tag, $"<script src=\"{combinedScriptPath}?v=2.0.0.7\"></script>");

        foreach (var script in scripts)
        {
            index = index.Replace(script.Tag, string.Empty);
            File.Delete(Path.Combine(webroot, script.Path));
        }

        await File.WriteAllTextAsync(indexhtml, index);

        return scripts.Select(s => s.Path)
            .Append(combinedScriptPath)
            .Append("index.html")
            .Select(x => Path.Combine(webroot, x));
    }
}
