namespace Duplicati.Library.AutoUpdater;

public interface IUpdateManagerAccessor
{
    bool HasUpdateInstalled { get; }
}