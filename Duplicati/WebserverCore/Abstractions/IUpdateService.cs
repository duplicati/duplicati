namespace Duplicati.WebserverCore.Abstractions;

public interface IUpdateService
{
    UpdateInfo? GetUpdateInfo();
}