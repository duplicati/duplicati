using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Abstractions;

public interface IStatusService
{
    ServerStatusDto GetStatus();
}