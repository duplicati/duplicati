namespace Duplicati.WebserverCore.Options;

public class StaticFilesOptions
{
    public const string SectionName = "Duplicati:StaticFiles";

    public string Webroot { get; set; } = "webroot";
    public string? ContentRootPathOverride { get; set; }
}