using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.WebserverCore.Dto;

public class ServerStatusDto : IServerStatus
{
    public Tuple<long, string>? ActiveTask { get; init; }
    public LiveControlState ProgramState { get; set; }
    public IList<Tuple<long, string>> SchedulerQueueIds { get; init; } = [];
    public bool HasWarning { get; set; }
    public bool HasError { get; set; }

    //TODO: This should not be here. FE should decide what is the appropriate icon for state
    public SuggestedStatusIcon SuggestedStatusIcon { get; set; }
    public DateTime EstimatedPauseEnd { get; set; }
    public long LastEventID { get; init; }
    public long LastDataUpdateID { get; init; }
    public long LastNotificationUpdateID { get; init; }
    public string? UpdatedVersion { get; init; }
    public UpdatePollerStates UpdaterState { get; init; }
    public bool UpdateReady { get; init; }
    public double UpdateDownloadProgress { get; init; }
}