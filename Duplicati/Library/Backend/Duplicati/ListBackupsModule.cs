using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.Duplicati;

/// <summary>
/// Module to list backup folders on a Duplicati storage server
/// </summary>
public class ListBackupsModule : IWebModule
{
    /// <inheritdoc />
    public string Key => "duplicati-list-backups";

    /// <inheritdoc />
    public string DisplayName => Strings.ListFoldersModule.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.ListFoldersModule.Description;

    /// <summary>
    /// The operation this module can perform
    /// </summary>
    public enum Operation
    {
        /// <summary>
        /// List backup folders
        /// </summary>
        ListBackups
    }

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => [
        new CommandLineArgument("action", CommandLineArgument.ArgumentType.Enumeration, Strings.ListFoldersModule.ActionDescriptionShort, Strings.ListFoldersModule.ActionDescriptionLong, null, Enum.GetNames(typeof(Operation))),
        new CommandLineArgument("url", CommandLineArgument.ArgumentType.String, Strings.ListFoldersModule.UrlDescriptionShort, Strings.ListFoldersModule.UrlDescriptionLong),
    ];

    /// <summary>
    /// Default constructor for metadata extraction
    /// </summary>
    public ListBackupsModule()
    {
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, string>> Execute(IDictionary<string, string?> options, CancellationToken cancellationToken)
    {
        var opts = new Dictionary<string, string?>(options);

        var action = opts.GetValueOrDefault("action");
        if (string.IsNullOrEmpty(action))
            throw new UserInformationException("The 'action' option must be specified", "ActionOptionMissing");

        if (!Enum.TryParse<Operation>(action, out var operation))
            throw new UserInformationException($"The specified action '{action}' is not valid", "ActionOptionInvalid");

        if (operation != Operation.ListBackups)
            throw new UserInformationException($"The specified action '{action}' is not supported", "ActionOptionUnsupported");

        var url = opts.GetValueOrDefault("url");
        if (string.IsNullOrEmpty(url))
            throw new UserInformationException("The 'url' option must be specified", "UrlOptionMissing");
        var uri = new Library.Utility.Uri(url);
        foreach (var key in uri.QueryParameters.AllKeys)
            if (key != null)
                opts[key] = uri.QueryParameters[key];

        // We do not need a backup id to list backups, but the backend requires one
        opts[DuplicatiBackend.BACKUP_ID_OPTION] = "--unused--";

        using var backend = new DuplicatiBackend(url, opts);
        return new Dictionary<string, string>()
        {
            { "folders", JsonSerializer.Serialize( await backend.ListBackupFolders(cancellationToken).ToArrayAsync(cancellationToken) ) }
        };
    }

    /// <inheritdoc />
    public IDictionary<string, IDictionary<string, string>> GetLookups() => new Dictionary<string, IDictionary<string, string>>();
}
