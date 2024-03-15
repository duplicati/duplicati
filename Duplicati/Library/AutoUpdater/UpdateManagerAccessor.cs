namespace Duplicati.Library.AutoUpdater;

public class UpdateManagerAccessor : IUpdateManagerAccessor
{
    public bool HasUpdateInstalled => UpdaterManager.HasUpdateInstalled;
}
