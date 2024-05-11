using System.CommandLine;

namespace ReleaseBuilder;

/// <summary>
/// Options and arguments shared between commands
/// </summary>
public static class SharedOptions
{
    /// <summary>
    /// The password to the key file to use for signing release manifests
    /// </summary>
    public static readonly Option<string> passwordOption = new Option<string>(
        name: "--password",
        description: "The password to use for the keyfile",
        getDefaultValue: () => string.Empty
    );

}
