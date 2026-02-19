namespace Duplicati.Library.Interface;

public interface ICommonModule : IDynamicModule
{
    /// <summary>
    /// Gets the module key
    /// </summary>
    string Key { get; }
    /// <summary>
    /// Gets the display name of the module
    /// </summary>
    string DisplayName { get; }
    /// <summary>
    /// Gets the description of the module
    /// </summary>
    string Description { get; }
}
